"""Generates src/Phantom/Assets/Phantom.ico from assets/phosphor-icon.svg.

The mark is a dot-matrix of lit cells on a 10-unit pitch (in a 240-unit tile), which
no icon size maps onto exactly (a cell is 0.67 px at 16 px, 1.33 px at 32 px...). A
plain vector render smears it, so below 256 px each size is composed from layers:

  1. the screen (tile gradient + dot grid), rendered by resvg at the exact size;
  2. the lit cells re-drawn snapped to whole pixels (pitch rounded, min 1 px; the gap
     between cells kept from 4 px up), with the mark's vertical gradient, and from
     3 px up the faint glow around each cell;
  3. the glass highlight and rim on top, rendered by resvg.

At 16 and 20 px the 13-column mark can't keep its proportions (it needs 13 px), so
it's drawn at 1 px per cell and fills more of the tile than the design does.

Cell positions and colors are parsed from the SVG, so edits to the design are picked
up by rerunning this. Requires: pip install resvg-py pillow
Usage: python scripts/gen-icon.py [--preview out.png]
"""
import io
import pathlib
import re
import sys

import resvg_py
from PIL import Image, ImageDraw

ROOT = pathlib.Path(__file__).resolve().parent.parent
SVG = ROOT / "assets" / "phosphor-icon.svg"
ICO = ROOT / "src" / "Phantom" / "Assets" / "Phantom.ico"
SIZES = [256, 128, 96, 64, 48, 40, 32, 24, 20, 16]

src = SVG.read_text(encoding="utf-8")
src = re.sub(r"<metadata>.*?</metadata>", "", src, flags=re.S)  # provenance blob, not drawing

view = float(re.search(r'viewBox="0 0 ([\d.]+) ', src).group(1))
lit_group = re.search(r'<g fill="url\(#lit\)">.*?</g>', src, re.S).group(0)
glow_group = re.search(r'<g fill="#3DF2A0" fill-opacity="([\d.]+)"><rect.*?</g>', src, re.S)
glow_opacity = float(glow_group.group(1))
highlight = re.search(r'<rect[^>]*fill="url\(#hl\)"[^>]*/>', src).group(0)
rim = re.search(r'<rect[^>]*stroke="url\(#edge\)"[^>]*/>', src).group(0)
lit_gradient = re.search(r'<linearGradient id="lit".*?</linearGradient>', src, re.S).group(0)
lit_top, lit_bottom = re.findall(r'stop-color="(#[0-9a-fA-F]{6})"', lit_gradient)
glow_color = "#3DF2A0"

# Lit cells: 8.6-unit squares on a 10-unit pitch; the cell index is the square's tens digit.
lit = [(float(x), float(y), float(w)) for x, y, w in
       re.findall(r'<rect x="([\d.]+)" y="([\d.]+)" width="([\d.]+)"', lit_group)]
