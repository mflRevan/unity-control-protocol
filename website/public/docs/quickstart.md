# Quick Start

Get from zero to full Unity automation in under a minute.

## Step 1: Install the CLI

```bash
npm install -g @mflrevan/ucp
```

## Step 2: Install the Bridge

Open your Unity project directory and install the bridge:

```bash
cd /path/to/MyUnityProject
ucp install
```

Open the project in Unity Editor. The bridge starts automatically.

## Step 3: Connect & Automate

```bash
# Verify connection
ucp connect

# Capture root hierarchy overview
ucp scene snapshot

# Enter play mode
ucp play

# Take a screenshot
ucp screenshot -o capture.png

# Focus the Scene view for spatial iteration
ucp scene focus --id 46894 --axis 1 0 0

# Edit scripts directly in the project workspace, then import them
ucp compile

# Read recent logs
ucp logs --count 10

# Or get a quick curated overview
ucp logs status

# Or follow new error logs live
ucp logs --follow --level error

# Run tests
ucp run-tests --mode edit

# Inspect a GameObject
ucp object get-fields --id 46894 --component Transform

# Search for assets
ucp asset search -t Material

# Check build targets
ucp build targets
```

## What's Next?

- [CLI Overview](https://unityctl.dev/docs/overview.md) - Global flags, lifecycle, and recommended workflows
- [Project Setup & Bridge](https://unityctl.dev/docs/overview/project-setup.md) - Install, connect, and diagnose bridge state
- [Objects & Components](https://unityctl.dev/docs/authoring/objects.md) - Inspect and modify GameObjects
