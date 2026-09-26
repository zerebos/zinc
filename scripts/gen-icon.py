"""Generates src/Phantom/Assets/Phantom.ico from assets/shade-phosphor-fluent.svg.

The mark is pixel art on a 16-unit grid, which no icon size maps onto exactly
(a block is 0.85 px at 16 px, 1.71 px at 32 px...). A plain vector render smears
it, so each size is composed from three layers:

  1. the tile (rounded square + gradient), rendered by resvg at the exact size;
  2. the blocks, re-drawn snapped to whole pixels (block size rounded, min 1 px),
     with the mark's vertical gradient;
  3. the highlight rim on top, rendered by resvg.

Block positions are parsed from the SVG's mark path, so edits to the design are
picked up by rerunning this. Requires: pip install resvg-py pillow
Usage: python scripts/gen-icon.py [--preview out.png]
"""
import io
import pathlib
import re
import sys

import resvg_py
from PIL import Image, ImageDraw

ROOT = pathlib.Path(__file__).resolve().parent.parent
SVG = ROOT / "assets" / "shade-phosphor-fluent.svg"
ICO = ROOT / "src" / "Phantom" / "Assets" / "Phantom.ico"
SIZES = [256, 128, 96, 64, 48, 40, 32, 24, 20, 16]

src = SVG.read_text(encoding="utf-8")
src = re.sub(r"<metadata>.*?</metadata>", "", src, flags=re.S)  # provenance blob, not drawing

view = [float(v) for v in re.search(r'viewBox="([^"]+)"', src).group(1).split()]
group = re.search(r'<g transform="translate\(([\d.]+) ([\d.]+)\) scale\(([\d.]+)\)">(.*?)</g>', src, re.S)
tx, ty, scale = (float(v) for v in group.groups()[:3])
mark_body = group.group(4)
highlight = re.findall(r"<rect[^>]*url\(#hl\)[^>]*>(?:</rect>)?", src)[0]
stops = re.search(r'<linearGradient id="mark".*?</linearGradient>', src, re.S).group(0)
mark_top, mark_bottom = re.findall(r'stop-color="(#[0-9a-fA-F]{6})"', stops)

# Blocks: "M x y h16v16h-16Z" on a 16-unit grid.
blocks = [(int(x), int(y)) for x, y in re.findall(r"M(\d+) (\d+)h16v16h-16Z", mark_body)]
cols = sorted({x for x, _ in blocks})
rows = sorted({y for _, y in blocks})
x0, y0 = cols[0], rows[0]
grid_w = (cols[-1] - x0) // 16 + 1
grid_h = (rows[-1] - y0) // 16 + 1
cells = {((x - x0) // 16, (y - y0) // 16) for x, y in blocks}


def render(svg: str, size: int) -> Image.Image:
    png = resvg_py.svg_to_bytes(svg_string=svg, width=size, height=size)
    return Image.open(io.BytesIO(bytes(png))).convert("RGBA")


def hex_rgb(h: str):
    return tuple(int(h[i:i + 2], 16) for i in (1, 3, 5))


def vector(size: int) -> Image.Image:
    return render(src, size)


def snapped(size: int) -> Image.Image:
    base = render(src.replace(group.group(0), "").replace(highlight, ""), size)
    rim = render(src.replace(group.group(0), "").replace(re.search(r"<rect[^>]*url\(#tile\)[^>]*>(?:</rect>)?", src).group(0), ""), size)

    # Block size: the design's size in px, rounded, at least one pixel.
    unit = size / view[2]
    block = max(1, round(16 * scale * unit))
    # Center the grid where the design puts it, snapped to whole pixels.
    cx = (tx + scale * (x0 + grid_w * 8)) * unit
    cy = (ty + scale * (y0 + grid_h * 8)) * unit
    left = round(cx - grid_w * block / 2)
    top = round(cy - grid_h * block / 2)

    marks = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    draw = ImageDraw.Draw(marks)
    top_c, bot_c = hex_rgb(mark_top), hex_rgb(mark_bottom)
    height = grid_h * block
    for gx, gy in cells:
        for dy in range(block):
            y = top + gy * block + dy
            t = (y - top + 0.5) / height
            color = tuple(round(a + (b - a) * t) for a, b in zip(top_c, bot_c)) + (255,)
            draw.line([(left + gx * block, y), (left + gx * block + block - 1, y)], fill=color)

    return Image.alpha_composite(Image.alpha_composite(base, marks), rim)


def preview(path: pathlib.Path):
    """Vector vs snapped at small sizes, magnified 8x, on dark and light grounds."""
    small = [16, 20, 24, 32, 48]
    zoom = 6
    cell = 48 * zoom + 16
    sheet = Image.new("RGBA", (cell * len(small), cell * 4), (255, 255, 255, 255))
    for row, (label, fn, ground) in enumerate([
        ("vector/dark", vector, (32, 32, 32)), ("snapped/dark", snapped, (32, 32, 32)),
        ("vector/light", vector, (243, 243, 243)), ("snapped/light", snapped, (243, 243, 243)),
    ]):
        for i, s in enumerate(small):
            tile = Image.new("RGBA", (cell, cell), ground + (255,))
            icon = fn(s).resize((s * zoom, s * zoom), Image.NEAREST)
            tile.alpha_composite(icon, ((cell - s * zoom) // 2, (cell - s * zoom) // 2))
            sheet.alpha_composite(tile, (i * cell, row * cell))
    sheet.save(path)


if __name__ == "__main__":
    if "--preview" in sys.argv:
        preview(pathlib.Path(sys.argv[sys.argv.index("--preview") + 1]))
    images = [snapped(s) for s in SIZES]
    ICO.parent.mkdir(parents=True, exist_ok=True)
    images[0].save(ICO, format="ICO", sizes=[(s, s) for s in SIZES], append_images=images[1:])
    print(f"{ICO.relative_to(ROOT)}: {', '.join(map(str, SIZES))} px; grid {grid_w}x{grid_h}, {len(cells)} blocks")
