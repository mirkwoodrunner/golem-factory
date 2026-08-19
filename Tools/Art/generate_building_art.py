"""Top-down pixel art for the three placeables still wearing the generic crate.

`building_block.png` is a tinted brown box, and it was standing in for six different buildings.
Three of them (the Clock Tower, the Boiler, the Hand-Crank Bench) were given real sprites in the
§1.5 art pass; these are the other three.

They are two different jobs, which is why they were missed together. `SteamPipePrefab` is built
by ProgressionSceneAuthoring.BuildPlaceable and only ever needed a sprite argument. `DepotPrefab`
and `GolemConstructionStationPrefab` are built by ApplyCost, which restores a cost and never
touches the SpriteRenderer at all -- so reading the tint table would never have led anyone to
them.

CONVENTIONS, matching the three sprites that already landed:
  * 64 art pixels to the world unit (one cell), so a sprite's WIDTH in pixels is its width in
    cells x 64.
  * Trimmed to alpha bounds and imported BottomCenter, so the sprite stands on its cell rather
    than floating above it. Nothing here draws padding under the footprint.
  * Warm brass/wood/iron palette with a near-black outline; the floor is warm brown, so cool
    greys are reserved for iron and read as a different material rather than as shadow.
  * TINT RETIRED. A tint exists to tell identical boxes apart; once a building has its own
    silhouette the tint is the thing that wrecks it, so all three are authored for Color.white.

    python Tools/Art/generate_building_art.py

Re-runnable and deterministic -- no randomness, so a re-run writes byte-identical files.
"""

import os

from PIL import Image, ImageDraw

OUT_DIR = os.path.join(
    os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))),
    "Assets", "_Project", "Art")

TRANSPARENT = (0, 0, 0, 0)
OUTLINE = (28, 18, 22, 255)

# Sampled from the three buildings that already have art, so these sit in the same world.
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


def save(image: Image.Image, name: str) -> None:
    """Writes trimmed to the drawing's alpha bounds -- see the BottomCenter note above."""
    bounds = image.getbbox()
    if bounds is not None:
        image = image.crop(bounds)

    os.makedirs(OUT_DIR, exist_ok=True)
    path = os.path.join(OUT_DIR, name)
    image.save(path)
    print(f"wrote {path} ({image.width}x{image.height})")


def _shade(color, amount):
    """Lighten (amount > 0) or darken (amount < 0) without leaving the palette's warmth."""
    r, g, b, a = color
    if amount >= 0:
        return (int(r + (255 - r) * amount), int(g + (255 - g) * amount),
                int(b + (255 - b) * amount), a)

    k = 1.0 + amount
    return (int(r * k), int(g * k), int(b * k), a)


def _hash2(x, y, salt=0):
    """Deterministic per-pixel noise -- a re-run must produce byte-identical files."""
    n = (x * 374761393 + y * 668265263 + salt * 2246822519) & 0xFFFFFFFF
    n = (n ^ (n >> 13)) * 1274126177 & 0xFFFFFFFF
    return ((n ^ (n >> 16)) & 0xFFFF) / 65535.0


def _panel(draw, box, fill, light, dark, texture=0.0, salt=0, image=None):
    """A block with a lit top edge, a shaded bottom one, and optional speckle.

    The speckle is what stops a 60x66 sprite reading as a flat rectangle at gameplay zoom --
    the boiler and the bench both carry it, and without it these three sat visibly apart from
    the rest of the set.
    """
    x0, y0, x1, y1 = box
    draw.rectangle([x0, y0, x1, y1], fill=fill, outline=OUTLINE)

    if texture > 0.0 and image is not None:
        pixels = image.load()
        for y in range(y0 + 1, y1):
            for x in range(x0 + 1, x1):
                noise = _hash2(x, y, salt)
                if noise > 0.80:
                    pixels[x, y] = _shade(fill, texture)
                elif noise < 0.20:
                    pixels[x, y] = _shade(fill, -texture)

    draw.line([(x0 + 1, y0 + 1), (x1 - 1, y0 + 1)], fill=light)
    draw.line([(x0 + 1, y1 - 1), (x1 - 1, y1 - 1)], fill=dark)


def _rivets(draw, xs, ys, color=None):
    """Two-pixel rivets: the cheapest detail that reads as fabricated metal."""
    color = color or BRASS_LIGHT
    for x in xs:
        for y in ys:
            draw.point([(x, y)], fill=color)
            draw.point([(x, y + 1)], fill=_shade(color, -0.45))


def _contact_shadow(draw, box):
    """A dark band along the footprint, so the sprite sits ON the tile instead of over it.

    BottomCenter puts the pivot on the last row of the drawing, so this is the last thing the
    eye reads before the floor -- the three sprites that already had art all carry one.
    """
    x0, y0, x1, y1 = box
    draw.rectangle([x0, y0, x1, y1], fill=(38, 24, 26, 190))


