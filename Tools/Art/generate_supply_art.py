"""The supply street's roped-off cart spot (docs/supply-design.md §1).

    cart_spot.png    an empty spot: four brass posts, two swags of rope, a plaque with a cart wheel

A spot is where a cart would stand, so it shares a cart's canvas and pivot: 96x120, its feet on
row 117 (the carts' wheels; SpritePivots.Stall). The rope is mahogany, not the tower site's red:
red means the player must act (the root CLAUDE.md's settled rules), and an empty spot is an
offer, not an alarm. Every colour comes from Tools/Art/palette.json's ramps.

Deterministic, no randomness, so verify_art.py can regenerate it byte for byte.

    python Tools/Art/generate_supply_art.py --out-root <dir>
"""
import os

from PIL import Image, ImageDraw

from art_paths import art_dir

OUT_DIR = art_dir()

WIDTH, HEIGHT = 96, 120
FOOT = 117                      # the carts' wheel row: where a spot's front posts stand

OUTLINE = (0x43, 0x2A, 0x24, 255)
BRASS_DARK = (0x7D, 0x71, 0x53, 255)
BRASS = (0xC4, 0x95, 0x44, 255)
BRASS_LIGHT = (0xE2, 0xC2, 0x7A, 255)
ROPE = (0x87, 0x4F, 0x33, 255)
ROPE_LIGHT = (0x81, 0x5E, 0x47, 255)
ROPE_DARK = (0x55, 0x3C, 0x30, 255)
PLAQUE = (0xD0, 0xBF, 0xAA, 255)
PLAQUE_SHADE = (0xBD, 0xA6, 0x94, 255)
TRANSPARENT = (0, 0, 0, 0)

LEFT, RIGHT = 12, 83            # the posts' columns
BACK = 66                       # the back posts' base row: well above the front posts' caps
POST = 18                       # a post's height above its base


def _post(draw, x, base):
    """A short brass bollard: a shaft, a lit edge, a cap and a foot."""
    draw.rectangle((x - 2, base - POST, x + 2, base), fill=BRASS, outline=OUTLINE)
    draw.line([(x - 1, base - POST + 1), (x - 1, base - 1)], fill=BRASS_LIGHT)
    draw.ellipse((x - 4, base - POST - 5, x + 4, base - POST + 2), fill=BRASS_LIGHT, outline=OUTLINE)
    draw.rectangle((x - 4, base - 2, x + 4, base), fill=BRASS_DARK, outline=OUTLINE)


def _rope(draw, a, b, sag):
    """A swag of rope between two post tops: a shallow catenary, two pixels thick."""
    (x0, y0), (x1, y1) = a, b
    steps = max(abs(x1 - x0), abs(y1 - y0), 1)
    points = []
    for i in range(steps + 1):
        t = i / steps
        points.append((round(x0 + (x1 - x0) * t), round(y0 + (y1 - y0) * t + sag * 4 * t * (1 - t))))
    draw.line(points, fill=OUTLINE, width=4)
    draw.line(points, fill=ROPE, width=2)
    draw.line([(px, py + 1) for px, py in points[1::3]], fill=ROPE_DARK)
    draw.line([(px, py - 1) for px, py in points[::4]], fill=ROPE_LIGHT)


def _plaque(draw, x, y):
    """A small plaque hung from the front rope, a cart wheel painted on it: a cart goes here."""
    draw.line([(x - 6, y - 5), (x - 6, y)], fill=OUTLINE)
    draw.line([(x + 6, y - 5), (x + 6, y)], fill=OUTLINE)
    draw.rectangle((x - 10, y, x + 10, y + 13), fill=PLAQUE, outline=OUTLINE)
    draw.line([(x - 9, y + 12), (x + 9, y + 12)], fill=PLAQUE_SHADE)
    cx, cy = x, y + 6
    draw.ellipse((cx - 5, cy - 5, cx + 5, cy + 5), outline=ROPE_DARK)
    draw.line([(cx - 4, cy), (cx + 4, cy)], fill=ROPE_DARK)
    draw.line([(cx, cy - 4), (cx, cy + 4)], fill=ROPE_DARK)
    draw.point((cx, cy), fill=BRASS)


def make_spot() -> Image.Image:
    image = Image.new("RGBA", (WIDTH, HEIGHT), TRANSPARENT)
    draw = ImageDraw.Draw(image)
    back_top, front_top = BACK - POST, FOOT - POST
    # Two ropes, back then front. No side ropes: seen from above they run down the posts' own
    # columns and read as two tall poles.
    _post(draw, LEFT, BACK)
    _post(draw, RIGHT, BACK)
    _rope(draw, (LEFT, back_top), (RIGHT, back_top), 6)
    _post(draw, LEFT, FOOT)
    _post(draw, RIGHT, FOOT)
    _rope(draw, (LEFT, front_top), (RIGHT, front_top), 9)
    _plaque(draw, WIDTH // 2, front_top + 4)
    return image


PICTURES = {"cart_spot.png": make_spot}


def main() -> None:
    os.makedirs(OUT_DIR, exist_ok=True)
    for name, make in PICTURES.items():
        path = os.path.join(OUT_DIR, name)
        make().save(path)
        print(f"wrote {path} ({WIDTH}x{HEIGHT})")


if __name__ == "__main__":
    main()
