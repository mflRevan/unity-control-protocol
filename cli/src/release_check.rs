use crate::config;
use crate::output;
use anyhow::Context;
use chrono::{DateTime, Utc};
use semver::Version;
use serde::{Deserialize, Serialize};
use std::fs;
use std::path::{Path, PathBuf};
use std::time::Duration;

const DEFAULT_RELEASE_CHECK_URL: &str =
    "https://api.github.com/repos/mflRevan/unity-control-protocol/releases/latest";
const DEFAULT_CACHE_FILE_NAME: &str = "release-check.json";
const DEFAULT_TTL_SECS: u64 = 24 * 60 * 60;
const DEFAULT_TIMEOUT_SECS: u64 = 3;
const PACKAGE_NAME: &str = "@mflrevan/ucp";

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct ReleaseStatus {
    pub current_version: String,
    pub latest_version: String,
    pub release_url: Option<String>,
    pub install_method: InstallMethod,
    pub update_available: bool,
}

impl ReleaseStatus {
    pub fn warning_lines(&self) -> Vec<String> {
        if !self.update_available {
            return Vec::new();
        }

        let mut lines = vec![format!(
            "A newer UCP release is available: v{} (installed v{}).",
            self.latest_version, self.current_version
        )];

        if let Some(command) = self.install_method.update_command() {
            lines.push(format!("Update with `{command}`."));
        } else {
            lines.push(format!(
                "Update with your install method. Common commands: `npm update -g {PACKAGE_NAME}` or `pnpm update -g {PACKAGE_NAME}`."
            ));
        }

        lines.push(
            "Then run `ucp doctor` in your Unity project; if the bridge package is behind, run `ucp bridge update`."
                .to_string(),
        );

        lines
    }
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum InstallMethod {
    Npm,
    Pnpm,
    Cargo,
    Standalone,
    Unknown,
}

impl InstallMethod {
    fn detect_from_path(path: &Path) -> Self {
        let lowered = path.to_string_lossy().to_ascii_lowercase();

        if lowered.contains("node_modules")
            && lowered.contains("@mflrevan")
            && lowered.contains("ucp")
        {
            if lowered.contains(".pnpm")
                || lowered.contains("\\pnpm\\")
                || lowered.contains("/pnpm/")
            {
                return Self::Pnpm;
            }
            return Self::Npm;
        }

        if lowered.contains(".cargo\\bin\\")
            || lowered.contains(".cargo/bin/")
            || lowered.ends_with("\\cargo\\bin\\ucp.exe")
            || lowered.ends_with("/cargo/bin/ucp")
        {
            return Self::Cargo;
        }

        if path.parent().is_some() {
            return Self::Standalone;
        }

        Self::Unknown
    }

    fn current() -> Self {
        if let Ok(raw) = std::env::var("UCP_INSTALL_METHOD_HINT") {
            let normalized = raw.trim().to_ascii_lowercase();
            return match normalized.as_str() {
                "npm" => Self::Npm,
                "pnpm" => Self::Pnpm,
                "cargo" => Self::Cargo,
                "standalone" => Self::Standalone,
                _ => Self::Unknown,
            };
        }

        std::env::current_exe()
            .map(|path| Self::detect_from_path(&path))
            .unwrap_or(Self::Unknown)
    }

    fn update_command(&self) -> Option<&'static str> {
        match self {
            Self::Npm => Some("npm update -g @mflrevan/ucp"),
            Self::Pnpm => Some("pnpm update -g @mflrevan/ucp"),
            Self::Cargo => {
                Some("cargo install --git https://github.com/mflRevan/unity-control-protocol ucp")
            }
            Self::Standalone => None,
            Self::Unknown => None,
        }
    }
}

#[derive(Debug, Clone, Serialize, Deserialize)]
struct ReleaseCache {
    checked_at: DateTime<Utc>,
    latest_version: String,
    #[serde(default)]
    release_url: Option<String>,
}