def make_steam_pipe() -> Image.Image:
    """A low brass pipe run lying across the tile.

    Deliberately the SHORTEST of the three. A pipe is the one placeable a player lays in long
    runs, and anything tall would wall off the room it is threaded through -- steam pipes are
    plumbing, not architecture, and they have to read as something you can see over.
    """
    width, height = 64, 34
    image = Image.new("RGBA", (width, height), TRANSPARENT)
    draw = ImageDraw.Draw(image)

    _contact_shadow(draw, (4, 28, 59, 31))

    # The run itself, edge to edge, so two pipes on neighbouring cells read as one line.
    _panel(draw, (0, 13, 63, 27), BRASS, BRASS_LIGHT, BRASS_DARK, 0.12, 11, image)
    # A cylinder needs a specular line, or it is a plank.
    draw.line([(1, 16), (62, 16)], fill=_shade(BRASS_LIGHT, 0.25))
    draw.line([(1, 25), (62, 25)], fill=_shade(BRASS_DARK, -0.2))

    # Flanges at both ends: what makes the join between two cells look deliberate.
    for x in (2, 55):
        _panel(draw, (x, 9, x + 6, 31), BRASS_DARK, BRASS, (60, 40, 22, 255), 0.10, 5, image)
        _rivets(draw, (x + 2,), (12, 26))

    # A pressure gauge on the crown, the one detail that says "steam" rather than "drainpipe".
    draw.ellipse([26, 2, 38, 14], fill=IRON, outline=OUTLINE)
    draw.ellipse([28, 4, 36, 12], fill=_shade(IRON, -0.25))
    draw.ellipse([29, 5, 35, 11], fill=BRASS_LIGHT)
    draw.line([(32, 8), (34, 6)], fill=OUTLINE)
    draw.point([(30, 5)], fill=(255, 246, 226, 255))

    return image


def make_depot() -> Image.Image:
    """A stacked pallet of crates under a brass-banded frame.

    Reads as STORAGE from its silhouette (a stepped stack, not a single box), because the depot
    is the one building a player has several of and needs to pick out of a row at a glance.
    """
    width, height = 64, 70
    image = Image.new("RGBA", (width, height), TRANSPARENT)
    draw = ImageDraw.Draw(image)

    _contact_shadow(draw, (1, 66, 62, 69))

    # Pallet, with the gaps between its bearers picked out.
    _panel(draw, (2, 58, 61, 67), WOOD_DARK, WOOD, (44, 26, 30, 255), 0.10, 3, image)
    for x in range(8, 58, 12):
        draw.rectangle([x, 61, x + 4, 65], fill=(40, 24, 28, 255))

    # Lower crate, full width, planked.
    _panel(draw, (4, 32, 59, 58), WOOD, WOOD_LIGHT, WOOD_DARK, 0.14, 7, image)
    for y in (39, 46, 52):
        draw.line([(5, y), (58, y)], fill=_shade(WOOD, -0.28))
        draw.line([(5, y + 1), (58, y + 1)], fill=_shade(WOOD, 0.12))
    draw.line([(31, 33), (31, 57)], fill=_shade(WOOD, -0.3))

    # Upper crate, narrower and offset -- the step that makes the stack legible in silhouette.
    _panel(draw, (10, 10, 47, 32), WOOD, WOOD_LIGHT, WOOD_DARK, 0.14, 19, image)
    for y in (17, 24):
        draw.line([(11, y), (46, y)], fill=_shade(WOOD, -0.28))
        draw.line([(11, y + 1), (46, y + 1)], fill=_shade(WOOD, 0.12))

    # Brass banding and corner brackets.
    for (x0, x1, y) in ((4, 59, 43), (10, 47, 21)):
        draw.line([(x0, y), (x1, y)], fill=BRASS)
        draw.line([(x0, y + 1), (x1, y + 1)], fill=BRASS_DARK)

    for (cx, cy) in ((5, 33), (55, 33), (11, 11), (43, 11)):
        _panel(draw, (cx, cy, cx + 4, cy + 4), BRASS_DARK, BRASS, (52, 34, 20, 255))

    _rivets(draw, (7, 56), (36,))

    # Manifest plate: a depot is addressed, and this is where its label would hang.
    _panel(draw, (21, 1, 38, 10), BRASS, BRASS_LIGHT, BRASS_DARK, 0.08, 23, image)
    for y in (4, 6):
        draw.line([(24, y), (35, y)], fill=BRASS_DARK)

    return image


