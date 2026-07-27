#!/usr/bin/env python3
"""Generates placeholder art for the graphics demo (see docs/unity-implementation-plan.md,
"Graphics demo implementation notes"). Not final art -- simple, intentional stand-ins in the
warm wood-and-brass palette from docs/digital-design.md, meant to be swapped for bespoke
pixel art later without touching any code. Re-run to regenerate; output goes to
Assets/_Project/Art/.
"""

import os
from PIL import Image, ImageDraw

OUT_DIR = os.path.join(os.path.dirname(__file__), "..", "..", "Assets", "_Project", "Art")

# Warm wood-and-brass steampunk palette (docs/digital-design.md).
WOOD_DARK = (74, 48, 33, 255)
WOOD_MID = (107, 71, 45, 255)
WOOD_LIGHT = (140, 97, 62, 255)
STONE = (120, 112, 98, 255)
STONE_LIGHT = (150, 141, 124, 255)
BRASS = (196, 149, 68, 255)
BRASS_DARK = (140, 100, 45, 255)
COPPER = (184, 108, 68, 255)
COPPER_DARK = (128, 72, 44, 255)
STEEL = (128, 136, 140, 255)
STEEL_DARK = (86, 92, 96, 255)
TEAL_GLOW = (94, 214, 200, 255)
OUTLINE = (40, 28, 20, 255)
TRANSPARENT = (0, 0, 0, 0)


def save(img: Image.Image, name: str) -> None:
    os.makedirs(OUT_DIR, exist_ok=True)
    path = os.path.join(OUT_DIR, name)
    img.save(path)
    print(f"wrote {path} ({img.width}x{img.height})")


def upscale(img: Image.Image, factor: int) -> Image.Image:
    return img.resize((img.width * factor, img.height * factor), Image.NEAREST)


