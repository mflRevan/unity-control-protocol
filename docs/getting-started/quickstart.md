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

# Capture scene hierarchy
ucp snapshot

# Enter play mode
ucp play

# Take a screenshot
ucp screenshot -o capture.png

# Read a file
ucp read-file Assets/Scripts/Player.cs

# Write a file
ucp write-file Assets/Scripts/Config.cs --content "public class Config {}"

# Stream console logs
ucp logs --level error

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

- [Commands Overview](/docs/commands) — Full reference for every command
- [Connection](/docs/commands/connection) — How connection and discovery works
- [Object & Components](/docs/commands/objects) — Inspect and modify GameObjects
