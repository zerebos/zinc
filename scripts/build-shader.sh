#!/usr/bin/env bash
# Builds zinc-shader.dll (custom shaders: GLSL -> SPIR-V -> HLSL) as native/<arch>/zinc-shader.dll:
# native-src/shader/zinc_shader.zig over glslang (Ghostty's own package, from the pinned checkout: run
# scripts/build-libghostty.sh first; it runs this too) and SPIRV-Cross with its HLSL backend (built here from
# the source Ghostty pins: Ghostty's package leaves that backend out).
#
# Both are C++, linked with Microsoft's *static* C and C++ runtimes (as Ghostty's packages compile them), so the
# DLL imports only KERNEL32. Zig doesn't search the folders of those static libraries: they're found here with
# vswhere (Visual Studio or its Build Tools with the C++ workload, and a Windows SDK).
#
# Requires Zig 0.16.x on PATH. Usage: scripts/build-shader.sh [x64|arm64]
set -euo pipefail

ARCH="${1:-x64}"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
SRC="$ROOT/external/ghostty"

case "$ARCH" in
  x64) TARGET=x86_64-windows-msvc ;;
  arm64) TARGET=aarch64-windows-msvc ;;
  *) echo "unknown arch: $ARCH" >&2; exit 1 ;;
esac

[ -d "$SRC/pkg/glslang" ] || { echo "no Ghostty checkout at $SRC: run scripts/build-libghostty.sh first" >&2; exit 1; }

VSWHERE="/c/Program Files (x86)/Microsoft Visual Studio/Installer/vswhere.exe"
[ -x "$VSWHERE" ] || { echo "vswhere.exe not found: install Visual Studio or its Build Tools (C++ workload)" >&2; exit 1; }
VS="$("$VSWHERE" -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath | tr -d '\r')"
[ -n "$VS" ] || { echo "no Visual Studio with the C++ tools" >&2; exit 1; }
MSVC_LIB="$(ls -d "$(cygpath -u "$VS")"/VC/Tools/MSVC/*/lib/"$ARCH" 2>/dev/null | sort -V | tail -1)"
UCRT_LIB="$(ls -d "/c/Program Files (x86)/Windows Kits/10/Lib/"*/ucrt/"$ARCH" 2>/dev/null | sort -V | tail -1)"
[ -f "$MSVC_LIB/libcpmt.lib" ] || { echo "no static MSVC runtime (libcpmt.lib) for $ARCH in $VS" >&2; exit 1; }
[ -f "$UCRT_LIB/libucrt.lib" ] || { echo "no static UCRT (libucrt.lib) for $ARCH in the Windows SDK" >&2; exit 1; }

(cd "$ROOT/native-src/shader" && zig build -Doptimize=ReleaseFast -Dtarget="$TARGET" \
  -Ducrt-lib="$(cygpath -w "$UCRT_LIB")" -Dmsvc-lib="$(cygpath -w "$MSVC_LIB")")

mkdir -p "$ROOT/native/$ARCH"
cp "$ROOT/native-src/shader/zig-out/bin/zinc-shader.dll" "$ROOT/native-src/shader/zig-out/bin/zinc-shader.pdb" "$ROOT/native/$ARCH/"
echo "zinc-shader ($(git -C "$SRC" rev-parse --short HEAD)) -> native/$ARCH/zinc-shader.dll"
