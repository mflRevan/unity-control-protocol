use crate::output;
use clap::Subcommand;

use super::{Context, TargetArgs, UnityLifecyclePolicy, vec3_json};

/// Geometric queries against the live scene. Physics queries (`raycast`, `overlap`, `ground`) hit
/// colliders only; `bounds` and `nearest` also see render-only objects.
#[derive(Subcommand)]
pub enum SpatialAction {
    /// Cast a ray and report the first collider hit (point, normal, distance, object).
    Raycast {
        /// Ray origin: X Y Z
        #[arg(long, num_args = 3, value_names = ["X", "Y", "Z"], allow_hyphen_values = true)]
        origin: Vec<f64>,
        /// Ray direction: X Y Z (need not be normalized)
        #[arg(long, num_args = 3, value_names = ["X", "Y", "Z"], allow_hyphen_values = true)]
        direction: Vec<f64>,
        /// Maximum distance (default: unbounded)
        #[arg(long)]
        max_distance: Option<f64>,
        /// Restrict to a layer: integer mask or a single layer name
        #[arg(long)]
        layer_mask: Option<String>,
        /// Also hit trigger colliders
        #[arg(long)]
        query_triggers: bool,
    },
    /// List colliders overlapping a shape at a point.
    Overlap {
        /// Shape: sphere, box, or capsule
        #[arg(long, default_value = "sphere")]
        shape: String,
        /// Shape center: X Y Z
        #[arg(long, num_args = 3, value_names = ["X", "Y", "Z"], allow_hyphen_values = true)]
        center: Vec<f64>,
        /// Radius (sphere/capsule)
        #[arg(long, default_value_t = 1.0)]
        radius: f64,
        /// Half-extents for box: X Y Z
        #[arg(long, num_args = 3, value_names = ["X", "Y", "Z"])]
        half_extents: Vec<f64>,
        /// Restrict to a layer: integer mask or a single layer name
        #[arg(long)]
        layer_mask: Option<String>,
        /// Also include trigger colliders
        #[arg(long)]
        query_triggers: bool,
    },
    /// Report an object's world-space axis-aligned bounding box (center/size/min/max).
    Bounds {
        #[command(flatten)]
        target: TargetArgs,
        /// Only this object's own renderers/colliders, not its children
        #[arg(long)]
        no_children: bool,
    },
    /// Drop an object (or probe a point) straight down onto the first surface below it. By default
    /// the object is moved to rest on the surface; pass `--no-apply` to only report the hit.
    Ground {
        #[command(flatten)]
        target: TargetArgs,
        /// Probe from a world point instead of an object: X Y Z
        #[arg(long, num_args = 3, value_names = ["X", "Y", "Z"], allow_hyphen_values = true)]
        point: Vec<f64>,
        /// Cast direction: X Y Z (default 0 -1 0)
        #[arg(long, num_args = 3, value_names = ["X", "Y", "Z"], allow_hyphen_values = true)]
        direction: Vec<f64>,
        /// Maximum cast distance
        #[arg(long, default_value_t = 1000.0)]
        max_distance: f64,
        /// Restrict to a layer: integer mask or a single layer name
        #[arg(long)]
        layer_mask: Option<String>,
        /// Report the hit but do not move the object
        #[arg(long)]
        no_apply: bool,
        /// Save the active scene afterward (when the object is moved)
        #[arg(long)]
        save: bool,
    },
    /// Find the nearest objects to a point or object, sorted by distance.
    Nearest {
        #[command(flatten)]
        target: TargetArgs,
        /// Measure from a world point instead of an object: X Y Z
        #[arg(long, num_args = 3, value_names = ["X", "Y", "Z"], allow_hyphen_values = true)]
        point: Vec<f64>,
        /// Maximum results
        #[arg(long, default_value_t = 5)]
        max: u32,
        /// Only objects with this component type
        #[arg(long)]
        component: Option<String>,
        /// Only objects with this tag
        #[arg(long)]
        tag: Option<String>,
    },
}

