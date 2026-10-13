use crate::output;
use clap::Subcommand;

use super::{Context, TargetArgs};

/// Composed visual capture for agents. Every render is saved as a PNG (to `--output PATH`, or
/// `.ucp/captures/` in the project) and its path is printed; `isolate` and `orbit` render an
/// object on its own so an LLM can perceive its 3D shape from one image.
#[derive(Subcommand)]
pub enum ViewAction {
    /// Render the scene. With no target, renders the main (or a chosen) camera. With a target,
    /// places a temporary camera framed on that object, still showing the surrounding scene.
    Capture {
        /// Object to frame (same as --path; an integer is an id); omit to render the camera's own view
        #[arg(value_name = "PATH|ID")]
        object: Option<String>,
        /// Frame a temporary camera on this object (--path/--name/--id); omit to render the camera's own view
        #[command(flatten)]
        target: TargetArgs,
        /// Frame on this object by instance id (same as --id)
        #[arg(long, allow_hyphen_values = true, hide = true)]
        target_id: Option<i64>,
        /// Frame on this object by name (same as --name)
        #[arg(long, hide = true)]
        target_name: Option<String>,
        /// Render from this specific camera object (instance id) instead of the main camera
        #[arg(long, allow_hyphen_values = true)]
        camera: Option<i64>,
        /// Cap the longest image edge (px) to keep the payload small
        #[arg(long)]
        max_edge: Option<u32>,
        /// Background: `transparent` for an alpha PNG (otherwise a solid neutral color)
        #[arg(long)]
        background: Option<String>,
        /// Save the PNG here (default: .ucp/captures/capture-<time>.png in the project)
        #[arg(short, long)]
        output: Option<String>,
        /// Print the PNG as base64 on stdout instead of saving a file
        #[arg(long, conflicts_with = "output")]
        base64: bool,
    },
    /// Render a single object in isolation, auto-framed from its bounds. By default produces a
    /// composite grid of Front/Right/Back/Top; use `--views front,right` for specific angles.
    Isolate {
        /// Object to render (same as --path; an integer is an id)
        #[arg(value_name = "PATH|ID")]
        object: Option<String>,
        #[command(flatten)]
        target: TargetArgs,
        /// Comma-separated views: front,back,left,right,top,bottom — or omit for a composite grid
        #[arg(long, value_delimiter = ',')]
        views: Vec<String>,
        /// Per-tile edge length in px (default 512)
        #[arg(long)]
        max_edge: Option<u32>,
        /// Background: `transparent` for an alpha PNG (otherwise a solid neutral color)
        #[arg(long)]
        background: Option<String>,
        /// Save the PNG here (default: .ucp/captures/isolate-<time>.png; several views write PATH-<view>.png)
        #[arg(short, long)]
        output: Option<String>,
        /// Print the PNG as base64 on stdout instead of saving a file
        #[arg(long, conflicts_with = "output")]
        base64: bool,
    },
    /// Render a ring of angles around an object as one composite grid image.
    Orbit {
        /// Object to orbit (same as --path; an integer is an id)
        #[arg(value_name = "PATH|ID")]
        object: Option<String>,
        #[command(flatten)]
        target: TargetArgs,
        /// Number of evenly spaced angles (1-12, default 4)
        #[arg(long, default_value_t = 4)]
        count: u32,
        /// Camera elevation in degrees (default 20)
        #[arg(long, default_value_t = 20.0)]
        elevation: f64,
        /// Per-tile edge length in px (default 384)
        #[arg(long)]
        max_edge: Option<u32>,
        /// Background: `transparent` for an alpha PNG
        #[arg(long)]
        background: Option<String>,
        /// Save the PNG here (default: .ucp/captures/orbit-<time>.png in the project)
        #[arg(short, long)]
        output: Option<String>,
        /// Print the PNG as base64 on stdout instead of saving a file
        #[arg(long, conflicts_with = "output")]
        base64: bool,
    },
}

