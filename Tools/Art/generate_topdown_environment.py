#!/usr/bin/env python3
"""Square, TOP-DOWN environment tiles for the projection switch.

This is a NEW file rather than surgery on generate_placeholder_art.py, which still holds ~600
lines of 2:1 isometric diamond drawing (make_wood_floor_tile, make_wall_segment, make_floor_edge,
...). Keeping the isometric generator intact means the old art stays reproducible while the
top-down set is being judged, and there is exactly one place to delete when it wins.

Target: cozy, detailed, Stardew-Valley-adjacent -- warm varnished wood read at a glance, with
enough per-plank variation that a floor of these does not visibly tile, but never so busy that
it fights the golem sprites standing on it. The floor is background.

Output: Assets/_Project/Art/, overwriting the diamond tiles of the same name.
"""

import colorsys
import os
import random

from PIL import Image, ImageDraw

OUT_DIR = os.path.join(os.path.dirname(__file__), "..", "..", "Assets", "_Project", "Art")

TILE = 64  # square, one cell, at PPU 64 => exactly 1 world unit

# The market stalls are SHAPE work, not texture work, so they are drawn with ImageDraw while
# everything above stays on raw pixel access. That split is deliberate: per-brick and per-plank
# tone variation needs pixel writes, and a scalloped awning does not.
TRANSPARENT = (0, 0, 0, 0)
OUTLINE = (28, 19, 13, 255)

# Warm wood-and-brass palette, carried over from generate_placeholder_art.py so the top-down
# floor sits in the same world as the buildings already imported.
# Values tuned against the art already in the game rather than by eye. The first pass measured
# floor median luminance 91 / saturation 0.60 against boiler 63/0.43, bench 85/0.46, tower 80/0.47
# -- i.e. the floor was BRIGHTER and MORE saturated than everything standing on it, so props read
# as dark blobs on a bright ground. These target ~72 / ~0.44, which settles the floor behind the
# sprites and leaves headroom for the grain highlights. The hue (~26 degrees, warm orange-brown)
# is unchanged: it was already right.
#
# The four tones now span ~35 in the red channel (was 13). That span is what separates one board
# from the next; the previous set differed by less than the seam lines drawn between them, which
# is why the floor read as a flat field with rules on it rather than as timber.
# Four base tones that differ in HUE as well as value -- 18 to 34 degrees, warm red-brown to
# warm ochre. They were all one hue, which capped the whole floor at six hue degrees no matter
# what _shade did on top, because _shade's swing scales with the shade amount and most board
# tones are shaded by less than +/-0.1.
PLANK_TONES = [
    (56, 40, 33, 255),
    (64, 46, 34, 255),
    (70, 51, 36, 255),
    (76, 57, 40, 255),
]
# Three seam tones, not one. A single flat value drawn 64px wide was carrying most of the tile's
# contrast budget and reading as a ruled line.
PLANK_SEAMS = [
    (40, 26, 19, 255),
    (46, 31, 22, 255),
    (35, 23, 16, 255),
]
PLANK_JOINT = (44, 29, 21, 255)
GRAIN_DARK = (52, 35, 26, 255)
GRAIN_LIGHT = (95, 68, 52, 255)
# Brass pulled down from luminance 154 to ~122 and desaturated toward the boiler's brass; it was
# out-contrasting every building in the game.
BRASS = (72, 57, 33, 255)
BRASS_LIGHT = (84, 67, 40, 255)
BRASS_DARK = (52, 41, 25, 255)
# Warmed off hue 147 -- the only cool colour anywhere in this game. The first correction landed
# at saturation 0.14, i.e. still effectively neutral grey, and left the tile BRIGHTER than the
# floor it is set into. More red, and the void lifted to read as shadowed floor seen through the
# bars rather than as a hole.
IRON = (96, 80, 64, 255)
IRON_DARK = (58, 47, 38, 255)
# You must be able to see THROUGH a grate. At (72,58,47) the void was 12 luminance below
# IRON_DARK, so the grate read as a solid grey panel -- a manhole cover, or floor damage.
VOID = (30, 24, 19, 255)

# The room set (walls, props, belt) was authored at the OLD pre-correction saturation, so after
# the floor was pulled to 0.42 the walls and crates were left ~20% hotter than the ground they
# stand on. These are the corrected room tones: saturation ~0.44, value pulled down 8-10.
ROOM_PLASTER = (70, 48, 39, 255)
ROOM_MORTAR = (50, 37, 30, 255)
ROOM_PANEL = (62, 44, 35, 255)
ROOM_PANEL_DARK = (44, 32, 26, 255)
# Props go UP while the floor goes down. They are the brightest mid-ground objects in the room;
# they used to be dimmer than the floor variant they were standing on, which is why a crate
# against the wall vanished in a squint test.
ROOM_CRATE = (120, 86, 66, 255)
ROOM_STAVE = (114, 82, 63, 255)
# THE BELT IS METAL, NOT WOOD. These were three tones out of the same brown family as the floor
# and at the same value, so a belt run read as a plank walkway -- in an automation game, where
# the belt is the most important readability object on screen. Cool-shifted dark iron puts the
# deck BELOW the floor's value band and the brass rails above it, so a line of belts traces
# legibly across a brown room from either direction.
BELT_FRAME = (44, 42, 47, 255)
BELT_DECK = (56, 54, 61, 255)
BELT_SLAT = (76, 74, 81, 255)
BELT_RAIL = (104, 84, 50, 255)


def _shade(color, amount):
    """Shade with a HUE SHIFT, the standard pixel-art ramp.

    The previous version lerped toward white going up and multiplied toward black going down.
    Both of those are hue-preserving, and every colour in this file comes out of this one
    function -- so the whole environment set was a single hue at a single saturation. Measured:
    floor_tile spent 78 distinct colours across hue 19-22 degrees, six degrees in total, while
    the hand-authored buildings it has to sit with span 36-45. That is the concrete reason the
    generated art read as procedural and the authored art did not.

    Real pigment does not just get lighter: a lit face swings warm and desaturates, a shadowed
    face swings cool and saturates. Doing that here lifts every asset in the file at once.
    """
    r, g, b, a = color
    h, s, v = colorsys.rgb_to_hsv(r / 255.0, g / 255.0, b / 255.0)
    h = (h + 0.090 * amount) % 1.0            # ~32 degrees of swing across the full ramp
    s = min(1.0, max(0.0, s - 0.30 * amount))
    v = min(1.0, max(0.0, v + 0.55 * amount))
    rr, gg, bb = colorsys.hsv_to_rgb(h, s, v)
    return (int(rr * 255), int(gg * 255), int(bb * 255), a)


_BOARD_TONES = {}


def rng_board_tone(board, seed):
    """Stable per-board tone jitter, memoised so a board keeps its value across scanlines."""
    key = (seed, board)
    if key not in _BOARD_TONES:
        _BOARD_TONES[key] = random.Random(seed * 131 + board).uniform(-0.12, 0.12)
    return _BOARD_TONES[key]


def _save(img, name):
    os.makedirs(OUT_DIR, exist_ok=True)
    path = os.path.join(OUT_DIR, name)
    img.save(path)
    print("wrote %s (%dx%d)" % (os.path.abspath(path), img.width, img.height))


# The tone each row carries ACROSS the tile boundary, keyed on the row and nothing else -- so
# every variant agrees on it, and any variant can sit to the left of any other.
#
# This is what makes the plank floor genuinely tileable in X. A board that reaches x=63 does not
# end there; it continues into x=0 of whatever tile is placed next. For that to be true the
# first and last segment of a row must be the SAME board, which means the same colour, in all
# four variants at once. Nothing else in the row is constrained, so the interior still varies.
#
# Y needs no equivalent: rows start at multiples of plank_height and 64/8 = 8 divides exactly,
# so the top of one tile already meets the bottom of the next on a plank seam.
_WRAP_RNG = random.Random(31337)
WRAP_TONES = {}


