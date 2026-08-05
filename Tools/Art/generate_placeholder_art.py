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

# --- Environment palette -----------------------------------------------------------------
# The environment pass (floor/walls/edges) deliberately uses a *warmer, lower-contrast*
# extension of the palette above: the floor is background, so it must not fight the golem
# sprites, but it still has to read as varnished wood rather than the grey stone-noise it
# used to be. Plank tones stay inside a narrow warm-brown band; brass appears only on
# sparse accent tiles and wall rails so it stays an accent, not a texture.
PLANK_TONES = (
    (126, 82, 48, 255),
    (133, 88, 52, 255),
    (120, 78, 45, 255),
    (129, 85, 50, 255),
)
PLATE_FIELD = (78, 57, 35, 255)
PLANK_SEAM = (66, 41, 25, 255)
PLANK_JOINT = (84, 53, 32, 255)
PLANK_GRAIN = (104, 66, 38, 255)
PLASTER_BRICK = (124, 76, 54, 255)
PLASTER_MORTAR = (82, 58, 44, 255)
SHADOW_INK = (24, 15, 10, 255)


def _shade(color, amount):
    """Lighten (amount > 0) / darken (amount < 0) an RGBA tuple by a 0..1 fraction."""
    r, g, b, a = color
    if amount >= 0:
        return (
            int(r + (255 - r) * amount),
            int(g + (255 - g) * amount),
            int(b + (255 - b) * amount),
            a,
        )
    k = 1.0 + amount
    return (int(r * k), int(g * k), int(b * k), a)


def _hash2(x, y, salt=0):
    """Small deterministic integer hash -- keeps generated texture stable across runs
    (a random seed would make every regeneration a spurious diff)."""
    h = (x * 374761393) ^ (y * 668265263) ^ (salt * 2246822519)
    h = (h ^ (h >> 13)) * 1274126177
    return (h ^ (h >> 16)) & 0x7FFFFFFF


def _iso_cell_fraction(px, py, w):
    """Pixel center -> (u, v) cell fraction in [-0.5, 0.5], matching
    GridCoordinateConverter.WorldToCellFraction for a 1 x 0.5 cell drawn `w` px wide.
    +u is the +cellX (screen up-right) axis, +v is the +cellY (screen up-left) axis."""
    dx = (px + 0.5 - w * 0.5) / float(w)
    dy = -(py + 0.5 - w * 0.25) / float(w)
    return dx + 2.0 * dy, 2.0 * dy - dx


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


# =========================================================================================
# Environment: warm wood-and-brass workshop floor, perimeter walls, slab edges, shadows.
#
# ONE art-pixel scale for the whole environment: 32 art pixels per world unit, authored at
# native size and nearest-upscaled x4, imported at PPU 128. So:
#   art pixel   = 4 image px = 1/32 world unit
#   floor cell  = 1 x 0.5 world = 32 x 16 native = 128 x 64 image px
#   wall segment (one cell edge) = 0.5 world wide = 16 native = 64 image px
# That last line is the important one. The floor tiles previously imported at PPU 64, which
# made each 128x64 tile 2 x 1 world units -- twice its own cell -- so the painted diamonds
# never lined up with the grid at all. PPU 128 is what makes sprite size == cell size.
#
# 32 art px/world is chosen for the *look*: at the default orthographicSize of 5 that is
# roughly 3-4 screen pixels per art pixel, which reads as deliberate pixel art. Authoring
# finer would have produced exactly the high-frequency visual noise the old stone floor was
# criticised for.
# =========================================================================================

ENV_UPSCALE = 4
FLOOR_NATIVE_W = 32  # -> 128 px wide after the x4 upscale
FLOOR_NATIVE_H = 16  # -> 64 px tall
PLANKS_PER_CELL = 2  # boards 0.25 world units wide; 4 made the floor read as hatching


def make_wood_floor_tile(variant: int) -> Image.Image:
    """A varnished-plank isometric floor diamond. Planks run along the +cellX axis, so plank
    seams fall on constant-cellY lines and stay continuous across neighbouring cells -- that
    is what keeps the floor reading as a floor instead of a grid of separate tiles. `variant`
    shifts the board joints, the per-plank tone assignment, and the grain, so a handful of
    variants sprinkled by FloorTileVariant.Select break up the tiling."""
    w, h = FLOOR_NATIVE_W, FLOOR_NATIVE_H
    img = Image.new("RGBA", (w, h), TRANSPARENT)
    px = img.load()

    # One native pixel is this far in u/v units (|grad u| == |grad v| == sqrt(5)/w), so every
    # line width below stays exactly one pixel wide whatever the canvas size.
    px_uv = 5.0 ** 0.5 / w
    joint_spacing = 1.0  # one board end per cell along the plank -- boards are long

    for iy in range(h):
        for ix in range(w):
            u, v = _iso_cell_fraction(ix, iy, w)
            if abs(u) > 0.5 or abs(v) > 0.5:
                continue

            strip = int((v + 0.5) * PLANKS_PER_CELL)
            if strip >= PLANKS_PER_CELL:
                strip = PLANKS_PER_CELL - 1
            tone = PLANK_TONES[(strip + variant) % len(PLANK_TONES)]

            # Board joints: staggered per plank strip *and* per variant so no single joint
            # line ever runs unbroken across the whole floor.
            stagger = ((strip * 7 + variant * 5) % 12) / 12.0 * joint_spacing
            joint_phase = (u + 0.5 + stagger) % joint_spacing
            board = int((u + 0.5 + stagger) / joint_spacing)

            color = _shade(tone, ((_hash2(board, strip, variant) % 7) - 3) * 0.016)

            # Seams between planks (constant-cellY lines) -- the strongest cue, and the one
            # that gives the floor its isometric direction.
            phase = (v + 0.5) * PLANKS_PER_CELL % 1.0
            seam_dist = min(phase, 1.0 - phase) / PLANKS_PER_CELL
            if seam_dist < px_uv * 0.5:
                color = PLANK_SEAM
            elif seam_dist < px_uv * 1.45 and phase > 0.5:
                color = _shade(tone, 0.16)   # lit lip on the up-left side of each plank
            elif (strip + variant) % 2 == 0 and abs(seam_dist - px_uv * 1.6) < px_uv * 0.5:
                color = _shade(PLANK_GRAIN, -0.04)  # one grain line on every other plank

            # Board ends.
            if min(joint_phase, joint_spacing - joint_phase) < px_uv * 0.5:
                color = PLANK_JOINT

            px[ix, iy] = color

    return upscale(img, ENV_UPSCALE)


