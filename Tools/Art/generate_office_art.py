"""The City Office in the town square (docs/contracts-design.md §1).

    city_office.png         the office: a small stone hall, a mahogany door, a notice board
    city_office_closed.png  the same, roped off in front until contracts open

Three cells wide and two deep, beside the Clock Tower, so it is drawn on a 192x176 canvas with
its feet on row 173, the footprint's south edge (CityOfficeView puts the node there). The rope is
the cart spot's mahogany, not red: a closed office is a promise, not an alarm (red means the
player must act, the root CLAUDE.md's settled rules). Every colour comes from
Tools/Art/palette.json's ramps.

Deterministic, no randomness, so verify_art.py can regenerate it byte for byte.

    python Tools/Art/generate_office_art.py --out-root <dir>
"""
import os

from PIL import Image, ImageDraw

from art_paths import art_dir

OUT_DIR = art_dir()

WIDTH, HEIGHT = 192, 176
FOOT = 173                      # the footprint's south edge

OUTLINE = (0x43, 0x2A, 0x24, 255)
OUTLINE_DARK = (0x2F, 0x21, 0x1C, 255)
STONE_DARK = (0x51, 0x53, 0x4F, 255)
STONE = (0x6A, 0x62, 0x61, 255)
STONE_MID = (0x7D, 0x73, 0x6F, 255)
STONE_LIGHT = (0x9A, 0x8D, 0x85, 255)
ROOF_DARK = (0x43, 0x2E, 0x25, 255)
ROOF = (0x55, 0x3C, 0x30, 255)
ROOF_LIGHT = (0x77, 0x50, 0x39, 255)
WOOD_DARK = (0x34, 0x26, 0x1F, 255)
WOOD = (0x87, 0x4F, 0x33, 255)
WOOD_LIGHT = (0x81, 0x5E, 0x47, 255)
BRASS_DARK = (0x7D, 0x71, 0x53, 255)
BRASS = (0xC4, 0x95, 0x44, 255)
BRASS_LIGHT = (0xE2, 0xC2, 0x7A, 255)
PAPER = (0xD0, 0xBF, 0xAA, 255)
PAPER_SHADE = (0xBD, 0xA6, 0x94, 255)
GLOW = (0xF0, 0xA0, 0x40, 255)
GLOW_DEEP = (0xC4, 0x56, 0x1F, 255)
ROPE = (0x87, 0x4F, 0x33, 255)
ROPE_LIGHT = (0x81, 0x5E, 0x47, 255)
ROPE_DARK = (0x55, 0x3C, 0x30, 255)
TRANSPARENT = (0, 0, 0, 0)

WALL_LEFT, WALL_RIGHT = 14, 177
WALL_TOP, WALL_BOTTOM = 72, 160


def _plinth(draw):
    """Two stone steps the hall stands on, across the whole front."""
    draw.rectangle((6, 160, 185, FOOT), fill=STONE_DARK, outline=OUTLINE)
    draw.line([(7, 161), (184, 161)], fill=STONE_MID)
    draw.rectangle((10, 152, 181, 160), fill=STONE, outline=OUTLINE)
    draw.line([(11, 153), (180, 153)], fill=STONE_LIGHT)


def _walls(draw):
    """Dressed stone: courses every 8 rows, joints staggered, lit along the top of each course."""
    draw.rectangle((WALL_LEFT, WALL_TOP, WALL_RIGHT, WALL_BOTTOM - 8), fill=STONE, outline=OUTLINE)
    for i, y in enumerate(range(WALL_TOP + 8, WALL_BOTTOM - 8, 8)):
        draw.line([(WALL_LEFT + 1, y), (WALL_RIGHT - 1, y)], fill=STONE_DARK)
        draw.line([(WALL_LEFT + 1, y + 1), (WALL_RIGHT - 1, y + 1)], fill=STONE_MID)
        start = WALL_LEFT + (8 if i % 2 else 20)
        for x in range(start, WALL_RIGHT - 4, 24):
            draw.line([(x, y + 2), (x, y + 7)], fill=STONE_DARK)
    # Pilasters at the corners.
    for x0 in (WALL_LEFT, WALL_RIGHT - 6):
        draw.rectangle((x0, WALL_TOP, x0 + 6, WALL_BOTTOM - 8), fill=STONE_MID, outline=OUTLINE)
        draw.line([(x0 + 2, WALL_TOP + 2), (x0 + 2, WALL_BOTTOM - 10)], fill=STONE_LIGHT)


