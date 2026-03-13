# Introduction

Welcome to the **Unity Control Protocol** documentation. UCP is a cross-platform CLI + Unity Editor bridge that enables programmatic control of the Unity Editor over WebSocket.

## What is UCP?

UCP consists of two components:

- **CLI** — A Rust-based command line tool (`ucp`) that sends commands over WebSocket
- **Bridge** — A Unity Editor package that receives and executes commands via JSON-RPC 2.0

Together, they let you automate virtually any Unity Editor operation from the terminal, scripts, CI/CD pipelines, or AI agents.

## Key Features

- **Full Editor Control** — Play mode, scene management, file operations, screenshots, logs, and more
- **GameObject Inspection** — Read/write component properties, add/remove components, create/delete objects
- **Asset Management** — Search, inspect, and modify project assets including materials and ScriptableObjects
- **Project Settings** — Read/write player, quality, physics, and lighting settings
- **Prefab Workflow** — Check status, apply/revert overrides, create prefabs, unpack instances
- **Build Pipeline** — List targets, manage scenes, control scripting defines, trigger builds
- **Version Control** — Full Plastic SCM / Unity VCS integration
- **Editor Scripting** — Execute custom C# scripts remotely with parameters
- **Cross-Platform** — Works on macOS (x64 + ARM), Linux, and Windows

## How It Works

```
┌─────────┐    WebSocket/JSON-RPC    ┌──────────────┐
│  CLI    │ ◄──────────────────────► │ Unity Bridge │
│  (ucp)  │     Token Auth           │  (Editor)    │
└─────────┘                          └──────────────┘
```

The CLI discovers the running Unity Editor instance via a lock file, establishes a WebSocket connection with token authentication, and sends JSON-RPC 2.0 requests. The bridge processes these requests on Unity's main thread and returns results.

## Quick Links

- [Installation](/docs/installation) — Get UCP set up
- [Quick Start](/docs/quickstart) — Your first automation in 60 seconds
- [Commands Overview](/docs/commands) — Full command reference