def make_brass_plate_tile() -> Image.Image:
    """Sparse accent tile: a riveted brass inspection plate set into the planks. Placed on a
    lattice by FloorTileVariant so the floor has landmarks and the eye can still read the
    isometric grid without a per-tile bevel on every plank tile."""
    w, h = FLOOR_NATIVE_W, FLOOR_NATIVE_H
    img = Image.new("RGBA", (w, h), TRANSPARENT)
    px = img.load()
    p = 5.0 ** 0.5 / w   # one native pixel, in u/v units

    for iy in range(h):
        for ix in range(w):
            u, v = _iso_cell_fraction(ix, iy, w)
            if abs(u) > 0.5 or abs(v) > 0.5:
                continue
            inset = 0.5 - max(abs(u), abs(v))
            upper = v > 0 or u > 0                  # the two edges facing the light
            if inset < p:
                color = (36, 24, 16, 255)                            # recess in the planks
            elif inset < p * 1.2:
                # Brass rim, lit on the two up-facing edges and in shadow on the others. Kept
                # to a single pixel and to a *darkened* brass: the rim wraps the whole tile, so
                # at full BRASS and two pixels wide it swamped the field and the plate read as
                # a pale blob rather than a dark plate with a metallic edge.
                color = _shade(BRASS_DARK, -0.20) if upper else (24, 16, 10, 255)
            else:
                color = PLATE_FIELD
                if int((u - v) * (w / 4.0)) % 2 == 0:                # diagonal tread ribs
                    color = _shade(BRASS_DARK, -0.28)
            px[ix, iy] = color

    draw = ImageDraw.Draw(img)
    cx, cy = w // 2, h // 2
    for rx, ry in ((cx - 6, cy), (cx + 5, cy), (cx - 1, cy - 3), (cx - 1, cy + 3)):
        draw.point([(rx, ry)], fill=BRASS_DARK)
        draw.point([(rx, ry + 1)], fill=(30, 20, 13, 255))
    return upscale(img, ENV_UPSCALE)


def make_grate_tile() -> Image.Image:
    """Rarer accent: a dark steam grate. Reads as depth in the floor, and the warm glow line
    hints at the boiler works under the workshop."""
    w, h = FLOOR_NATIVE_W, FLOOR_NATIVE_H
    img = Image.new("RGBA", (w, h), TRANSPARENT)
    px = img.load()

    for iy in range(h):
        for ix in range(w):
            u, v = _iso_cell_fraction(ix, iy, w)
            if abs(u) > 0.5 or abs(v) > 0.5:
                continue
            edge = max(abs(u), abs(v))
            if edge > 0.44:
                color = _shade(WOOD_DARK, -0.15)
            elif edge > 0.39:
                color = _shade(BRASS_DARK, -0.35)
            else:
                bar = int((v + 0.5) * 9) % 2 == 0
                if bar:
                    color = _shade(BRASS_DARK, -0.05)
                else:
                    color = (34, 24, 18, 255)
                    if abs(u) < 0.18 and abs(v) < 0.26:
                        color = (74, 40, 24, 255)  # faint furnace glow down the shaft
            px[ix, iy] = color

    return upscale(img, ENV_UPSCALE)


# --- Perimeter walls ---------------------------------------------------------------------
# A wall segment covers exactly ONE cell edge. In world units that edge is 0.5 wide and
# 0.25 tall (the 2:1 isometric run), so the sprite must be 32 x (16 + height) px at PPU 64
# with a base line that rises 16 px across its width. The previous wall art was 88x109 with
# a FLAT bottom edge placed one per perimeter *cell* (0.5 world apart) -- 2.75x too wide,
# with a silhouette that never matched the isometric run, which is exactly why the run read
# as a staircase of stacked blocks rather than a wall.
WALL_NATIVE_W = 16    # -> 32 px  == 0.5 world units
WALL_NATIVE_RISE = 8  # -> 16 px  == 0.25 world units
WALL_NATIVE_H = 60    # -> 120 px; 52 native px of wall face above the base line
WALL_FACE_H = 52      # 1.625 world units -- deliberately taller than a 1.5-unit golem so
                      # the back edges read as an enclosing room, not a balustrade.