#[derive(Debug, Deserialize)]
struct LatestReleaseResponse {
    tag_name: String,
    #[serde(default)]
    html_url: Option<String>,
}

pub async fn maybe_print_update_notice() {
    let Ok(Some(status)) = check_for_update().await else {
        return;
    };

    if !status.update_available {
        return;
    }

    for line in status.warning_lines() {
        if line.starts_with("A newer UCP release is available:") {
            output::print_warn(&line);
        } else {
            output::print_info(&line);
        }
    }
}

pub async fn check_for_update() -> anyhow::Result<Option<ReleaseStatus>> {
    let cache_path = cache_path()?;
    let ttl = cache_ttl();
    let cache = read_fresh_cache(&cache_path, ttl)?;
    let cache = match cache {
        Some(cache) => cache,
        None => {
            let fetched = fetch_latest_release().await?;
            write_cache(&cache_path, &fetched)?;
            fetched
        }
    };

    build_status(cache)
}

fn build_status(cache: ReleaseCache) -> anyhow::Result<Option<ReleaseStatus>> {
    let current_version = env!("CARGO_PKG_VERSION").to_string();
    let current = parse_version(&current_version)?;
    let latest = parse_version(&cache.latest_version)?;

    Ok(Some(ReleaseStatus {
        current_version,
        latest_version: cache.latest_version,
        release_url: cache.release_url,
        install_method: InstallMethod::current(),
        update_available: latest > current,
    }))
}

fn parse_version(raw: &str) -> anyhow::Result<Version> {
    Version::parse(raw.trim().trim_start_matches('v'))
        .with_context(|| format!("Invalid semantic version `{raw}`"))
}

fn cache_ttl() -> Duration {
    std::env::var("UCP_VERSION_CHECK_TTL_SECS")
        .ok()
        .and_then(|raw| raw.parse::<u64>().ok())
        .map(Duration::from_secs)
        .unwrap_or_else(|| Duration::from_secs(DEFAULT_TTL_SECS))
}

fn request_timeout() -> Duration {
    std::env::var("UCP_RELEASE_CHECK_TIMEOUT_SECS")
        .ok()
        .and_then(|raw| raw.parse::<u64>().ok())
        .map(Duration::from_secs)
        .unwrap_or_else(|| Duration::from_secs(DEFAULT_TIMEOUT_SECS))
}

fn cache_path() -> anyhow::Result<PathBuf> {
    if let Ok(override_path) = std::env::var("UCP_RELEASE_CHECK_CACHE_PATH") {
        return Ok(PathBuf::from(override_path));
    }

    let cache_dir = config::cli_cache_dir().context("UCP cache directory unavailable")?;
    Ok(cache_dir.join(DEFAULT_CACHE_FILE_NAME))
}

fn read_fresh_cache(path: &Path, ttl: Duration) -> anyhow::Result<Option<ReleaseCache>> {
    let content = match fs::read_to_string(path) {
        Ok(content) => content,
        Err(error) if error.kind() == std::io::ErrorKind::NotFound => return Ok(None),
        Err(error) => {
            return Err(anyhow::anyhow!(
                "Failed to read release cache {}: {error}",
                path.display()
            ));
        }
    };

    let cache: ReleaseCache = serde_json::from_str(&content)
        .with_context(|| format!("Failed to parse release cache from {}", path.display()))?;

    let age = Utc::now()
        .signed_duration_since(cache.checked_at)
        .to_std()
        .unwrap_or_default();
    if age <= ttl {
        return Ok(Some(cache));
    }

    Ok(None)
}

fn write_cache(path: &Path, cache: &ReleaseCache) -> anyhow::Result<()> {
    if let Some(parent) = path.parent() {
        fs::create_dir_all(parent).with_context(|| {
            format!(
                "Failed to create release cache directory {}",
                parent.display()
            )
        })?;
    }

    fs::write(path, format!("{}\n", serde_json::to_string_pretty(cache)?))
        .with_context(|| format!("Failed to write release cache {}", path.display()))?;

    Ok(())
}

