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


PIPE_TILE = 64          # one cell at PPU 64 -- these are FLOOR TILES, not standing buildings
PIPE_HALF = 11          # half the bore's width, so the run is 22px across
PIPE_COLLAR = 16        # half-width of the flange collar at a tile edge
PIPE_CENTRE = 31.5      # the axis, in the middle of a 64px tile


def save_tile(image: Image.Image, name: str) -> None:
    """Writes a 64x64 tile UNTRIMMED, unlike save() above.

    The difference is the whole reason this exists. save() crops to the alpha bounds because a
    standing building is pivoted BottomCenter on its own footprint. A pipe is a floor tile
    pivoted at its CENTRE, rotated in quarter turns at runtime, and it has to line up with the
    tile next to it -- so cropping an end cap to its ink would move its axis off the cell centre
    and shear the run.
    """
    os.makedirs(OUT_DIR, exist_ok=True)
    path = os.path.join(OUT_DIR, name)
    image.save(path)
    print(f"wrote {path} ({image.width}x{image.height})")


def _pipe_arm(draw, image, side: str) -> None:
    """One run of pipe from the tile's centre out to the named edge, with its end flange.

    `side` is in SCREEN terms ("n" is up), and PIL's y grows downward, so north is the low rows.
    The flange sits a few pixels in from the edge rather than on it: two neighbouring tiles then
    show a pair of collars at their shared boundary, which is what makes a joint read as bolted
    rather than as a seam in a texture.
    """
    lo, hi = int(PIPE_CENTRE - PIPE_HALF), int(PIPE_CENTRE + PIPE_HALF)
    if side in ("e", "w"):
        x0, x1 = (int(PIPE_CENTRE), PIPE_TILE - 1) if side == "e" else (0, int(PIPE_CENTRE))
        draw.rectangle([x0, lo, x1, hi], fill=BRASS)
        # A cylinder needs a specular line and a shadow line, or it is a plank. Both run ALONG
        # the axis, which is the one direction that survives the runtime quarter turns: rotate a
        # lengthwise highlight and it is still lengthwise. A baked top-light would not be.
        draw.line([(x0, lo + 3), (x1, lo + 3)], fill=_shade(BRASS_LIGHT, 0.10))
        draw.line([(x0, hi - 2), (x1, hi - 2)], fill=BRASS_DARK)
        draw.line([(x0, lo), (x1, lo)], fill=OUTLINE)
        draw.line([(x0, hi), (x1, hi)], fill=OUTLINE)
        fx = PIPE_TILE - 8 if side == "e" else 3
        draw.rectangle([fx, int(PIPE_CENTRE - PIPE_COLLAR), fx + 4,
                        int(PIPE_CENTRE + PIPE_COLLAR)], fill=BRASS_DARK, outline=OUTLINE)
        _rivets(draw, (fx + 1,), (int(PIPE_CENTRE) - 12, int(PIPE_CENTRE) + 9))
    else:
        y0, y1 = (0, int(PIPE_CENTRE)) if side == "n" else (int(PIPE_CENTRE), PIPE_TILE - 1)
        draw.rectangle([lo, y0, hi, y1], fill=BRASS)
        draw.line([(lo + 3, y0), (lo + 3, y1)], fill=_shade(BRASS_LIGHT, 0.10))
        draw.line([(hi - 2, y0), (hi - 2, y1)], fill=BRASS_DARK)
        draw.line([(lo, y0), (lo, y1)], fill=OUTLINE)
        draw.line([(hi, y0), (hi, y1)], fill=OUTLINE)
        fy = 3 if side == "n" else PIPE_TILE - 8
        draw.rectangle([int(PIPE_CENTRE - PIPE_COLLAR), fy,
                        int(PIPE_CENTRE + PIPE_COLLAR), fy + 4],
                       fill=BRASS_DARK, outline=OUTLINE)
        _rivets(draw, (int(PIPE_CENTRE) - 12, int(PIPE_CENTRE) + 9), (fy + 1,))


def _pipe_hub(draw, image) -> None:
    """The junction casting the arms meet in.

    Only drawn where more than two arms meet or where they meet at an angle -- a straight run
    has no casting, so a long pipe reads as one length rather than as a row of couplings.
    """
    lo, hi = int(PIPE_CENTRE - PIPE_HALF - 2), int(PIPE_CENTRE + PIPE_HALF + 2)
    # Flat, not speckled. The speckle _panel offers is what stops a large building face reading
    # as a flat rectangle; on a 26px casting it just reads as grit, and a casting a shade darker
    # than the bore already separates the junction from the runs meeting in it.
    _panel(draw, (lo, lo, hi, hi), _shade(BRASS, -0.13), BRASS_LIGHT, BRASS_DARK, 0.0, 71, image)
    _rivets(draw, (lo + 3, hi - 3), (lo + 3, hi - 5))