def make_construction_station() -> Image.Image:
    """An assembly cradle: two uprights, a brass arch, and a golem-shaped gap under it.

    The gap is the point. This is the building golems come OUT of, so its silhouette is an empty
    frame rather than a solid mass -- the one shape in the set that reads as a doorway.
    """
    width, height = 78, 98
    image = Image.new("RGBA", (width, height), TRANSPARENT)
    draw = ImageDraw.Draw(image)

    _contact_shadow(draw, (0, 94, 77, 97))

    # Base plinth, with tread plates.
    _panel(draw, (0, 78, 77, 95), IRON_DARK, IRON, (34, 30, 44, 255), 0.12, 31, image)
    for x in range(5, 74, 12):
        _panel(draw, (x, 83, x + 6, 90), IRON, IRON_LIGHT, IRON_DARK)

    # Uprights, with a lit inner edge so the gap between them reads as depth.
    for (x0, x1) in ((2, 17), (60, 75)):
        _panel(draw, (x0, 18, x1, 79), IRON, IRON_LIGHT, IRON_DARK, 0.12, 41, image)
        draw.line([(x0 + 2, 20), (x0 + 2, 77)], fill=_shade(IRON_LIGHT, 0.15))
        draw.line([(x1 - 1, 20), (x1 - 1, 77)], fill=IRON_DARK)
        _rivets(draw, (x0 + 5, x1 - 5), (24, 44, 64), IRON_LIGHT)

    # Brass arch across the top, and the crossbeam that gives it a workshop's gantry look.
    _panel(draw, (0, 2, 77, 19), BRASS, BRASS_LIGHT, BRASS_DARK, 0.12, 53, image)
    draw.line([(2, 5), (75, 5)], fill=_shade(BRASS_LIGHT, 0.2))
    _panel(draw, (17, 20, 60, 29), BRASS_DARK, BRASS, (52, 34, 20, 255), 0.10, 59, image)
    _rivets(draw, (6, 71), (8,))

    # Chain hoist hanging in the gap: the cue that something is assembled here.
    for y in range(30, 46, 3):
        draw.point([(38, y), (38, y + 1)], fill=IRON_LIGHT)
        draw.point([(39, y + 1)], fill=IRON_DARK)

    _panel(draw, (31, 46, 46, 60), BRASS, BRASS_LIGHT, BRASS_DARK, 0.10, 67, image)
    draw.rectangle([35, 50, 42, 56], fill=BRASS_DARK)

    # Two lit indicator lamps on the arch, in the same teal every golem's eye uses.
    for x in (9, 68):
        draw.ellipse([x - 4, 6, x + 4, 14], fill=OUTLINE)
        draw.ellipse([x - 3, 7, x + 3, 13], fill=GLOW)
        draw.point([(x - 1, 9)], fill=(226, 255, 250, 255))

    return image


def make_freight_mast() -> Image.Image:
    """A mooring mast: a tall lattice tower with a docking ring and a landing pad at its foot.

    TALL AND THIN on purpose. It is the one building whose job is to be seen from the far side
    of the map -- a Zeppelin bound to it may be launching from anywhere -- so the silhouette has
    to read at a glance from outside its own screen, which a squat box cannot do.
    """
    width, height = 52, 120
    image = Image.new("RGBA", (width, height), TRANSPARENT)
    draw = ImageDraw.Draw(image)

    _contact_shadow(draw, (2, 116, 49, 119))

    # Landing pad: where the freight actually lands, so it is the widest thing at ground level.
    _panel(draw, (0, 100, 51, 115), IRON_DARK, IRON, (34, 30, 44, 255), 0.12, 71, image)
    for x in range(4, 48, 10):
        draw.rectangle([x, 104, x + 6, 111], fill=IRON)

    # Lattice tower: two legs and their cross-bracing, drawn as a zigzag rather than a solid
    # block so it reads as a frame you can see the sky through.
    for x in (14, 34):
        _panel(draw, (x, 24, x + 4, 101), IRON, IRON_LIGHT, IRON_DARK, 0.10, 73, image)

    for y in range(30, 100, 10):
        draw.line([(18, y), (34, y + 5)], fill=IRON_LIGHT)
        draw.line([(18, y + 5), (34, y)], fill=_shade(IRON, -0.2))

    # Docking ring and its beacon: the two details that say "something moors here".
    _panel(draw, (6, 12, 45, 24), BRASS, BRASS_LIGHT, BRASS_DARK, 0.10, 79, image)
    draw.ellipse([18, 2, 33, 17], outline=OUTLINE, fill=BRASS_DARK)
    draw.ellipse([21, 5, 30, 14], fill=BRASS_LIGHT)
    draw.ellipse([24, 8, 27, 11], fill=GLOW)
    _rivets(draw, (9, 42), (16,))

    return image


def main() -> None:
    save(make_freight_mast(), "freight_mast.png")
    save(make_steam_pipe(), "steam_pipe.png")
    save(make_depot(), "depot.png")
    save(make_construction_station(), "golem_construction_station.png")


if __name__ == "__main__":
    main()
