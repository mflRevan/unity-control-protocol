# @mflrevan/ucp

Version `0.4.4` of the Unity Control Protocol CLI.

This package installs the `ucp` command, downloads the matching published binary for your platform during `postinstall`, and ships the matching Unity bridge payload inside the npm package.

## Install

```bash
npm install -g @mflrevan/ucp
```

### pnpm

```bash
pnpm add -g @mflrevan/ucp
pnpm approve-builds
```

## Supported platforms

- Windows x64
- macOS x64
- macOS arm64
- Linux x64

## Usage

```bash
cd /path/to/MyUnityProject
ucp doctor
ucp connect
ucp scene snapshot
ucp object get-fields --id 46894 --component Transform
ucp asset search -t Material
ucp build targets
```

## Bridge install

```bash
ucp install
```

Default `ucp install` writes a tracked git dependency to `Packages/manifest.json` pinned to the CLI version. It does not write a local `file:` dependency unless you explicitly choose a local embedded mode.

## Release asset source

The installer downloads from:

`https://github.com/mflRevan/unity-control-protocol/releases/download/v<version>/...`

That means npm publish depends on the matching GitHub release artifacts existing for the same tag.

## Links

- Repository: https://github.com/mflRevan/unity-control-protocol
- Releases: https://github.com/mflRevan/unity-control-protocol/releases

## License

MIT
