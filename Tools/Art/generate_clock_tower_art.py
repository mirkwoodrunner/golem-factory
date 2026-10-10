"""The Clock Tower, stage by stage: the endgame building the town square was made for.

G10, at the user's call: the tower "is supposed to be the final project", and the single-cell
`clock_tower.png` looked like a grandfather clock. The tower now has a fixed 3x3 site in the town
square, roped off until the factory has built a Zeppelin, and it GROWS: one picture per stage
the player has completed.

    clock_tower_site.png      roped off: brass posts, a sagging rope, a sign
    clock_tower_stage0.png    open: the plinth chalked out, survey stakes
    clock_tower_stage1.png    Foundation: a stone plinth, scaffolding going up
    clock_tower_stage2.png    The Movement: the lower tower, its great cog showing
    clock_tower_stage3.png    Aether Illumination: taller, a band of glowing lenses
    clock_tower_stage4.png    The Chronometer: the finished tower, clock faces and spire

CONVENTIONS, and the one that differs from generate_building_art.py:
  * 64 art pixels to a cell, the warm brass/wood/iron palette, a near-black outline.
  * NOT TRIMMED. Every picture is the same 192x448 canvas (three cells wide, seven tall) with the
    bottom row on the footprint's south edge. The stages swap in place as the tower grows, so a
    per-picture trim would make the whole building jump between stages.
  * Deterministic: no randomness, so a re-run writes byte-identical files (verify_art.py).

    python Tools/Art/generate_clock_tower_art.py
"""

import math
import os
import sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from art_paths import art_dir  # noqa: E402

from PIL import Image, ImageDraw

OUT_DIR = art_dir()

WIDTH = 192    # three cells
HEIGHT = 448   # seven cells: the finished tower's spire reaches the top
CENTRE = WIDTH // 2

TRANSPARENT = (0, 0, 0, 0)
OUTLINE = (28, 18, 22, 255)

# The same world as generate_building_art.py's palette.
WOOD = (116, 68, 61, 255)
WOOD_LIGHT = (184, 125, 95, 255)
WOOD_DARK = (72, 41, 48, 255)
BRASS = (176, 122, 52, 255)
BRASS_LIGHT = (214, 168, 88, 255)
BRASS_DARK = (108, 72, 34, 255)
IRON = (94, 92, 104, 255)
IRON_LIGHT = (138, 136, 148, 255)
IRON_DARK = (58, 54, 68, 255)
GLOW = (120, 214, 198, 255)
GLOW_LIGHT = (196, 246, 236, 255)

# The tower's own materials: a warm sandstone and a brick, so it reads as civic masonry rather
# than as one more machine of the factory's brass.
STONE = (150, 132, 112, 255)
STONE_LIGHT = (190, 172, 148, 255)
STONE_DARK = (98, 84, 74, 255)
BRICK = (132, 70, 56, 255)
BRICK_LIGHT = (168, 100, 78, 255)
BRICK_DARK = (88, 44, 40, 255)
SLATE = (64, 70, 92, 255)
SLATE_LIGHT = (96, 104, 130, 255)
CREAM = (232, 220, 190, 255)
ROPE = (150, 42, 46, 255)
ROPE_LIGHT = (196, 74, 70, 255)
CHALK = (226, 218, 200, 200)

# The ground plane: the footprint seen from the camera's three-quarter angle.
GROUND_TOP = HEIGHT - 192 + 2   # the whole 3x3 footprint, three cells deep
GROUND_BOTTOM = HEIGHT - 2
GROUND_LEFT = 4
GROUND_RIGHT = WIDTH - 5

# The plinth the tower stands on, set inside the footprint.
PLINTH_LEFT = 18
PLINTH_RIGHT = WIDTH - 19
PLINTH_FRONT = HEIGHT - 16        # bottom of the plinth's front face
PLINTH_FACE = 40                  # front face height
PLINTH_TOP_DEPTH = 96             # the visible top surface, receding

# The tower's body sits on the plinth's back half.
BODY_LEFT = CENTRE - 52
BODY_RIGHT = CENTRE + 51
BODY_BASE = PLINTH_FRONT - PLINTH_FACE - 34