def make_floor_tile(fill, fill_light, accent=None) -> Image.Image:
    # Small canvas, nearest-neighbor upscaled -- classic 2:1 isometric diamond
    # matching GridCoordinateConverter's "1 x 0.5" cell size.
    w, h = 32, 16
    img = Image.new("RGBA", (w, h), TRANSPARENT)
    draw = ImageDraw.Draw(img)
    top = (w // 2, 0)
    right = (w - 1, h // 2)
    bottom = (w // 2, h - 1)
    left = (0, h // 2)
    draw.polygon([top, right, bottom, left], fill=fill, outline=OUTLINE)
    # Simple top-left highlight sliver to suggest a light source.
    draw.line([top, left], fill=fill_light)
    if accent:
        cx, cy = w // 2, h // 2
        draw.point([(cx - 3, cy), (cx + 2, cy - 1), (cx - 1, cy + 2)], fill=accent)
    return upscale(img, 4)  # -> 128x64


def make_golem(body, body_dark, accent) -> Image.Image:
    # 16x24 canvas: tripod-ish base, boxy torso, single glowing "eye" -- generic
    # enough to stand in for any of the roster's chassis until real art lands.
    w, h = 16, 24
    img = Image.new("RGBA", (w, h), TRANSPARENT)
    draw = ImageDraw.Draw(img)

    # Legs.
    draw.rectangle([3, 18, 5, 22], fill=body_dark, outline=OUTLINE)
    draw.rectangle([10, 18, 12, 22], fill=body_dark, outline=OUTLINE)
    # Torso.
    draw.rectangle([3, 8, 12, 18], fill=body, outline=OUTLINE)
    # Shoulders/arms.
    draw.rectangle([1, 9, 2, 14], fill=body_dark, outline=OUTLINE)
    draw.rectangle([13, 9, 14, 14], fill=body_dark, outline=OUTLINE)
    # Head.
    draw.rectangle([5, 3, 10, 8], fill=body, outline=OUTLINE)
    # Eye / glow.
    draw.point([(7, 5), (8, 5)], fill=accent)

    return upscale(img, 4)  # -> 64x96


def make_clockwork_scavenger(body, body_dark, accent) -> Image.Image:
    # Rickety tripod laborer (early game): thin/spindly build, a stubby center-back
    # leg peeking between the two front legs to hint at three-point support, plus a
    # small sensor antenna -- distinct from make_golem's sturdier generic silhouette.
    w, h = 16, 24
    img = Image.new("RGBA", (w, h), TRANSPARENT)
    draw = ImageDraw.Draw(img)

    draw.rectangle([7, 20, 8, 22], fill=body_dark, outline=OUTLINE)  # center-back leg stub
    draw.rectangle([3, 19, 4, 23], fill=body_dark, outline=OUTLINE)  # left leg
    draw.rectangle([11, 19, 12, 23], fill=body_dark, outline=OUTLINE)  # right leg
    draw.rectangle([4, 10, 11, 19], fill=body, outline=OUTLINE)  # narrow torso
    draw.rectangle([2, 11, 3, 15], fill=body_dark, outline=OUTLINE)
    draw.rectangle([12, 11, 13, 15], fill=body_dark, outline=OUTLINE)
    draw.rectangle([6, 5, 9, 10], fill=body, outline=OUTLINE)  # small head
    draw.line([(7, 5), (7, 2)], fill=body_dark)  # antenna
    draw.point([(7, 1)], fill=accent)
    draw.point([(7, 7), (8, 7)], fill=accent)

    return upscale(img, 4)  # -> 64x96


def make_brass_presser(body, body_dark, accent) -> Image.Image:
    # Stationary inline processor, bolted to the floor (early-mid game): flush riveted
    # base plate instead of legs, wide squat housing, tall central stamping piston.
    w, h = 16, 24
    img = Image.new("RGBA", (w, h), TRANSPARENT)
    draw = ImageDraw.Draw(img)

    draw.rectangle([2, 20, 13, 23], fill=body_dark, outline=OUTLINE)  # bolted base plate
    draw.point([(3, 21), (12, 21)], fill=OUTLINE)  # rivets
    draw.rectangle([2, 10, 13, 20], fill=body, outline=OUTLINE)  # wide press housing
    draw.rectangle([6, 3, 9, 10], fill=body_dark, outline=OUTLINE)  # piston shaft
    draw.rectangle([4, 2, 11, 4], fill=body_dark, outline=OUTLINE)  # press plate
    draw.point([(7, 14), (8, 14)], fill=accent)  # status light

    return upscale(img, 4)  # -> 64x96


def make_aether_hauler(body, body_dark, accent) -> Image.Image:
    # Armored cargo shuttle on treads (mid game): wide low vehicle profile, tread
    # hash-marks instead of legs, angled front armor plate, horizontal sensor slit
    # instead of a round eye -- reads as a vehicle, not a humanoid.
    w, h = 16, 24
    img = Image.new("RGBA", (w, h), TRANSPARENT)
    draw = ImageDraw.Draw(img)

    draw.rectangle([1, 19, 14, 23], fill=body_dark, outline=OUTLINE)  # tread unit
    for tx in (3, 6, 9, 12):
        draw.line([(tx, 20), (tx, 22)], fill=OUTLINE)
    draw.rectangle([2, 10, 13, 19], fill=body, outline=OUTLINE)  # armored hull
    draw.polygon([(2, 10), (13, 10), (11, 13), (4, 13)], fill=body_dark, outline=OUTLINE)  # angled front plate
    draw.rectangle([6, 15, 9, 16], fill=accent)  # sensor slit

    return upscale(img, 4)  # -> 64x96


def make_mainspring_overclocker(body, body_dark, accent) -> Image.Image:
    # Stationary clockwork butler (utility): tall, slender, formal stance, a brass
    # gear emblem on the chest and a top-hat brim -- reads as refined rather than
    # laboring, matching its "projects a speed-boost wave" role.
    w, h = 16, 24
    img = Image.new("RGBA", (w, h), TRANSPARENT)
    draw = ImageDraw.Draw(img)

    draw.rectangle([6, 19, 7, 23], fill=body_dark, outline=OUTLINE)
    draw.rectangle([9, 19, 10, 23], fill=body_dark, outline=OUTLINE)
    draw.rectangle([4, 9, 11, 19], fill=body, outline=OUTLINE)  # tall coat/torso
    draw.ellipse([6, 12, 9, 15], fill=BRASS, outline=OUTLINE)  # brass gear emblem
    draw.rectangle([2, 10, 3, 14], fill=body_dark, outline=OUTLINE)
    draw.rectangle([12, 10, 13, 14], fill=body_dark, outline=OUTLINE)
    draw.rectangle([6, 3, 9, 8], fill=body, outline=OUTLINE)  # head
    draw.rectangle([5, 2, 10, 3], fill=body_dark, outline=OUTLINE)  # hat brim
    draw.point([(7, 5), (8, 5)], fill=accent)

    return upscale(img, 4)  # -> 64x96


def make_zeppelin_freight_loader(body, body_dark, accent) -> Image.Image:
    # Massive industrial behemoth (late game): the roster's bulkiest silhouette --
    # shoulder plates reaching the canvas edges, thick short legs, a vented stack --
    # with a small head to emphasize the body doing the heavy lifting.
    w, h = 16, 24
    img = Image.new("RGBA", (w, h), TRANSPARENT)
    draw = ImageDraw.Draw(img)

    draw.rectangle([2, 19, 5, 23], fill=body_dark, outline=OUTLINE)  # wide left leg
    draw.rectangle([10, 19, 13, 23], fill=body_dark, outline=OUTLINE)  # wide right leg
    draw.rectangle([2, 8, 13, 19], fill=body, outline=OUTLINE)  # massive torso
    draw.rectangle([1, 9, 2, 15], fill=body_dark, outline=OUTLINE)  # shoulder plate
    draw.rectangle([13, 9, 14, 15], fill=body_dark, outline=OUTLINE)  # shoulder plate
    draw.rectangle([7, 2, 9, 8], fill=body_dark, outline=OUTLINE)  # vent stack
    draw.rectangle([6, 4, 9, 8], fill=body, outline=OUTLINE)  # head
    draw.point([(7, 6), (8, 6)], fill=accent)

    return upscale(img, 4)  # -> 64x96


def make_player(body, body_dark, accent) -> Image.Image:
    # 16x24 canvas, human silhouette (round head, cloak, hat brim) -- deliberately
    # distinct from make_golem's boxy tripod-and-eye shape so player and golem read
    # apart at a glance even sharing the same warm wood-and-brass palette.
    w, h = 16, 24
    img = Image.new("RGBA", (w, h), TRANSPARENT)
    draw = ImageDraw.Draw(img)

    # Legs.
    draw.rectangle([5, 18, 7, 23], fill=body_dark, outline=OUTLINE)
    draw.rectangle([9, 18, 11, 23], fill=body_dark, outline=OUTLINE)
    # Cloak/torso, wider at the hem than the shoulders.
    draw.polygon([(6, 9), (10, 9), (13, 18), (3, 18)], fill=body, outline=OUTLINE)
    # Arms.
    draw.rectangle([2, 10, 3, 15], fill=body_dark, outline=OUTLINE)
    draw.rectangle([13, 10, 14, 15], fill=body_dark, outline=OUTLINE)
    # Head.
    draw.ellipse([5, 2, 11, 9], fill=STONE_LIGHT, outline=OUTLINE)
    # Hat brim -- the accent color, standing in for a golem's glowing eye.
    draw.rectangle([3, 3, 13, 4], fill=accent, outline=OUTLINE)

    return upscale(img, 4)  # -> 64x96


def make_building_block(fill, fill_dark) -> Image.Image:
    w, h = 16, 16
    img = Image.new("RGBA", (w, h), TRANSPARENT)
    draw = ImageDraw.Draw(img)
    draw.rectangle([1, 3, 14, 14], fill=fill, outline=OUTLINE)
    draw.rectangle([1, 3, 14, 5], fill=fill_dark, outline=OUTLINE)
    draw.rectangle([6, 7, 9, 10], fill=BRASS_DARK, outline=OUTLINE)
    return upscale(img, 4)  # -> 64x64


def make_item_icon(fill, fill_dark) -> Image.Image:
    w, h = 8, 8
    img = Image.new("RGBA", (w, h), TRANSPARENT)
    draw = ImageDraw.Draw(img)
    draw.rectangle([1, 1, 6, 6], fill=fill, outline=OUTLINE)
    draw.rectangle([1, 1, 6, 3], fill=fill_dark)
    return upscale(img, 4)  # -> 32x32


def make_ghost_placeholder() -> Image.Image:
    base = make_building_block(BRASS, BRASS_DARK)
    r, g, b, a = base.split()
    a = a.point(lambda v: v // 2)
    return Image.merge("RGBA", (r, g, b, a))


def main() -> None:
    save(make_floor_tile(STONE, STONE_LIGHT), "floor_tile.png")
    save(make_floor_tile(WOOD_MID, WOOD_LIGHT, accent=BRASS), "floor_tile_accent.png")

    save(make_golem(COPPER, COPPER_DARK, TEAL_GLOW), "golem_generic_copper.png")
    save(make_golem(BRASS, BRASS_DARK, TEAL_GLOW), "golem_generic_brass.png")
    save(make_golem(STEEL, STEEL_DARK, TEAL_GLOW), "golem_generic_steel.png")

    save(make_clockwork_scavenger(COPPER, COPPER_DARK, TEAL_GLOW), "chassis_clockwork_scavenger.png")
    save(make_brass_presser(BRASS, BRASS_DARK, TEAL_GLOW), "chassis_brass_presser.png")
    save(make_aether_hauler(STEEL, STEEL_DARK, TEAL_GLOW), "chassis_aether_hauler.png")
    save(make_mainspring_overclocker(WOOD_MID, WOOD_DARK, TEAL_GLOW), "chassis_mainspring_overclocker.png")
    save(make_zeppelin_freight_loader(BRASS_DARK, WOOD_DARK, TEAL_GLOW), "chassis_zeppelin_freight_loader.png")

    save(make_player(WOOD_MID, WOOD_DARK, BRASS), "player.png")

    save(make_building_block(WOOD_DARK, WOOD_MID), "building_block.png")

    save(make_item_icon(STONE_LIGHT, STONE), "item_scrap.png")
    save(make_item_icon(BRASS, BRASS_DARK), "item_brass.png")
    save(make_item_icon(TEAL_GLOW, STEEL_DARK), "item_aether.png")

    save(make_ghost_placeholder(), "ghost_placeholder.png")


if __name__ == "__main__":
    main()
