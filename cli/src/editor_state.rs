//! The editor-state appendix: one line at the end of every command that touched the bridge.
//!
//! The bridge attaches an `editor` object to each response it produces on Unity's main thread
//! (mode, active scene and dirtiness, console counts, and a few conditional flags). The client
//! stores the last one it saw here, and `main` prints it once, after the command's own output,
//! so an agent is always told what state it left the editor in without a second request and
//! without any per-command plumbing. `UCP_EDITOR_STATE=0` disables the line and the JSON field.

use serde::ser::{Serialize, SerializeMap, Serializer};
use serde_json::Value;
use std::sync::{Mutex, OnceLock};

static LAST_STATE: OnceLock<Mutex<Option<Value>>> = OnceLock::new();
static MODAL: OnceLock<Mutex<Option<ModalNote>>> = OnceLock::new();

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct ModalNote {
    pub title: String,
    pub buttons: Vec<String>,
}

pub fn enabled() -> bool {
    match std::env::var("UCP_EDITOR_STATE") {
        Ok(value) => {
            let value = value.trim().to_ascii_lowercase();
            !(value == "0" || value == "off" || value == "false" || value == "no")
        }
        Err(_) => true,
    }
}

/// Called by the client for every matched response carrying an `editor` object.
pub fn record(state: Value) {
    if let Ok(mut slot) = LAST_STATE.get_or_init(|| Mutex::new(None)).lock() {
        *slot = Some(state);
    }
}

/// Called when the CLI itself detects a modal dialog blocking the editor's main thread.
pub fn note_modal(title: &str, buttons: &[String]) {
    if let Ok(mut slot) = MODAL.get_or_init(|| Mutex::new(None)).lock() {
        *slot = Some(ModalNote {
            title: title.to_string(),
            buttons: buttons.to_vec(),
        });
    }
}

pub fn current() -> Option<Value> {
    LAST_STATE
        .get()
        .and_then(|m| m.lock().ok().and_then(|s| s.clone()))
}

pub fn modal() -> Option<ModalNote> {
    MODAL
        .get()
        .and_then(|m| m.lock().ok().and_then(|s| s.clone()))
}

/// The line `main` prints after a command, or `None` when nothing was observed.
pub fn summary_line() -> Option<String> {
    if !enabled() {
        return None;
    }
    let state = current();
    let modal = modal();
    format_line(state.as_ref(), modal.as_ref())
}

/// Formats the appendix. Kept pure so it can be tested without a bridge.
pub fn format_line(state: Option<&Value>, modal: Option<&ModalNote>) -> Option<String> {
    let mut parts: Vec<String> = Vec::with_capacity(8);

    if let Some(state) = state {
        let mode = state.get("mode").and_then(Value::as_str).unwrap_or("edit");
        let changing = flag(state, "modeChanging");
        parts.push(match (mode, changing) {
            ("edit", true) => "entering play".to_string(),
            ("edit", false) => "edit mode".to_string(),
            ("paused", _) => "play mode (paused)".to_string(),
            (_, true) => "exiting play".to_string(),
            (_, false) => "play mode".to_string(),
        });

        if let Some(scene) = state.get("scene") {
            let name = scene
                .get("name")
                .and_then(Value::as_str)
                .filter(|n| !n.is_empty())
                .unwrap_or("Untitled");
            let mut qualifiers = Vec::with_capacity(2);
            if flag(scene, "untitled") {
                qualifiers.push("untitled");
            }
            if flag(scene, "dirty") {
                qualifiers.push("dirty");
            }
            let mut text = format!("scene {name}");
            if !qualifiers.is_empty() {
                text.push_str(&format!(" ({})", qualifiers.join(", ")));
            }
            let other_dirty = count(scene, "otherDirty");
            if other_dirty > 0 {
                text.push_str(&format!(
                    " +{other_dirty} more dirty scene{}",
                    plural(other_dirty)
                ));
            }
            parts.push(text);
        }

        if let Some(console) = state.get("console") {
            let errors = count(console, "errors");
            let warnings = count(console, "warnings");
            let new_errors = count(console, "newErrors");
            let new_warnings = count(console, "newWarnings");
            let mut text = if errors == 0 && warnings == 0 {
                "console clean".to_string()
            } else {
                format!(
                    "console {errors} error{}, {warnings} warning{}",
                    plural(errors),
                    plural(warnings)
                )
            };
            if new_errors > 0 || new_warnings > 0 {
                let mut fresh = Vec::with_capacity(2);
                if new_errors > 0 {
                    fresh.push(format!("+{new_errors} error{}", plural(new_errors)));
                }
                if new_warnings > 0 {
                    fresh.push(format!("+{new_warnings} warning{}", plural(new_warnings)));
                }
                text.push_str(&format!(" ({} from this command)", fresh.join(", ")));
            }
            parts.push(text);
        }

        if flag(state, "compileErrors") {
            parts.push("COMPILE ERRORS".to_string());
        }
        if flag(state, "compiling") {
            parts.push("compiling".to_string());
        }
        if flag(state, "importing") {
            parts.push("importing assets".to_string());
        }
        if flag(state, "building") {
            parts.push("building player".to_string());
        }
        if let Some(stage) = state.get("prefabStage").and_then(Value::as_str) {
            parts.push(format!("prefab stage {stage}"));
        }
        match state.get("recording").and_then(Value::as_str) {
            Some("recording") => parts.push("recording".to_string()),
            Some("armed") => parts.push("recording armed".to_string()),
            _ => {}
        }
    }

    if let Some(modal) = modal {
        parts.push(format!(
            "MODAL \"{}\" [{}]",
            modal.title,
            modal.buttons.join(" | ")
        ));
    }

    if parts.is_empty() {
        None
    } else {
        Some(format!("[editor] {}", parts.join(" · ")))
    }
}