def _roof(draw):
    """A low mahogany-shingled roof with a pediment over the door, and a brass clock in it."""
    draw.polygon([(6, WALL_TOP + 2), (24, 40), (167, 40), (185, WALL_TOP + 2)], fill=ROOF, outline=OUTLINE)
    for i, y in enumerate(range(46, WALL_TOP, 6)):
        # Inside the roof's sloped ends: they run from (24, 40) down to (6, 74), and mirrored.
        left = 24 - (y - 40) * 18 // 34 + 3
        right = WIDTH - 1 - left
        draw.line([(left, y), (right, y)], fill=ROOF_DARK)
        for x in range(left + 4 + (6 if i % 2 else 0), right - 2, 12):
            draw.line([(x, y + 1), (x, y + 5)], fill=ROOF_DARK)
        draw.line([(left + 1, y + 1), (right - 1, y + 1)], fill=ROOF_LIGHT)
    # The eave's shadow on the stone.
    draw.line([(WALL_LEFT + 1, WALL_TOP + 3), (WALL_RIGHT - 1, WALL_TOP + 3)], fill=STONE_DARK)
    # Pediment.
    draw.polygon([(66, 44), (96, 18), (126, 44)], fill=STONE_MID, outline=OUTLINE)
    draw.line([(70, 43), (96, 21)], fill=STONE_LIGHT)
    cx, cy = 96, 36
    draw.ellipse((cx - 8, cy - 8, cx + 8, cy + 8), fill=PAPER, outline=OUTLINE)
    draw.ellipse((cx - 6, cy - 6, cx + 6, cy + 6), outline=BRASS)
    draw.line([(cx, cy), (cx, cy - 5)], fill=OUTLINE_DARK)
    draw.line([(cx, cy), (cx + 4, cy)], fill=OUTLINE_DARK)
    draw.point((cx, cy), fill=BRASS_DARK)


def _door(draw):
    """A mahogany double door under a brass plate, lit from inside."""
    x0, x1, top = 80, 111, 106
    draw.rectangle((x0 - 4, top - 4, x1 + 4, WALL_BOTTOM - 8), fill=STONE_MID, outline=OUTLINE)
    draw.rectangle((x0, top, x1, WALL_BOTTOM - 8), fill=WOOD, outline=OUTLINE)
    draw.line([(96, top + 1), (96, WALL_BOTTOM - 9)], fill=WOOD_DARK)
    for px in (x0 + 4, 99):
        draw.rectangle((px, top + 6, px + 9, top + 20), outline=WOOD_DARK)
        draw.rectangle((px, top + 26, px + 9, WALL_BOTTOM - 14), outline=WOOD_DARK)
        draw.line([(px + 1, top + 7), (px + 1, top + 19)], fill=WOOD_LIGHT)
    draw.point((93, top + 26), fill=BRASS_LIGHT)
    draw.point((99, top + 26), fill=BRASS_LIGHT)
    # The plate above the door: brass, a dark rule where the name would be cut.
    draw.rectangle((x0 - 2, top - 18, x1 + 2, top - 8), fill=BRASS, outline=OUTLINE)
    draw.line([(x0 + 1, top - 17), (x1 - 1, top - 17)], fill=BRASS_LIGHT)
    draw.line([(x0 + 6, top - 13), (x1 - 6, top - 13)], fill=BRASS_DARK)