def _hash2(x, y, salt=0):
    """Deterministic per-pixel noise -- a re-run must produce byte-identical files."""
    n = (x * 374761393 + y * 668265263 + salt * 2246822519) & 0xFFFFFFFF
    n = (n ^ (n >> 13)) * 1274126177 & 0xFFFFFFFF
    return ((n ^ (n >> 16)) & 0xFFFF) / 65535.0


def _shade(color, amount):
    r, g, b, a = color
    if amount >= 0:
        return (int(r + (255 - r) * amount), int(g + (255 - g) * amount),
                int(b + (255 - b) * amount), a)
    k = 1.0 + amount
    return (int(r * k), int(g * k), int(b * k), a)


def _speckle(image, box, fill, amount, salt):
    """Light and dark flecks, so a large face does not read as a flat rectangle."""
    x0, y0, x1, y1 = box
    pixels = image.load()
    for y in range(max(0, y0 + 1), min(HEIGHT, y1)):
        for x in range(max(0, x0 + 1), min(WIDTH, x1)):
            noise = _hash2(x, y, salt)
            if noise > 0.84:
                pixels[x, y] = _shade(fill, amount)
            elif noise < 0.16:
                pixels[x, y] = _shade(fill, -amount)


def _block(draw, image, box, fill, light, dark, salt, speckle=0.10):
    x0, y0, x1, y1 = box
    draw.rectangle(box, fill=fill, outline=OUTLINE)
    _speckle(image, box, fill, speckle, salt)
    draw.line([(x0 + 1, y0 + 1), (x1 - 1, y0 + 1)], fill=light)
    draw.line([(x1 - 1, y0 + 1), (x1 - 1, y1 - 1)], fill=dark)


def _courses(draw, box, mortar, height, salt):
    """Masonry courses: running bond, every other row offset by half a stone."""
    x0, y0, x1, y1 = box
    row = 0
    for y in range(y1 - height, y0, -height):
        draw.line([(x0 + 1, y), (x1 - 1, y)], fill=mortar)
        offset = (height if row % 2 else 0) + int(_hash2(row, salt) * 3)
        for x in range(x0 + offset + 2, x1 - 1, height * 2):
            draw.line([(x, y + 1), (x, min(y + height - 1, y1 - 1))], fill=mortar)
        row += 1


# --- The ground ----------------------------------------------------------------------------------

def _flagstones(draw, image):
    """The footprint paved in big flags: the square's floor, finer than the street's cobbles."""
    box = (GROUND_LEFT, GROUND_TOP, GROUND_RIGHT, GROUND_BOTTOM)
    draw.rectangle(box, fill=STONE_DARK, outline=OUTLINE)
    size = 24
    for gy, y in enumerate(range(GROUND_TOP + 2, GROUND_BOTTOM - 1, size)):
        for gx, x in enumerate(range(GROUND_LEFT + 2 + (gy % 2) * 12, GROUND_RIGHT - 1, size)):
            tone = _shade(STONE, (_hash2(gx, gy, 7) - 0.5) * 0.18)
            flag = (x, y, min(x + size - 3, GROUND_RIGHT - 2), min(y + size - 3, GROUND_BOTTOM - 2))
            if flag[2] > flag[0] and flag[3] > flag[1]:
                draw.rectangle(flag, fill=tone)
                draw.line([(flag[0], flag[1]), (flag[2], flag[1])], fill=_shade(tone, 0.18))
    _speckle(image, box, STONE, 0.06, 3)


def _post(draw, x, base, height=34):
    """A brass bollard: the rope's post."""
    draw.rectangle((x - 3, base - height, x + 3, base), fill=BRASS, outline=OUTLINE)
    draw.line([(x - 2, base - height + 1), (x - 2, base - 1)], fill=BRASS_LIGHT)
    draw.ellipse((x - 5, base - height - 6, x + 5, base - height + 2), fill=BRASS_LIGHT, outline=OUTLINE)
    draw.rectangle((x - 5, base - 3, x + 5, base), fill=BRASS_DARK, outline=OUTLINE)