pub async fn run(action: ViewAction, ctx: &Context) -> anyhow::Result<()> {
    let (project, _, mut client) = super::connect_client(ctx).await?;

    let (method, params, output, base64) = build_request(&action)?;
    let result = client.call(method, params).await?;
    client.close().await;

    // Base64 on stdout is opt-in: a render dumped into an agent's context by accident costs
    // hundreds of thousands of tokens, so the default is always a file plus its path.
    if base64 {
        if ctx.json {
            output::print_json(&output::success_json(result));
        } else if let Some(images) = result.get("images").and_then(|v| v.as_array()) {
            for img in images {
                if let Some(b64) = img.get("data").and_then(|v| v.as_str()) {
                    println!("{b64}");
                }
            }
        } else if let Some(b64) = result.get("data").and_then(|v| v.as_str()) {
            println!("{b64}");
        }
        return Ok(());
    }

    let kind = method.rsplit('/').next().unwrap_or("view");
    let base = match output {
        Some(path) => path,
        None => super::default_capture_path(&project, kind, "png")?,
    };

    // Multi-image (isolate with explicit multiple views): write each as PATH-<view>.png.
    let mut saved = Vec::with_capacity(1);
    if let Some(images) = result.get("images").and_then(|v| v.as_array()) {
        for img in images {
            let view = img.get("view").and_then(|v| v.as_str()).unwrap_or("view");
            let path = suffix_path(&base, view);
            write_b64(img.get("data"), &path)?;
            saved.push(serde_json::json!({
                "path": path,
                "view": view,
                "width": img.get("width"),
                "height": img.get("height"),
            }));
        }
    } else {
        write_b64(result.get("data"), &base)?;
        saved.push(serde_json::json!({
            "path": base,
            "width": result.get("width"),
            "height": result.get("height"),
        }));
    }

    if ctx.json {
        let mut payload = result.clone();
        if let Some(obj) = payload.as_object_mut() {
            obj.remove("data");
            obj.remove("images");
            obj.insert("saved".into(), serde_json::Value::Array(saved));
        }
        output::print_json(&output::success_json(payload));
    } else {
        for image in &saved {
            let path = image.get("path").and_then(|v| v.as_str()).unwrap_or("?");
            let dims = match (
                image.get("width").and_then(|v| v.as_u64()),
                image.get("height").and_then(|v| v.as_u64()),
            ) {
                (Some(w), Some(h)) => format!(" ({w}x{h})"),
                _ => String::new(),
            };
            output::print_success(&format!("Saved {path}{dims}"));
        }
    }
    Ok(())
}

fn build_request(
    action: &ViewAction,
) -> anyhow::Result<(&'static str, serde_json::Value, Option<String>, bool)> {
    let mut obj = serde_json::Map::new();
    match action {
        ViewAction::Capture {
            object,
            target,
            target_id,
            target_name,
            camera,
            max_edge,
            background,
            output,
            base64,
        } => {
            let target = &with_positional(target, object);
            if let Some(id) = target.id.or(*target_id) {
                obj.insert("targetId".into(), serde_json::json!(id));
            } else if let Some(path) = &target.path {
                obj.insert("targetPath".into(), serde_json::json!(path));
            } else if let Some(name) = target.name.as_ref().or(target_name.as_ref()) {
                obj.insert("targetName".into(), serde_json::json!(name));
            }
            if let Some(c) = camera {
                obj.insert("camera".into(), serde_json::json!(c));
            }
            insert_common(&mut obj, max_edge, background);
            Ok(("view/capture", obj.into(), output.clone(), *base64))
        }
        ViewAction::Isolate {
            object,
            target,
            views,
            max_edge,
            background,
            output,
            base64,
        } => {
            with_positional(target, object).apply(&mut obj)?;
            if !views.is_empty() {
                obj.insert("views".into(), serde_json::json!(views));
            }
            insert_common(&mut obj, max_edge, background);
            Ok(("view/isolate", obj.into(), output.clone(), *base64))
        }
        ViewAction::Orbit {
            object,
            target,
            count,
            elevation,
            max_edge,
            background,
            output,
            base64,
        } => {
            with_positional(target, object).apply(&mut obj)?;
            obj.insert("count".into(), serde_json::json!(count));
            obj.insert("elevation".into(), serde_json::json!(elevation));
            insert_common(&mut obj, max_edge, background);
            Ok(("view/orbit", obj.into(), output.clone(), *base64))
        }
    }
}

/// `ucp view isolate Orb` means `--path Orb` (or `--id` for an integer); explicit flags win.
fn with_positional(target: &TargetArgs, object: &Option<String>) -> TargetArgs {
    let mut merged = target.clone();
    if let Some(object) = object {
        if !merged.is_set() {
            match object.trim().parse::<i64>() {
                Ok(id) => merged.id = Some(id),
                Err(_) => merged.path = Some(object.clone()),
            }
        }
    }
    merged
}

fn insert_common(
    obj: &mut serde_json::Map<String, serde_json::Value>,
    max_edge: &Option<u32>,
    background: &Option<String>,
) {
    if let Some(m) = max_edge {
        obj.insert("maxEdge".into(), serde_json::json!(m));
    }
    if let Some(b) = background {
        obj.insert("background".into(), serde_json::json!(b));
    }
}

fn write_b64(data: Option<&serde_json::Value>, path: &str) -> anyhow::Result<()> {
    use base64::Engine;
    let b64 = data
        .and_then(|v| v.as_str())
        .ok_or_else(|| anyhow::anyhow!("response contained no image data"))?;
    let bytes = base64::engine::general_purpose::STANDARD
        .decode(b64)
        .map_err(|e| anyhow::anyhow!("failed to decode base64: {e}"))?;
    std::fs::write(path, &bytes)?;
    Ok(())
}

fn suffix_path(base: &str, view: &str) -> String {
    match base.rsplit_once('.') {
        Some((stem, ext)) => format!("{stem}-{view}.{ext}"),
        None => format!("{base}-{view}.png"),
    }
}