def _wall_base_row(x, mirror):
    """Row (native, y-down) of the base line at column x. Non-mirrored rises to the LEFT,
    which is the +cellX boundary (screen up-right wall); mirrored rises to the RIGHT, the
    +cellY boundary. The //2 staircase is the exact 2:1 isometric step, so consecutive
    segments placed 16 native px apart join seamlessly."""
    step = (x // 2) if mirror else ((WALL_NATIVE_W - 1 - x) // 2)
    return (WALL_NATIVE_H - 1) - step


def wall_pivot(mirror: bool):
    """Normalized sprite pivot: the midpoint of the base line, i.e. the point that must land
    on the boundary anchor FloorLayout.GetNorthEastWallAnchor / ...NorthWest returns."""
    final_h = WALL_NATIVE_H * ENV_UPSCALE
    base_final_row = _wall_base_row(WALL_NATIVE_W // 2, mirror) * ENV_UPSCALE
    return (0.5, (final_h - base_final_row) / float(final_h))


def make_wall_segment(mirror: bool = False, lamp: bool = False) -> Image.Image:
    w, h = WALL_NATIVE_W, WALL_NATIVE_H
    face_h = WALL_FACE_H
    img = Image.new("RGBA", (w, h), TRANSPARENT)
    px = img.load()

    for x in range(w):
        base = _wall_base_row(x, mirror)
        for b in range(face_h):          # b = height above the base line, in native px
            y = base - b
            if y < 0:
                continue
            # Mirror the *detail* phase too, so the flipped wall's panels/bricks stay in
            # register with the un-flipped one where the two runs meet at the corner.
            xm = (w - 1 - x) if mirror else x

            if b <= 1:                                                 # contact shadow
                color = _shade(SHADOW_INK, 0.10)
            elif b <= 8:                                               # skirting board
                color = _shade(WOOD_DARK, -0.12 + (b - 2) * 0.03)
                if b == 8:
                    color = _shade(WOOD_LIGHT, -0.18)
            elif b <= 24:                                              # panelled dado
                # One raised panel per wall segment (period == WALL_NATIVE_W), not a picket
                # of narrow stripes -- at 32 art px per world unit narrow stripes alias into
                # a fence.
                inset = xm % WALL_NATIVE_W
                color = _shade(WOOD_MID, -0.20 + (b - 9) * 0.008)
                if inset <= 1 or inset >= WALL_NATIVE_W - 2 or b <= 10 or b >= 23:
                    color = _shade(WOOD_DARK, -0.08)                   # stile / rail frame
                elif inset == 2 or b == 11:
                    color = _shade(WOOD_MID, 0.14)                     # lit inner bevel
                elif inset == WALL_NATIVE_W - 3 or b == 22:
                    color = _shade(WOOD_DARK, -0.26)                   # shaded inner bevel
            elif b <= 27:                                              # brass chair rail
                # Deliberately BRASS_DARK, not BRASS. This rail runs the entire length of both
                # back walls; at full brass (plus bloom) it was the brightest thing on screen
                # and pulled the eye to the edge of the room instead of the factory floor.
                # The periodic brackets carry the glint instead.
                color = (_shade(BRASS_DARK, -0.45) if b == 25
                         else (_shade(BRASS_DARK, -0.12) if b == 26 else BRASS_DARK))
                if xm % 8 == 0:
                    color = _shade(BRASS, 0.10) if b == 27 else _shade(BRASS_DARK, -0.5)
            else:                                                      # warm brickwork
                row = (b - 28) // 3
                if (b - 28) % 3 == 0 or (xm + 4 * (row % 2)) % 8 == 0:
                    color = PLASTER_MORTAR
                else:
                    brick = (xm + 4 * (row % 2)) // 8
                    color = _shade(PLASTER_BRICK, ((_hash2(brick, row) % 5) - 2) * 0.05)
                # Ambient occlusion: darkest just above the rail, opening up toward the top.
                color = _shade(color, -0.30 + (b - 28) * 0.013)
                if b >= face_h - 3:                                    # wood coping
                    color = _shade(WOOD_DARK, 0.08 if b == face_h - 2 else -0.25)

            px[x, y] = color

    if lamp:
        # A brass sconce bracket + burning mantle, baked into the sprite rather than parented
        # as a separate object: a child sprite would need its own sort order relative to the
        # wall, and there is nothing to gain from that. The matching warm Light2D IS a child
        # object (lights do not sort), placed by SandboxFloorGenerator.
        cx = w // 2
        base = _wall_base_row(cx, mirror)

        def put(dx, b, color):
            gx, gy = cx + dx, base - b
            if 0 <= gx < w and 0 <= gy < h:
                px[gx, gy] = color

        # Warm spill on the brickwork behind the flame, drawn first so the fixture sits on it.
        for b in range(29, 47):
            for dx in range(-4, 5):
                gx, gy = cx + dx, base - b
                if not (0 <= gx < w and 0 <= gy < h) or px[gx, gy][3] == 0:
                    continue
                fall = 1.0 - (abs(dx) / 5.0) - abs(b - 40) / 14.0
                if fall > 0:
                    px[gx, gy] = _shade(px[gx, gy], 0.45 * fall)

        for b in (34, 35):                                    # bracket arm
            for dx in (-1, 0, 1):
                put(dx, b, BRASS_DARK)
        put(0, 36, BRASS)
        put(-1, 37, BRASS_DARK)
        put(1, 37, BRASS_DARK)
        put(0, 37, _shade(BRASS, 0.35))
        for dx in (-2, -1, 0, 1, 2):                          # lamp bowl
            put(dx, 38, _shade(BRASS, 0.15))
        for dx in (-1, 0, 1):
            put(dx, 39, (255, 206, 128, 255))
        put(0, 40, (255, 232, 176, 255))
        put(0, 41, (255, 248, 214, 255))

    return upscale(img, ENV_UPSCALE)


# --- Near-edge slab skirting -------------------------------------------------------------
# The two camera-facing edges stay open (classic isometric room), but the floor used to just
# stop dead against the background. A short slab side under the near edges gives the
# workshop a thickness so it reads as a raised platform in a dark room instead of a diamond
# floating in a void.
EDGE_NATIVE_H = 20  # 8 px of isometric rise + 12 px of slab thickness -> 32x40 final


def _edge_top_row(x, mirror):
    step = (x // 2) if mirror else ((WALL_NATIVE_W - 1 - x) // 2)
    return (WALL_NATIVE_RISE - 1) - step


def edge_pivot(mirror: bool):
    final_h = EDGE_NATIVE_H * ENV_UPSCALE
    top_final_row = _edge_top_row(WALL_NATIVE_W // 2, mirror) * ENV_UPSCALE
    return (0.5, (final_h - top_final_row) / float(final_h))


def make_floor_edge(mirror: bool = False) -> Image.Image:
    w, h = WALL_NATIVE_W, EDGE_NATIVE_H
    img = Image.new("RGBA", (w, h), TRANSPARENT)
    px = img.load()
    thickness = 12

    for x in range(w):
        top = _edge_top_row(x, mirror)
        xm = (w - 1 - x) if mirror else x
        for d in range(thickness):
            y = top + d
            if y >= h:
                continue
            if d == 0:
                color = _shade(WOOD_LIGHT, 0.14)                 # lit lip of the slab
            elif d <= 5:
                color = _shade(WOOD_MID, -0.10 - d * 0.05)       # facing board
                if xm % 8 == 0:
                    color = _shade(color, -0.22)                 # board joint, not a picket
            elif d <= 8:
                color = _shade(WOOD_DARK, -0.45)                 # joist / underside shadow
                if xm % 8 in (2, 3):
                    color = _shade(WOOD_DARK, -0.28)             # exposed joist end
            else:
                color = _shade(SHADOW_INK, 0.14 - (d - 9) * 0.05)
            px[x, y] = color

    return upscale(img, ENV_UPSCALE)


# --- Corner post -------------------------------------------------------------------------
POST_NATIVE_W = 12
POST_NATIVE_H = 64
POST_BASE_ROW = 60


def post_pivot():
    final_h = POST_NATIVE_H * ENV_UPSCALE
    return (0.5, (final_h - POST_BASE_ROW * ENV_UPSCALE) / float(final_h))


def make_corner_post() -> Image.Image:
    w, h = POST_NATIVE_W, POST_NATIVE_H
    img = Image.new("RGBA", (w, h), TRANSPARENT)
    px = img.load()

    for x in range(w):
        for b in range(POST_BASE_ROW + 1):
            y = POST_BASE_ROW - b
            if y < 0:
                continue
            if b <= 1:
                color = _shade(SHADOW_INK, 0.10)
            elif b in (9, 10, 27, 28, 45, 46, 55, 56):
                color = BRASS if b % 2 == 1 else BRASS_DARK      # brass banding
                if x in (0, w - 1):
                    color = _shade(color, -0.3)
            else:
                color = _shade(WOOD_DARK, 0.16 - abs(x - (w - 1) / 2.0) * 0.05)
            px[x, y] = color

    return upscale(img, ENV_UPSCALE)


def make_ground_shadow() -> Image.Image:
    """Soft isometric contact shadow. Alpha-only ramp so it multiplies into whatever floor
    variant it lands on rather than tinting toward one plank tone."""
    w, h = 24, 12
    img = Image.new("RGBA", (w, h), TRANSPARENT)
    px = img.load()
    r, g, b, _ = SHADOW_INK
    for iy in range(h):
        for ix in range(w):
            nx = (ix + 0.5 - w / 2.0) / (w / 2.0)
            ny = (iy + 0.5 - h / 2.0) / (h / 2.0)
            d = nx * nx + ny * ny
            if d >= 1.0:
                continue
            px[ix, iy] = (r, g, b, int(225 * (1.0 - d) ** 0.95))
    return upscale(img, ENV_UPSCALE)


# --- Workshop props ----------------------------------------------------------------------
# Without these the room is a well-lit empty wooden box; "cozy, detailed" needs clutter.
# Both are ground-anchored: the sprite pivot is the CENTRE of the base diamond, i.e. the
# cell centre, so the same CellToWorldCenter placement golems use works unchanged.

def _iso_box_bounds(x, cx0, half_w, half_h):
    """Lower boundary row of an isometric diamond of the given half-extents at column x."""
    dx = abs(x - cx0)
    if dx > half_w:
        return None
    return half_h * (1.0 - dx / float(half_w))


def prop_pivot(native_h, base_center_row):
    return (0.5, (native_h - base_center_row) / float(native_h))


CRATE_NATIVE_W, CRATE_NATIVE_H, CRATE_BASE_ROW = 24, 30, 23


def make_crate() -> Image.Image:
    w, h = CRATE_NATIVE_W, CRATE_NATIVE_H
    box_h = 16
    half_w, half_h = 11.5, 5.75
    cx0 = (w - 1) / 2.0
    top_c = CRATE_BASE_ROW - box_h
    img = Image.new("RGBA", (w, h), TRANSPARENT)
    px = img.load()

    for x in range(w):
        span = _iso_box_bounds(x, cx0, half_w, half_h)
        if span is None:
            continue
        # Top face.
        for y in range(int(round(top_c - span)), int(round(top_c + span)) + 1):
            if 0 <= y < h:
                color = _shade(WOOD_LIGHT, 0.06 if (y - top_c) < 0 else -0.04)
                if int(x + (y - top_c) * 2) % 6 == 0:
                    color = _shade(WOOD_DARK, 0.02)     # plank lines across the lid
                px[x, y] = color
        # Side faces.
        y0 = int(round(top_c + span))
        for y in range(y0, y0 + box_h):
            if not (0 <= y < h):
                continue
            left = x < cx0
            color = _shade(WOOD_MID, -0.34 if left else -0.14)
            d = y - y0
            if d in (0, box_h - 1) or x in (0, w - 1):
                color = _shade(WOOD_DARK, -0.30)         # frame edge
            elif d in (box_h // 2 - 1, box_h // 2):
                color = BRASS_DARK if left else BRASS    # brass strapping band
            elif int(x + d) % 7 == 0:
                color = _shade(color, -0.16)             # board seam
            px[x, y] = color

    return upscale(img, ENV_UPSCALE)


BARREL_NATIVE_W, BARREL_NATIVE_H, BARREL_BASE_ROW = 18, 30, 24


def make_barrel() -> Image.Image:
    w, h = BARREL_NATIVE_W, BARREL_NATIVE_H
    body_h = 17
    half_w, half_h = 8.0, 4.0
    cx0 = (w - 1) / 2.0
    top_c = BARREL_BASE_ROW - body_h
    img = Image.new("RGBA", (w, h), TRANSPARENT)
    px = img.load()

    for x in range(w):
        span = _iso_box_bounds(x, cx0, half_w, half_h)
        if span is None:
            continue
        for y in range(int(round(top_c - span)), int(round(top_c + span)) + 1):
            if 0 <= y < h:
                px[x, y] = _shade(WOOD_DARK, 0.24 if y < top_c else 0.12)
        y0 = int(round(top_c + span))
        for y in range(y0, y0 + body_h):
            if not (0 <= y < h):
                continue
            d = y - y0
            lit = 0.18 - abs(x - cx0) / half_w * 0.34
            color = _shade(WOOD_MID, lit)
            if x % 4 == 0:
                color = _shade(color, -0.22)             # stave seams
            if d in (2, 3, body_h - 4, body_h - 3):
                color = _shade(BRASS if lit > 0 else BRASS_DARK, lit * 0.5)  # hoops
            if d >= body_h - 1:
                color = _shade(SHADOW_INK, 0.12)
            px[x, y] = color

    return upscale(img, ENV_UPSCALE)


def generate_environment() -> None:
    # Floor: warm plank variants (floor_tile.png / floor_tile_accent.png keep their original
    # names so the existing Tile assets and their GUIDs survive the reskin).
    save(make_wood_floor_tile(0), "floor_tile.png")
    save(make_brass_plate_tile(), "floor_tile_accent.png")
    save(make_wood_floor_tile(1), "floor_tile_wood_b.png")
    save(make_wood_floor_tile(2), "floor_tile_wood_c.png")
    save(make_wood_floor_tile(3), "floor_tile_wood_d.png")
    save(make_grate_tile(), "floor_tile_grate.png")

    # Perimeter walls / slab edges / posts. Pivots are printed because they are not the
    # sprite centre -- they are the midpoint of the base line, and the Editor import step
    # has to set them as custom pivots for the segments to land on the boundary anchors.
    save(make_wall_segment(mirror=False), "wall_segment_ne.png")
    save(make_wall_segment(mirror=True), "wall_segment_nw.png")
    save(make_wall_segment(mirror=False, lamp=True), "wall_segment_ne_lamp.png")
    save(make_wall_segment(mirror=True, lamp=True), "wall_segment_nw_lamp.png")
    save(make_floor_edge(mirror=False), "floor_edge_se.png")
    save(make_floor_edge(mirror=True), "floor_edge_sw.png")
    save(make_corner_post(), "wall_corner_post.png")
    save(make_ground_shadow(), "ground_shadow.png")
    save(make_crate(), "prop_crate.png")
    save(make_barrel(), "prop_barrel.png")
    print("pivot wall_ne   =", wall_pivot(False))
    print("pivot wall_nw   =", wall_pivot(True))
    print("pivot edge_se   =", edge_pivot(False))
    print("pivot edge_sw   =", edge_pivot(True))
    print("pivot post      =", post_pivot())
    print("pivot crate     =", prop_pivot(CRATE_NATIVE_H, CRATE_BASE_ROW))
    print("pivot barrel    =", prop_pivot(BARREL_NATIVE_H, BARREL_BASE_ROW))


# =========================================================================================
# Belts: the lane itself, its scrolling direction arrows, the end rollers, and the three
# cargo items that ride on it.
#
# Same art scale as the environment above -- 32 art px per world unit, x4 upscale, PPU 128 --
# so a belt lane is exactly as chunky as the floor planks under it. The ONE hard constraint
# here: make_belt_lane must be uniform along X. BeltSegmentVisual stretches a single lane
# sprite to fit whatever length the segment is, so any per-column detail (rivets, seams,
# tread cleats) would smear by an arbitrary factor. All the "moving parts" of the belt are
# therefore in the scrolling arrow sprite, not baked into the lane.
#
# The arrow is authored WHITE with only a dark rim, because BeltSegmentVisual tints it at
# runtime (warm amber when flowing -> jam red when backed up). Tinting a pre-coloured sprite
# would multiply the two hues together and mud both ends of that readout.
# =========================================================================================

BELT_UPSCALE = 4
BELT_LANE_NATIVE = (32, 14)  # -> 1.0 x 0.4375 world at PPU 128

BELT_RAIL_LIGHT = (206, 163, 88, 255)
BELT_RAIL = (168, 126, 60, 255)
BELT_RAIL_DARK = (112, 80, 38, 255)
BELT_TREAD = (68, 51, 40, 255)
BELT_TREAD_LIGHT = (88, 67, 52, 255)
BELT_TREAD_DARK = (44, 33, 26, 255)

# Item palette: all three sit in the warm workshop range, separated by hue AND silhouette so
# they stay tellable apart at 3-4 screen px per art px (and for colour-blind players).
RUST = (150, 88, 52, 255)
RUST_LIGHT = (182, 116, 72, 255)
RUST_DARK = (96, 54, 32, 255)
INGOT = (214, 168, 76, 255)
INGOT_LIGHT = (243, 210, 132, 255)
INGOT_DARK = (146, 104, 42, 255)
AETHER = (96, 214, 200, 255)
AETHER_LIGHT = (186, 245, 238, 255)
AETHER_DARK = (36, 116, 118, 255)


def make_belt_lane() -> Image.Image:
    """A cross-section extruded along X: dark rim, brass side rails, dark leather tread with a
    single sheen row. Every column is identical -- see the module note above."""
    w, h = BELT_LANE_NATIVE
    rows = [
        OUTLINE,
        BELT_RAIL_LIGHT,
        BELT_RAIL,
        BELT_RAIL_DARK,
        BELT_TREAD_DARK,
        BELT_TREAD,
        BELT_TREAD,
        BELT_TREAD_LIGHT,
        BELT_TREAD,
        BELT_TREAD,
        BELT_TREAD_DARK,
        BELT_RAIL,
        BELT_RAIL_DARK,
        OUTLINE,
    ]
    assert len(rows) == h
    img = Image.new("RGBA", (w, h), TRANSPARENT)
    draw = ImageDraw.Draw(img)
    for y, color in enumerate(rows):
        draw.line([(0, y), (w - 1, y)], fill=color)
    return upscale(img, BELT_UPSCALE)  # -> 128x56


def make_belt_arrow() -> Image.Image:
    """A right-pointing chevron, white body + dark rim, so the runtime tint owns its colour."""
    # 6 x 8 art px -> 0.1875 x 0.25 world, i.e. a bit over half the lane's height. Bigger than
    # this and the arrows tile into a solid amber stripe that hides the tread they sit on.
    w, h, stroke = 6, 8, 2
    body = set()
    for r in range(h):
        # Mirror the stroke about the vertical midline so the apex lands on the right.
        run = r if r < h // 2 else (h - 1 - r)
        for k in range(stroke):
            body.add((run + k, r))

    img = Image.new("RGBA", (w, h), TRANSPARENT)
    px = img.load()
    rim = (26, 18, 12, 235)
    for (x, y) in body:
        for dx in (-1, 0, 1):
            for dy in (-1, 0, 1):
                nx, ny = x + dx, y + dy
                if 0 <= nx < w and 0 <= ny < h and (nx, ny) not in body:
                    px[nx, ny] = rim
    for (x, y) in body:
        px[x, y] = (255, 255, 255, 255)
    return upscale(img, BELT_UPSCALE)  # -> 24x32


def make_belt_roller() -> Image.Image:
    """Brass drum capping each end of a lane, so a belt terminates in machinery rather than
    just stopping mid-air."""
    w, h = 10, 12
    img = Image.new("RGBA", (w, h), TRANSPARENT)
    draw = ImageDraw.Draw(img)
    draw.ellipse([0, 1, w - 1, h - 2], fill=BELT_RAIL, outline=OUTLINE)
    draw.ellipse([2, 3, w - 3, h - 4], fill=BELT_RAIL_DARK)
    draw.line([(2, 3), (2, h - 4)], fill=BELT_RAIL_LIGHT)
    draw.line([(0, h - 3), (w - 1, h - 3)], fill=OUTLINE)
    return upscale(img, BELT_UPSCALE)  # -> 40x48


def _item_canvas():
    # 16 art px upscaled x2 -> a 32x32 file at PPU 64, i.e. 0.5 x 0.5 world. Same file size and
    # world size as the sprites these replace (so nothing needs rewiring), but authored at the
    # environment's 32-art-px-per-world density instead of 64, so item pixels are finally the
    # same size as floor pixels.
    return Image.new("RGBA", (16, 16), TRANSPARENT)


def make_scrap_item() -> Image.Image:
    """Jagged offcut of rusted plate -- irregular silhouette, warm rust, not cold grey."""
    img = _item_canvas()
    draw = ImageDraw.Draw(img)
    # Two overlapping angular offcuts rather than one blob: the stepped silhouette is what
    # separates Scrap from Brass's clean trapezoid at a glance, before colour is even read.
    draw.polygon([(6, 2), (13, 3), (13, 8), (7, 8)], fill=RUST_DARK, outline=OUTLINE)
    draw.polygon([(1, 8), (8, 6), (12, 9), (9, 13), (3, 13)], fill=RUST, outline=OUTLINE)
    draw.line([(2, 8), (8, 7)], fill=RUST_LIGHT)
    draw.line([(9, 4), (12, 4)], fill=RUST)
    # Bolt holes read as "salvaged hardware".
    draw.point([(5, 10), (9, 10)], fill=BELT_TREAD_DARK)
    return upscale(img, 2)  # -> 32x32


def make_brass_item() -> Image.Image:
    """Poured brass ingot -- a clean trapezoid, the most 'manufactured' of the three."""
    img = _item_canvas()
    draw = ImageDraw.Draw(img)
    draw.polygon([(3, 6), (12, 6), (14, 12), (1, 12)], fill=INGOT, outline=OUTLINE)
    draw.polygon([(4, 4), (11, 4), (12, 6), (3, 6)], fill=INGOT_LIGHT, outline=OUTLINE)
    draw.line([(3, 10), (13, 10)], fill=INGOT_DARK)
    draw.line([(5, 8), (9, 8)], fill=INGOT_LIGHT)
    return upscale(img, 2)  # -> 32x32


def make_aether_item() -> Image.Image:
    """Aether shard -- tall, pointed, and the only cool-hued item, so it never reads as metal."""
    img = _item_canvas()
    draw = ImageDraw.Draw(img)
    draw.polygon([(7, 1), (11, 6), (10, 14), (5, 14), (4, 6)], fill=AETHER, outline=OUTLINE)
    draw.polygon([(7, 1), (11, 6), (8, 6)], fill=AETHER_LIGHT)
    draw.polygon([(8, 7), (10, 7), (10, 13), (8, 13)], fill=AETHER_DARK)
    draw.line([(6, 4), (6, 12)], fill=AETHER_LIGHT)
    return upscale(img, 2)  # -> 32x32


def generate_belts() -> None:
    save(make_belt_lane(), "belt_lane.png")
    save(make_belt_arrow(), "belt_arrow.png")
    save(make_belt_roller(), "belt_roller.png")
    # These three keep their original file names (and therefore their GUIDs and every existing
    # scene reference) -- they are a reskin, not new assets.
    save(make_scrap_item(), "item_scrap.png")
    save(make_brass_item(), "item_brass.png")
    save(make_aether_item(), "item_aether.png")


# =========================================================================================
# Interaction affordances: the ring that marks the currently-targeted interactable, and the
# cell footprint the build ghost uses.
#
# Both are authored PURE WHITE with only alpha shaping them, for the same reason
# make_belt_arrow is: they are tinted at runtime (amber = ready / dim steel = out of range /
# red = blocked). Tinting pre-coloured art multiplies the two hues and muds both ends.
#
# They are also authored BRIGHT on purpose. The build ghost previously reused
# building_block.png, whose mean colour is (63, 42, 28) -- darker than the warm plank floor
# it sits on -- so multiplying it by a green or red tint could only ever produce something
# *darker* than the floor. Measured, the old valid/blocked pair differed by a contrast ratio
# of 1.06:1 against each other: effectively invisible. A near-white source is the only way a
# runtime tint can move the composite both above and below the floor's luminance.
#
# Same art scale as the environment: 32 art px per world unit, x4 upscale, PPU 128, so both
# are exactly one isometric cell (1.0 x 0.5 world units).
# =========================================================================================

INTERACTION_UPSCALE = 4
INTERACTION_NATIVE_W = 32
INTERACTION_NATIVE_H = 16


def _diamond_half_width(y: int) -> float:
    """Half-width in native px of the isometric cell diamond at row y."""
    cy = (INTERACTION_NATIVE_H - 1) / 2.0
    return (INTERACTION_NATIVE_W / 2.0) * (1.0 - abs(y - cy) / (INTERACTION_NATIVE_H / 2.0))


def make_interaction_ring() -> Image.Image:
    """A 2:1 isometric ring that sits under the currently-targeted interactable. Drawn as an
    ellipse inscribed in the cell diamond, with four cardinal ticks so it reads as a
    deliberate targeting reticle rather than a shadow."""
    img = Image.new("RGBA", (INTERACTION_NATIVE_W, INTERACTION_NATIVE_H), TRANSPARENT)
    draw = ImageDraw.Draw(img)
    white = (255, 255, 255, 255)
    soft = (255, 255, 255, 110)

    # Outer ring, then a dimmer inner ring one pixel in: two concentric strokes read as a
    # ring at 3-4 screen px per art px, where a single 1px stroke reads as a smudge.
    draw.ellipse([1, 0, 30, 15], outline=white)
    draw.ellipse([3, 1, 28, 14], outline=soft)

    # Cardinal ticks (E/W on the long axis, N/S on the short) -- the ring's silhouette alone
    # is nearly identical to GroundShadow's ellipse, and these are what separate them.
    draw.line([(0, 7), (2, 7)], fill=white)
    draw.line([(0, 8), (2, 8)], fill=white)
    draw.line([(29, 7), (31, 7)], fill=white)
    draw.line([(29, 8), (31, 8)], fill=white)
    draw.point((15, 0), fill=white)
    draw.point((16, 0), fill=white)
    draw.point((15, 15), fill=white)
    draw.point((16, 15), fill=white)
    return upscale(img, INTERACTION_UPSCALE)


def make_build_ghost_tile() -> Image.Image:
    """The build-mode cell footprint: a bright diamond outline with a translucent interior and
    solid corner brackets. Replaces building_block.png as the ghost sprite -- the question the
    ghost has to answer is "which cell", and a building silhouette answers "which building",
    which the build menu already says in words."""
    img = Image.new("RGBA", (INTERACTION_NATIVE_W, INTERACTION_NATIVE_H), TRANSPARENT)
    px = img.load()
    edge = (255, 255, 255, 255)
    fill = (255, 255, 255, 64)
    cx = INTERACTION_NATIVE_W / 2.0

    for y in range(INTERACTION_NATIVE_H):
        half = _diamond_half_width(y)
        if half < 0.5:
            continue
        left = int(round(cx - half))
        right = int(round(cx + half)) - 1
        for x in range(max(0, left), min(INTERACTION_NATIVE_W, right + 1)):
            px[x, y] = fill
        for x in (left, left + 1, right - 1, right):
            if 0 <= x < INTERACTION_NATIVE_W:
                px[x, y] = edge

    # Solid brackets at the four diamond vertices: the corners are where the eye checks
    # alignment against the floor's plank seams.
    draw = ImageDraw.Draw(img)
    draw.line([(0, 7), (4, 7)], fill=edge)
    draw.line([(0, 8), (4, 8)], fill=edge)
    draw.line([(27, 7), (31, 7)], fill=edge)
    draw.line([(27, 8), (31, 8)], fill=edge)
    draw.line([(13, 0), (18, 0)], fill=edge)
    draw.line([(14, 1), (17, 1)], fill=edge)
    draw.line([(13, 15), (18, 15)], fill=edge)
    draw.line([(14, 14), (17, 14)], fill=edge)
    return upscale(img, INTERACTION_UPSCALE)


def generate_interaction() -> None:
    save(make_interaction_ring(), "interaction_ring.png")
    save(make_build_ghost_tile(), "build_ghost_tile.png")


# =========================================================================================
# Facing-based spatial routing: the player-placeable belt tile, and the chevron used to show
# which way a golem/belt/ghost points.
#
# Only two new sprites, on purpose. The tile highlights that mark a golem's source and target
# cells reuse build_ghost_tile.png, which is already a pure-white one-cell diamond authored
# specifically to be tinted at runtime -- exactly what a "pull from here / push to there"
# overlay needs, in the two colours the palette actually supports.
#
# The belt tile is deliberately DIRECTION-NEUTRAL. Proper isometric belt art needs a mirrored
# NE/NW pair (as the wall segments do) plus per-direction variants; instead the tile is a
# plain tread platform and the chevron on top carries the direction. That keeps this to one
# rotatable overlay sprite instead of four hand-authored tiles, and the chevron is the part
# the player actually reads.
#
# Same scale as the rest of the environment: 32 art px per world unit, x4 upscale, PPU 128.
# =========================================================================================

ROUTING_UPSCALE = 4
ROUTING_NATIVE_W = 32
ROUTING_NATIVE_H = 16


def make_belt_tile() -> Image.Image:
    """One isometric cell of conveyor: a dark leather tread diamond inset inside a brass rim,
    so a run of them reads as continuous machinery laid into the floor."""
    img = Image.new("RGBA", (ROUTING_NATIVE_W, ROUTING_NATIVE_H), TRANSPARENT)
    px = img.load()
    cx = ROUTING_NATIVE_W / 2.0

    for y in range(ROUTING_NATIVE_H):
        half = _diamond_half_width(y)
        if half < 0.5:
            continue
        left = int(round(cx - half))
        right = int(round(cx + half)) - 1
        for x in range(max(0, left), min(ROUTING_NATIVE_W, right + 1)):
            # Two-row sheen band across the middle of the tread, so the surface reads as
            # leather catching the light rather than a flat hole in the floor.
            if y in (7, 8):
                px[x, y] = BELT_TREAD_LIGHT
            elif y in (6, 9):
                px[x, y] = BELT_TREAD
            else:
                px[x, y] = BELT_TREAD_DARK

        # Brass rim: the two outermost columns of every row. This is what makes adjacent
        # tiles read as separate segments instead of one undifferentiated dark mass.
        for x in (left, left + 1, right - 1, right):
            if 0 <= x < ROUTING_NATIVE_W:
                px[x, y] = BELT_RAIL if x in (left + 1, right - 1) else BELT_RAIL_DARK

    return upscale(img, ROUTING_UPSCALE)


def make_facing_arrow() -> Image.Image:
    """A small SOLID right-pointing triangle, pure white so the runtime tint owns its colour.

    Solid, and small, both learned the hard way. The first version was a bold outlined chevron
    at 9x12 art px: floating beside a golem it was nearly as tall as the tile it pointed off,
    and because it is rotated to an isometric angle (~27 or ~153 degrees, never a multiple of
    90) point-filtered sampling tore the two thin diagonal strokes into a jagged W that read as
    a lightning bolt rather than an arrow.

    A filled triangle has no interior strokes to alias, so it survives rotation to an arbitrary
    angle, and at 7x7 it reads as a marker on the tile instead of competing with the golem.
    """
    n = 7
    img = Image.new("RGBA", (n, n), TRANSPARENT)
    px = img.load()
    mid = (n - 1) / 2.0
    body = set()
    for y in range(n):
        # Width shrinks linearly toward the apex on the right.
        reach = int(round((1.0 - abs(y - mid) / (mid + 1.0)) * (n - 1)))
        for x in range(reach + 1):
            body.add((x, y))

    rim = (26, 18, 12, 235)
    for (x, y) in body:
        for dx in (-1, 0, 1):
            for dy in (-1, 0, 1):
                nx, ny = x + dx, y + dy
                if 0 <= nx < n and 0 <= ny < n and (nx, ny) not in body:
                    px[nx, ny] = rim
    for (x, y) in body:
        px[x, y] = (255, 255, 255, 255)
    return upscale(img, ROUTING_UPSCALE)


def generate_routing() -> None:
    save(make_belt_tile(), "belt_tile.png")
    save(make_facing_arrow(), "facing_arrow.png")


def generate_legacy_placeholders() -> None:
    """The original placeholder character/item sprites. NOT run by default any more: the
    golem chassis, player, and item sprites in Assets/_Project/Art/ have since been replaced
    with better art at different resolutions (see git history), and regenerating these would
    silently clobber it. Opt in with `--legacy` only if you actually want the placeholders
    back."""
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

    # NOTE: item_scrap/item_brass/item_aether used to be regenerated here from
    # make_item_icon. They now live in generate_belts() (the belt readability pass reskinned
    # them warm and gave each a distinct silhouette); regenerating the flat 8x8 icons from
    # here would silently undo that, exactly the way --legacy would clobber the chassis art.

    save(make_ghost_placeholder(), "ghost_placeholder.png")


def main() -> None:
    import sys

    only = [a for a in sys.argv[1:] if not a.startswith("--")]
    if not only or "environment" in only:
        generate_environment()
    if not only or "belts" in only:
        generate_belts()
    if not only or "interaction" in only:
        generate_interaction()
    if not only or "routing" in only:
        generate_routing()
    if "--legacy" in sys.argv:
        generate_legacy_placeholders()


if __name__ == "__main__":
    main()