def wrap_tone(row):
    if row not in WRAP_TONES:
        base = PLANK_TONES[row % len(PLANK_TONES)]
        WRAP_TONES[row] = _shade(base, _WRAP_RNG.uniform(-0.10, 0.10))
    return WRAP_TONES[row]


def make_plank_floor(variant=0, plank_height=8, stagger=True):
    """Horizontal planks, the standard cozy-farmsim floor.

    Seeded per variant so the four variants differ but each is stable across runs -- a floor that
    reshuffles every regeneration is impossible to art-direct against.

    plank_height is 8, not 16. See the note on the cut count: at 16 the boards came out 1.64:1,
    which is a brick, and the floor read as masonry rather than timber. 8 still divides 64, so
    the tile keeps meeting its neighbour on a plank seam.

    NO PER-VARIANT ROW PHASE. It is tempting -- every variant putting its seams at the same
    absolute y makes long unbroken rules across a big floor -- but a phase offset and X-tiling
    are mutually exclusive: shifting variant B's rows means B's row structure no longer lines up
    with A's where they meet, so every vertical tile boundary becomes a mismatch in both tone and
    seam position. Continuous courses are also what a real plank floor has. The rules are
    softened instead, by keeping the seam low-contrast (see PLANK_SEAMS).
    """
    rng = random.Random(9000 + variant)
    img = Image.new("RGBA", (TILE, TILE), PLANK_TONES[variant % len(PLANK_TONES)])
    px = img.load()

    rows = TILE // plank_height
    for row in range(rows):
        top = row * plank_height
        bottom = min(top + plank_height, TILE)

        # FEW CUTS, LONG BOARDS. This used to make 1-2 cuts in a 16px-tall row, i.e. boards of
        # mean length 26px at height 16 -- an aspect of 1.64:1. A house brick is 2:1; a floorboard
        # is 8:1 to 20:1. Combined with the running-bond stagger that is literally stretcher-bond
        # masonry, which is why the floor read as brickwork and why the brick WALL disappeared
        # into it: same pattern, same scale, same value.
        #
        # Zero cuts is the common case now, so most rows are one board running the full width and
        # straight on into the next tile.
        cuts = []
        for _ in range(rng.choice([0, 1, 1, 2])):
            for _attempt in range(8):
                c = (rng.randrange(14, TILE - 14) + (23 if stagger else 0) * (row + variant)) % TILE
                if 14 < c < TILE - 14 and all(abs(c - e) > 10 for e in cuts):
                    cuts.append(c)
                    break
        bounds = [0] + sorted(cuts) + [TILE]

        for s in range(len(bounds) - 1):
            x0, x1 = bounds[s], bounds[s + 1]
            if s == 0 or s == len(bounds) - 2:
                # The board that straddles the tile boundary -- same colour in every variant.
                tone = wrap_tone(row)
            else:
                base = PLANK_TONES[(row + variant + s * 2) % len(PLANK_TONES)]
                # Widened from +/-0.06. Adjacent boards must differ by more than the seam
                # between them, or the seam is all the eye sees.
                tone = _shade(base, rng.uniform(-0.16, 0.16))
            for y in range(top, bottom):
                for x in range(x0, x1):
                    px[x, y] = tone

            # Seam along the top: CONTINUOUS, varying in tone along its length. It used to skip
            # 18% of its pixels at random, which on a 26px board is a morse-code dash rather than
            # a broken seam -- at 1:1 the floor was covered in dashed rules that read as scratches
            # and grit. A board seam is continuous; only its colour wanders.
            for x in range(x0, x1):
                px[x, top] = PLANK_SEAMS[(x // 7) % len(PLANK_SEAMS)]

            # Thickness highlight: halved, and only in a couple of broken runs rather than the
            # full width, with dithered ends.
            if top + 1 < TILE:
                hx = x0
                while hx < x1:
                    run = rng.randint(6, 16)
                    if rng.random() < 0.60:
                        for x in range(hx, min(hx + run, x1)):
                            edge = x - hx < 2 or (hx + run - x) < 2
                            if edge and rng.random() < 0.5:
                                continue
                            px[x, top + 1] = _shade(tone, 0.05)
                    hx += run + rng.randint(2, 7)

            # The joint itself -- interior board ends only. NOT at x1 == TILE: the board there
            # does not end, it WRAPS (see WRAP_TONES). Drawing a joint at x=63 as well would put
            # a dark column on every cell boundary in every row, which is a vertical rule down
            # the whole floor once per cell -- a worse artefact than the tone step it removes.
            if x1 < TILE:
                for y in range(top + 1, bottom):
                    px[x1 - 1, y] = PLANK_JOINT

            # Grain, wrapped so streaks cross cell boundaries instead of stopping dead at x=63.
            # LONG now (24-56px), because the boards are long: an 8-22px streak on a full-width
            # board reads as a dash, and a floor covered in dashes reads as grit, not timber.
            for _ in range(rng.randint(1, 2)):
                gy = rng.randrange(top + 2, bottom - 1)
                gx = rng.randrange(0, TILE)
                length = rng.randint(24, 56)
                grain = GRAIN_DARK if rng.random() < 0.65 else GRAIN_LIGHT
                for k in range(length):
                    px[(gx + k) % TILE, gy] = _shade(grain, rng.uniform(-0.05, 0.05))

            # Knots: RARE. At p=0.22 with 16px rows there was roughly one per tile, and at 1:1 a
            # 5x3 blob reads as a plus-sign, so the floor was strewn with little crosses. One good
            # knot every few tiles beats one bad knot on every tile.
            if rng.random() < 0.06:
                kx = rng.randrange(6, TILE - 6)
                ky = (top + bottom) // 2
                for dy in range(-1, 2):
                    for dx in range(-3, 4):
                        if (dx * dx) / 9.0 + (dy * dy) / 1.6 <= 1.0:
                            yy = ky + dy
                            if top < yy < bottom:
                                ring = abs(dx) >= 2 or abs(dy) == 1
                                px[(kx + dx) % TILE, yy] = _shade(
                                    GRAIN_DARK, -0.22 if ring else 0.08)

    return img


def make_brass_plate():
    """Accent tile: a riveted brass plate. Sparse by design -- brass is an accent, not a floor."""
    img = Image.new("RGBA", (TILE, TILE), BRASS_DARK)
    px = img.load()
    for y in range(TILE):
        for x in range(TILE):
            edge = min(x, y, TILE - 1 - x, TILE - 1 - y)
            if edge < 2:
                px[x, y] = _shade(BRASS_DARK, -0.25)
            elif edge < 4:
                px[x, y] = BRASS_DARK
            else:
                # A FLAT PLATE WITH A GLINT, not a ramp. Two prior attempts were wrong:
                # `((x+y) % 32)/32` was a wrapping airbrush gradient, and replacing it with three
                # bands keyed off (x + y) still produced three 27px wedges sweeping corner to
                # corner -- quantised, but still a ramp, and lit from the top-LEFT while every
                # other asset here is lit from the top of the screen.
                #
                # Keyed off y alone now, and reproportioned: a narrow highlight near the top over
                # a mostly flat field.
                if y < 9:
                    px[x, y] = BRASS_LIGHT
                elif y < 11:
                    px[x, y] = BRASS_LIGHT if (x + y) % 2 == 0 else BRASS
                elif y < TILE - 8:
                    px[x, y] = BRASS
                elif y < TILE - 6:
                    px[x, y] = BRASS if (x + y) % 2 == 0 else BRASS_DARK
                else:
                    px[x, y] = BRASS_DARK
    for cx, cy in ((7, 7), (TILE - 8, 7), (7, TILE - 8), (TILE - 8, TILE - 8)):
        px[cx, cy] = _shade(BRASS_LIGHT, 0.30)
        px[cx + 1, cy] = BRASS_DARK
        px[cx, cy + 1] = BRASS_DARK
        px[cx + 1, cy + 1] = _shade(BRASS_DARK, -0.2)
    return img


def make_grate():
    """Iron floor grate: dark voids between bars, for the boiler/steam areas."""
    img = Image.new("RGBA", (TILE, TILE), IRON_DARK)
    px = img.load()
    # PITCH MUST DIVIDE TILE, on BOTH axes. Pitch 11 was the x=63 brace bug reintroduced on y:
    # 64 % 11 = 9, so the bottom void band truncated to 2px and collided with the next tile's
    # bright top line at every horizontal cell boundary. 16 divides 64.
    pitch = 16
    # INVERTED from the first attempt: void is now the majority and the bars are thin. You should
    # see THROUGH a floor grate -- bar-dominant is what made it read as a ladder.
    bar = 6
    for y in range(TILE):
        for x in range(TILE):
            px[x, y] = IRON if (y % pitch) < bar else VOID
    for x in range(TILE):
        for y in range(TILE):
            if (y % pitch) == 0:
                px[x, y] = _shade(IRON, 0.14)
            if (y % pitch) == bar - 1:
                px[x, y] = IRON_DARK
    for x in range(0, TILE, 16):
        for y in range(TILE):
            px[x, y] = _shade(IRON, 0.10)
            px[(x + 1) % TILE, y] = IRON_DARK
    return img


def make_build_ghost():
    """Placement ghost: a SQUARE cell outline. Was a 128x64 diamond.

    This one and interaction_ring are why the game still read as isometric after the floor was
    squared off -- they sit on top of the floor and follow the cursor/player, so a diamond here
    is a diamond in the player's eye at all times.
    """
    img = Image.new("RGBA", (TILE, TILE), (0, 0, 0, 0))
    px = img.load()
    edge = (150, 220, 170, 210)
    fill = (110, 200, 140, 46)
    for y in range(TILE):
        for x in range(TILE):
            d = min(x, y, TILE - 1 - x, TILE - 1 - y)
            if d < 2:
                px[x, y] = edge
            elif d < 3:
                px[x, y] = (edge[0], edge[1], edge[2], 90)
            else:
                px[x, y] = fill
    # Corner ticks: reads as a placement reticle rather than a plain box.
    for cx, cy in ((0, 0), (TILE - 1, 0), (0, TILE - 1), (TILE - 1, TILE - 1)):
        for k in range(6):
            px[min(max(cx + (k if cx == 0 else -k), 0), TILE - 1), cy] = (235, 255, 240, 255)
            px[cx, min(max(cy + (k if cy == 0 else -k), 0), TILE - 1)] = (235, 255, 240, 255)
    return img


def make_interaction_ring():
    """Interaction highlight: a CIRCLE. Was a 2:1 ellipse for the isometric ground plane.

    Top-down means the ground plane is the screen plane, so a circle on the floor is a circle on
    screen -- squashing it to an ellipse is exactly the isometric tell.
    """
    img = Image.new("RGBA", (TILE, TILE), (0, 0, 0, 0))
    px = img.load()
    cx = cy = (TILE - 1) / 2.0
    outer = TILE / 2.0 - 1.5
    inner = outer - 3.0
    for y in range(TILE):
        for x in range(TILE):
            dx, dy = x - cx, y - cy
            dist = (dx * dx + dy * dy) ** 0.5
            if inner <= dist <= outer:
                px[x, y] = (245, 236, 205, 235)
            elif outer < dist <= outer + 1.2:
                px[x, y] = (245, 236, 205, 90)
    return img


def make_belt_tile():
    """Belt surface for one cell, running +X: a SQUARE lane. Was a 128x64 diamond.

    THE FRAME USED TO GO ALL FOUR WAYS. `min(x, y, TILE-1-x, TILE-1-y) < 3` put a bar across the
    direction of travel at every cell boundary, so a belt run read as a row of separate crates
    rather than as one continuous lane. Rails belong on the SIDES of a belt only.

    Also now metal rather than wood (see BELT_DECK), and carrying a direction chevron. In an
    automation game the belt is the object the player reads most often, and the old one had no
    arrow, no roller, no metal and no direction -- at floor value, in floor colours, it was a
    plank walkway.
    """
    img = Image.new("RGBA", (TILE, TILE), (0, 0, 0, 0))
    px = img.load()
    rail = 5
    for y in range(TILE):
        for x in range(TILE):
            if y < rail or y >= TILE - rail:
                inner = y == rail - 1 or y == TILE - rail
                px[x, y] = _shade(BELT_RAIL, -0.30 if inner else 0.0)
                if y == 0 or y == TILE - 1:
                    px[x, y] = _shade(BELT_FRAME, -0.20)
            else:
                # Slats ACROSS the lane, period 8 -- 8 divides 64, so they close across the cell
                # boundary and a run of belts reads as one moving surface.
                tone = rng_board_tone(x // 8, 5150)
                px[x, y] = (_shade(BELT_SLAT, tone) if (x % 8) < 3
                            else _shade(BELT_DECK, tone * 0.6))

    # Direction chevron every 16px (16 divides 64, so the cadence continues cell to cell).
    cy = TILE // 2
    for cx in range(8, TILE, 16):
        for k in range(6):
            for dy in (-k, k):
                x, y = (cx + 5 - k) % TILE, cy + dy
                if rail <= y < TILE - rail:
                    px[x, y] = _shade(BELT_RAIL, 0.26)
    return img


def make_ground_shadow():
    """Contact shadow: a near-round blob. Was a 96x48 2:1 ellipse.

    Kept slightly wider than tall (48x36, not 48x48) because the sprites standing on it are still
    drawn front-on, so a perfectly circular shadow reads as a hole rather than as contact.
    """
    w, h = 48, 36
    img = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    px = img.load()
    cx, cy = (w - 1) / 2.0, (h - 1) / 2.0
    for y in range(h):
        for x in range(w):
            nx = (x - cx) / (w / 2.0)
            ny = (y - cy) / (h / 2.0)
            dist = (nx * nx + ny * ny) ** 0.5
            if dist <= 1.0:
                # Quantised falloff -- a smooth gradient would read as non-pixel-art.
                alpha = int(130 * (1.0 - dist))
                alpha = (alpha // 26) * 26
                if alpha > 0:
                    px[x, y] = (24, 15, 10, alpha)
    return img


WALL_H = 96  # 1.5 cells tall at PPU 64 -- tall enough to enclose, short enough to see over
WALL_RAIL_Y = WALL_H - 34  # the brass rail course; the corner post reads this so they line up

# Three fixed tones for the sconce pool, brightest first. See the blend in make_wall.
LAMP_POOL = [(178, 138, 86, 255), (146, 110, 70, 255), (120, 90, 58, 255)]


def make_wall(lamp=False):
    """A straight, front-on wall face one cell wide.

    THE ISOMETRIC TELL THIS REMOVES: the old wall_segment_ne/nw were a MIRRORED PAIR whose brick
    courses and base line rose diagonally across the sprite (the 2:1 run). Two things follow
    under top-down. The slope goes -- a wall seen head-on has horizontal courses. And the mirror
    pair stops being meaningful, so ne/nw are now the same image written twice, kept as two names
    only because SandboxFloorGenerator's sprite/pivot table is keyed off them.
    """
    # ONE SEED FOR BOTH VARIANTS. Seeding on `lamp` re-rolled every brick tone, so the lit
    # segment was a different wall, not the same wall with a sconce on it -- and with a lamp
    # every fourth segment that put 7 visibly mismatched panels in a 25-segment run.
    rng = random.Random(4400)
    img = Image.new("RGBA", (TILE, WALL_H), (0, 0, 0, 0))
    px = img.load()

    rail_y = WALL_RAIL_Y
    mortars = [ROOM_MORTAR, _shade(ROOM_MORTAR, 0.10), _shade(ROOM_MORTAR, -0.12)]

    # PER-BRICK tone, the same treatment that rescued the plank segments. The first version gave
    # every brick in a course an identical value, which is why the wall measured 1.5 colours per
    # 1000 pixels against the reference art's 8.6-12.7 -- a flat field with a grid drawn on it.
    # EXACTLY TILE//16 COLUMNS, and the lookup below wraps on the same modulus. It used to be
    # `+ 2`, i.e. 6 buckets for 4 brick columns, so column 4 never wrapped to column 0. Even
    # courses hid it (their mortar lands on x=0) but odd courses are offset by half a brick, so
    # the brick straddling x=63|x=0 was painted from two different tones -- a hard vertical
    # tone step at every cell boundary, 24 of them across the north run.
    brick_tone = {}
    for course in range(WALL_H // 7 + 2):
        for col in range(TILE // 16):
            t = rng.uniform(-0.10, 0.10)
            if rng.random() < 0.12:
                t -= 0.16  # the occasional darker/chipped brick
            brick_tone[(course, col)] = t

    for y in range(WALL_H):
        for x in range(TILE):
            if y < rail_y - 3:
                # Upper: brick courses. HORIZONTAL, not sloped.
                course = y // 7
                offset = (course % 2) * 8
                brick_x = (x + offset) % 16
                if y % 7 == 0 or brick_x == 0:
                    px[x, y] = mortars[(course + x // 16) % len(mortars)]
                else:
                    px[x, y] = _shade(
                        ROOM_PLASTER, brick_tone[(course, ((x + offset) // 16) % (TILE // 16))])
            elif y < rail_y:
                px[x, y] = BRASS if y == rail_y - 2 else BRASS_DARK
            else:
                # Lower: wood panelling, per-board tone.
                inset = 6 <= x < TILE - 6 and rail_y + 5 <= y < WALL_H - 6
                board = (x - 6) // 13
                px[x, y] = (_shade(ROOM_PANEL, rng_board_tone(board, 4400))
                            if inset else ROOM_PANEL_DARK)
                if inset and (x == 6 or y == rail_y + 5):
                    px[x, y] = _shade(ROOM_PANEL, 0.16)

    # Contact shadow along the foot so the wall sits on the floor rather than floating.
    # DARKEST AT THE CONTACT. k counts up from the bottom-most row, i.e. AWAY from the body, so
    # the old (150, 90, 40) put the dark end at the outer edge with a pale gap against the wall
    # -- which reads as a dark line floating below the wall, the exact artefact this prevents.
    # make_floor_edge always had it the right way round; these two were the odd ones out.
    for x in range(TILE):
        for k, a in ((0, 40), (1, 90), (2, 150)):
            y = WALL_H - 1 - k
            px[x, y] = (24, 15, 10, a)

    if lamp:
        cx = TILE // 2
        for y in range(10, 20):
            px[cx, y] = BRASS_DARK
        for dy in range(6):
            for dx in range(-3 + dy // 2, 4 - dy // 2):
                px[cx + dx, 20 + dy] = BRASS if dy < 3 else BRASS_DARK
        # Warm pool of light, QUANTISED INTO THREE RINGS. The smooth radial falloff this replaces
        # added 126 distinct colours to an 11-colour wall -- an airbrushed halo, and the least
        # pixel-art thing in the set. Stepping it is the same discipline the ground shadow's
        # alpha already uses.
        for dy in range(-8, 16):
            for dx in range(-10, 11):
                yy, xx = 24 + dy, cx + dx
                if 0 <= yy < WALL_H and 0 <= xx < TILE:
                    fall = 1.0 - ((dx * dx) / 121.0 + (dy * dy) / 256.0) ** 0.5
                    if fall > 0:
                        # BLEND TOWARD A FIXED WARM COLOUR, do not shade what is already there.
                        # Shading quantised the falloff into 3 rings and then applied it to ~20
                        # different per-brick tones, giving 60 luminance values -- a soft multiply
                        # wearing a quantised falloff's clothes, and the least pixel-art thing in
                        # the file. Blending to a fixed target lands every pixel of a ring on the
                        # same colour, which is what makes a pool of light read as a pool.
                        ring = min(int(fall * 3), 2)
                        r, g, b, a = px[xx, yy]
                        target = LAMP_POOL[2 - ring]
                        t = (0.15, 0.32, 0.55)[ring]
                        px[xx, yy] = (
                            int(r + (target[0] - r) * t),
                            int(g + (target[1] - g) * t),
                            int(b + (target[2] - b) * t),
                            a,
                        )
    return img


SIDE_W = 40  # 3px shadow + 7px inboard face + 27px cap + 3px outer edge


def make_side_wall(mirror=False):
    """A wall running NORTH-SOUTH, for the east and west edges of the room.

    THIS SPRITE DID NOT EXIST UNDER ISOMETRIC AND ITS ABSENCE IS A REAL GAP THE PROJECTION
    SWITCH OPENED. Isometric had two mirrored wall runs and both of them tiled along a screen
    diagonal, so one sprite pair covered both back edges. Top-down splits that: make_wall is a
    face seen head-on and tiles along X, which is correct for the north edge and nonsense for
    the east/west edges -- running it down a vertical edge stacks 1.5-cell-tall sprites one cell
    apart, and the brass rail and panelling repeat as horizontal stripes down the side of the
    room. A wall along Y has to be its own art.

    What you see looking down at a wall that runs away from you is mostly its CAP -- the top
    face -- so the courses run ACROSS the band (perpendicular to its length) and repeat along
    it. Period 8 divides 64, so the strip tiles seamlessly cell to cell.

    WHY A CAP AND NOT A FRONT-ON FACE. An art review argued, fairly, that every other object in
    this game is a front-on elevation and a top-view cap is a second camera in the same room.
    The reason it stays a cap is that a face cannot tile along Y. A wall's face carries its
    structure -- brick field, brass rail, wainscot -- up the SCREEN, which for an east/west run
    is the same axis the run travels along. One sprite per cell stepping in Y would repeat that
    whole vertical structure every cell, which is the stacked-wall artefact this file exists to
    avoid. A face here would have to be a single sprite spanning the entire run, and the
    generator places one piece per edge index.
    So: cap, but built to the review's own fallback -- wide enough to read, with a visible
    inboard face beneath it, plaster rather than brick, and no left/right value asymmetry.

    Laid out inner-edge-first: 3px of contact shadow that overhangs onto the floor (the pivot
    puts those three columns inside the boundary line), 7px of inboard face, the cap, then a
    dark outer edge. `mirror` flips it for the west edge, where the room is on the other side.
    """
    rng = random.Random(4500)
    w, h = SIDE_W, TILE
    img = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    px = img.load()

    # Per-course mottling on the cap. course = y // 8 with 8 courses in 64, so it is period-64
    # and the strip tiles cell to cell.
    cap_tone = {}
    for course in range(h // 8):
        t = rng.uniform(-0.07, 0.07)
        if rng.random() < 0.15:
            t -= 0.10
        cap_tone[course] = t

    face_x0, cap_x0, out_x0 = 3, 10, w - 3
    for y in range(h):
        course = y // 8
        for x in range(w):
            if x < face_x0:
                # Contact shadow onto the floor -- semi-transparent, so the planks read through.
                # Darkest against the body (x=2), fading outward; see the note in make_wall.
                px[x, y] = (24, 15, 10, (40, 90, 150)[x])
            elif x < cap_x0:
                # THE INBOARD FACE: the sliver of vertical wall a player actually sees under the
                # cap. Without it the strip was a flat band of texture lying in the floor plane,
                # which is why it read as a runner rug or a brick path rather than as a wall.
                px[x, y] = _shade(ROOM_PLASTER, -0.30)
                if x in (5, 6):
                    # The brass rail, running along the wall's length -- the same rail as the
                    # north face, seen end-on, so the two runs share a horizontal language.
                    px[x, y] = BRASS if x == 5 else BRASS_DARK
            elif x < out_x0:
                # The cap: plain plaster seen from above, with a shadowed reveal where it meets
                # the face. Deliberately NOT brickwork -- brick at this scale is the same pattern
                # as the floor, which is what made the two read as one material.
                if x == cap_x0:
                    px[x, y] = _shade(ROOM_PLASTER, -0.20)
                else:
                    px[x, y] = _shade(ROOM_PLASTER, cap_tone[course] - 0.06)
            else:
                px[x, y] = _shade(ROOM_MORTAR, -0.22 if x == w - 1 else -0.10)

    # NO left/right value asymmetry. The first pass graded east and west differently, on the
    # reasoning that they sit at opposite grazing angles to an upper-left key. That reasoning
    # belongs to vertical faces; these are horizontal cap surfaces, and two horizontal surfaces
    # under one top key light are the same value. The only thing that legitimately differs is
    # which side the inboard face is on, which the mirror already handles.
    if mirror:
        img = img.transpose(Image.FLIP_LEFT_RIGHT)
    return img


def make_floor_edge():
    """The floor's south edge seen front-on: a raised deck and the joist face below it.

    Was a sloped, mirrored 64x80 iso pair, then a 14px flat bar of four colours -- which made
    the one edge the camera looks in through, the edge whose whole job is to sell the floor's
    thickness, the least authored asset in the set. The floor simply stopped against black.

    Three bands now: a light top lip (the deck's chamfer, catching the top key), the joist face
    with board divisions on the floor's own 16px plank scale, and a falloff into the dark.
    """
    h, lip, face = 24, 2, 14
    img = Image.new("RGBA", (TILE, h), (0, 0, 0, 0))
    px = img.load()
    for y in range(h):
        for x in range(TILE):
            if y < lip:
                px[x, y] = _shade(PLANK_JOINT, 0.26 if y == 0 else 0.12)
            elif y < lip + face:
                # Vertical board divisions every 16px, matching the plank scale above so the
                # edge reads as the ends of the same boards rather than as an applied trim.
                board = x % 16
                tone = -0.16 if board == 0 else (0.05 if board == 1 else 0.0)
                px[x, y] = _shade(PLANK_JOINT, tone)
            else:
                k = y - (lip + face)
                px[x, y] = (24, 15, 10, max(0, 170 - k * 22))
    return img


POST_W = 50  # see make_corner_post


def make_corner_post():
    """Corner pillar, wall-height. Was 48x256 -- four cells tall, sized for the iso room.

    WIDTH IS LOAD-BEARING, NOT TASTE. The pillar has to cover the junction where the head-on
    north run (which stops dead at x = +/-12.5) meets a side wall (whose body reaches 25/64 of
    a cell further out, to 12.891). At the 16px this was, the post spanned only +/-0.125 of a
    cell and left a 17 x 95 px hole of empty background at each north corner where the side
    wall's outer edge stopped against nothing. 50px, centre-pivoted, spans exactly +/-25/64 --
    which reaches the side wall's outer edge on either hand, so ONE symmetric sprite caps both
    corners and no mirrored variant is needed.
    """
    w = POST_W
    img = Image.new("RGBA", (w, WALL_H), (0, 0, 0, 0))
    px = img.load()
    for y in range(WALL_H):
        for x in range(w):
            edge = min(x, w - 1 - x)
            px[x, y] = _shade((78, 51, 32, 255), 0.14 if edge > 12 else -0.10)
    # ONE band, at the wall's own rail course, read from the same constant make_wall uses. The
    # three arbitrary bands at y = 14/46/78 lined up with nothing on the wall they cap -- the
    # rail sits at WALL_H - 34 = 62 -- so post and wall did not share a horizontal language and
    # the corner read as two unrelated objects butted together.
    for y in range(WALL_RAIL_Y, WALL_RAIL_Y + 4):
        for x in range(w):
            px[x, y] = BRASS if y < WALL_RAIL_Y + 2 else BRASS_DARK
    for x in range(w):
        px[x, WALL_H - 1] = (24, 15, 10, 150)
    return img


def make_crate():
    """Front-on crate with a FLAT top. The old one had a rhombus lid -- an iso top face."""
    w, h = 56, 56
    rng = random.Random(7710)
    img = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    px = img.load()

    # FOUR VERTICAL PLANKS with per-board tone, plus corner brackets. The first version was a
    # single flat field with one centre line, measured 1.6 colours per 1000 pixels, and read as
    # a flat brown door rather than a crate.
    plank_w = w // 4
    for y in range(h):
        for x in range(w):
            board = min(x // plank_w, 3)
            tone = rng_board_tone(board, 7710)
            edge = min(x, y, w - 1 - x, h - 1 - y)
            if edge < 2:
                px[x, y] = _shade(ROOM_CRATE, -0.32)
            elif x % plank_w == 0 and x > 0:
                px[x, y] = _shade(ROOM_CRATE, -0.26)  # gap between boards
            else:
                px[x, y] = _shade(ROOM_CRATE, tone)

    # Horizontal battens top and bottom.
    for y in range(h):
        for x in range(w):
            if 3 <= y < 9 or h - 10 <= y < h - 3:
                px[x, y] = _shade(ROOM_CRATE, 0.12 + rng.uniform(-0.03, 0.03))

    # Corner brackets.
    for cx0, cy0 in ((2, 2), (w - 8, 2), (2, h - 8), (w - 8, h - 8)):
        for dy in range(6):
            for dx in range(6):
                if dx < 2 or dy < 2:
                    px[cx0 + dx, cy0 + dy] = BRASS_DARK if (dx + dy) % 3 else BRASS

    for x in range(w):
        px[x, h - 1] = (24, 15, 10, 140)
    return img


def make_barrel():
    """Front-on barrel with a FLAT elliptical top, not an iso-angled one."""
    w, h = 40, 56
    img = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    px = img.load()

    # FOUR FLAT STAVE BANDS, not a cylinder ramp. `0.18 * (1 - abs(nx) * 1.6)` produced 18
    # distinct values across 36px -- an airbrushed tube. Quantising into staves is both more
    # pixel-art and more barrel-like, since a real barrel IS made of discrete staves.
    stave_steps = (0.20, 0.08, -0.04, -0.18)
    for y in range(h):
        for x in range(w):
            nx = (x - (w - 1) / 2.0) / (w / 2.0)
            bulge = 1.0 - 0.12 * (1.0 - abs((y - h / 2.0) / (h / 2.0)))
            if abs(nx) <= bulge:
                band = min(int(abs(nx) * 4), 3) if nx >= 0 else min(int(abs(nx) * 4), 3)
                # Light from the upper-left, consistent with the rest of the set.
                step = stave_steps[band] if nx < 0 else stave_steps[min(band + 1, 3)]
                px[x, y] = _shade(ROOM_STAVE, step)
                if abs(nx) > 0.001 and int(abs(nx) * 4) != int((abs(nx) - 0.05) * 4):
                    if (x + y) % 2 == 0:
                        px[x, y] = _shade(ROOM_STAVE, step - 0.05)

    # Flat cap: two steps, not a second ramp.
    for y in range(6):
        for x in range(w):
            nx = (x - (w - 1) / 2.0) / (w / 2.0)
            if abs(nx) <= 0.90 and (nx * nx) / 0.81 + ((y - 3) ** 2) / 9.0 <= 1.0:
                px[x, y] = _shade(ROOM_STAVE, 0.26 if y < 3 else 0.12)
    for band in (12, 30, 44):
        for y in range(band, band + 3):
            for x in range(w):
                if px[x, y][3] > 0:
                    px[x, y] = BRASS if y == band else BRASS_DARK
    for x in range(w):
        if px[x, h - 1][3] > 0:
            px[x, h - 1] = (24, 15, 10, 140)
    return img


# =========================================================================================
# THE MARKET STREET
#
# docs/game-design.md, the tabletop source of truth, has this and the digital adaptation lost
# it: "The Loading Docks: Resource markets on the edge of the board where raw materials
# (Scrap, Brass, Aether) arrive in full truckload shipments." What the digital version built
# instead was five ore boulders standing on the workshop floor.
#
# So raw goods are BOUGHT, not dug, and that finally makes the code honest: every node has been
# infinite since 5.1 (scarcity is access, not depletion). A seam that never empties is a fiction
# problem. A merchant who never runs out is just a merchant.
#
# COBBLES, NOT PLANKS. The street has to read as outside at a glance, from the tile alone, with
# no walls to help -- so it changes both TEXTURE (round stones against straight boards) and HUE
# (cool grey-blue against the floor's warm ochre). Those are the two channels the plank floor
# spends its whole budget on being warm and rectilinear, which is exactly why the contrast works.
# =========================================================================================

# Pulled DOWN, deliberately. The floor palette note above records this exact mistake being made
# once already: the ground was brighter and more saturated than everything standing on it, so
# props read as dark blobs. The first cobble pass landed at 60-85 luminance -- brighter than the
# plank floor's ~72 -- and the stalls sat on it as silhouettes. These sit just under the planks,
# so the street reads as a cooler, slightly darker surface and the stalls own the contrast.
COBBLE_TONES = [
    (52, 51, 57, 255),
    (60, 59, 65, 255),
    (46, 45, 51, 255),
    (66, 64, 70, 255),
]
COBBLE_MORTAR = (32, 31, 36, 255)
COBBLE_MOSS = (48, 58, 46, 255)
CANVAS = (198, 186, 164, 255)
CANVAS_DARK = (150, 139, 120, 255)
STALL_WOOD = (92, 66, 44, 255)
STALL_WOOD_DARK = (62, 43, 28, 255)
STALL_WOOD_LIGHT = (120, 90, 62, 255)

SETT_W, SETT_H = 16, 8          # both divide 64 exactly -- see the note in make_cobbles


def make_cobbles(variant=0):
    """Street setts. Cool and slightly darker than the plank floor, so the street reads as
    outside from the tile alone with no walls to help.

    IT HAS TO TILE, IN BOTH AXES. The first pass laid stones at an arbitrary pitch and drew each
    tile independently, so a paved street showed its 64px repeat as hard horizontal banding and
    every stone crossing a tile edge was cut against a mismatched neighbour. The fix is the same
    one the plank floor uses: make the pattern PERIODIC on the tile. Sett height 8 divides 64
    (eight exact rows) and width 16 divides 64 (four per row), alternate rows offset by half a
    sett, and any sett that runs past the right edge is drawn a second time wrapped to the left.
    """
    img = Image.new("RGBA", (TILE, TILE), COBBLE_MORTAR)
    d = ImageDraw.Draw(img)
    rng = random.Random(9100 + variant)

    def sett(x, y, w, tone, mossy, top, bot, rad):
        # THE JOINT HAS TO WOBBLE. Every stone sharing an exact top and bottom edge produces one
        # unbroken horizontal joint across the whole street, and an unbroken horizontal joint at
        # a regular pitch is precisely what brickwork IS -- which is why varying the widths and
        # the bond phase did not stop the first two passes reading as a brick wall laid flat.
        # A per-stone inset of 0-1 px at top and bottom keeps every stone inside its row band,
        # so the tile still wraps exactly, while the joint stops being a ruled line.
        d.rounded_rectangle([x + 1, y + top, x + w - 2, y + SETT_H - bot], radius=rad, fill=tone)
        d.line([(x + 3, y + top), (x + w - 4, y + top)], fill=_shade(tone, 0.14))
        d.line([(x + 3, y + SETT_H - bot), (x + w - 4, y + SETT_H - bot)], fill=_shade(tone, -0.12))
        if mossy:
            d.point((x + 3 + rng.randrange(max(1, w - 6)), y + 2 + rng.randrange(3)), fill=COBBLE_MOSS)

    # A FOUR-PHASE BOND, AND MIXED SETT WIDTHS. Two-phase alternation at a single width is
    # running bond -- it is what brickwork IS, so the first pass read as a brick wall laid flat
    # no matter how the stones were coloured. Real setts are laid in courses but not in a
    # two-phase stagger at one size. Both the phase cycle (0, 8, 4, 12) and the width pattern
    # repeat on a multiple of SETT_W, so the tile still wraps exactly.
    PHASES = (0, SETT_W // 2, SETT_W // 4, 3 * SETT_W // 4)
    WIDTHS = ((12, 20), (16, 12, 20), (20, 12), (16,))

    for row in range(TILE // SETT_H):
        offset = PHASES[row % len(PHASES)] - SETT_W
        widths = WIDTHS[row % len(WIDTHS)]
        x = offset
        i = 0
        while x < TILE + SETT_W:
            w = widths[i % len(widths)]
            tone = COBBLE_TONES[rng.randrange(len(COBBLE_TONES))]
            mossy = rng.random() < 0.07
            shape = (rng.randrange(2), rng.randrange(2), rng.choice((2, 3, 3, 4)))
            sett(x, row * SETT_H, w, tone, mossy, *shape)
            # The wrap: a sett crossing either edge is drawn again one tile over, with the SAME
            # jitter, which is what makes the seam invisible when the tile is laid next to itself.
            if x + w > TILE:
                sett(x - TILE, row * SETT_H, w, tone, False, *shape)
            if x < 0:
                sett(x + TILE, row * SETT_H, w, tone, False, *shape)
            x += w
            i += 1
    return img


def make_street_edge():
    """The street's far kerb seen front-on: the kerbstone's worn top and its face below.

    THE STONE COUNTERPART OF make_floor_edge, AND IT HAS TO BE ITS OWN ASSET. floor_edge_sw is a
    JOIST FACE -- the plank deck's thickness, in the plank palette, divided on the plank's own
    16px board pitch. That is exactly right where the workshop's raised floor stops against the
    road, and exactly wrong at the far side of a cobbled street, where it reads as a timber sill
    holding back the pavement. Reusing it was the obvious shortcut and it is the one that would
    have undone the whole point of paving the street in a different material.

    Same 64x24 geometry and the same top-of-canvas pivot as the joist face, so the road's two
    edges -- near, at the shop front; far, at the kerb -- hang the same distance below their
    boundary lines and frame the street symmetrically.
    """
    h, lip, face = 24, 3, 13
    img = Image.new("RGBA", (TILE, h), (0, 0, 0, 0))
    px = img.load()
    # Paler than any sett in COBBLE_TONES on purpose: a kerb is a dressed stone, cut and set,
    # not one of the rubble setts laid between them. The value difference is what makes the
    # line read as an edge at gameplay zoom, where 24px is three or four screen pixels.
    kerb = (78, 76, 83, 255)
    for y in range(h):
        for x in range(TILE):
            if y < lip:
                # The worn top, catching the same top key the deck chamfer does.
                px[x, y] = _shade(kerb, 0.22 if y == 0 else 0.09)
            elif y < lip + face:
                # Joints on the setts' own 16px pitch, so the kerb reads as the ends of the same
                # stonework rather than as an applied trim -- the rule make_floor_edge follows
                # against the planks.
                joint = x % SETT_W
                tone = -0.24 if joint == 0 else (0.07 if joint == 1 else 0.0)
                # A kerb face is in its own shadow toward the bottom.
                px[x, y] = _shade(kerb, tone - 0.013 * (y - lip))
            else:
                k = y - (lip + face)
                px[x, y] = (18, 17, 22, max(0, 175 - k * 22))
    return img


# BIGGER CARTS. The first pass drew each stall on a 64x96 canvas -- one cell wide -- and beside
# the player they read as toy barrows rather than as market carts a golem queues at. These are
# 96x120: one and a half cells wide, not quite two tall.
#
# A SPRITE MAY BE WIDER THAN ITS CELL. A stall still OCCUPIES one cell, exactly like the wall
# segments overhang their boundary and the corner post rises a cell and a half above its anchor.
# The street lays stalls on a two-cell pitch, so a 96-wide sprite centred on its cell reaches
# three quarters of a cell either side and never collides with its neighbour.
#
# Most of the added size goes into the CART, not the canopy. The awning grew by half; the counter
# body more than doubled and gained wheels and a splayed trestle. That is the half of a market
# barrow that says "this is a vehicle someone pushed here", and it was the half that was missing.
STALL_W, STALL_H = 96, 120
SHADOW_ROWS = 3


def _stall_canvas():
    return Image.new("RGBA", (STALL_W, STALL_H), TRANSPARENT)


def _contact_shadow(d, x0, x1):
    base = STALL_H - SHADOW_ROWS
    for x in range(x0, x1):
        d.point((x, base), fill=(24, 16, 10, 150))
        d.point((x, base + 1), fill=(24, 16, 10, 90))


def _stall_frame(d, awning, awning_dark):
    """The parts every cart shares: wheels, a splayed trestle, a deep counter, and a striped
    awning on posts. Drawn on the 96x120 canvas whose bottom rows are contact shadow, so a stall
    stands on its cell the same way a wall stands on its boundary."""
    # Wheels first, so the cart body overlaps them and they read as tucked underneath.
    for wx in (14, 70):
        d.ellipse([wx, 88, wx + 20, 108], fill=STALL_WOOD_DARK, outline=OUTLINE)
        d.ellipse([wx + 6, 94, wx + 14, 102], fill=STALL_WOOD, outline=OUTLINE)
        for a, b in [((wx + 10, 89), (wx + 10, 107)), ((wx + 1, 98), (wx + 19, 98))]:
            d.line([a, b], fill=STALL_WOOD)
        d.point((wx + 10, 98), fill=OUTLINE)

    # Trestle legs, splayed outward -- a straight pair reads as a table, a splayed pair as a cart.
    for lx, dx in ((10, -4), (86, 4)):
        d.polygon([(lx, 74), (lx + 4, 74), (lx + 4 + dx, 104), (lx + dx, 104)],
                  fill=STALL_WOOD_DARK, outline=OUTLINE)

    # Counter: a thick plank top with a deep boarded front, which is where the bulk lives.
    d.rectangle([2, 74, 93, 84], fill=STALL_WOOD_LIGHT, outline=OUTLINE)
    d.line([(4, 76), (91, 76)], fill=_shade(STALL_WOOD_LIGHT, 0.18))
    d.rectangle([5, 84, 90, 104], fill=STALL_WOOD, outline=OUTLINE)
    for by in (89, 95, 101):
        d.line([(7, by), (88, by)], fill=STALL_WOOD_DARK)
    # Iron strapping at each end -- Victorian cart hardware, and it breaks up the plank field.
    for sx in (10, 82):
        d.rectangle([sx, 85, sx + 3, 103], fill=(70, 62, 56, 255), outline=OUTLINE)

    # Posts carrying the canopy.
    for px in (8, 84):
        d.rectangle([px, 34, px + 4, 76], fill=STALL_WOOD_DARK, outline=OUTLINE)

    # Awning: a shallow scalloped canopy, striped in the stall's own colour.
    d.polygon([(0, 16), (95, 16), (88, 38), (7, 38)], fill=CANVAS, outline=OUTLINE)
    for i in range(0, 96, 14):
        d.polygon([(i + 2, 16), (i + 9, 16), (i + 8, 38), (i + 3, 38)], fill=awning)
    d.line([(0, 16), (95, 16)], fill=awning_dark)
    for i in range(6, 88, 9):
        d.arc([i, 34, i + 9, 43], 0, 180, fill=OUTLINE)
    d.line([(8, 38), (87, 38)], fill=CANVAS_DARK)
    _contact_shadow(d, 8, 88)


def make_stall_scrap():
    """The rag-and-bone cart. Scrap is salvage, so this is the cluttered one -- bent plate heaped
    on the counter and a cartwheel leaning against it. Names the Clockwork Scavenger directly."""
    img = _stall_canvas()
    d = ImageDraw.Draw(img)
    _stall_frame(d, (150, 88, 52, 255), (96, 54, 32, 255))
    # Heaped salvage, deliberately no two edges parallel.
    d.polygon([(12, 60), (32, 54), (40, 74), (14, 74)], fill=(150, 88, 52, 255), outline=OUTLINE)
    d.polygon([(38, 58), (56, 52), (64, 74), (40, 74)], fill=(116, 68, 40, 255), outline=OUTLINE)
    d.polygon([(62, 62), (84, 56), (86, 74), (64, 74)], fill=(150, 88, 52, 255), outline=OUTLINE)
    d.line([(16, 63), (30, 58)], fill=(182, 116, 72, 255))
    d.point([(24, 68), (50, 64), (74, 67)], fill=(70, 52, 40, 255))
    # A spare wheel leaning on the cart -- the silhouette tell at a distance.
    d.ellipse([60, 82, 92, 112], outline=OUTLINE, width=2)
    d.ellipse([70, 92, 82, 104], outline=STALL_WOOD_DARK)
    for a, b in [((76, 83), (76, 111)), ((61, 97), (91, 97))]:
        d.line([a, b], fill=STALL_WOOD_DARK)
    return img


def make_stall_coal():
    """The coal merchant. Sacks and a scuttle under the only sooty awning -- everything here is
    value-dark, so it reads as coal before any shape resolves.

    EVERY DARK SHAPE GETS A RIM. Coal is the one good whose own colour sits below the ground it
    stands on, so it cannot rely on fill alone: without a lit shoulder the whole cart reads as a
    hole punched in the street."""
    img = _stall_canvas()
    d = ImageDraw.Draw(img)
    _stall_frame(d, (74, 78, 88, 255), (44, 47, 54, 255))
    for sx in (12, 38, 64) :
        body = [(sx, 74), (sx + 3, 56), (sx + 10, 50), (sx + 17, 56), (sx + 20, 74)]
        d.polygon(body, fill=(52, 48, 56, 255), outline=OUTLINE)
        d.line([(sx + 3, 56), (sx + 10, 50)], fill=(120, 116, 130, 255))
        d.line([(sx + 1, 68), (sx + 3, 57)], fill=(96, 92, 104, 255))
        d.line([(sx + 6, 53), (sx + 14, 53)], fill=(138, 132, 146, 255))
        d.point([(sx + 7, 59), (sx + 13, 62), (sx + 10, 66)], fill=(24, 22, 28, 255))
    # Scuttle on the ground beside the cart.
    d.polygon([(2, 108), (7, 90), (27, 90), (30, 108)], fill=(58, 54, 62, 255), outline=OUTLINE)
    d.line([(7, 90), (27, 90)], fill=(122, 118, 132, 255))
    d.line([(3, 105), (7, 92)], fill=(96, 92, 104, 255))
    return img


def make_stall_ore(green=True):
    """The ore factor -- copper or zinc. Crates of graded rock rather than sacks, because ore
    comes in lots. COPPER ORE IS GREEN in the ground (malachite), which is worth drawing: it
    separates the stall you buy at from the orange ingot you smelt it into."""
    img = _stall_canvas()
    d = ImageDraw.Draw(img)
    if green:
        awning, awning_dark = (86, 132, 92, 255), (52, 84, 58, 255)
        rock, vein, spec = (104, 92, 80, 255), (96, 158, 104, 255), (150, 200, 150, 255)
    else:
        awning, awning_dark = (140, 168, 186, 255), (88, 112, 130, 255)
        rock, vein, spec = (110, 106, 100, 255), (186, 200, 210, 255), (226, 236, 244, 255)
    _stall_frame(d, awning, awning_dark)
    for cx in (10, 52):
        d.rectangle([cx, 54, cx + 34, 74], fill=STALL_WOOD_DARK, outline=OUTLINE)
        d.rectangle([cx + 3, 57, cx + 31, 72], fill=_shade(STALL_WOOD_DARK, -0.28))
        d.line([(cx + 3, 57), (cx + 31, 57)], fill=_shade(STALL_WOOD_DARK, 0.20))
        for ox, oy in [(cx + 5, 61), (cx + 15, 59), (cx + 24, 63), (cx + 11, 66)]:
            d.polygon([(ox, oy), (ox + 8, oy - 3), (ox + 11, oy + 5), (ox + 3, oy + 8)],
                      fill=rock, outline=OUTLINE)
            d.line([(ox + 3, oy + 1), (ox + 8, oy)], fill=vein)
            d.point((ox + 6, oy + 4), fill=spec)
    return img


def make_stall_aether():
    """The aether dealer, and the one that must NOT look like the others. No canvas awning and no
    cart: a lantern-lit cabinet on a plinth with a specimen under glass. Aether is the exotic good
    in this economy and its pitch should read as a curiosity shop beside four honest traders."""
    img = _stall_canvas()
    d = ImageDraw.Draw(img)
    aether = (96, 214, 200, 255)
    aether_dark = (36, 116, 118, 255)
    aether_lit = (186, 245, 238, 255)
    # Plinth. Widened with the carts: the cabinet is meant to read as a DIFFERENT KIND of pitch,
    # not a smaller one, and beside 96px carts the first version just looked undersized.
    d.rectangle([8, 98, 88, 112], fill=STALL_WOOD_DARK, outline=OUTLINE)
    d.line([(10, 100), (86, 100)], fill=STALL_WOOD)
    # Cabinet body with a peaked lid -- still taller and narrower in PROPORTION than a cart, which
    # is what keeps it distinct, but no longer smaller in absolute terms.
    d.polygon([(12, 34), (48, 12), (84, 34), (84, 100), (12, 100)],
              fill=STALL_WOOD_DARK, outline=OUTLINE)
    d.polygon([(12, 34), (48, 12), (84, 34)], fill=STALL_WOOD, outline=OUTLINE)
    d.line([(17, 32), (48, 15)], fill=STALL_WOOD_LIGHT)
    # Glazed front, with the glow spilling onto the frame.
    d.rectangle([20, 44, 76, 92], fill=aether_dark, outline=OUTLINE)
    for gx in (38, 58):
        d.line([(gx, 45), (gx, 91)], fill=_shade(aether_dark, 0.25))
    # The specimen: one standing shard, the same silhouette as the Aether good itself.
    d.polygon([(42, 48), (55, 64), (52, 88), (40, 88), (36, 64)], fill=aether, outline=OUTLINE)
    d.polygon([(42, 48), (55, 64), (45, 64)], fill=aether_lit)
    d.point([(42, 73), (49, 81)], fill=aether_lit)
    # Hanging lanterns -- a curiosity dealer works after dark.
    for lx in (2, 84):
        d.line([(lx + 4, 24), (lx + 4, 34)], fill=OUTLINE)
        d.ellipse([lx, 34, lx + 10, 45], fill=aether_dark, outline=OUTLINE)
        d.point((lx + 5, 39), fill=aether_lit)
    _contact_shadow(d, 10, 86)
    return img


def main():
    _save(make_cobbles(0), "street_cobble.png")
    _save(make_cobbles(1), "street_cobble_b.png")
    _save(make_street_edge(), "street_edge.png")
    _save(make_stall_scrap(), "stall_scrap.png")
    _save(make_stall_coal(), "stall_coal.png")
    _save(make_stall_ore(green=True), "stall_copper_ore.png")
    _save(make_stall_ore(green=False), "stall_zinc_ore.png")
    _save(make_stall_aether(), "stall_aether.png")

    _save(make_plank_floor(0), "floor_tile.png")
    _save(make_plank_floor(1), "floor_tile_wood_b.png")
    _save(make_plank_floor(2), "floor_tile_wood_c.png")
    _save(make_plank_floor(3), "floor_tile_wood_d.png")
    _save(make_brass_plate(), "floor_tile_accent.png")
    _save(make_grate(), "floor_tile_grate.png")

    # The overlays. These matter more than the floor for "does it read as top-down", because
    # they sit ON the floor and follow the cursor and the player.
    _save(make_build_ghost(), "build_ghost_tile.png")
    _save(make_interaction_ring(), "interaction_ring.png")
    _save(make_belt_tile(), "belt_tile.png")
    _save(make_ground_shadow(), "ground_shadow.png")

    # Room boundary. ne/nw are the same image under top-down (see make_wall) -- the mirrored
    # pair was an isometric artefact.
    wall, wall_lamp = make_wall(False), make_wall(True)
    _save(wall, "wall_segment_ne.png")
    _save(wall, "wall_segment_nw.png")
    _save(wall_lamp, "wall_segment_ne_lamp.png")
    _save(wall_lamp, "wall_segment_nw_lamp.png")
    # The east/west runs, which isometric never needed as separate art (see make_side_wall).
    _save(make_side_wall(False), "wall_side_e.png")
    _save(make_side_wall(True), "wall_side_w.png")

    # Only the SOUTH edge is skirted now -- it is the one edge left open so the camera can see
    # in. floor_edge_se is still written because the sprite/pivot table is keyed by edge name
    # and the west edge answers to "SouthEast"; it is the same band, unused by the generator.
    edge = make_floor_edge()
    _save(edge, "floor_edge_se.png")
    _save(edge, "floor_edge_sw.png")
    _save(make_corner_post(), "wall_corner_post.png")

    _save(make_crate(), "prop_crate.png")
    _save(make_barrel(), "prop_barrel.png")


if __name__ == "__main__":
    main()
