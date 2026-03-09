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

    #[error("Protocol version mismatch -- CLI: {cli}, Bridge: {bridge}")]
    VersionMismatch { cli: String, bridge: String },

    #[error("{0}")]
    Other(String),
}
