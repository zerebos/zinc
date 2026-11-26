#!/bin/zsh
# Builds the native libraries for macOS into native/macos-arm64: libghostty-vt and the PTY helper. Needs Zig 0.17.0
# on PATH and git. It follows libghostty.ps1 (keep its pinned tag and commit the same as that script's), without the
# sprite and shader libraries, which aren't built for macOS yet.
set -euo pipefail
root=${0:A:h:h:h}
src=$root/external/ghostty
tag=vt-1.0.1
commit=108cacb17ed8990840cd14abfce43d8ed8503ca1

[[ -d $src/.git ]] || { git init -q $src; git -C $src remote add origin https://github.com/ghostty-org/ghostty.git; }
if [[ "$(git -C $src rev-parse HEAD 2>/dev/null)" != $commit ]]; then
  git -C $src fetch -q --depth 1 origin $commit
  git -C $src checkout -q --force --detach FETCH_HEAD
fi
git -C $src fetch -q --depth 1 origin "+refs/tags/${tag}:refs/tags/${tag}"
[[ "$(git -C $src rev-parse "refs/tags/${tag}^{commit}")" == $commit ]] || { echo "$tag doesn't point at $commit"; exit 1; }
git -C $src checkout -q -- .
for patch in $root/patches/ghostty/*.patch(N); do git -C $src apply $patch; echo "applied ${patch:t}"; done

(cd $src && zig build -Demit-lib-vt=true -Doptimize=ReleaseFast)
out=$root/native/macos-arm64
mkdir -p $out
cp $src/zig-out/lib/libghostty-vt.dylib $out/libghostty-vt.dylib
zig cc -O2 -shared -o $out/libzinc-pty.dylib $root/native-src/pty/zinc_pty.c
echo "native libraries -> $out"