PITCH = 10.0
cell_size = lit[0][2]
cells = {(int(x // PITCH), int(y // PITCH)) for x, y, _ in lit}
cols = sorted({c for c, _ in cells})
rows = sorted({r for _, r in cells})
c0, r0 = cols[0], rows[0]
grid_w = cols[-1] - c0 + 1
grid_h = rows[-1] - r0 + 1
offset = min(x for x, _, _ in lit) - c0 * PITCH  # where a square sits inside its cell
# The mark's center in design units (first square's left edge to last square's right edge).
center_x = c0 * PITCH + offset + ((grid_w - 1) * PITCH + cell_size) / 2
center_y = r0 * PITCH + offset + ((grid_h - 1) * PITCH + cell_size) / 2


def render(svg: str, size: int) -> Image.Image:
    png = resvg_py.svg_to_bytes(svg_string=svg, width=size, height=size)
    return Image.open(io.BytesIO(bytes(png))).convert("RGBA")


def hex_rgb(h: str):
    return tuple(int(h[i:i + 2], 16) for i in (1, 3, 5))


def vector(size: int) -> Image.Image:
    return render(src, size)


def snapped(size: int) -> Image.Image:
    screen = render(src.replace(lit_group, "").replace(glow_group.group(0), "")
                    .replace(highlight, "").replace(rim, ""), size)
    # Only the highlight and rim: drop the screen fill, dot grid, glow and cells.
    glass = re.sub(r'<rect width="[\d.]+" height="[\d.]+" fill="url\(#screen\)"/>', "", src)
    glass = re.sub(r'<g fill="#3DF2A0" fill-opacity="[\d.]+">.*?</g>', "", glass, flags=re.S)
    glass = glass.replace(lit_group, "")
    top_layer = render(glass, size)

    unit = size / view
    pitch = max(1, round(PITCH * unit))
    gap = round(pitch * (PITCH - cell_size) / PITCH) if pitch >= 4 else 0
    square = pitch - gap
    width, height = grid_w * pitch - gap, grid_h * pitch - gap
    left = round(center_x * unit - width / 2)
    top = round(center_y * unit - height / 2)

    layer = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    if pitch >= 3:
        # The glow: a faint square around each cell, overlapping its neighbours'.
        halo = round(pitch * 0.25)
        alpha = round(255 * glow_opacity)
        g = hex_rgb(glow_color)
        for gx, gy in cells:
            spot = Image.new("RGBA", (size, size), (0, 0, 0, 0))
            x = left + (gx - c0) * pitch - halo
            y = top + (gy - r0) * pitch - halo
            ImageDraw.Draw(spot).rectangle([x, y, x + square + 2 * halo - 1, y + square + 2 * halo - 1],
                                           fill=g + (alpha,))
            layer = Image.alpha_composite(layer, spot)

    draw = ImageDraw.Draw(layer)
    top_c, bot_c = hex_rgb(lit_top), hex_rgb(lit_bottom)
    for gx, gy in cells:
        x = left + (gx - c0) * pitch
        for dy in range(square):
            y = top + (gy - r0) * pitch + dy
            t = (y - top + 0.5) / height
            color = tuple(round(a + (b - a) * t) for a, b in zip(top_c, bot_c)) + (255,)
            draw.line([(x, y), (x + square - 1, y)], fill=color)

    return Image.alpha_composite(Image.alpha_composite(screen, layer), top_layer)


def icon(size: int) -> Image.Image:
    # At 256 px a cell is ~11 px: the vector render is crisp enough and keeps the rounding.
    return vector(size) if size >= 256 else snapped(size)


def preview(path: pathlib.Path):
    """Vector vs snapped at small sizes, magnified, on dark and light grounds."""
    small = [16, 20, 24, 32, 40, 48, 64]
    zoom = 5
    cell = 64 * zoom + 16
    sheet = Image.new("RGBA", (cell * len(small), cell * 4), (255, 255, 255, 255))
    for row, (fn, ground) in enumerate([
        (vector, (32, 32, 32)), (snapped, (32, 32, 32)),
        (vector, (243, 243, 243)), (snapped, (243, 243, 243)),
    ]):
        for i, s in enumerate(small):
            tile = Image.new("RGBA", (cell, cell), ground + (255,))
            img = fn(s).resize((s * zoom, s * zoom), Image.NEAREST)
            tile.alpha_composite(img, ((cell - s * zoom) // 2, (cell - s * zoom) // 2))
            sheet.alpha_composite(tile, (i * cell, row * cell))
    sheet.save(path)


if __name__ == "__main__":
    if "--preview" in sys.argv:
        preview(pathlib.Path(sys.argv[sys.argv.index("--preview") + 1]))
    images = [icon(s) for s in SIZES]
    ICO.parent.mkdir(parents=True, exist_ok=True)
    images[0].save(ICO, format="ICO", sizes=[(s, s) for s in SIZES], append_images=images[1:])
    print(f"{ICO.relative_to(ROOT)}: {', '.join(map(str, SIZES))} px; grid {grid_w}x{grid_h}, {len(cells)} cells")
