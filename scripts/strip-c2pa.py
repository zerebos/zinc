"""Run from the repository root with the venv that has pillow + resvg-py (.scratch/venv):
    python scripts/strip-c2pa.py
Removes C2PA provenance (Content Credentials) from the repository's brand artwork, losslessly:
PNG: drops caBX chunks (the image chunks are copied byte for byte); SVG: drops <metadata>...</metadata>
and the xmlns:c2pa declaration. Then checks each file renders exactly as before."""
import glob, io, re, struct, sys, zlib
from PIL import Image, ImageChops
import resvg_py

def strip_png(data: bytes) -> bytes:
    assert data[:8] == b"\x89PNG\r\n\x1a\n"
    out, pos = [data[:8]], 8
    while pos < len(data):
        length, kind = struct.unpack(">I4s", data[pos:pos + 8])
        chunk = data[pos:pos + 12 + length]
        if kind != b"caBX":
            out.append(chunk)
        pos += 12 + length
    return b"".join(out)

def strip_svg(text: str) -> str:
    text = re.sub(r"\s*<metadata>.*?</metadata>", "", text, flags=re.S)
    return re.sub(r'\s+xmlns:c2pa="[^"]*"', "", text)

def render_svg(text: str):
    return Image.open(io.BytesIO(bytes(resvg_py.svg_to_bytes(svg_string=text, width=512)))).convert("RGBA")

changed = 0
for path in sorted(glob.glob("assets/brand/*/*")):
    raw = open(path, "rb").read()
    if b"c2pa" not in raw.lower():
        continue
    if path.endswith(".png"):
        new = strip_png(raw)
        same = ImageChops.difference(Image.open(io.BytesIO(raw)).convert("RGBA"), Image.open(io.BytesIO(new)).convert("RGBA")).getbbox() is None
    elif path.endswith(".svg"):
        text = raw.decode("utf-8")
        new_text = strip_svg(text)
        new = new_text.encode("utf-8")
        same = ImageChops.difference(render_svg(text), render_svg(new_text)).getbbox() is None
    else:
        print("skipped (unknown type):", path)
        continue
    left = b"c2pa" in new.lower()
    if not same or left:
        sys.exit(f"STOP {path}: same={same} c2pa_left={left}")
    open(path, "wb").write(new)
    changed += 1
    print(f"{path}: {len(raw)} -> {len(new)} bytes, renders identically")
print(changed, "files stripped")
