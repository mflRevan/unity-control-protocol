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

    #[error(
        "The Unity editor (pid {pid}) exited while '{method}' was running -- the bridge \
         connection dropped mid-command. This almost always means the editor crashed. Check \
         Unity's Editor.log and its crash dumps before re-running; the next ucp command will \
         relaunch the editor."
    )]
    EditorProcessDied { method: String, pid: u32 },

    #[error(
        "The bridge closed the connection during '{method}' but the Unity editor (pid {pid}) is \
         still running -- the bridge was most likely restarted by a domain reload. Retry the \
         command."
    )]
    BridgeConnectionLost { method: String, pid: u32 },

    #[error("Protocol version mismatch -- CLI: {cli}, Bridge: {bridge}")]
    VersionMismatch { cli: String, bridge: String },

    #[error("{0}")]
    Other(String),
}
