//! Compact text renderings of bridge payloads for the default (non-`--json`) mode.
//!
//! The rule for every renderer here: one line per fact an agent acts on, numbers rounded to
//! what a scene author can use, defaults left out, and the address (path or instance id) the
//! agent needs for its next command always present. Anything deeper is one `--json` away.

use serde_json::Value;

/// A float rounded to three decimals with trailing zeros removed; `-0` and `1e-6` noise print
/// as `0`.
pub fn fnum(f: f64) -> String {
    let r = (f * 1000.0).round() / 1000.0;
    if r == 0.0 {
        return "0".to_string();
    }
    let s = format!("{r:.3}");
    s.trim_end_matches('0').trim_end_matches('.').to_string()
}

pub fn num(v: &Value) -> String {
    match v.as_f64() {
        Some(f) => fnum(f),
        None => v.to_string(),
    }
}

/// `(x, y, z)` for a JSON number array, or `None` when the value is not one.
pub fn vec(v: Option<&Value>) -> Option<String> {
    let arr = v?.as_array()?;
    if arr.is_empty() || !arr.iter().all(Value::is_number) {
        return None;
    }
    Some(format!(
        "({})",
        arr.iter().map(num).collect::<Vec<_>>().join(", ")
    ))
}

pub fn vec_or(v: Option<&Value>, fallback: &str) -> String {
    vec(v).unwrap_or_else(|| fallback.to_string())
}

pub fn str_of<'a>(v: &'a Value, key: &str) -> Option<&'a str> {
    v.get(key).and_then(Value::as_str)
}

pub fn i64_of(v: &Value, key: &str) -> Option<i64> {
    v.get(key).and_then(Value::as_i64)
}

pub fn u64_of(v: &Value, key: &str) -> u64 {
    v.get(key).and_then(Value::as_u64).unwrap_or(0)
}

pub fn flag(v: &Value, key: &str) -> Option<bool> {
    v.get(key).and_then(Value::as_bool)
}

fn vec_eq(a: Option<&Value>, b: Option<&Value>) -> bool {
    match (a.and_then(Value::as_array), b.and_then(Value::as_array)) {
        (Some(a), Some(b)) if a.len() == b.len() => a
            .iter()
            .zip(b)
            .all(|(x, y)| (x.as_f64().unwrap_or(0.0) - y.as_f64().unwrap_or(0.0)).abs() < 1e-4),
        _ => false,
    }
}

/// `Name #id` — the two handles an agent addresses an object by.
pub fn handle(v: &Value) -> String {
    let name = str_of(v, "name").unwrap_or("?");
    match i64_of(v, "instanceId") {
        Some(id) => format!("{name} #{id}"),
        None => name.to_string(),
    }
}

/// A scalar property value, or an object reference as `Name (Type: path)`; anything else is
/// compact JSON.
pub fn value(v: &Value) -> String {
    match v {
        Value::String(s) => format!("\"{s}\""),
        Value::Number(_) => num(v),
        Value::Bool(b) => b.to_string(),
        Value::Null => "null".to_string(),
        Value::Array(_) => vec(Some(v)).unwrap_or_else(|| v.to_string()),
        Value::Object(obj) => match (str_of(v, "name"), str_of(v, "type")) {
            (Some(name), Some(kind)) if obj.contains_key("path") || obj.contains_key("instanceId") => {
                match str_of(v, "path") {
                    Some(path) if !path.is_empty() => format!("{name} ({kind}: {path})"),
                    _ => format!("{name} ({kind})"),
                }
            }
            _ => v.to_string(),
        },
    }
}

// ---------------------------------------------------------------------------------------------
// Hierarchy
// ---------------------------------------------------------------------------------------------

