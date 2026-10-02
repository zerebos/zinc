#!/usr/bin/env bash
# Builds Ghostty's sprite rasterizer (box drawing, blocks, Symbols for Legacy Computing,
# Powerline, Braille: src/font/sprite) as native/<arch>/zinc-sprite.dll, from the same
# pinned checkout as libghostty-vt (run scripts/build-libghostty.sh first; it runs this too).
#
# native-src/sprite/zinc_sprite.zig is the C API; it goes into the checkout as
# src/zinc_sprite.zig so Ghostty's relative imports resolve. One line of Ghostty is
# repointed for the build and restored after: the sprite canvas takes its std.Io from Ghostty's
# app-wide state (global.zig, which refuses to build outside the app), so it uses the wrapper's.
#
# Requires Zig 0.16.x on PATH. Usage: scripts/build-sprites.sh [x64|arm64]
set -euo pipefail

ARCH="${1:-x64}"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
SRC="$ROOT/external/ghostty"
CANVAS="$SRC/src/font/sprite/canvas.zig"

case "$ARCH" in
  x64) TARGET=x86_64-windows-msvc ;;
  arm64) TARGET=aarch64-windows-msvc ;;
  *) echo "unknown arch: $ARCH" >&2; exit 1 ;;
esac

[ -f "$CANVAS" ] || { echo "no Ghostty checkout at $SRC: run scripts/build-libghostty.sh first" >&2; exit 1; }

cp "$ROOT/native-src/sprite/zinc_sprite.zig" "$SRC/src/zinc_sprite.zig"
cp "$CANVAS" "$CANVAS.zinc-orig"
restore() {
  mv -f "$CANVAS.zinc-orig" "$CANVAS"
  rm -f "$SRC/src/zinc_sprite.zig"
}
trap restore EXIT

grep -q 'global\.io()' "$CANVAS" || { echo "canvas.zig no longer calls global.io(): update this script" >&2; exit 1; }
sed -i 's/global\.io()/@import("..\/..\/zinc_sprite.zig").io()/' "$CANVAS"

GHOSTTY_WIN="$(cd "$SRC" && (pwd -W 2>/dev/null || pwd))"
(cd "$ROOT/native-src/sprite" && zig build -Dghostty="$GHOSTTY_WIN" -Doptimize=ReleaseFast -Dtarget="$TARGET")

mkdir -p "$ROOT/native/$ARCH"
cp "$ROOT/native-src/sprite/zig-out/bin/zinc-sprite.dll" "$ROOT/native-src/sprite/zig-out/bin/zinc-sprite.pdb" "$ROOT/native/$ARCH/"
echo "zinc-sprite ($(git -C "$SRC" rev-parse --short HEAD)) -> native/$ARCH/zinc-sprite.dll"
