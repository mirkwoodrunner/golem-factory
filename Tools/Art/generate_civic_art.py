"""The civic works' site in the town square (docs/endless-design.md §1.2): a placeholder.

    civic_site.png         a stone platform with timber scaffolding and dressed blocks waiting
    civic_site_closed.png  the same, roped off until the Clock Tower stands

The works themselves (the Tramway, the Observatory, the Great Bell, each stage's picture) are
art for the user to commission (endless-design §1.2); until then every work is built on this
site. It shares the tower's 3x3 footprint, 192 pixels wide, its feet on row 197, the footprint's
south edge. The rope is the cart spot's mahogany, never red. Every colour is from
Tools/Art/palette.json's ramps.

Deterministic, no randomness, so verify_art.py can regenerate it byte for byte.

    python Tools/Art/generate_civic_art.py --out-root <dir>
"""
import os

from PIL import Image, ImageDraw

from art_paths import art_dir

OUT_DIR = art_dir()

WIDTH, HEIGHT = 192, 200
FOOT = 197

OUTLINE = (0x43, 0x2A, 0x24, 255)
OUTLINE_DARK = (0x2F, 0x21, 0x1C, 255)
STONE_DARK = (0x51, 0x53, 0x4F, 255)
STONE = (0x6A, 0x62, 0x61, 255)
STONE_MID = (0x7D, 0x73, 0x6F, 255)
STONE_LIGHT = (0x9A, 0x8D, 0x85, 255)
WOOD_DARK = (0x43, 0x2E, 0x25, 255)
WOOD = (0x77, 0x50, 0x39, 255)
WOOD_LIGHT = (0x87, 0x4F, 0x33, 255)
BRASS_DARK = (0x7D, 0x71, 0x53, 255)
BRASS = (0xC4, 0x95, 0x44, 255)
BRASS_LIGHT = (0xE2, 0xC2, 0x7A, 255)
CLOTH = (0xBD, 0xA6, 0x94, 255)
CLOTH_LIGHT = (0xD0, 0xBF, 0xAA, 255)
ROPE = (0x87, 0x4F, 0x33, 255)
ROPE_LIGHT = (0x81, 0x5E, 0x47, 255)
ROPE_DARK = (0x55, 0x3C, 0x30, 255)
TRANSPARENT = (0, 0, 0, 0)

RAMPS = [
    [STONE_DARK, STONE, STONE_MID, STONE_LIGHT],
    [WOOD_DARK, WOOD, WOOD_LIGHT],
    [BRASS_DARK, BRASS, BRASS_LIGHT],
    [CLOTH, CLOTH_LIGHT],
]


def _platform(draw):
    """A low dressed-stone platform filling the footprint, two courses high."""
    draw.rectangle((6, 150, 185, FOOT), fill=STONE, outline=OUTLINE)
    draw.line([(7, 151), (184, 151)], fill=STONE_LIGHT)
    for y in (166, 182):
        draw.line([(7, y), (184, y)], fill=STONE_DARK)
        draw.line([(7, y + 1), (184, y + 1)], fill=STONE_MID)
    for i, y in enumerate((152, 168, 184)):
        for x in range(18 + (12 if i % 2 else 0), 184, 24):
            draw.line([(x, y), (x, y + 13)], fill=STONE_DARK)
    draw.polygon([(6, 150), (24, 120), (167, 120), (185, 150)], fill=STONE_MID, outline=OUTLINE)
    draw.line([(25, 121), (166, 121)], fill=STONE_LIGHT)