def make_steam_pipe_piece(sides: str) -> Image.Image:
    """One pipe tile, open on the given SCREEN sides ("e", "ew", "ne", "nes", "nesw").

    THE FIVE PIECES AND WHY THEY ARE FIVE. Steam/PipeShapeRules picks one of these from a cell's
    four neighbours and then rotates it, so the set only has to cover the shapes a quarter turn
    cannot reach: one open side, two opposite, two adjacent, three, four. Sixteen baked masks
    would be the same five pictures written sixteen times, and the rotation is exact -- these are
    axis-aligned at 64px with no baked light direction, which is the same property that lets the
    belt chevron be one sprite rather than four (docs/open-items.md 2).
    """
    image = Image.new("RGBA", (PIPE_TILE, PIPE_TILE), TRANSPARENT)
    draw = ImageDraw.Draw(image)

    for side in sides:
        _pipe_arm(draw, image, side)

    if sides not in ("ew", "ns"):
        _pipe_hub(draw, image)

    if sides == "e":
        # The capped end, and the one place the pressure gauge survives the move to tiles. It was
        # on every pipe when a pipe was a single hero sprite; on a laid run that repeated the same
        # dial every 64 pixels. A terminus is exactly where a gauge belongs, and it doubles as the
        # readable difference between "the run stops here" and "the run continues off-screen".
        _panel(draw, (10, 14, 20, 49), BRASS_DARK, BRASS, _shade(BRASS_DARK, -0.3), 0.0, 5, image)
        _rivets(draw, (13, 17), (18, 42))
        draw.ellipse([25, 20, 41, 36], fill=IRON, outline=OUTLINE)
        draw.ellipse([27, 22, 39, 34], fill=_shade(IRON, -0.25))
        draw.ellipse([28, 23, 38, 33], fill=BRASS_LIGHT)
        draw.line([(33, 28), (36, 24)], fill=OUTLINE)
        draw.point([(30, 24)], fill=(255, 246, 226, 255))

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


def make_slag_heap() -> Image.Image:
    """A cooling slag pile in an iron collar, with a burner grate at its foot.

    LOW AND WIDE, the opposite silhouette to the mast: this is a thing you dump into, and the
    heap itself is the readable shape. The grate says it burns something -- disposal costs Coke,
    and a pile with no burner would read as free.
    """
    width, height = 68, 52
    image = Image.new("RGBA", (width, height), TRANSPARENT)
    draw = ImageDraw.Draw(image)

    _contact_shadow(draw, (2, 48, 65, 51))

    # Iron collar: the retaining ring the pile sits in.
    _panel(draw, (0, 36, 67, 47), IRON_DARK, IRON, (34, 30, 44, 255), 0.12, 83, image)

    # The pile itself, drawn as three stepped mounds so it reads as loose material rather than
    # a solid block. Warm greys shading to a dull ember at the base of the middle mound.
    slag = (86, 74, 78, 255)
    slag_light = (124, 110, 112, 255)
    slag_dark = (54, 46, 52, 255)
    draw.polygon([(6, 37), (20, 14), (34, 37)], fill=slag, outline=OUTLINE)
    draw.polygon([(28, 37), (44, 8), (60, 37)], fill=slag, outline=OUTLINE)
    draw.polygon([(46, 37), (58, 20), (66, 37)], fill=slag_dark, outline=OUTLINE)

    pixels = image.load()
    for y in range(9, 37):
        for x in range(4, 66):
            if pixels[x, y][3] == 0 or pixels[x, y] == OUTLINE:
                continue
            noise = _hash2(x, y, 91)
            if noise > 0.82:
                pixels[x, y] = slag_light
            elif noise < 0.18:
                pixels[x, y] = slag_dark

    # Lit edges on the two sunward faces, so the mounds have direction.
    draw.line([(20, 15), (33, 36)], fill=slag_light)
    draw.line([(44, 9), (59, 36)], fill=slag_light)

    # The burner grate: where the Coke goes in.
    _panel(draw, (24, 40, 43, 46), (44, 30, 28, 255), IRON, (28, 20, 22, 255))
    for x in range(26, 42, 4):
        draw.line([(x, 41), (x, 45)], fill=(226, 120, 46, 255))

    _rivets(draw, (4, 62), (39,), IRON_LIGHT)
    return image