def _rope(draw, a, b, sag):
    """A swag of rope between two post tops: a shallow catenary, two pixels thick."""
    (x0, y0), (x1, y1) = a, b
    steps = max(abs(x1 - x0), abs(y1 - y0), 1)
    points = []
    for i in range(steps + 1):
        t = i / steps
        x = x0 + (x1 - x0) * t
        y = y0 + (y1 - y0) * t + sag * 4 * t * (1 - t)
        points.append((round(x), round(y)))
    draw.line(points, fill=OUTLINE, width=4)
    draw.line(points, fill=ROPE, width=2)
    draw.line([(px, py - 1) for px, py in points[::3]], fill=ROPE_LIGHT)


def _sign(draw, x, y):
    """A small hanging plaque on the front rope -- the in-world 'closed'."""
    draw.line([(x - 8, y - 6), (x - 8, y)], fill=OUTLINE)
    draw.line([(x + 8, y - 6), (x + 8, y)], fill=OUTLINE)
    draw.rectangle((x - 13, y, x + 13, y + 14), fill=WOOD, outline=OUTLINE)
    draw.line([(x - 12, y + 1), (x + 12, y + 1)], fill=WOOD_LIGHT)
    # A painted bar across it: a wordless "no entry", which no font can box.
    draw.rectangle((x - 8, y + 6, x + 8, y + 8), fill=CREAM)


# --- The tower -----------------------------------------------------------------------------------

