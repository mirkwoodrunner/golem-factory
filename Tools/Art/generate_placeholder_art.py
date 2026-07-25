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

    save(make_player(WOOD_MID, WOOD_DARK, BRASS), "player.png")

    save(make_building_block(WOOD_DARK, WOOD_MID), "building_block.png")

    save(make_item_icon(STONE_LIGHT, STONE), "item_scrap.png")
    save(make_item_icon(BRASS, BRASS_DARK), "item_brass.png")

    save(make_ghost_placeholder(), "ghost_placeholder.png")


if __name__ == "__main__":
    main()