def _scaffold(draw):
    """Timber uprights and ledgers round a half-built wall, braced."""
    # The half-built wall, behind the timbers.
    draw.rectangle((44, 70, 148, 138), fill=STONE, outline=OUTLINE)
    for y in range(78, 138, 8):
        draw.line([(45, y), (147, y)], fill=STONE_DARK)
    draw.polygon([(44, 70), (60, 62), (80, 70)], fill=STONE_MID, outline=OUTLINE)
    for x in (34, 78, 118, 158):
        draw.rectangle((x - 2, 30, x + 2, 140), fill=WOOD, outline=OUTLINE)
        draw.line([(x - 1, 31), (x - 1, 139)], fill=WOOD_LIGHT)
    for y in (58, 96):
        draw.rectangle((28, y, 164, y + 4), fill=WOOD, outline=OUTLINE)
        draw.line([(29, y + 1), (163, y + 1)], fill=WOOD_LIGHT)
        for x in range(32, 160, 10):
            draw.line([(x, y + 1), (x, y + 3)], fill=WOOD_DARK)
    draw.line([(36, 96), (76, 60)], fill=WOOD_DARK, width=2)
    draw.line([(120, 60), (156, 96)], fill=WOOD_DARK, width=2)
    # A brass pulley hanging from the top ledger.
    draw.line([(96, 30), (96, 50)], fill=OUTLINE_DARK)
    draw.ellipse((90, 48, 102, 60), fill=BRASS, outline=OUTLINE)
    draw.point((96, 54), fill=BRASS_LIGHT)
    draw.rectangle((28, 26, 164, 30), fill=WOOD, outline=OUTLINE)


def _blocks(draw):
    """Dressed blocks and a covered pile waiting on the platform."""
    for (x, y) in ((20, 132), (36, 132), (28, 120)):
        draw.rectangle((x, y, x + 14, y + 12), fill=STONE_MID, outline=OUTLINE)
        draw.line([(x + 1, y + 1), (x + 13, y + 1)], fill=STONE_LIGHT)
    draw.polygon([(140, 140), (152, 118), (176, 118), (184, 140)], fill=CLOTH, outline=OUTLINE)
    draw.line([(154, 120), (174, 120)], fill=CLOTH_LIGHT)


def _post(draw, x, base, height=16):
    draw.rectangle((x - 2, base - height, x + 2, base), fill=BRASS, outline=OUTLINE)
    draw.line([(x - 1, base - height + 1), (x - 1, base - 1)], fill=BRASS_LIGHT)
    draw.ellipse((x - 4, base - height - 5, x + 4, base - height + 2), fill=BRASS_LIGHT, outline=OUTLINE)
    draw.rectangle((x - 4, base - 2, x + 4, base), fill=BRASS_DARK, outline=OUTLINE)


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
    px = image.load()
    outline = {OUTLINE, OUTLINE_DARK}
    for y in range(HEIGHT):
        xs = [x for x in range(WIDTH) if px[x, y][3] == 255]
        if len(xs) < 3:
            continue
        for start, end, step, by in ((xs[0], xs[-1], 1, 1), (xs[-1], xs[0], -1, -1)):
            x = start
            while x != end and px[x, y] in outline:
                x += step
            if px[x, y] not in outline:
                px[x, y] = _step(px[x, y], by)
    return image


def _site() -> Image.Image:
    image = Image.new("RGBA", (WIDTH, HEIGHT), TRANSPARENT)
    draw = ImageDraw.Draw(image)
    _scaffold(draw)
    _platform(draw)
    _blocks(draw)
    return image


def make_open() -> Image.Image:
    return _edge_light(_site())


def make_closed() -> Image.Image:
    image = _site()
    draw = ImageDraw.Draw(image)
    for x in (10, 96, 181):
        _post(draw, x, FOOT)
    _rope(draw, (10, FOOT - 16), (96, FOOT - 16), 6)
    _rope(draw, (96, FOOT - 16), (181, FOOT - 16), 6)
    return _edge_light(image)


PICTURES = {"civic_site.png": make_open, "civic_site_closed.png": make_closed}


def main() -> None:
    os.makedirs(OUT_DIR, exist_ok=True)
    for name, make in PICTURES.items():
        path = os.path.join(OUT_DIR, name)
        make().save(path)
        print(f"wrote {path} ({WIDTH}x{HEIGHT})")


if __name__ == "__main__":
    main()