pub async fn run(action: SpatialAction, ctx: &Context) -> anyhow::Result<()> {
    let (project, lock, mut client) = super::connect_client(ctx).await?;

    let (method, params, mutates, save) = build_request(&action)?;
    let mut result = client.call(method, params).await?;

    if save {
        super::save_active_scene(&mut client, ctx).await?;
    }
    client.close().await;

    let policy = if mutates {
        UnityLifecyclePolicy::editor_settle("Waiting for Unity to apply the move...", "ground move")
    } else {
        UnityLifecyclePolicy::None
    };
    let lifecycle = super::await_unity_lifecycle(&project, Some(&lock), policy, ctx).await?;
    result = super::attach_lifecycle_log_status(result, &lifecycle);

    if ctx.json {
        output::print_json(&output::success_json(result));
    } else {
        output::print_json(&result);
    }
    Ok(())
}

fn build_request(
    action: &SpatialAction,
) -> anyhow::Result<(&'static str, serde_json::Value, bool, bool)> {
    let mut obj = serde_json::Map::new();
    match action {
        SpatialAction::Raycast {
            origin,
            direction,
            max_distance,
            layer_mask,
            query_triggers,
        } => {
            require3(origin, "--origin")?;
            require3(direction, "--direction")?;
            obj.insert("origin".into(), vec3_json(origin));
            obj.insert("direction".into(), vec3_json(direction));
            if let Some(d) = max_distance {
                obj.insert("maxDistance".into(), serde_json::json!(d));
            }
            insert_layer(&mut obj, layer_mask);
            obj.insert("queryTriggers".into(), serde_json::json!(query_triggers));
            Ok(("physics/raycast", obj.into(), false, false))
        }
        SpatialAction::Overlap {
            shape,
            center,
            radius,
            half_extents,
            layer_mask,
            query_triggers,
        } => {
            require3(center, "--center")?;
            obj.insert("shape".into(), serde_json::json!(shape));
            obj.insert("center".into(), vec3_json(center));
            obj.insert("radius".into(), serde_json::json!(radius));
            if half_extents.len() == 3 {
                obj.insert("halfExtents".into(), vec3_json(half_extents));
            }
            insert_layer(&mut obj, layer_mask);
            obj.insert("queryTriggers".into(), serde_json::json!(query_triggers));
            Ok(("physics/overlap", obj.into(), false, false))
        }
        SpatialAction::Bounds {
            target,
            no_children,
        } => {
            target.apply(&mut obj)?;
            obj.insert("includeChildren".into(), serde_json::json!(!no_children));
            Ok(("object/bounds", obj.into(), false, false))
        }
        SpatialAction::Ground {
            target,
            point,
            direction,
            max_distance,
            layer_mask,
            no_apply,
            save,
        } => {
            if target.is_set() {
                target.apply(&mut obj)?;
            } else if point.len() == 3 {
                obj.insert("point".into(), vec3_json(point));
            } else {
                anyhow::bail!("specify an object with --id/--path/--name or a --point X Y Z");
            }
            if direction.len() == 3 {
                obj.insert("direction".into(), vec3_json(direction));
            }
            obj.insert("maxDistance".into(), serde_json::json!(max_distance));
            insert_layer(&mut obj, layer_mask);
            obj.insert("apply".into(), serde_json::json!(!no_apply));
            let mutates = !no_apply && target.is_set();
            Ok(("spatial/ground", obj.into(), mutates, *save && mutates))
        }
        SpatialAction::Nearest {
            target,
            point,
            max,
            component,
            tag,
        } => {
            if target.is_set() {
                target.apply(&mut obj)?;
            } else if point.len() == 3 {
                obj.insert("point".into(), vec3_json(point));
            } else {
                anyhow::bail!("specify an object with --id/--path/--name or a --point X Y Z");
            }
            obj.insert("max".into(), serde_json::json!(max));
            if let Some(c) = component {
                obj.insert("component".into(), serde_json::json!(c));
            }
            if let Some(t) = tag {
                obj.insert("tag".into(), serde_json::json!(t));
            }
            Ok(("spatial/nearest", obj.into(), false, false))
        }
    }
}

fn require3(v: &[f64], flag: &str) -> anyhow::Result<()> {
    if v.len() != 3 {
        anyhow::bail!("{flag} requires three numbers: X Y Z");
    }
    Ok(())
}

fn insert_layer(obj: &mut serde_json::Map<String, serde_json::Value>, layer_mask: &Option<String>) {
    if let Some(lm) = layer_mask {
        if let Ok(n) = lm.parse::<i64>() {
            obj.insert("layerMask".into(), serde_json::json!(n));
        } else {
            obj.insert("layerMask".into(), serde_json::json!(lm));
        }
    }
}
