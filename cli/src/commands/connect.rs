use crate::output;
use console::style;

use super::Context;

pub async fn run(ctx: &Context) -> anyhow::Result<()> {
    let (_, _, mut client) = super::connect_client(ctx).await?;

    if !ctx.json {
        output::print_info("Connecting to bridge...");
    }

    let mut info = client.handshake().await?;
    client.close().await;

    // A reachable socket is not a usable editor: the bridge answers the handshake off-thread
    // while Unity may still be importing or compiling. Say which it is.
    let responsive = super::main_thread_responsive(&info);
    let tick_age = super::main_thread_tick_age_ms(&info);
    if let Some(obj) = info.as_object_mut() {
        obj.insert("mainThreadResponsive".into(), serde_json::json!(responsive));
    }

    if ctx.json {
        output::print_json(&output::success_json(info));
    } else {
        output::print_success("Connected to Unity bridge");
        if let Some(obj) = info.as_object() {
            let bar = if output::supports_unicode() {
                "│"
            } else {
                "|"
            };
            if let Some(v) = obj.get("unityVersion") {
                eprintln!("  {} Unity {}", style(bar).dim(), v.as_str().unwrap_or("?"));
            }
            if let Some(v) = obj.get("projectName") {
                eprintln!(
                    "  {} Project: {}",
                    style(bar).dim(),
                    v.as_str().unwrap_or("?")
                );
            }
            if let Some(v) = obj.get("protocolVersion") {
                eprintln!(
                    "  {} Protocol: {}",
                    style(bar).dim(),
                    v.as_str().unwrap_or("?")
                );
            }
            match tick_age {
                Some(age) if responsive => eprintln!(
                    "  {} Main thread: responsive (last tick {age} ms ago)",
                    style(bar).dim()
                ),
                Some(age) if age < 0 => eprintln!(
                    "  {} Main thread: not serving yet (first import or compile in progress)",
                    style(bar).dim()
                ),
                Some(age) => eprintln!(
                    "  {} Main thread: blocked for {:.1}s (modal, import, or compile)",
                    style(bar).dim(),
                    age as f64 / 1000.0
                ),
                None => {}
            }
        }
    }

    Ok(())
}
