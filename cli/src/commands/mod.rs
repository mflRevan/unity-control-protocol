pub mod asset;
pub mod build;
pub mod compile;
pub mod connect;
pub mod doctor;
pub mod exec;
pub mod files;
pub mod install;
pub mod logs;
pub mod material;
pub mod object;
pub mod play;
pub mod prefab;
pub mod scene;
pub mod screenshot;
pub mod settings;
pub mod snapshot;
pub mod tests;
pub mod vcs;

use clap::Subcommand;

#[derive(Debug, Clone)]
pub struct Context {
    pub project: Option<String>,
    #[allow(dead_code)]
    pub port: Option<u16>,
    pub json: bool,
    pub timeout: u64,
    #[allow(dead_code)]
    pub verbose: bool,
}

#[derive(Subcommand)]
pub enum Command {
    /// Install the UCP bridge package into a Unity project
    Install {
        /// Path to Unity project (defaults to current directory)
        path: Option<String>,
    },
    /// Uninstall the UCP bridge package
    Uninstall,
    /// Check CLI and bridge health
    Doctor,
    /// Test connection to the bridge
    Connect,
    /// Enter play mode
    Play,
    /// Exit play mode
    Stop,
    /// Toggle pause
    Pause,
    /// Trigger recompilation (blocks until done by default)
    Compile {
        /// Return immediately without waiting for compilation to finish
        #[arg(long)]
        no_wait: bool,
    },
    /// Scene management
    Scene {
        #[command(subcommand)]
        action: scene::SceneAction,
    },
    /// Capture a state snapshot
    Snapshot {
        /// Filter objects by name pattern
        #[arg(long)]
        filter: Option<String>,
        /// Max hierarchy depth
        #[arg(long)]
        depth: Option<u32>,
    },
    /// Capture a screenshot
    Screenshot {
        /// View to capture: game or scene
        #[arg(long, default_value = "game")]
        view: String,
        /// Width in pixels
        #[arg(long, default_value = "1920")]
        width: u32,
        /// Height in pixels
        #[arg(long, default_value = "1080")]
        height: u32,
        /// Output file path (base64 to stdout if omitted)
        #[arg(short, long)]
        output: Option<String>,
    },
    /// Stream console logs
    Logs {
        /// Filter by level: info, warn, error
        #[arg(long)]
        level: Option<String>,
        /// Get last N logs then exit
        #[arg(long)]
        count: Option<u32>,
    },
    /// Run tests
    RunTests {
        /// Test mode: edit or play
        #[arg(long, default_value = "edit")]
        mode: String,
        /// Filter test names
        #[arg(long)]
        filter: Option<String>,
    },
    /// Read a file from the project
    ReadFile {
        /// File path relative to project root
        path: String,
    },
    /// Write a file to the project
    WriteFile {
        /// File path relative to project root
        path: String,
        /// File content (reads from stdin if omitted)
        #[arg(long)]
        content: Option<String>,
        /// Trigger recompilation after write and wait for it to finish
        #[arg(long)]
        compile: bool,
    },
    /// Apply a find/replace patch to a project file
    PatchFile {
        /// File path relative to project root
        path: String,
        /// Text to find
        #[arg(long)]
        find: Option<String>,
        /// Text to replace with
        #[arg(long)]
        replace: Option<String>,
    },
    /// Execute a UCP script (Playwright-like editor automation)
    Exec {
        #[command(subcommand)]
        action: ExecAction,
    },
    /// Version control (Unity VCS / Plastic SCM)
    Vcs {
        #[command(subcommand)]
        action: vcs::VcsAction,
    },
    /// Inspect and modify GameObjects, components, and properties
    Object {
        #[command(subcommand)]
        action: object::ObjectAction,
    },
    /// Search and manage project assets
    Asset {
        #[command(subcommand)]
        action: asset::AssetAction,
    },
    /// Read and modify project settings
    Settings {
        #[command(subcommand)]
        action: settings::SettingsAction,
    },
    /// Inspect and modify materials
    Material {
        #[command(subcommand)]
        action: material::MaterialAction,
    },
    /// Prefab operations (status, apply, revert, unpack, create)
    Prefab {
        #[command(subcommand)]
        action: prefab::PrefabAction,
    },
    /// Build pipeline operations
    Build {
        #[command(subcommand)]
        action: build::BuildAction,
    },
}

#[derive(Subcommand)]
pub enum ExecAction {
    /// List available scripts
    List,
    /// Run a named script
    Run {
        /// Script name
        name: String,
        /// JSON parameters
        #[arg(long)]
        params: Option<String>,
    },
}

pub async fn run(cmd: Command, ctx: Context) -> anyhow::Result<()> {
    match cmd {
        Command::Install { path } => install::run(path.or(ctx.project.clone()), &ctx).await,
        Command::Uninstall => install::uninstall(&ctx).await,
        Command::Doctor => doctor::run(&ctx).await,
        Command::Connect => connect::run(&ctx).await,
        Command::Play => play::run("play", &ctx).await,
        Command::Stop => play::run("stop", &ctx).await,
        Command::Pause => play::run("pause", &ctx).await,
        Command::Compile { no_wait } => compile::run(no_wait, &ctx).await,
        Command::Scene { action } => scene::run(action, &ctx).await,
        Command::Snapshot { filter, depth } => snapshot::run(filter, depth, &ctx).await,
        Command::Screenshot {
            view,
            width,
            height,
            output,
        } => screenshot::run(&view, width, height, output, &ctx).await,
        Command::Logs { level, count } => logs::run(level, count, &ctx).await,
        Command::RunTests { mode, filter } => tests::run(&mode, filter, &ctx).await,
        Command::ReadFile { path } => files::read(&path, &ctx).await,
        Command::WriteFile { path, content, compile } => files::write(&path, content, compile, &ctx).await,
        Command::PatchFile { path, find, replace } => files::patch(&path, find, replace, &ctx).await,
        Command::Exec { action } => match action {
            ExecAction::List => exec::list(&ctx).await,
            ExecAction::Run { name, params } => exec::run(&name, params, &ctx).await,
        },
        Command::Vcs { action } => vcs::run(action, &ctx).await,
        Command::Object { action } => object::run(action, &ctx).await,
        Command::Asset { action } => asset::run(action, &ctx).await,
        Command::Settings { action } => settings::run(action, &ctx).await,
        Command::Material { action } => material::run(action, &ctx).await,
        Command::Prefab { action } => prefab::run(action, &ctx).await,
        Command::Build { action } => build::run(action, &ctx).await,
    }
}
