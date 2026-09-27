#!/usr/bin/env bash
# Refreshes assets/themes from iTerm2-Color-Schemes (only its ghostty/ folder is fetched).
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT
git clone --quiet --depth 1 --filter=blob:none --sparse https://github.com/mbadolato/iTerm2-Color-Schemes.git "$tmp/schemes"
git -C "$tmp/schemes" sparse-checkout set ghostty
rev="$(git -C "$tmp/schemes" log -1 --format='%H %cs')"
find "$root/assets/themes" -type f ! -name README.md -delete
cp "$tmp/schemes/ghostty/"* "$root/assets/themes/"
cp "$tmp/schemes/LICENSE" "$root/assets/themes/LICENSE.txt"
sed -i "s/at commit .*\. Each/at commit ${rev}. Each/" "$root/assets/themes/README.md"
echo "$(ls "$root/assets/themes" | wc -l) files, $rev"
