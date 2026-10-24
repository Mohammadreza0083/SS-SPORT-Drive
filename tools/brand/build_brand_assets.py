"""Builds vector and derived brand assets from the owner's PNG logos.

Usage: pip install vtracer resvg-py pillow; python tools/brand/build_brand_assets.py brand
"""
import io
import re
import sys
from pathlib import Path

import resvg_py
import vtracer
from PIL import Image, ImageFilter

RED = (0xCC, 0x00, 0x00)
MAROON = (0x66, 0x00, 0x00)
WHITE = (0xFF, 0xFF, 0xFF)
DARK_BG = (0x07, 0x08, 0x0B)
SCALE = 6  # upscale before tracing for smooth curves

brand = Path(sys.argv[1])
svg_dir = brand / "svg"
png_dir = brand / "png"
icon_dir = brand / "icons"
for d in (svg_dir, png_dir, icon_dir):
    d.mkdir(exist_ok=True)


def layer_masks(img: Image.Image) -> dict[str, Image.Image]:
    """Splits the logo into a red and a maroon alpha mask, upscaled and smoothed."""
    big = img.resize((img.width * SCALE, img.height * SCALE), Image.LANCZOS)
    px = big.load()
    red = Image.new("L", big.size, 0)
    maroon = Image.new("L", big.size, 0)
    rp, mp = red.load(), maroon.load()
    for y in range(big.height):
        for x in range(big.width):
            r, g, b, a = px[x, y]
            if a < 8:
                continue
            # red ~204, maroon ~102: split at the midpoint of the red channel
            if r >= 153:
                rp[x, y] = a
            else:
                mp[x, y] = a
    blur = ImageFilter.GaussianBlur(SCALE / 2)
    return {
        "red": red.filter(blur).point(lambda v: 255 if v >= 128 else 0),
        "maroon": maroon.filter(blur).point(lambda v: 255 if v >= 128 else 0),
    }


def trace(mask: Image.Image) -> list[str]:
    """Traces a binary mask and returns its SVG path elements without fill."""
    buf = io.BytesIO()
    inv = Image.eval(mask, lambda v: 255 - v).convert("RGB")  # black shape on white
    inv.save(buf, format="PNG")
    svg = vtracer.convert_raw_image_to_svg(
        buf.getvalue(), img_format="png", colormode="binary", mode="spline",
        filter_speckle=8, corner_threshold=60, length_threshold=4.0, splice_threshold=45, path_precision=2,
    )
    paths = re.findall(r"<path[^>]*/>", svg)
    return [re.sub(r'\s*fill="[^"]*"', "", p) for p in paths]


def hexc(c: tuple[int, int, int]) -> str:
    return "#%02X%02X%02X" % c


def build_svg(layers: dict[str, list[str]], size: tuple[int, int], colors: dict[str, tuple[int, int, int]]) -> str:
    w, h = size[0] * SCALE, size[1] * SCALE
    body = []
    for name in ("maroon", "red"):
        body.append(f'  <g fill="{hexc(colors[name])}">\n    ' + "\n    ".join(layers[name]) + "\n  </g>")
    return (
        f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {w} {h}" width="{size[0]}" height="{size[1]}">\n'
        f'  <title>SS-SPORT</title>\n' + "\n".join(body) + "\n</svg>\n"
    )


def render(svg: str, width: int) -> Image.Image:
    data = resvg_py.svg_to_bytes(svg_string=svg, width=width)
    return Image.open(io.BytesIO(bytes(data))).convert("RGBA")


VARIANTS = {
    "color": {"red": RED, "maroon": MAROON},
    "reversed": {"red": RED, "maroon": WHITE},
    "white": {"red": WHITE, "maroon": WHITE},
}

sources = {
    "logo_horizontal": Image.open(brand / "logo_horizontal_color.png").convert("RGBA"),
    "logo_vertical": Image.open(brand / "logo_vertical_color.png").convert("RGBA"),
}

# The mark alone: the horizontal logo without the boxed wordmark in its top-left corner.
mark = sources["logo_horizontal"].copy()
mpx = mark.load()
for y in range(0, 84):
    for x in range(0, 392):
        mpx[x, y] = (0, 0, 0, 0)
mark = mark.crop(mark.getbbox())
sources["mark"] = mark

rendered: dict[str, Image.Image] = {}
for name, img in sources.items():
    masks = layer_masks(img)
    layers = {k: trace(v) for k, v in masks.items()}
    for variant, colors in VARIANTS.items():
        svg = build_svg(layers, img.size, colors)
        (svg_dir / f"{name}_{variant}.svg").write_text(svg, encoding="utf-8")
        width = 1024 if name == "mark" else 2048
        out = render(svg, width)
        out.save(png_dir / f"{name}_{variant}.png", optimize=True)
        rendered[f"{name}_{variant}"] = out


def square(img: Image.Image, size: int, pad: float, bg: tuple[int, int, int] | None) -> Image.Image:
    canvas = Image.new("RGBA", (size, size), (*bg, 255) if bg else (0, 0, 0, 0))
    inner = int(size * (1 - 2 * pad))
    copy = img.copy()
    copy.thumbnail((inner, inner), Image.LANCZOS)
    canvas.alpha_composite(copy, ((size - copy.width) // 2, (size - copy.height) // 2))
    return canvas


mark_color = rendered["mark_color"]
mark_rev = rendered["mark_reversed"]
square(mark_color, 256, 0.04, None).save(icon_dir / "favicon.ico", sizes=[(16, 16), (32, 32), (48, 48)])
square(mark_color, 512, 0.06, None).save(icon_dir / "icon.png", optimize=True)
square(mark_rev, 180, 0.14, DARK_BG).save(icon_dir / "apple-touch-icon.png", optimize=True)
square(mark_rev, 192, 0.20, DARK_BG).save(icon_dir / "icon-192-maskable.png", optimize=True)
square(mark_rev, 512, 0.20, DARK_BG).save(icon_dir / "icon-512-maskable.png", optimize=True)

# Preview sheet: every variant on light and dark.
cells = [k for k in rendered]
cw, ch = 520, 300
sheet = Image.new("RGBA", (cw * 2, ch * len(cells)), (255, 255, 255, 255))
for i, key in enumerate(cells):
    for j, bg in enumerate([(245, 245, 245), DARK_BG]):
        tile = Image.new("RGBA", (cw, ch), (*bg, 255))
        img = rendered[key].copy()
        img.thumbnail((cw - 60, ch - 60), Image.LANCZOS)
        tile.alpha_composite(img, ((cw - img.width) // 2, (ch - img.height) // 2))
        sheet.paste(tile, (j * cw, i * ch))
(brand / "previews").mkdir(exist_ok=True)
sheet.convert("RGB").save(brand / "previews" / "variants.png")
print("done", len(rendered), "variants")