async fn fetch_latest_release() -> anyhow::Result<ReleaseCache> {
    let client = reqwest::Client::builder()
        .timeout(request_timeout())
        .build()
        .context("Failed to build release-check HTTP client")?;
    let url = std::env::var("UCP_RELEASE_CHECK_URL")
        .unwrap_or_else(|_| DEFAULT_RELEASE_CHECK_URL.to_string());

    let response = client
        .get(&url)
        .header(
            reqwest::header::USER_AGENT,
            format!("ucp/{}", env!("CARGO_PKG_VERSION")),
        )
        .send()
        .await
        .with_context(|| format!("Failed to query latest release from {url}"))?
        .error_for_status()
        .with_context(|| format!("Latest release endpoint returned an error for {url}"))?;

    let payload: LatestReleaseResponse = response
        .json()
        .await
        .with_context(|| format!("Failed to decode latest release response from {url}"))?;

    let latest_version = payload.tag_name.trim().trim_start_matches('v').to_string();
    parse_version(&latest_version)?;

    Ok(ReleaseCache {
        checked_at: Utc::now(),
        latest_version,
        release_url: payload.html_url,
    })
}

#[cfg(test)]
mod tests {
    use super::*;
    use chrono::Duration as ChronoDuration;

    #[test]
    fn detects_npm_path() {
        let path = PathBuf::from(
            r"C:\Users\me\AppData\Roaming\npm\node_modules\@mflrevan\ucp\native\ucp.exe",
        );
        assert_eq!(InstallMethod::detect_from_path(&path), InstallMethod::Npm);
    }

    #[test]
    fn detects_pnpm_path() {
        let path = PathBuf::from(
            r"C:\Users\me\AppData\Local\pnpm\global\5\node_modules\.pnpm\@mflrevan\ucp\node_modules\@mflrevan\ucp\native\ucp.exe",
        );
        assert_eq!(InstallMethod::detect_from_path(&path), InstallMethod::Pnpm);
    }

    #[test]
    fn detects_cargo_path() {
        let path = PathBuf::from(r"C:\Users\me\.cargo\bin\ucp.exe");
        assert_eq!(InstallMethod::detect_from_path(&path), InstallMethod::Cargo);
    }

    #[test]
    fn build_status_marks_update_available() {
        let status = build_status(ReleaseCache {
            checked_at: Utc::now(),
            latest_version: "9.9.9".to_string(),
            release_url: None,
        })
        .expect("status should build")
        .expect("status should exist");

        assert!(status.update_available);
        assert_eq!(status.current_version, env!("CARGO_PKG_VERSION"));
    }

    #[test]
    fn fresh_cache_respects_ttl() {
        let cache = ReleaseCache {
            checked_at: Utc::now() - ChronoDuration::seconds(10),
            latest_version: "0.4.4".to_string(),
            release_url: None,
        };
        let path = std::env::temp_dir().join(format!(
            "ucp-release-cache-test-{}.json",
            std::process::id()
        ));
        write_cache(&path, &cache).expect("cache should write");

        let cached = read_fresh_cache(&path, Duration::from_secs(60))
            .expect("cache read should succeed")
            .expect("cache should be fresh");
        assert_eq!(cached.latest_version, "0.4.4");

        let stale =
            read_fresh_cache(&path, Duration::from_secs(1)).expect("cache read should succeed");
        assert!(stale.is_none());

        let _ = fs::remove_file(path);
    }

    #[test]
    fn warning_lines_include_bridge_guidance() {
        let status = ReleaseStatus {
            current_version: "0.4.1".to_string(),
            latest_version: "0.4.4".to_string(),
            release_url: None,
            install_method: InstallMethod::Npm,
            update_available: true,
        };

        let lines = status.warning_lines();
        assert!(lines.iter().any(|line| line.contains("npm update -g")));
        assert!(lines.iter().any(|line| line.contains("ucp bridge update")));
    }
}
