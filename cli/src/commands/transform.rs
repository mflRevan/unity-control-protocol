use crate::output;
use clap::Subcommand;

use super::{Context, TargetArgs, UnityLifecyclePolicy, vec3_json};

/// Author GameObject transforms directly. Rotations are Euler angles in degrees; positions and
/// scales are X Y Z. Every subcommand targets an object with `--id`, `--path`, or `--name`.
#[derive(Subcommand)]
pub enum TransformAction {
    /// Move an object. `--space world` (default) or `local`; `--relative` adds to the current
    /// position instead of replacing it.
    Move {
        #[command(flatten)]
        target: TargetArgs,
        /// Destination/offset as three numbers: X Y Z
        #[arg(long, alias = "position", num_args = 3, value_names = ["X", "Y", "Z"], allow_hyphen_values = true)]
        to: Vec<f64>,
        /// Coordinate space: world or local
        #[arg(long, default_value = "world")]
        space: String,
        /// Treat --to as an offset from the current position
        #[arg(long)]
        relative: bool,
        /// Save the active scene afterward
        #[arg(long)]
        save: bool,
    },
    /// Rotate an object by Euler angles (degrees). `--relative` rotates from the current
    /// orientation instead of setting an absolute one.
    Rotate {
        #[command(flatten)]
        target: TargetArgs,
        /// Euler angles in degrees: X Y Z
        #[arg(long, alias = "to", alias = "rotation", num_args = 3, value_names = ["X", "Y", "Z"], allow_hyphen_values = true)]
        euler: Vec<f64>,
        /// Coordinate space: world or local
        #[arg(long, default_value = "world")]
        space: String,
        /// Rotate relative to the current orientation
        #[arg(long)]
        relative: bool,
        /// Save the active scene afterward
        #[arg(long)]
        save: bool,
    },
    /// Set or multiply local scale. Use `--scale X Y Z` for non-uniform, or `--uniform N` for all
    /// axes. `--relative` multiplies the current scale.
    Scale {
        #[command(flatten)]
        target: TargetArgs,
        /// Non-uniform scale: X Y Z
        #[arg(long, alias = "to", num_args = 3, value_names = ["X", "Y", "Z"], conflicts_with = "uniform")]
        scale: Vec<f64>,
        /// Uniform scale applied to all three axes
        #[arg(long)]
        uniform: Option<f64>,
        /// Multiply the current scale instead of replacing it
        #[arg(long)]
        relative: bool,
        /// Save the active scene afterward
        #[arg(long)]
        save: bool,
    },
    /// Orient an object to face a point or another object. Provide `--at X Y Z` for a world
    /// point, or `--target <PATH|NAME|ID>` for another object.
    LookAt {
        #[command(flatten)]
        target: TargetArgs,
        /// World point to face: X Y Z
        #[arg(long, num_args = 3, value_names = ["X", "Y", "Z"], allow_hyphen_values = true)]
        at: Vec<f64>,
        /// Face another object: its hierarchy path, name, or instance id
        #[arg(long = "target", allow_hyphen_values = true, conflicts_with = "at", value_name = "PATH|NAME|ID")]
        face: Option<String>,
        /// Face another object by instance id (same as --target with an id)
        #[arg(long, allow_hyphen_values = true, conflicts_with = "at", hide = true)]
        target_id: Option<i64>,
        /// Up vector hint: X Y Z (default 0 1 0)
        #[arg(long, num_args = 3, value_names = ["X", "Y", "Z"], allow_hyphen_values = true)]
        up: Vec<f64>,
        /// Save the active scene afterward
        #[arg(long)]
        save: bool,
    },
    /// Read transforms (position, rotation, scale, bounds). One object via `--id`/`--path`/`--name`,
    /// or many via `--ids 12,34,56`.
    Get {
        #[command(flatten)]
        target: TargetArgs,
        /// Comma-separated instance IDs for a bulk read
        #[arg(long, value_delimiter = ',')]
        ids: Vec<i64>,
    },
}