/// Serializes a JSON object with the editor state appended as a trailing `editor` member,
/// without cloning the (possibly large) original payload.
pub struct WithEditorState<'a> {
    pub inner: &'a serde_json::Map<String, Value>,
    pub editor: Option<Value>,
    pub modal: Option<ModalNote>,
}

impl Serialize for WithEditorState<'_> {
    fn serialize<S: Serializer>(&self, serializer: S) -> Result<S::Ok, S::Error> {
        let extra = usize::from(self.editor.is_some()) + usize::from(self.modal.is_some());
        let mut map = serializer.serialize_map(Some(self.inner.len() + extra))?;
        for (key, value) in self.inner {
            map.serialize_entry(key, value)?;
        }
        if let Some(editor) = &self.editor {
            map.serialize_entry("editor", editor)?;
        }
        if let Some(modal) = &self.modal {
            map.serialize_entry(
                "modal",
                &serde_json::json!({ "title": modal.title, "buttons": modal.buttons }),
            )?;
        }
        map.end()
    }
}

/// Whether `value` is a top-level command envelope the appendix should be added to.
pub fn is_envelope(value: &Value) -> bool {
    value
        .as_object()
        .is_some_and(|object| object.contains_key("success"))
}

fn flag(value: &Value, key: &str) -> bool {
    value.get(key).and_then(Value::as_bool).unwrap_or(false)
}

fn count(value: &Value, key: &str) -> u64 {
    value.get(key).and_then(Value::as_u64).unwrap_or(0)
}

fn plural(n: u64) -> &'static str {
    if n == 1 { "" } else { "s" }
}

#[cfg(test)]
mod tests {
    use super::*;
    use serde_json::json;

    #[test]
    fn clean_editor_is_one_short_line() {
        let state = json!({
            "mode": "edit",
            "scene": { "name": "SampleScene", "dirty": false },
            "console": { "errors": 0, "warnings": 0 }
        });
        assert_eq!(
            format_line(Some(&state), None).as_deref(),
            Some("[editor] edit mode · scene SampleScene · console clean")
        );
    }

    #[test]
    fn dirty_scene_console_counts_and_fresh_entries_are_spelled_out() {
        let state = json!({
            "mode": "play",
            "scene": { "name": "Arena", "dirty": true, "otherDirty": 2, "loaded": 3 },
            "console": { "errors": 3, "warnings": 1, "newErrors": 2 },
            "compileErrors": true,
            "recording": "armed"
        });
        assert_eq!(
            format_line(Some(&state), None).as_deref(),
            Some(
                "[editor] play mode · scene Arena (dirty) +2 more dirty scenes · \
                 console 3 errors, 1 warning (+2 errors from this command) · COMPILE ERRORS · \
                 recording armed"
            )
        );
    }

    #[test]
    fn transitions_untitled_scenes_prefab_stage_and_modals_are_flagged() {
        let state = json!({
            "mode": "edit",
            "modeChanging": true,
            "scene": { "name": "", "dirty": true, "untitled": true },
            "console": { "errors": 0, "warnings": 1, "newWarnings": 1 },
            "prefabStage": "Assets/Prefabs/Door.prefab",
            "importing": true
        });
        let modal = ModalNote {
            title: "Save Scene?".into(),
            buttons: vec!["Save".into(), "Don't Save".into(), "Cancel".into()],
        };
        assert_eq!(
            format_line(Some(&state), Some(&modal)).as_deref(),
            Some(
                "[editor] entering play · scene Untitled (untitled, dirty) · \
                 console 0 errors, 1 warning (+1 warning from this command) · importing assets · \
                 prefab stage Assets/Prefabs/Door.prefab · MODAL \"Save Scene?\" [Save | Don't Save | Cancel]"
            )
        );
        assert_eq!(
            format_line(None, Some(&modal)).as_deref(),
            Some("[editor] MODAL \"Save Scene?\" [Save | Don't Save | Cancel]")
        );
        assert!(format_line(None, None).is_none());
    }

    #[test]
    fn envelope_serialization_appends_editor_without_touching_the_payload() {
        let envelope = json!({ "success": true, "data": { "instanceId": 42 } });
        let wrapped = WithEditorState {
            inner: envelope.as_object().unwrap(),
            editor: Some(json!({ "mode": "edit" })),
            modal: None,
        };
        let text = serde_json::to_string(&wrapped).unwrap();
        // serde_json orders object keys; the appendix must come last regardless.
        assert_eq!(
            text,
            r#"{"data":{"instanceId":42},"success":true,"editor":{"mode":"edit"}}"#
        );
        let reparsed: Value = serde_json::from_str(&text).unwrap();
        assert_eq!(reparsed["data"]["instanceId"], 42);
        assert_eq!(reparsed["editor"]["mode"], "edit");
        assert!(is_envelope(&envelope));
        assert!(!is_envelope(&json!([1, 2])));
        assert!(!is_envelope(&json!({ "instanceId": 42 })));
    }
}
