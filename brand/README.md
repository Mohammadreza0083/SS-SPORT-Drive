# brand

SS-SPORT logo files (backlog S-8). Source of truth for the web app, the video watermark, player cards, Open Graph images and emails.

## Originals (supplied by the owner)

These three PNGs are everything that exists. There are no vector originals.

| File | What it is | Size |
|---|---|---|
| `logo_horizontal_color.png` | Horizontal lockup, full colour (red `#CC0000` + maroon `#660000`), transparent background | 704 × 346 |
| `logo_horizontal_mono.png` | Horizontal lockup, grey monochrome, transparent background | 704 × 346 |
| `logo_vertical_color.png` | Vertical lockup, full colour, transparent background | 346 × 704 |

## Derived assets (built from the originals)

Built by [`tools/brand/build_brand_assets.py`](../tools/brand/build_brand_assets.py): each colour layer is upscaled, traced to vector paths and recoloured. Re-run it if the originals change.

| Folder | Contents |
|---|---|
| `svg/` | Traced vectors for `logo_horizontal`, `logo_vertical` and `mark` (the infinity mark alone, wordmark removed), each in three variants |
| `png/` | The same nine assets rendered large (lockups 2048 px wide, mark 1024 px) |
| `icons/` | `favicon.ico` (16/32/48), `icon.png` (512), `apple-touch-icon.png` (180), `icon-192-maskable.png`, `icon-512-maskable.png` |
| `previews/` | `variants.png` (every variant on light and dark), `watermark_1080p.png` / `watermark_360p.png` (proposed watermark, see `.claude/decisions.md` §6) |

**Variants:**

| Variant | Colours | Use |
|---|---|---|
| `color` | red + maroon | Light backgrounds |
| `reversed` | red + white | Dark backgrounds (the site's `#07080B`), where maroon has too little contrast |
| `white` | white only | The burned-in video watermark, overlays on photos |

Use the SVGs wherever possible: the watermark, the 1200 × 630 Open Graph cards and print QR codes need more resolution than the original PNGs have.
