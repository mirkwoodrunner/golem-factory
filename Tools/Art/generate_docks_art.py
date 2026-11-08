"""The Docks (docs/region-design.md): placeholders until the user commissions art.

    docks_gate.png         the gate at the street's end: two brass-capped posts, an arch and an
                           anchor plaque
    docks_gate_closed.png  the same, roped across until the first Zeppelin
    docks_water.png        a 64x64 tile of harbour water under the quay

The gate stands on the lane's middle column, its feet on row 157 of a 128x160 canvas (the cell's
south edge, as SpritePivots' standing art). The rope is the cart spot's mahogany, never red. Every
colour is from Tools/Art/palette.json's ramps.

Deterministic, no randomness, so verify_art.py can regenerate it byte for byte.

    python Tools/Art/generate_docks_art.py --out-root <dir>
"""
import os

from PIL import Image, ImageDraw

from art_paths import art_dir

OUT_DIR = art_dir()

GATE_W, GATE_H = 128, 160
FOOT = 157

OUTLINE = (0x43, 0x2A, 0x24, 255)
OUTLINE_DARK = (0x2F, 0x21, 0x1C, 255)
WOOD_DARK = (0x43, 0x2E, 0x25, 255)
WOOD = (0x77, 0x50, 0x39, 255)
WOOD_LIGHT = (0x87, 0x4F, 0x33, 255)
BRASS_DARK = (0x7D, 0x71, 0x53, 255)
BRASS = (0xC4, 0x95, 0x44, 255)
BRASS_LIGHT = (0xE2, 0xC2, 0x7A, 255)
PLAQUE = (0xD0, 0xBF, 0xAA, 255)
PLAQUE_SHADE = (0xBD, 0xA6, 0x94, 255)
ROPE = (0x87, 0x4F, 0x33, 255)
ROPE_LIGHT = (0x81, 0x5E, 0x47, 255)
ROPE_DARK = (0x55, 0x3C, 0x30, 255)
WATER_DEEP = (0x29, 0x6B, 0x6A, 255)
WATER = (0x32, 0x6F, 0x69, 255)
WATER_LIGHT = (0x5E, 0xD6, 0xC8, 255)
TRANSPARENT = (0, 0, 0, 0)

RAMPS = [
    [WOOD_DARK, WOOD, WOOD_LIGHT],
    [BRASS_DARK, BRASS, BRASS_LIGHT],
    [PLAQUE_SHADE, PLAQUE],
]

LEFT, RIGHT = 14, 113


def _post(draw, x):
    """A tall timber post with a brass cap and foot."""
    draw.rectangle((x - 5, 40, x + 5, FOOT), fill=WOOD, outline=OUTLINE)
    draw.line([(x - 3, 42), (x - 3, FOOT - 2)], fill=WOOD_LIGHT)
    draw.line([(x + 3, 42), (x + 3, FOOT - 2)], fill=WOOD_DARK)
    draw.ellipse((x - 8, 30, x + 8, 44), fill=BRASS, outline=OUTLINE)
    draw.point((x - 3, 34), fill=BRASS_LIGHT)
    draw.rectangle((x - 8, FOOT - 6, x + 8, FOOT), fill=BRASS_DARK, outline=OUTLINE)


def _arch(draw):
    """A timber beam between the posts, with an anchor plaque hung under it."""
    draw.rectangle((LEFT - 4, 46, RIGHT + 4, 56), fill=WOOD, outline=OUTLINE)
    draw.line([(LEFT - 3, 47), (RIGHT + 3, 47)], fill=WOOD_LIGHT)
    cx = (LEFT + RIGHT) // 2
    draw.line([(cx - 14, 56), (cx - 14, 62)], fill=OUTLINE)
    draw.line([(cx + 14, 56), (cx + 14, 62)], fill=OUTLINE)
    draw.rectangle((cx - 22, 62, cx + 22, 90), fill=PLAQUE, outline=OUTLINE)
    draw.line([(cx - 21, 89), (cx + 21, 89)], fill=PLAQUE_SHADE)
    # The anchor.
    draw.ellipse((cx - 3, 66, cx + 3, 72), outline=OUTLINE_DARK)
    draw.line([(cx, 72), (cx, 85)], fill=OUTLINE_DARK, width=2)
    draw.line([(cx - 7, 75), (cx + 7, 75)], fill=OUTLINE_DARK)
    draw.arc((cx - 11, 76, cx + 11, 90), 20, 160, fill=OUTLINE_DARK, width=2)


