#!/usr/bin/env bash
# Build UCP binaries for distribution.
# Usage: ./scripts/build.sh [target]
# Without target, builds for the current platform only.
# Supported targets: x86_64-pc-windows-msvc, x86_64-unknown-linux-gnu, x86_64-apple-darwin, aarch64-apple-darwin

set -euo pipefail

cd "$(dirname "$0")/.."

TARGET="${1:-}"
CLI_DIR="cli"
DIST_DIR="dist"

mkdir -p "$DIST_DIR"

build_target() {
    local target="$1"
    echo "Building for $target..."

    if [ -n "$target" ]; then
        cargo build --release --manifest-path "$CLI_DIR/Cargo.toml" --target "$target"
        local bin_dir="$CLI_DIR/target/$target/release"
    else
        cargo build --release --manifest-path "$CLI_DIR/Cargo.toml"
        local bin_dir="$CLI_DIR/target/release"
    fi

    # Determine output name based on target
    local ext=""
    local platform_arch=""
    case "$target" in
        x86_64-pc-windows-msvc)
            ext=".exe"; platform_arch="win32-x64" ;;
        x86_64-unknown-linux-gnu)
            platform_arch="linux-x64" ;;
        x86_64-apple-darwin)
            platform_arch="darwin-x64" ;;
        aarch64-apple-darwin)
            platform_arch="darwin-arm64" ;;
        "")
            # Auto-detect current platform
            case "$(uname -s)-$(uname -m)" in
                Linux-x86_64)  platform_arch="linux-x64" ;;
                Darwin-x86_64) platform_arch="darwin-x64" ;;
                Darwin-arm64)  platform_arch="darwin-arm64" ;;
                MINGW*|MSYS*)  platform_arch="win32-x64"; ext=".exe" ;;
                *)             platform_arch="unknown" ;;
            esac
            ;;
    esac

    local src="$bin_dir/ucp${ext}"
    local dst="$DIST_DIR/ucp-${platform_arch}${ext}"

    if [ -f "$src" ]; then
        cp "$src" "$dst"
        echo "  -> $dst"
    else
        echo "  ERROR: $src not found"
        exit 1
    fi
}

if [ -n "$TARGET" ]; then
    build_target "$TARGET"
else
    build_target ""
fi

echo "Done."