def _window(draw, x0):
    """A tall window with a warm glow and a mullion."""
    top, bottom = 86, 128
    draw.rectangle((x0 - 2, top - 2, x0 + 20, bottom + 4), fill=STONE_MID, outline=OUTLINE)
    draw.rectangle((x0, top, x0 + 18, bottom), fill=GLOW_DEEP, outline=OUTLINE_DARK)
    draw.rectangle((x0 + 2, top + 2, x0 + 16, bottom - 12), fill=GLOW)
    draw.line([(x0 + 9, top + 1), (x0 + 9, bottom - 1)], fill=OUTLINE_DARK)
    draw.line([(x0 + 1, top + 20), (x0 + 17, top + 20)], fill=OUTLINE_DARK)
    draw.line([(x0 - 2, bottom + 4), (x0 + 20, bottom + 4)], fill=STONE_LIGHT)


def _notice_board(draw):
    """The notice the player reads (E): a mahogany frame with two pinned papers, beside the door."""
    x0, y0, x1, y1 = 116, 100, 142, 134
    draw.rectangle((x0, y0, x1, y1), fill=WOOD_DARK, outline=OUTLINE)
    draw.rectangle((x0 + 2, y0 + 2, x1 - 2, y1 - 2), fill=WOOD)
    for (px, py, w, h) in ((x0 + 4, y0 + 4, 9, 14), (x0 + 15, y0 + 8, 7, 18)):
        draw.rectangle((px, py, px + w, py + h), fill=PAPER, outline=OUTLINE)
        draw.line([(px + 1, py + h - 1), (px + w - 1, py + h - 1)], fill=PAPER_SHADE)
        for ly in range(py + 4, py + h - 2, 3):
            draw.line([(px + 2, ly), (px + w - 2, ly)], fill=PAPER_SHADE)
        draw.point((px + w // 2, py + 1), fill=BRASS)
    draw.rectangle((x0 + 12, y1, x0 + 18, y1 + 18), fill=WOOD_DARK, outline=OUTLINE)


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


# Each material's tones, darkest first: the edge pass steps one tone up on the lit (left) edge
# and one down on the shaded (right) edge, as the style bible's light from the upper left asks.
RAMPS = [
    [STONE_DARK, STONE, STONE_MID, STONE_LIGHT],
    [ROOF_DARK, ROOF, ROOF_LIGHT],
    [WOOD_DARK, WOOD_LIGHT, WOOD],
    [BRASS_DARK, BRASS, BRASS_LIGHT],
]


def _step(colour, by):
    for ramp in RAMPS:
        if colour in ramp:
            i = max(0, min(len(ramp) - 1, ramp.index(colour) + by))
            return ramp[i]
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


def make_office() -> Image.Image:
    image = Image.new("RGBA", (WIDTH, HEIGHT), TRANSPARENT)
    draw = ImageDraw.Draw(image)
    _plinth(draw)
    _walls(draw)
    _roof(draw)
    _window(draw, 26)
    _window(draw, 148)
    _door(draw)
    _notice_board(draw)
    return image


def make_open() -> Image.Image:
    return _edge_light(make_office())


def make_closed() -> Image.Image:
    image = make_office()
    draw = ImageDraw.Draw(image)
    # A rope across the steps, three posts, the middle one in front of the door.
    for x in (10, 96, 181):
        _post(draw, x, FOOT)
    _rope(draw, (10, FOOT - 16), (96, FOOT - 16), 6)
    _rope(draw, (96, FOOT - 16), (181, FOOT - 16), 6)
    return _edge_light(image)


PICTURES = {"city_office.png": make_open, "city_office_closed.png": make_closed}


def main() -> None:
    os.makedirs(OUT_DIR, exist_ok=True)
    for name, make in PICTURES.items():
        path = os.path.join(OUT_DIR, name)
        make().save(path)
        print(f"wrote {path} ({WIDTH}x{HEIGHT})")


if __name__ == "__main__":
    main()