pub async fn run(action: TransformAction, ctx: &Context) -> anyhow::Result<()> {
    let (project, lock, mut client) = super::connect_client(ctx).await?;

    let (method, params, mutates, save) = build_request(&action)?;

    let mut result = client.call(method, params).await?;

    if save {
        super::save_active_scene(&mut client, ctx).await?;
    }
    client.close().await;

    let policy = if mutates {
        UnityLifecyclePolicy::editor_settle(
            "Waiting for Unity to apply the transform change...",
            "transform change",
        )
    } else {
        UnityLifecyclePolicy::None
    };
    let lifecycle = super::await_unity_lifecycle(&project, Some(&lock), policy, ctx).await?;
    result = super::attach_lifecycle_log_status(result, &lifecycle);

    if ctx.json {
        output::print_json(&output::success_json(result));
    } else {
        print_human(&action, &result);
    }
    Ok(())
}

fn build_request(
    action: &TransformAction,
) -> anyhow::Result<(&'static str, serde_json::Value, bool, bool)> {
    let mut obj = serde_json::Map::new();
    match action {
        TransformAction::Move {
            target,
            to,
            space,
            relative,
            save,
        } => {
            target.apply(&mut obj)?;
            obj.insert("position".into(), vec3_json(to));
            obj.insert("space".into(), serde_json::json!(space));
            obj.insert("relative".into(), serde_json::json!(relative));
            Ok(("transform/move", obj.into(), true, *save))
        }
        TransformAction::Rotate {
            target,
            euler,
            space,
            relative,
            save,
        } => {
            target.apply(&mut obj)?;
            obj.insert("euler".into(), vec3_json(euler));
            obj.insert("space".into(), serde_json::json!(space));
            obj.insert("relative".into(), serde_json::json!(relative));
            Ok(("transform/rotate", obj.into(), true, *save))
        }
        TransformAction::Scale {
            target,
            scale,
            uniform,
            relative,
            save,
        } => {
            target.apply(&mut obj)?;
            if let Some(u) = uniform {
                obj.insert("uniform".into(), serde_json::json!(u));
            } else if scale.len() == 3 {
                obj.insert("scale".into(), vec3_json(scale));
            } else {
                anyhow::bail!("provide --scale X Y Z or --uniform N");
            }
            obj.insert("relative".into(), serde_json::json!(relative));
            Ok(("transform/scale", obj.into(), true, *save))
        }
        TransformAction::LookAt {
            target,
            at,
            face,
            target_id,
            up,
            save,
        } => {
            target.apply(&mut obj)?;
            if let Some(tid) = target_id {
                obj.insert("targetId".into(), serde_json::json!(tid));
            } else if let Some(face) = face {
                match face.trim().parse::<i64>() {
                    Ok(id) => obj.insert("targetId".into(), serde_json::json!(id)),
                    Err(_) => obj.insert("targetPath".into(), serde_json::json!(face)),
                };
            } else if at.len() == 3 {
                obj.insert("target".into(), vec3_json(at));
            } else {
                anyhow::bail!("provide --at X Y Z or --target <PATH|NAME|ID>");
            }
            if up.len() == 3 {
                obj.insert("up".into(), vec3_json(up));
            }
            Ok(("transform/look-at", obj.into(), true, *save))
        }
        TransformAction::Get { target, ids } => {
            if !ids.is_empty() {
                obj.insert("ids".into(), serde_json::json!(ids.to_vec()));
            } else {
                target.apply(&mut obj)?;
            }
            Ok(("transform/get", obj.into(), false, false))
        }
    }
}

fn print_human(action: &TransformAction, result: &serde_json::Value) {
    match action {
        TransformAction::Get { .. } => {
            // A bulk read (`--ids`) comes back as a list; one object as a flat payload.
            if let Some(items) = result.get("transforms").and_then(|v| v.as_array()) {
                output::print_success(&format!("{} transform(s)", items.len()));
                for item in items {
                    let (header, lines) = super::text::transform_get_lines(item);
                    eprintln!("  {header}");
                    for line in lines {
                        eprintln!("  {line}");
                    }
                }
                return;
            }
            let (header, lines) = super::text::transform_get_lines(result);
            output::print_success(&header);
            for line in lines {
                eprintln!("{line}");
            }
        }
        _ => {
            let summary = super::text::transform_summary(result);
            let name = result.get("name").and_then(|v| v.as_str()).unwrap_or("?");
            output::print_success(&format!("{name}: {summary}"));
        }
    }
}