/// One hierarchy node as `Name #id [flags] · Component, Component`; children indented below.
/// Transform is implied on every object and left out. Only non-default tag/layer and inactive
/// state are shown, and a collapsed subtree says how many children it hides.
pub fn node_line(node: &Value, indent: usize) -> String {
    let mut line = format!("{}{}", "  ".repeat(indent), handle(node));
    if flag(node, "active") == Some(false) {
        line.push_str(" (inactive)");
    }
    if let Some(tag) = str_of(node, "tag").filter(|t| *t != "Untagged" && !t.is_empty()) {
        line.push_str(&format!(" tag:{tag}"));
    }
    match (str_of(node, "layerName"), i64_of(node, "layer")) {
        (Some(layer), _) if layer != "Default" && !layer.is_empty() => {
            line.push_str(&format!(" layer:{layer}"))
        }
        (None, Some(layer)) if layer != 0 => line.push_str(&format!(" layer:{layer}")),
        _ => {}
    }
    let expanded = node
        .get("children")
        .and_then(Value::as_array)
        .is_some_and(|c| !c.is_empty());
    let child_count = u64_of(node, "childCount");
    if !expanded && child_count > 0 {
        line.push_str(&format!(" [{child_count} children]"));
    }
    if let Some(components) = node.get("components").and_then(Value::as_array) {
        let names: Vec<&str> = components
            .iter()
            .filter_map(Value::as_str)
            .filter(|c| *c != "Transform")
            .collect();
        if !names.is_empty() {
            line.push_str(" · ");
            line.push_str(&names.join(", "));
        }
    }
    line
}

pub fn push_tree(out: &mut Vec<String>, node: &Value, indent: usize) {
    out.push(node_line(node, indent));
    if let Some(children) = node.get("children").and_then(Value::as_array) {
        for child in children {
            push_tree(out, child, indent + 1);
        }
    }
}

/// The `snapshot` payload as a header plus an indented tree.
pub fn snapshot_lines(result: &Value, filter: Option<&str>, depth: u32) -> (String, Vec<String>) {
    let objects = result
        .get("objects")
        .and_then(Value::as_array)
        .cloned()
        .unwrap_or_default();
    let scene = str_of(result, "sceneName").unwrap_or("scene");
    let stats = result.get("stats").cloned().unwrap_or(Value::Null);
    let shown = u64_of(&stats, "objectCount");
    let roots = u64_of(&stats, "rootCount");
    let header = match filter {
        Some(filter) => format!("{shown} object(s) matching \"{filter}\" in {scene}"),
        None if depth == 0 => format!(
            "{scene}: {roots} root object(s); --depth N expands children, --filter NAME searches"
        ),
        None => format!("{scene}: {shown} object(s) to depth {depth}"),
    };
    let mut lines = Vec::with_capacity(shown as usize + 1);
    for node in &objects {
        push_tree(&mut lines, node, 1);
    }
    (header, lines)
}

/// The `scene/query` payload: a header and one line per match.
pub fn query_lines(result: &Value) -> (String, Vec<String>) {
    let objects = result
        .get("objects")
        .and_then(Value::as_array)
        .cloned()
        .unwrap_or_default();
    let query = str_of(result, "query").unwrap_or("query");
    let header = format!("{} object(s) match {query}", objects.len());
    let lines = objects
        .iter()
        .map(|node| {
            let mut line = node_line(node, 1);
            if let Some(path) = str_of(node, "path").filter(|p| p.contains('/')) {
                line.push_str(&format!(" @ {path}"));
            }
            line
        })
        .collect();
    (header, lines)
}

// ---------------------------------------------------------------------------------------------
// Transforms and spatial queries
// ---------------------------------------------------------------------------------------------

/// `position (..) · rotation (..) · scale (..)` from whichever of the keys a payload carries.
pub fn transform_summary(result: &Value) -> String {
    let mut parts = Vec::with_capacity(3);
    if let Some(p) = vec(result.get("position")) {
        parts.push(format!("position {p}"));
    }
    if let Some(r) = vec(result.get("eulerAngles")) {
        parts.push(format!("rotation {r}"));
    }
    if let Some(s) = vec(result.get("localScale")) {
        parts.push(format!("scale {s}"));
    }
    parts.join(" · ")
}