def _rope(draw, a, b, sag):
    (x0, y0), (x1, y1) = a, b
    steps = max(abs(x1 - x0), abs(y1 - y0), 1)
    points = [(round(x0 + (x1 - x0) * i / steps), round(y0 + (y1 - y0) * i / steps + sag * 4 * (i / steps) * (1 - i / steps)))
              for i in range(steps + 1)]
    draw.line(points, fill=OUTLINE, width=4)
    draw.line(points, fill=ROPE, width=2)
    draw.line([(px, py + 1) for px, py in points[1::3]], fill=ROPE_DARK)
    draw.line([(px, py - 1) for px, py in points[::4]], fill=ROPE_LIGHT)


def _step(colour, by):
    for ramp in RAMPS:
        if colour in ramp:
            return ramp[max(0, min(len(ramp) - 1, ramp.index(colour) + by))]
    return colour


def _edge_light(image):
    """Lights the first pixel inside the outline on each row's left and shades the last on its right."""
    w, h = image.size
    px = image.load()
    outline = {OUTLINE, OUTLINE_DARK}
    for y in range(h):
        xs = [x for x in range(w) if px[x, y][3] == 255]
        if len(xs) < 3:
            continue
        for start, end, step, by in ((xs[0], xs[-1], 1, 1), (xs[-1], xs[0], -1, -1)):
            x = start
            while x != end and px[x, y] in outline:
                x += step
            if px[x, y] not in outline:
                px[x, y] = _step(px[x, y], by)
    return image


def _gate() -> Image.Image:
    image = Image.new("RGBA", (GATE_W, GATE_H), TRANSPARENT)
    draw = ImageDraw.Draw(image)
    _post(draw, LEFT)
    _post(draw, RIGHT)
    _arch(draw)
    return image


def make_gate() -> Image.Image:
    return _edge_light(_gate())


def make_gate_closed() -> Image.Image:
    image = _gate()
    draw = ImageDraw.Draw(image)
    _rope(draw, (LEFT + 5, 112), (RIGHT - 5, 112), 10)
    _rope(draw, (LEFT + 5, 134), (RIGHT - 5, 134), 8)
    return _edge_light(image)


def make_water() -> Image.Image:
    """Harbour water: a deep teal with two rows of lighter ripples, seamless both ways."""
    image = Image.new("RGBA", (64, 64), WATER)
    draw = ImageDraw.Draw(image)
    for y in range(0, 64, 4):
        draw.line([(0, y), (63, y)], fill=WATER_DEEP if (y // 4) % 2 else WATER)
    for (x, y) in ((6, 10), (30, 18), (50, 6), (14, 38), (40, 46), (58, 30), (22, 56)):
        draw.line([(x, y), (x + 5, y)], fill=WATER_LIGHT)
        draw.line([(x + 2, y + 1), (x + 7, y + 1)], fill=WATER_DEEP)
    return image


PICTURES = {"docks_gate.png": make_gate, "docks_gate_closed.png": make_gate_closed, "docks_water.png": make_water}


def main() -> None:
    os.makedirs(OUT_DIR, exist_ok=True)
    for name, make in PICTURES.items():
        path = os.path.join(OUT_DIR, name)
        image = make()
        image.save(path)
        print(f"wrote {path} ({image.width}x{image.height})")


if __name__ == "__main__":
    main()