def make_scrap_recycler() -> Image.Image:
    """A hopper on legs with a chute, a firebox and a full crate of Scrap underneath.

    THE SILHOUETTE HAS TO SAY "IN AT THE TOP, OUT AT THE BOTTOM", because that is the one thing
    about this building a player must read without a tooltip: unlike the Slag Heap beside it, the
    recycler gives something back, and a golem has to be sent to collect it. So it is TALL where
    the heap is low and wide -- a funnel, not a pile -- and the output crate sits proud at the
    foot in Scrap's own rusty palette rather than the machine's iron.

    The firebox is the same warm grate the heap wears, and for the same reason: this burns Coke
    too, and a machine with no visible fire would read as free.
    """
    width, height = 68, 60
    image = Image.new("RGBA", (width, height), TRANSPARENT)
    draw = ImageDraw.Draw(image)

    _contact_shadow(draw, (3, 56, 64, 59))

    # The hopper mouth: a wide inverted trapezoid, open at the top. Drawn before the body so the
    # body's rim overlaps it and the mouth reads as recessed.
    mouth = (72, 66, 60, 255)
    draw.polygon([(4, 4), (63, 4), (52, 22), (15, 22)], fill=mouth, outline=OUTLINE)
    draw.polygon([(9, 6), (58, 6), (49, 19), (18, 19)], fill=(46, 40, 38, 255))
    # Junk in the throat -- three chips of different greys, so a full hopper is legible at a
    # glance and the mouth is obviously where things go.
    draw.polygon([(20, 12), (26, 8), (30, 14)], fill=(128, 116, 104, 255), outline=OUTLINE)
    draw.polygon([(32, 15), (38, 10), (43, 16)], fill=(96, 88, 92, 255), outline=OUTLINE)
    draw.polygon([(43, 13), (48, 11), (49, 17)], fill=(140, 120, 96, 255), outline=OUTLINE)

    # Body: the iron drum the grinding happens in.
    _panel(draw, (13, 21, 54, 40), IRON, IRON_LIGHT, IRON_DARK, 0.10, 57, image)

    # A brass inspection band across the drum, so it is not one flat slab of iron.
    _panel(draw, (13, 28, 54, 33), (150, 108, 46, 255), (196, 152, 74, 255), (98, 68, 26, 255))
    draw.ellipse([31, 29, 36, 32], fill=(44, 60, 52, 255), outline=OUTLINE)

    # Firebox at the drum's foot: where the Coke goes.
    _panel(draw, (17, 35, 32, 40), (44, 30, 28, 255), IRON, (28, 20, 22, 255))
    for x in range(19, 32, 4):
        draw.line([(x, 36), (x, 39)], fill=(226, 120, 46, 255))

    # Legs, leaving the crate visibly UNDER the machine rather than beside it.
    draw.rectangle([16, 40, 20, 52], fill=IRON_DARK, outline=OUTLINE)
    draw.rectangle([47, 40, 51, 52], fill=IRON_DARK, outline=OUTLINE)

    # The chute, angling out from under the drum toward the crate.
    draw.polygon([(34, 39), (46, 39), (44, 47), (36, 47)], fill=IRON, outline=OUTLINE)

    # Output crate: Scrap's own rusty palette, deliberately NOT the machine's iron, so the thing
    # you collect reads as a different substance from the thing that made it.
    rust = (122, 76, 48, 255)
    _panel(draw, (22, 45, 46, 55), rust, (158, 104, 66, 255), (78, 46, 28, 255), 0.16, 29, image)
    draw.line([(22, 49), (46, 49)], fill=(78, 46, 28, 255))
    # A heaped lip, so the crate reads as full rather than as a closed box.
    draw.polygon([(26, 45), (31, 41), (36, 45)], fill=rust, outline=OUTLINE)
    draw.polygon([(35, 45), (40, 42), (44, 45)], fill=(158, 104, 66, 255), outline=OUTLINE)

    _rivets(draw, (15, 52), (23, 38), IRON_LIGHT)
    return image


def main() -> None:
    save(make_scrap_recycler(), "scrap_recycler.png")
    save(make_slag_heap(), "slag_heap.png")
    save(make_freight_mast(), "freight_mast.png")
    # The pipe is now a FLOOR TILE FAMILY, not one standing sprite: five pieces, centre-pivoted
    # and square, rotated in quarter turns by Buildings/PlaceableSteamPipe. See
    # Steam/PipeShapeRules for which piece a cell gets. "steam_pipe.png" keeps its name and
    # stays the straight run, so every existing reference to it still resolves.
    save_tile(make_steam_pipe_piece("ew"), "steam_pipe.png")
    save_tile(make_steam_pipe_piece("e"), "steam_pipe_end.png")
    save_tile(make_steam_pipe_piece("ne"), "steam_pipe_corner.png")
    save_tile(make_steam_pipe_piece("nes"), "steam_pipe_tee.png")
    save_tile(make_steam_pipe_piece("nesw"), "steam_pipe_cross.png")
    save(make_depot(), "depot.png")
    save(make_construction_station(), "golem_construction_station.png")


if __name__ == "__main__":
    main()
