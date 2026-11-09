"""The Golem Foundry (docs/golem-sales-design.md §2.2): a placeholder until the user commissions art.

    golem_foundry.png   a one-cell brick furnace: a stone plinth, a brass-banded iron door with
                        a firebox glow, a chimney, and a brass cog plaque for what it makes

One cell wide, so 64 art pixels across, standing on its cell's south edge (bottom-center, as the
other buildings: feet on the last row). The glow is the fire ramp's amber and gold, not red: a
working furnace, not an alarm (red means the player must act). Every colour is from
Tools/Art/palette.json's ramps.

Deterministic, no randomness, so verify_art.py can regenerate it byte for byte.

    python Tools/Art/generate_foundry_art.py --out-root <dir>
"""
import os

from PIL import Image, ImageDraw

from art_paths import art_dir

OUT_DIR = art_dir()

WIDTH, HEIGHT = 64, 92
FOOT = HEIGHT - 1

OUTLINE = (0x43, 0x2A, 0x24, 255)
OUTLINE_DARK = (0x2F, 0x21, 0x1C, 255)
BRICK_DARK = (0x55, 0x3C, 0x30, 255)
BRICK = (0x77, 0x50, 0x39, 255)
BRICK_LIGHT = (0x87, 0x4F, 0x33, 255)
MORTAR = (0x81, 0x5E, 0x47, 255)
STONE_DARK = (0x51, 0x53, 0x4F, 255)
STONE = (0x6A, 0x62, 0x61, 255)
STONE_LIGHT = (0x7D, 0x73, 0x6F, 255)
IRON_DARK = (0x47, 0x42, 0x42, 255)
IRON = (0x6A, 0x62, 0x61, 255)
BRASS_DARK = (0x7D, 0x71, 0x53, 255)
BRASS = (0xC4, 0x95, 0x44, 255)
BRASS_LIGHT = (0xE2, 0xC2, 0x7A, 255)
FIRE_DEEP = (0xC4, 0x56, 0x1F, 255)
FIRE = (0xF0, 0xA0, 0x40, 255)
FIRE_LIGHT = (0xFF, 0xE0, 0x8A, 255)
SMOKE = (0xB0, 0xA0, 0x96, 255)
SMOKE_LIGHT = (0xDE, 0xD6, 0xCC, 255)


def _bricks(draw, box):
    """Courses of brick in a box: rows of 4 pixels, joints offset every other course."""
    x0, y0, x1, y1 = box
    draw.rectangle(box, fill=BRICK)
    row = 0
    for y in range(y0, y1, 4):
        draw.line([(x0, y), (x1, y)], fill=MORTAR)
        offset = 0 if row % 2 == 0 else 4
        for x in range(x0 + offset, x1, 8):
            draw.line([(x, y), (x, min(y + 3, y1))], fill=MORTAR)
        if row % 3 == 1:
            draw.line([(x0 + 1, y + 1), (x0 + 6, y + 1)], fill=BRICK_LIGHT)
        row += 1
    draw.line([(x1 - 1, y0), (x1 - 1, y1)], fill=BRICK_DARK)


def make_foundry() -> Image.Image:
    img = Image.new("RGBA", (WIDTH, HEIGHT), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)

    # The chimney, behind the hall's roof line, with a wisp of smoke.
    d.rectangle((40, 6, 51, 34), fill=BRICK, outline=OUTLINE)
    _bricks(d, (41, 7, 50, 33))
    d.rectangle((38, 4, 53, 8), fill=STONE, outline=OUTLINE)
    d.line([(39, 5), (52, 5)], fill=STONE_LIGHT)
    d.ellipse((41, 0, 47, 4), fill=SMOKE)
    d.point((43, 1), fill=SMOKE_LIGHT)

    # The hall: brick walls under a stone cornice.
    d.rectangle((4, 30, 59, 82), fill=BRICK, outline=OUTLINE)
    _bricks(d, (5, 31, 58, 81))
    d.rectangle((2, 26, 61, 32), fill=STONE, outline=OUTLINE)
    d.line([(3, 27), (60, 27)], fill=STONE_LIGHT)
    d.line([(3, 31), (60, 31)], fill=STONE_DARK)

    # The cog plaque over the door: what it makes.
    cx, cy = 32, 40
    for dx, dy in [(0, -6), (0, 6), (-6, 0), (6, 0), (-4, -4), (4, -4), (-4, 4), (4, 4)]:
        d.rectangle((cx + dx - 1, cy + dy - 1, cx + dx + 1, cy + dy + 1), fill=BRASS, outline=OUTLINE)
    d.ellipse((cx - 5, cy - 5, cx + 5, cy + 5), fill=BRASS, outline=OUTLINE)
    d.ellipse((cx - 2, cy - 2, cx + 2, cy + 2), fill=BRASS_DARK)
    d.point((cx - 3, cy - 3), fill=BRASS_LIGHT)

    # The furnace door: iron in a brass band, an arched firebox window glowing.
    d.rectangle((18, 50, 45, 82), fill=IRON, outline=OUTLINE)
    d.rectangle((17, 49, 46, 52), fill=BRASS, outline=OUTLINE)
    d.line([(18, 50), (45, 50)], fill=BRASS_LIGHT)
    d.rectangle((24, 56, 39, 70), fill=FIRE_DEEP, outline=OUTLINE_DARK)
    d.rectangle((26, 60, 37, 69), fill=FIRE)
    d.rectangle((29, 64, 34, 69), fill=FIRE_LIGHT)
    for x in (28, 32, 36):
        d.line([(x, 57), (x, 69)], fill=IRON_DARK)
    for y in (74, 78):
        d.line([(19, y), (44, y)], fill=IRON_DARK)
    for x, y in [(20, 54), (43, 54), (20, 80), (43, 80)]:
        d.point((x, y), fill=BRASS_LIGHT)

    # Lit from the upper left (art-style-bible.md): bright left and top edges, shaded right.
    d.line([(5, 33), (5, 81)], fill=BRICK_LIGHT)
    d.line([(6, 33), (6, 81)], fill=MORTAR)
    d.line([(57, 33), (57, 81)], fill=BRICK_DARK)
    d.line([(58, 33), (58, 81)], fill=BRICK_DARK)
    d.line([(41, 9), (41, 33)], fill=BRICK_LIGHT)
    d.line([(49, 9), (50, 33)], fill=BRICK_DARK)
    d.line([(19, 53), (19, 81)], fill=STONE_LIGHT)
    d.line([(44, 53), (44, 81)], fill=IRON_DARK)
    d.line([(60, 28), (60, 31)], fill=STONE_DARK)

    # A glow on the floor of the plinth before the door.
    d.rectangle((0, 82, 63, FOOT), fill=STONE, outline=OUTLINE)
    d.line([(1, 83), (62, 83)], fill=STONE_LIGHT)
    d.line([(1, FOOT - 1), (62, FOOT - 1)], fill=STONE_DARK)
    d.line([(62, 83), (62, FOOT - 1)], fill=STONE_DARK)
    d.line([(22, 84), (41, 84)], fill=FIRE)
    return img


PICTURES = {
    "golem_foundry.png": make_foundry,
}


def main() -> None:
    os.makedirs(OUT_DIR, exist_ok=True)
    for name, make in PICTURES.items():
        path = os.path.join(OUT_DIR, name)
        make().save(path)
        print(f"wrote {path} ({WIDTH}x{HEIGHT})")


if __name__ == "__main__":
    main()
