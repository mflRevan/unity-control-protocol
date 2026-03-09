use crate::client::BridgeClient;
use crate::discovery;
use crate::output;

use super::Context;

pub async fn run(no_wait: bool, ctx: &Context) -> anyhow::Result<()> {
    let project = discovery::resolve_project(ctx.project.as_deref())?;
    let lock = discovery::read_lock_file(&project)?;
    let old_token = lock.token.clone();
    let mut client = BridgeClient::connect(&lock).await?;
    client.handshake().await?;

    if !ctx.json {
        output::print_info("Triggering recompilation...");
    }

    let result = client.call("compile", serde_json::json!({})).await?;
    client.close().await;

    if no_wait {
        if ctx.json {
            output::print_json(&output::success_json(result));
        } else {
            output::print_success("Compilation triggered (not waiting)");
        }
        return Ok(());
    }

    // Block until compilation + domain reload completes and bridge restarts
    if !ctx.json {
        output::print_info("Waiting for compilation...");
    }

    // Grace period for domain reload to begin
    tokio::time::sleep(std::time::Duration::from_secs(3)).await;

    let max_wait = ctx.timeout.max(60);
    let start = std::time::Instant::now();
    let mut bridge_went_down = false;

    loop {
        if start.elapsed().as_secs() > max_wait {
            if bridge_went_down {
                anyhow::bail!("Bridge did not restart after compilation (waited {max_wait}s)");
            }
            // Bridge never went down — compilation likely had nothing to do
            break;
        }

        match discovery::read_lock_file(&project) {
            Ok(new_lock) => {
                match BridgeClient::connect(&new_lock).await {
                    Ok(mut c) => {
                        if c.handshake().await.is_ok() {
                            c.close().await;
                            if new_lock.token != old_token || bridge_went_down {
                                // Bridge restarted — compilation done
                                break;
                            }
                            if start.elapsed().as_secs() > 8 {
                                // Same token, never went down, waited long enough
                                // Compilation didn't trigger domain reload (no changes)
                                break;
                            }
                        }
                    }
                    Err(_) => {
                        bridge_went_down = true;
                    }
                }
            }
            Err(_) => {
                bridge_went_down = true;
            }
        }

        tokio::time::sleep(std::time::Duration::from_secs(2)).await;
    }

    if ctx.json {
        output::print_json(&output::success_json(serde_json::json!({
            "status": "ok",
            "message": "Compilation completed"
        })));
    } else {
        output::print_success("Compilation completed");
    }

    Ok(())
}
