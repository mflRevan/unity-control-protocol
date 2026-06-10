use thiserror::Error;

#[derive(Debug, Error)]
#[allow(dead_code)]
pub enum UcpError {
    #[error("Unity project not found -- run from a Unity project directory or use --project")]
    ProjectNotFound,

    #[error("Bridge not running -- open Unity and wait for the bridge to start")]
    BridgeNotRunning,

    #[error("Connection failed: {0}")]
    ConnectionFailed(String),

    #[error("Bridge returned error ({code}): {message}")]
    BridgeError { code: i32, message: String },

    #[error("Command timed out after {0}s")]
    Timeout(u64),

    #[error(
        "Unity did not respond to '{method}' within {secs}s. The Editor may be blocked by a \
         modal dialog (check the Unity window), compiling, or importing. Re-run with a larger \
         --timeout if the operation is expected to take longer."
    )]
    RequestTimeout { method: String, secs: u64 },

    #[error("Protocol version mismatch -- CLI: {cli}, Bridge: {bridge}")]
    VersionMismatch { cli: String, bridge: String },

    #[error("{0}")]
    Other(String),
}