def _plinth(draw, image):
    """The foundation: a stone platform with a lit top and a coursed front face."""
    top = (PLINTH_LEFT, PLINTH_FRONT - PLINTH_FACE - PLINTH_TOP_DEPTH,
           PLINTH_RIGHT, PLINTH_FRONT - PLINTH_FACE)
    _block(draw, image, top, STONE_LIGHT, _shade(STONE_LIGHT, 0.2), STONE, 11, 0.06)
    face = (PLINTH_LEFT, PLINTH_FRONT - PLINTH_FACE, PLINTH_RIGHT, PLINTH_FRONT)
    _block(draw, image, face, STONE, STONE_LIGHT, STONE_DARK, 12)
    _courses(draw, face, STONE_DARK, 12, 1)
    # Steps up the front: the square is a public place, the tower a public building.
    for i, inset in enumerate((44, 52, 60)):
        y = PLINTH_FRONT - 4 - i * 6
        draw.rectangle((CENTRE - inset // 2 - 4, y - 5, CENTRE + inset // 2 + 4, y),
                       fill=_shade(STONE_LIGHT, -0.05 * i), outline=OUTLINE)


def _body(draw, image, top):
    """The brick shaft, from the plinth up to <top>, with stone quoins at its corners."""
    box = (BODY_LEFT, top, BODY_RIGHT, BODY_BASE)
    _block(draw, image, box, BRICK, BRICK_LIGHT, BRICK_DARK, 21, 0.08)
    _courses(draw, box, BRICK_DARK, 8, 2)
    for x0, x1 in ((BODY_LEFT, BODY_LEFT + 9), (BODY_RIGHT - 9, BODY_RIGHT)):
        for i, y in enumerate(range(BODY_BASE - 12, top, -12)):
            w = 0 if i % 2 else 3
            draw.rectangle((x0 - (w if x0 == BODY_LEFT else 0), y, x1 + (w if x1 == BODY_RIGHT else 0), y + 10),
                           fill=STONE_LIGHT if i % 2 else STONE, outline=OUTLINE)
    # The door at its foot.
    door = (CENTRE - 12, BODY_BASE - 34, CENTRE + 12, BODY_BASE)
    draw.rectangle(door, fill=WOOD_DARK, outline=OUTLINE)
    draw.pieslice((CENTRE - 12, BODY_BASE - 46, CENTRE + 12, BODY_BASE - 22), 180, 360, fill=WOOD_DARK, outline=OUTLINE)
    draw.line([(CENTRE, BODY_BASE - 44), (CENTRE, BODY_BASE - 1)], fill=OUTLINE)
    draw.point([(CENTRE - 4, BODY_BASE - 16), (CENTRE + 4, BODY_BASE - 16)], fill=BRASS_LIGHT)


def _cornice(draw, image, y, overhang=6):
    """A stone band across the shaft: where one stage of building meets the next."""
    box = (BODY_LEFT - overhang, y - 7, BODY_RIGHT + overhang, y)
    _block(draw, image, box, STONE_LIGHT, _shade(STONE_LIGHT, 0.2), STONE_DARK, 31 + y, 0.05)


def _cog(draw, cx, cy, radius, teeth=12):
    """The great cog, seen through an opening in the shaft."""
    points = []
    for i in range(teeth * 2):
        angle = math.pi * i / teeth
        r = radius if i % 2 == 0 else radius - 5
        points.append((cx + r * math.cos(angle), cy + r * math.sin(angle)))
    draw.polygon(points, fill=BRASS, outline=OUTLINE)
    draw.ellipse((cx - radius + 9, cy - radius + 9, cx + radius - 9, cy + radius - 9), fill=BRASS_DARK, outline=OUTLINE)
    for i in range(4):
        angle = math.pi * i / 2 + math.pi / 4
        draw.line([(cx, cy), (cx + (radius - 10) * math.cos(angle), cy + (radius - 10) * math.sin(angle))],
                  fill=BRASS_LIGHT, width=3)
    draw.ellipse((cx - 5, cy - 5, cx + 5, cy + 5), fill=BRASS_LIGHT, outline=OUTLINE)


def _movement_window(draw, image, y):
    """A round opening in the shaft with the great cog turning behind it."""
    r = 30
    draw.ellipse((CENTRE - r - 4, y - r - 4, CENTRE + r + 4, y + r + 4), fill=STONE_LIGHT, outline=OUTLINE)
    draw.ellipse((CENTRE - r, y - r, CENTRE + r, y + r), fill=IRON_DARK, outline=OUTLINE)
    _cog(draw, CENTRE, y, r - 3)


def _lens_band(draw, image, y, lit=True):
    """Aether Illumination: a row of lenses set in brass, glowing teal."""
    _cornice(draw, image, y + 26, overhang=4)
    for i in range(4):
        cx = BODY_LEFT + 16 + i * 24
        draw.ellipse((cx - 9, y - 9, cx + 9, y + 9), fill=BRASS, outline=OUTLINE)
        draw.ellipse((cx - 6, y - 6, cx + 6, y + 6), fill=GLOW if lit else IRON_DARK, outline=OUTLINE)
        if lit:
            draw.point([(cx - 2, y - 3), (cx - 3, y - 2)], fill=GLOW_LIGHT)
    _cornice(draw, image, y - 14, overhang=4)


def _halo(image, cx, cy, radius):
    """A soft additive glow around the lenses, drawn as dithered pixels so it stays pixel art."""
    pixels = image.load()
    for y in range(max(0, cy - radius), min(HEIGHT, cy + radius)):
        for x in range(max(0, cx - radius), min(WIDTH, cx + radius)):
            d = math.hypot(x - cx, (y - cy) * 2.2) / radius
            if d < 1.0 and pixels[x, y][3] == 0 and _hash2(x, y, 41) < (1.0 - d) * 0.35:
                pixels[x, y] = (GLOW[0], GLOW[1], GLOW[2], 90)


def _belfry(draw, image, top):
    """The Chronometer: a clock face on the front of a belfry, under a slate spire."""
    belfry = (BODY_LEFT + 4, top - 70, BODY_RIGHT - 4, top)
    _block(draw, image, belfry, STONE, STONE_LIGHT, STONE_DARK, 51, 0.06)
    cx, cy, r = CENTRE, top - 36, 30
    draw.ellipse((cx - r - 3, cy - r - 3, cx + r + 3, cy + r + 3), fill=BRASS, outline=OUTLINE)
    draw.ellipse((cx - r, cy - r, cx + r, cy + r), fill=CREAM, outline=OUTLINE)
    for hour in range(12):
        angle = math.pi * 2 * hour / 12
        inner = r - (7 if hour % 3 == 0 else 4)
        draw.line([(cx + inner * math.sin(angle), cy - inner * math.cos(angle)),
                   (cx + (r - 2) * math.sin(angle), cy - (r - 2) * math.cos(angle))],
                  fill=OUTLINE, width=2 if hour % 3 == 0 else 1)
    # Ten past ten: the clockmaker's hour, both hands clear of each other.
    draw.line([(cx, cy), (cx - 15, cy - 9)], fill=OUTLINE, width=3)
    draw.line([(cx, cy), (cx + 19, cy - 13)], fill=OUTLINE, width=2)
    draw.ellipse((cx - 3, cy - 3, cx + 3, cy + 3), fill=BRASS_DARK, outline=OUTLINE)

    _cornice(draw, image, top - 70, overhang=10)
    # The spire: a slate pyramid seen front-on, ribbed, with a brass finial.
    base = top - 77
    apex = 14
    draw.polygon([(BODY_LEFT - 6, base), (CENTRE, apex), (BODY_RIGHT + 6, base)], fill=SLATE, outline=OUTLINE)
    for i in range(1, 6):
        t = i / 6
        draw.line([(BODY_LEFT - 6 + (CENTRE - BODY_LEFT + 6) * t, base - (base - apex) * t),
                   (BODY_RIGHT + 6 - (BODY_RIGHT + 6 - CENTRE) * t, base - (base - apex) * t)], fill=SLATE_LIGHT)
    draw.line([(CENTRE, apex), (CENTRE, base - 1)], fill=OUTLINE)
    draw.rectangle((CENTRE - 2, 2, CENTRE + 2, apex + 2), fill=BRASS, outline=OUTLINE)
    draw.ellipse((CENTRE - 4, 0, CENTRE + 4, 8), fill=BRASS_LIGHT, outline=OUTLINE)


def _scaffold(draw, top, bottom):
    """Timber scaffolding up the front of whatever is still being built."""
    for x in (BODY_LEFT - 14, BODY_RIGHT + 14):
        draw.rectangle((x - 2, top, x + 2, bottom), fill=WOOD, outline=OUTLINE)
    for i, y in enumerate(range(bottom - 6, top + 4, -30)):
        draw.rectangle((BODY_LEFT - 16, y - 3, BODY_RIGHT + 16, y), fill=WOOD_LIGHT, outline=OUTLINE)
        x0, x1 = (BODY_LEFT - 14, BODY_RIGHT + 14) if i % 2 == 0 else (BODY_RIGHT + 14, BODY_LEFT - 14)
        if y - 30 > top:
            draw.line([(x0, y), (x0 + (x1 - x0) // 4, y - 28)], fill=WOOD_DARK, width=2)


# --- The pictures --------------------------------------------------------------------------------

STAGE2_TOP = BODY_BASE - 130     # The Movement: the shaft to the top of the great cog
STAGE3_TOP = BODY_BASE - 190     # Aether Illumination: the lens band above it
STAGE4_TOP = BODY_BASE - 210     # The Chronometer: the belfry and spire above that


def make_site() -> Image.Image:
    image = Image.new("RGBA", (WIDTH, HEIGHT), TRANSPARENT)
    draw = ImageDraw.Draw(image)
    _flagstones(draw, image)
    back, front = GROUND_TOP + 18, GROUND_BOTTOM - 8
    left, right = GROUND_LEFT + 10, GROUND_RIGHT - 10
    # Back rope first, so the front one crosses over it.
    _post(draw, left, back)
    _post(draw, right, back)
    _rope(draw, (left, back - 36), (right, back - 36), 10)
    _rope(draw, (left, back - 36), (left, front - 36), 6)
    _rope(draw, (right, back - 36), (right, front - 36), 6)
    _post(draw, left, front)
    _post(draw, right, front)
    _rope(draw, (left, front - 36), (right, front - 36), 14)
    _sign(draw, CENTRE, front - 30)
    return image


def make_stage0() -> Image.Image:
    """Open: the rope is down and the plinth is chalked out, with survey stakes at its corners."""
    image = Image.new("RGBA", (WIDTH, HEIGHT), TRANSPARENT)
    draw = ImageDraw.Draw(image)
    _flagstones(draw, image)
    outline = (PLINTH_LEFT, PLINTH_FRONT - PLINTH_FACE - PLINTH_TOP_DEPTH + 30, PLINTH_RIGHT, PLINTH_FRONT - 6)
    for x in range(outline[0], outline[2], 6):
        draw.line([(x, outline[1]), (x + 3, outline[1])], fill=CHALK)
        draw.line([(x, outline[3]), (x + 3, outline[3])], fill=CHALK)
    for y in range(outline[1], outline[3], 6):
        draw.line([(outline[0], y), (outline[0], y + 3)], fill=CHALK)
        draw.line([(outline[2], y), (outline[2], y + 3)], fill=CHALK)
    for x, y in ((outline[0], outline[1]), (outline[2], outline[1]), (outline[0], outline[3]), (outline[2], outline[3])):
        draw.rectangle((x - 1, y - 18, x + 1, y), fill=WOOD, outline=OUTLINE)
        draw.polygon([(x + 1, y - 18), (x + 9, y - 15), (x + 1, y - 12)], fill=ROPE_LIGHT, outline=OUTLINE)
    return image


def make_stage1() -> Image.Image:
    """Foundation laid: the plinth stands, and the scaffolding goes up for what comes next."""
    image = Image.new("RGBA", (WIDTH, HEIGHT), TRANSPARENT)
    draw = ImageDraw.Draw(image)
    _flagstones(draw, image)
    _plinth(draw, image)
    _scaffold(draw, BODY_BASE - 90, BODY_BASE)
    return image


def make_stage2() -> Image.Image:
    """The Movement: the shaft rises to the great cog."""
    image = Image.new("RGBA", (WIDTH, HEIGHT), TRANSPARENT)
    draw = ImageDraw.Draw(image)
    _flagstones(draw, image)
    _plinth(draw, image)
    _body(draw, image, STAGE2_TOP)
    _movement_window(draw, image, BODY_BASE - 82)
    _cornice(draw, image, STAGE2_TOP + 7)
    _scaffold(draw, STAGE2_TOP - 50, STAGE2_TOP + 10)
    return image


def make_stage3() -> Image.Image:
    """Aether Illumination: a band of lenses above the cog, glowing."""
    image = Image.new("RGBA", (WIDTH, HEIGHT), TRANSPARENT)
    draw = ImageDraw.Draw(image)
    _flagstones(draw, image)
    _plinth(draw, image)
    _body(draw, image, STAGE3_TOP)
    _movement_window(draw, image, BODY_BASE - 82)
    _lens_band(draw, image, STAGE2_TOP - 22)
    _halo(image, CENTRE, STAGE2_TOP - 22, 70)
    _cornice(draw, image, STAGE3_TOP + 7)
    _scaffold(draw, STAGE3_TOP - 60, STAGE3_TOP + 10)
    return image


def make_stage4() -> Image.Image:
    """The Chronometer: the finished tower."""
    image = Image.new("RGBA", (WIDTH, HEIGHT), TRANSPARENT)
    draw = ImageDraw.Draw(image)
    _flagstones(draw, image)
    _plinth(draw, image)
    _body(draw, image, STAGE3_TOP)
    _movement_window(draw, image, BODY_BASE - 82)
    _lens_band(draw, image, STAGE2_TOP - 22)
    _halo(image, CENTRE, STAGE2_TOP - 22, 70)
    _belfry(draw, image, STAGE3_TOP + 7)
    return image


def save(image: Image.Image, name: str) -> None:
    """Written whole, never trimmed -- see the canvas note above."""
    assert image.size == (WIDTH, HEIGHT), name
    os.makedirs(OUT_DIR, exist_ok=True)
    path = os.path.join(OUT_DIR, name)
    image.save(path)
    print(f"wrote {path} ({image.width}x{image.height})")


PICTURES = {
    "clock_tower_site.png": make_site,
    "clock_tower_stage0.png": make_stage0,
    "clock_tower_stage1.png": make_stage1,
    "clock_tower_stage2.png": make_stage2,
    "clock_tower_stage3.png": make_stage3,
    "clock_tower_stage4.png": make_stage4,
}


def main() -> None:
    for name, make in PICTURES.items():
        save(make(), name)


if __name__ == "__main__":
    main()
