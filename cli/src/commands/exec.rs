use crate::output;

use super::Context;

pub struct RecordingOptions {
    pub path: String,
    pub view: String,
    pub max_edge: u32,
    pub fps: u32,
    pub bitrate_kbps: u32,
    pub overwrite: bool,
    pub lead: f64,
    pub tail: f64,
}

pub async fn list(ctx: &Context) -> anyhow::Result<()> {
    let (_, _, mut client) = super::connect_client(ctx).await?;

    let result = client.call("exec/list", serde_json::json!({})).await?;
    client.close().await;

    if ctx.json {
        output::print_json(&output::success_json(result));
    } else if let Some(scripts) = result.get("scripts").and_then(|v| v.as_array()) {
        if scripts.is_empty() {
            output::print_warn("No scripts found. Implement IUCPScript in your Editor scripts.");
        } else {
            output::print_info(&format!("Found {} script(s):", scripts.len()));
            for s in scripts {
                let name = s.get("name").and_then(|v| v.as_str()).unwrap_or("?");
                let desc = s.get("description").and_then(|v| v.as_str()).unwrap_or("");
                eprintln!("  {name} -- {desc}");
            }
        }
    }

    Ok(())
}

pub async fn run(
    name: &str,
    params: Option<String>,
    recording: Option<RecordingOptions>,
    ctx: &Context,
) -> anyhow::Result<()> {
    let (_, _, mut client) = super::connect_client(ctx).await?;

    if !ctx.json {
        output::print_info(&format!("Running script: {name}"));
    }

    let script_params = match &params {
        Some(p) => serde_json::from_str(p).unwrap_or(serde_json::json!({})),
        None => serde_json::json!({}),
    };

    if let Some(options) = recording.as_ref() {
        client
            .call(
                "record/start",
                serde_json::json!({
                    "path": options.path, "view": options.view, "maxEdge": options.max_edge,
                    "fps": options.fps, "format": "auto", "bitrateKbps": options.bitrate_kbps,
                    "overwrite": options.overwrite, "maxDuration": 60.0,
                }),
            )
            .await?;
        tokio::time::sleep(std::time::Duration::from_secs_f64(options.lead)).await;
    }

    let result = client
        .call(
            "exec/run",
            serde_json::json!({ "name": name, "params": script_params }),
        )
        .await;

    let finalized_recording = if let Some(options) = recording.as_ref() {
        tokio::time::sleep(std::time::Duration::from_secs_f64(options.tail)).await;
        let stop_result = client.call("record/stop", serde_json::json!({})).await;
        if result.is_ok() {
            Some(stop_result?)
        } else {
            None
        }
    } else {
        None
    };
    client.close().await;
    let mut result = result?;

    if let (Some(object), Some(recording)) = (result.as_object_mut(), finalized_recording.as_ref())
    {
        object.insert("recording".to_string(), recording.clone());
    }

    if ctx.json {
        output::print_json(&output::success_json(result));
    } else {
        output::print_success(&format!("Script '{name}' completed"));
        if let Some(script_result) = result.get("result") {
            output::print_json(script_result);
        }
        if let Some(path) = finalized_recording
            .as_ref()
            .and_then(|value| value.get("path"))
            .and_then(|value| value.as_str())
        {
            output::print_success(&format!("Recording finalized: {path}"));
        }
    }

    Ok(())
}