/// `transform get`: the world transform, the local one only where it differs, and the bounds.
pub fn transform_get_lines(result: &Value) -> (String, Vec<String>) {
    let header = format!("{}: {}", handle(result), transform_summary(result));
    let mut lines = Vec::with_capacity(2);
    let mut local = Vec::with_capacity(2);
    if !vec_eq(result.get("localPosition"), result.get("position")) {
        if let Some(p) = vec(result.get("localPosition")) {
            local.push(format!("position {p}"));
        }
    }
    if !vec_eq(result.get("localEulerAngles"), result.get("eulerAngles")) {
        if let Some(r) = vec(result.get("localEulerAngles")) {
            local.push(format!("rotation {r}"));
        }
    }
    if !local.is_empty() {
        lines.push(format!("  local: {}", local.join(" · ")));
    }
    if let (Some(center), Some(extents)) = (
        vec(result.get("boundsCenter")),
        result.get("boundsExtents").and_then(Value::as_array),
    ) {
        let size: Vec<String> = extents
            .iter()
            .map(|e| fnum(e.as_f64().unwrap_or(0.0) * 2.0))
            .collect();
        lines.push(format!("  bounds: center {center} size ({})", size.join(", ")));
    }
    (header, lines)
}

/// A raycast / ground-probe hit: what was hit, where, and how far.
pub fn hit_line(result: &Value) -> String {
    let surface = str_of(result, "gameObject")
        .or_else(|| str_of(result, "surface"))
        .unwrap_or("?");
    let id = i64_of(result, "instanceId").or_else(|| i64_of(result, "surfaceId"));
    let mut line = match id {
        Some(id) => format!("hit {surface} #{id}"),
        None => format!("hit {surface}"),
    };
    if let Some(collider) = str_of(result, "collider") {
        line.push_str(&format!(" ({collider})"));
    }
    line.push_str(&format!(
        " at {} distance {}",
        vec_or(result.get("point"), "?"),
        result.get("distance").map(num).unwrap_or_else(|| "?".into())
    ));
    if let Some(normal) = vec(result.get("normal")) {
        line.push_str(&format!(" normal {normal}"));
    }
    line
}

pub fn bounds_line(result: &Value) -> String {
    if flag(result, "empty") == Some(true) {
        return format!("{}: no renderer or collider bounds", handle(result));
    }
    let size = vec_or(result.get("size"), "?");
    format!(
        "{} bounds: center {} size {} min {} max {}",
        handle(result),
        vec_or(result.get("center"), "?"),
        size,
        vec_or(result.get("min"), "?"),
        vec_or(result.get("max"), "?")
    )
}

/// `spatial overlap` / `spatial nearest`: a header and one line per object, nearest first.
pub fn object_list_lines(result: &Value, key: &str, noun: &str) -> (String, Vec<String>) {
    let items = result
        .get(key)
        .and_then(Value::as_array)
        .cloned()
        .unwrap_or_default();
    let header = format!("{} {noun}", items.len());
    let lines = items
        .iter()
        .map(|item| {
            let name = str_of(item, "gameObject")
                .or_else(|| str_of(item, "name"))
                .unwrap_or("?");
            let mut line = match i64_of(item, "instanceId") {
                Some(id) => format!("  {name} #{id}"),
                None => format!("  {name}"),
            };
            if let Some(collider) = str_of(item, "collider") {
                line.push_str(&format!(" ({collider})"));
            }
            if let Some(d) = item.get("distance") {
                line.push_str(&format!(" distance {}", num(d)));
            }
            if let Some(p) = vec(item.get("position")) {
                line.push_str(&format!(" at {p}"));
            }
            line
        })
        .collect();
    (header, lines)
}

// ---------------------------------------------------------------------------------------------
// Misc payloads
// ---------------------------------------------------------------------------------------------

/// `record status` as one line.
pub fn record_status_line(result: &Value) -> String {
    let state = str_of(result, "state").unwrap_or("unknown");
    if state == "idle" {
        return "Recording idle".to_string();
    }
    let mut line = format!(
        "Recording {state}: {} frames, {} s",
        u64_of(result, "frames"),
        result.get("durationSeconds").map(num).unwrap_or_else(|| "?".into())
    );
    if let Some(path) = str_of(result, "path").filter(|p| !p.is_empty()) {
        line.push_str(&format!(", {path}"));
    }
    let dropped = u64_of(result, "droppedFrames");
    if dropped > 0 {
        line.push_str(&format!(", {dropped} dropped"));
    }
    line
}

