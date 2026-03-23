# Packages

Browse Unity packages, manage manifest dependencies and scoped registries, and inspect or selectively import `.unitypackage` archives.

Use `ucp packages add|remove` for normal Unity Package Manager installs such as official Unity packages, registry packages, or Git references. For explicit manifest editing and external local `file:` references, prefer `ucp packages dependency ...`.

Unless you opt into `--no-wait`, package-changing commands wait for Unity's package resolve/import/editor settle work before returning so the editor is ready for immediate follow-up automation.

Package-changing commands also preflight the active scene. If the scene is dirty, they fail early with a concise summary instead of falling through to Unity's native save prompt during package refresh or domain reload work.

## Commands

### `ucp packages list`

List installed packages known to Unity.

```bash
ucp packages list
ucp packages list --all
ucp packages list --offline
```

| Flag        | Description                           |
| ----------- | ------------------------------------- |
| `--all`     | Include indirect/transitive packages  |
| `--offline` | Use cached Package Manager data only  |

### `ucp packages search [query]`

Search packages available from Unity's configured registries.

```bash
ucp packages search com.unity.cinemachine
ucp packages search --max 20
ucp packages search com.company.tooling --offline
```

| Flag       | Description                          |
| ---------- | ------------------------------------ |
| `--max <n>`| Maximum results to return            |
| `--offline`| Use cached Package Manager data only |

### `ucp packages info <name>`

Inspect one installed or discoverable package.

```bash
ucp packages info com.unity.timeline
ucp packages info com.company.tooling
```

### `ucp packages add <package>`

Install a package through Unity Package Manager.

```bash
ucp packages add com.unity.cinemachine
ucp packages add com.company.tooling@1.4.0
ucp packages add https://github.com/org/repo.git?path=Packages/com.company.tooling#main
```

| Flag        | Description                                                        |
| ----------- | ------------------------------------------------------------------ |
| `--no-wait` | Return immediately after the add request instead of waiting for settle |

### `ucp packages remove <name>`

Remove a manifest-installed package.

```bash
ucp packages remove com.unity.cinemachine
```

| Flag        | Description                                                           |
| ----------- | --------------------------------------------------------------------- |
| `--no-wait` | Return immediately after the remove request instead of waiting for settle |

### `ucp packages dependencies`

List direct package references from `Packages/manifest.json`.

```bash
ucp packages dependencies
```

### `ucp packages dependency set <name> <reference>`

Set or update one manifest dependency reference directly.

This is the preferred path for explicit local `file:` references and other manifest-managed package sources.

```bash
ucp packages dependency set com.company.tooling 1.4.0
ucp packages dependency set com.company.tooling file:../tooling-package
ucp packages dependency set com.company.tooling https://github.com/org/repo.git?path=Packages/com.company.tooling#main
```

| Flag        | Description                                                            |
| ----------- | ---------------------------------------------------------------------- |
| `--no-wait` | Return immediately after the resolve request instead of waiting for settle |

### `ucp packages dependency remove <name>`

Remove one direct manifest dependency.

```bash
ucp packages dependency remove com.company.tooling
```

| Flag        | Description                                                            |
| ----------- | ---------------------------------------------------------------------- |
| `--no-wait` | Return immediately after the resolve request instead of waiting for settle |

### `ucp packages registries list`

List scoped registries from `Packages/manifest.json`.

```bash
ucp packages registries list
```

### `ucp packages registries add --name <name> --url <url> --scope <scope>...`

Add or update a scoped registry.

```bash
ucp packages registries add --name github --url https://npm.pkg.github.com --scope com.company
ucp packages registries add --name tooling --url https://packages.company.com --scope com.company --scope com.company.shared
```

| Flag        | Description                                                            |
| ----------- | ---------------------------------------------------------------------- |
| `--name`    | Scoped registry display name                                           |
| `--url`     | Registry base URL                                                      |
| `--scope`   | One or more package scopes served by the registry                      |
| `--no-wait` | Return immediately after the resolve request instead of waiting for settle |

Adding a brand-new scoped registry can trigger Unity's own **"Importing a scoped registry"** security/package-manager popup. That dialog is Unity-controlled, not a UCP-specific prompt.

### `ucp packages registries remove --name <name>`

Remove a scoped registry by name.

```bash
ucp packages registries remove --name github
```

| Flag        | Description                                                            |
| ----------- | ---------------------------------------------------------------------- |
| `--name`    | Scoped registry display name                                           |
| `--no-wait` | Return immediately after the resolve request instead of waiting for settle |

### `ucp packages unitypackage inspect <archive>`

Inspect a `.unitypackage` archive and return a machine-friendly hierarchy of the contained assets.

```bash
ucp packages unitypackage inspect Downloads/EnvironmentPack.unitypackage
```

### `ucp packages unitypackage import <archive>`

Selectively import content from a `.unitypackage` archive.

```bash
ucp packages unitypackage import Downloads/EnvironmentPack.unitypackage
ucp packages unitypackage import Downloads/EnvironmentPack.unitypackage --select Assets/Environment/Trees
ucp packages unitypackage import Downloads/EnvironmentPack.unitypackage --select Assets/Environment --unselect Assets/Environment/Demo
ucp packages unitypackage import Downloads/EnvironmentPack.unitypackage --dry-run --select Assets/Environment/Trees
```

| Flag             | Description                                                  |
| ---------------- | ------------------------------------------------------------ |
| `--select <path>`   | Include only matching asset paths or folders                 |
| `--unselect <path>` | Exclude matching asset paths or folders from the selection   |
| `--dry-run`         | Preview the selected import set without writing files        |
| `--no-reimport`     | Skip the final Unity refresh/reimport after extraction       |

Selective `.unitypackage` import is handled by the CLI by parsing the archive directly, because Unity does not expose a non-interactive editor API for selective import.
