#!/usr/bin/env bash
# Fetches ghostty at a pinned commit and builds libghostty-vt as a Windows DLL.
# Output: native/<arch>/ghostty-vt.dll (+ .pdb) and native/include/ghostty/
#
# Requires Zig 0.16.x on PATH. Usage: scripts/build-libghostty.sh [x64|arm64] [commit]
set -euo pipefail

ARCH="${1:-x64}"
# libghostty-vt's C API is unstable; bump deliberately and re-verify the
# bindings in src/Zinc/Ghostty/Native.cs against `ghostty_type_json()`.
COMMIT="${2:-6301810a48aaa3426887a4316668f18833a40138}"

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
SRC="$ROOT/external/ghostty"

case "$ARCH" in
  x64) TARGET=x86_64-windows-msvc ;;
  arm64) TARGET=aarch64-windows-msvc ;;
  *) echo "unknown arch: $ARCH" >&2; exit 1 ;;
esac

if [ ! -d "$SRC/.git" ]; then
  git init -q "$SRC"
  git -C "$SRC" remote add origin https://github.com/ghostty-org/ghostty.git
fi
if [ "$(git -C "$SRC" rev-parse HEAD 2>/dev/null)" != "$COMMIT" ]; then
  git -C "$SRC" fetch --depth 1 origin "$COMMIT"
  git -C "$SRC" checkout -q --detach FETCH_HEAD
fi

(cd "$SRC" && zig build -Demit-lib-vt=true -Doptimize=ReleaseFast -Dtarget="$TARGET")

mkdir -p "$ROOT/native/$ARCH" "$ROOT/native/include"
cp "$SRC/zig-out/bin/ghostty-vt.dll" "$SRC/zig-out/bin/ghostty-vt.pdb" "$ROOT/native/$ARCH/"
rm -rf "$ROOT/native/include/ghostty"
cp -r "$SRC/zig-out/include/ghostty" "$ROOT/native/include/"
echo "libghostty-vt ($COMMIT) -> native/$ARCH/ghostty-vt.dll"

# Ghostty's sprite rasterizer, from the same checkout.
"$ROOT/scripts/build-sprites.sh" "$ARCH"

# The Visual C++ runtime both DLLs import (not part of Windows), shipped beside them.
"$ROOT/scripts/copy-vcruntime.sh" "$ARCH"
