use crate::output;
use clap::Subcommand;

use super::{Context, TargetArgs};

/// Composed visual capture for agents. All renders return a PNG; pass `--output PATH` to save it
/// (or base64 is printed to stdout). `isolate` and `orbit` render an object on its own so an LLM
/// can perceive its 3D shape from one image.
#[derive(Subcommand)]
pub enum ViewAction {
    /// Render the scene. With no target, renders the main (or a chosen) camera. With a target,
    /// places a temporary camera framed on that object, still showing the surrounding scene.
    Capture {
        /// Frame a temporary camera on this object (instance id)
        #[arg(long, allow_hyphen_values = true)]
        target_id: Option<i64>,
        /// Frame on an object by name
        #[arg(long)]
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
        /// Save the PNG to this path (otherwise base64 to stdout)
        #[arg(short, long)]
        output: Option<String>,
    },
    /// Render a single object in isolation, auto-framed from its bounds. By default produces a
    /// composite grid of Front/Right/Back/Top; use `--views front,right` for specific angles.
    Isolate {
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
        /// Save the PNG to this path (a multi-view non-composite render writes PATH-<view>.png)
        #[arg(short, long)]
        output: Option<String>,
    },
    /// Render a ring of angles around an object as one composite grid image.
    Orbit {
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
        /// Save the PNG to this path
        #[arg(short, long)]
        output: Option<String>,
    },
}

pub async fn run(action: ViewAction, ctx: &Context) -> anyhow::Result<()> {
    let (_, _, mut client) = super::connect_client(ctx).await?;

    let (method, params, output) = build_request(&action)?;
    let result = client.call(method, params).await?;
    client.close().await;

    // Multi-image (isolate with explicit multiple views): write each as PATH-<view>.png.
    if let Some(images) = result.get("images").and_then(|v| v.as_array()) {
        if let Some(base) = &output {
            for img in images {
                let view = img.get("view").and_then(|v| v.as_str()).unwrap_or("view");
                let path = suffix_path(base, view);
                write_b64(img.get("data"), &path)?;
                if !ctx.json {
                    output::print_success(&format!("Saved {path}"));
                }
            }
        } else if ctx.json {
            output::print_json(&output::success_json(result));
        } else {
            output::print_json(&result);
        }
        return Ok(());
    }

    if let Some(path) = output {
        write_b64(result.get("data"), &path)?;
        if !ctx.json {
            let dims = format!(
                "{}x{}",
                result.get("width").and_then(|v| v.as_u64()).unwrap_or(0),
                result.get("height").and_then(|v| v.as_u64()).unwrap_or(0)
            );
            output::print_success(&format!("Saved {path} ({dims})"));
        }
    } else if ctx.json {
        output::print_json(&output::success_json(result));
    } else if let Some(b64) = result.get("data").and_then(|v| v.as_str()) {
        println!("{b64}");
    }
    Ok(())
}

fn build_request(
    action: &ViewAction,
) -> anyhow::Result<(&'static str, serde_json::Value, Option<String>)> {
    let mut obj = serde_json::Map::new();
    match action {
        ViewAction::Capture {
            target_id,
            target_name,
            camera,
            max_edge,
            background,
            output,
        } => {
            if let Some(tid) = target_id {
                obj.insert("targetId".into(), serde_json::json!(tid));
            } else if let Some(tn) = target_name {
                obj.insert("targetName".into(), serde_json::json!(tn));
            }
            if let Some(c) = camera {
                obj.insert("camera".into(), serde_json::json!(c));
            }
            insert_common(&mut obj, max_edge, background);
            Ok(("view/capture", obj.into(), output.clone()))
        }
        ViewAction::Isolate {
            target,
            views,
            max_edge,
            background,
            output,
        } => {
            target.apply(&mut obj)?;
            if !views.is_empty() {
                obj.insert("views".into(), serde_json::json!(views));
            }
            insert_common(&mut obj, max_edge, background);
            Ok(("view/isolate", obj.into(), output.clone()))
        }
        ViewAction::Orbit {
            target,
            count,
            elevation,
            max_edge,
            background,
            output,
        } => {
            target.apply(&mut obj)?;
            obj.insert("count".into(), serde_json::json!(count));
            obj.insert("elevation".into(), serde_json::json!(elevation));
            insert_common(&mut obj, max_edge, background);
            Ok(("view/orbit", obj.into(), output.clone()))
        }
    }
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