#[cfg(test)]
mod tests {
    use super::*;
    use serde_json::json;

    #[test]
    fn numbers_lose_float_noise() {
        assert_eq!(fnum(-1.43051511e-6), "0");
        assert_eq!(fnum(-84.31), "-84.31");
        assert_eq!(fnum(229.488419), "229.488");
        assert_eq!(fnum(2.0), "2");
        assert_eq!(vec(Some(&json!([1, 2.5, 0.0001]))).unwrap(), "(1, 2.5, 0)");
    }

    #[test]
    fn node_line_hides_defaults_and_counts_collapsed_children() {
        let node = json!({
            "instanceId": 49920, "name": "SceneSetup", "active": true, "tag": "Untagged",
            "layer": 0, "layerName": "Default", "childCount": 5, "components": ["Transform"]
        });
        assert_eq!(node_line(&node, 0), "SceneSetup #49920 [5 children]");
        let camera = json!({
            "instanceId": 50498, "name": "Physical Camera", "active": false, "tag": "MainCamera",
            "layer": 5, "layerName": "UI", "childCount": 0,
            "components": ["Transform", "Camera", "AudioListener"]
        });
        assert_eq!(
            node_line(&camera, 1),
            "  Physical Camera #50498 (inactive) tag:MainCamera layer:UI · Camera, AudioListener"
        );
    }

    #[test]
    fn snapshot_tree_indents_children() {
        let result = json!({
            "sceneName": "Demo",
            "stats": {"objectCount": 2, "rootCount": 1},
            "objects": [{
                "instanceId": 1, "name": "Root", "childCount": 1, "components": [],
                "children": [{"instanceId": 2, "name": "Leaf", "childCount": 0, "components": ["Light"]}]
            }]
        });
        let (header, lines) = snapshot_lines(&result, None, 1);
        assert_eq!(header, "Demo: 2 object(s) to depth 1");
        assert_eq!(lines, vec!["  Root #1", "    Leaf #2 · Light"]);
    }

    #[test]
    fn transform_get_shows_local_only_when_different() {
        let result = json!({
            "name": "Crate_A", "instanceId": -5,
            "position": [0, -83.81, 0], "localPosition": [0, -83.81, 0],
            "eulerAngles": [299.7449, 90.00083, 3.4e-6], "localEulerAngles": [10, 0, 0],
            "localScale": [1, 1, 1], "boundsCenter": [0, -83.81, 0], "boundsExtents": [0.5, 0.5, 0.5]
        });
        let (header, lines) = transform_get_lines(&result);
        assert_eq!(
            header,
            "Crate_A #-5: position (0, -83.81, 0) · rotation (299.745, 90.001, 0) · scale (1, 1, 1)"
        );
        assert_eq!(
            lines,
            vec![
                "  local: rotation (10, 0, 0)",
                "  bounds: center (0, -83.81, 0) size (1, 1, 1)"
            ]
        );
    }

    #[test]
    fn hit_and_value_renderings() {
        let hit = json!({
            "hit": true, "gameObject": "underwaterPlane", "instanceId": -492408, "collider": "MeshCollider",
            "distance": 134.309982, "point": [-1.4e-6, -84.31, 0.00002], "normal": [0, 1, 2.2e-20]
        });
        assert_eq!(
            hit_line(&hit),
            "hit underwaterPlane #-492408 (MeshCollider) at (0, -84.31, 0) distance 134.31 normal (0, 1, 0)"
        );
        let reference = json!({"name": "Atlas", "type": "Material", "path": "Assets/A.mat", "guid": "x"});
        assert_eq!(value(&reference), "Atlas (Material: Assets/A.mat)");
        assert_eq!(value(&json!(3.0)), "3");
        assert_eq!(value(&json!("On")), "\"On\"");
    }
}
