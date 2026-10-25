"""The Aether Hauler as a creature (art-style-bible.md invariant 17): a scripted, layered draft
of every frame in the three drawn facings, with a part map, so that

- each facing's base is the image-to-image start and ControlNet guide for ComfyUI, and
- every other frame is derived from the cleaned base by moving its rigid parts
  (Tools/Art/golem_set.py's compose step),
  so its surface texture never changes between frames.

The roster's "armoured cargo shuttle on treads" that "safely moves high-value, unstable Aether
crystals" (digital-design.md), the first carrier (progression-design.md §6): a stocky little
creature in cream painted armour on two caterpillar treads, a round helmet with a teal headlamp,
short arms holding up the cargo bed on its back, a mahogany crate of Aether crystals. Wide and
low where the others stand on legs, so its silhouette is its own (today's Hauler was a boxy
humanoid). Its work stroke heaves the crate up and tips it back. Copied from the Mainspring
Overclocker's overclocker_draft.py (art-pipeline-design.md §3.2); the shapes and poses are the
Hauler's, the drawing rules the same.

    python hauler_draft.py <out dir>   writes draft_<facing>_<state>.png (+ _8x on the key)
                                       and parts_<facing>_<state>.json for all 28 frames

Coordinates are pixels on the 64 x 96 canvas, y down, feet on row 95.
"""
import json
import math
import os
import sys

from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.normpath(os.path.join(HERE, "..", "..", "..", "..", "..", "Tools", "Art")))
from golem_set import layer_rank  # noqa: E402  (the per-facing layering rule)
PALETTE = os.path.normpath(os.path.join(HERE, "..", "..", "..", "..", "..", "Tools", "Art", "palette.json"))
RAMPS = {k: [tuple(int(c[i:i + 2], 16) for i in (1, 3, 5)) for c in v["colours"]]
         for k, v in json.load(open(PALETTE))["ramps"].items()}
# The draw order per facing is the chassis' own (chassis.json "layers"): the crate is behind the
# golem from the south and covers its back from the north, and the hands holding its near corners
# with it (the user's review: from behind, they showed through the crate).
LAYERS = json.load(open(os.path.join(HERE, "..", "chassis.json")))["layers"]
W, H = 64, 96
KEY = (40, 190, 70)
STATES = ["base", "idle_1", "work_0", "work_1", "work_2", "stall", "carry"]
FACINGS = ["south", "north", "east", "west"]
# Parts that move rigidly between frames: golem_set.py's compose copies the cleaned base's pixels for them,
# shifted by the frame's offset. The arms and the jumping crystals change shape, so they are always the draft's.
RIGID = ["treads", "body", "crate", "head"]


# --- shapes ------------------------------------------------------------------------------------

def rect(x0, y0, x1, y1):
    return {(x, y) for y in range(y0, y1 + 1) for x in range(x0, x1 + 1)}


def rrect(x0, y0, x1, y1, r):
    out = set()
    for y in range(y0, y1 + 1):
        for x in range(x0, x1 + 1):
            dx = max(x0 + r - x, 0, x - (x1 - r))
            dy = max(y0 + r - y, 0, y - (y1 - r))
            if dx * dx + dy * dy <= r * r + r * 0.6:
                out.add((x, y))
    return out


def ellipse(cx, cy, rx, ry):
    return {(x, y) for y in range(int(cy - ry) - 1, int(cy + ry) + 2) for x in range(int(cx - rx) - 1, int(cx + rx) + 2)
            if ((x - cx) / rx) ** 2 + ((y - cy) / ry) ** 2 <= 1.0}


def capsule(a, b, r):
    (x0, y0), (x1, y1) = a, b
    out = set()
    lx, ly = x1 - x0, y1 - y0
    ll = lx * lx + ly * ly or 1
    for y in range(int(min(y0, y1) - r) - 1, int(max(y0, y1) + r) + 2):
        for x in range(int(min(x0, x1) - r) - 1, int(max(x0, x1) + r) + 2):
            t = max(0.0, min(1.0, ((x - x0) * lx + (y - y0) * ly) / ll))
            px, py = x0 + t * lx, y0 + t * ly
            if (x - px) ** 2 + (y - py) ** 2 <= r * r:
                out.add((x, y))
    return out


def shift(points, dx, dy):
    return {(x + dx, y + dy) for x, y in points}


# --- the drawing -------------------------------------------------------------------------------

class Canvas:
    """Layers drawn back to front. Each layer is (part, ramp, pixels, tones, light); shading is
    per layer, from its own full shape, lit from the upper left: in each row the left fifth of
    the span is the light tone, the right quarter the shadow tone and the last pixel the darkest;
    the top row is lit, the bottom row shaded."""

    def __init__(self, facing, mirror=False):
        self.colour = {}
        self.part = {}
        self.facing = facing
        self.mirror = mirror  # west: the east profile's shapes mirrored, then shaded as ever
        self._ops = []
        self._done = False

    def _defer(self, part, op):
        """Every drawing call waits, then runs in the chassis' layer order for the facing
        (stable: within a part, in the order given)."""
        self._ops.append((layer_rank(self.facing, part, LAYERS), len(self._ops), op))

    def _flush(self):
        if not self._done:
            for _, _, op in sorted(self._ops, key=lambda o: (o[0], o[1])):
                op()
            self._done = True

    def _place(self, pixels):
        return {((W - 1 - x) if self.mirror else x, y) for x, y in pixels}

    def layer(self, part, ramp, pixels, tones=None, flat=None, light=True):
        self._defer(part, lambda: self._layer(part, ramp, pixels, tones, flat, light))

    def _layer(self, part, ramp, pixels, tones=None, flat=None, light=True):
        ramp_c = RAMPS[ramp]
        tones = tones or list(range(len(ramp_c)))
        pixels = {(x, y) for x, y in self._place(pixels) if 0 <= x < W and 0 <= y < H}
        rows = {}
        for x, y in pixels:
            rows.setdefault(y, []).append(x)
        ys = sorted(rows)
        for x, y in pixels:
            if flat is not None:
                t = flat
            else:
                xs = rows[y]
                xl, xr = min(xs), max(xs)
                f = (x - xl) / max(1, xr - xl)
                mid = tones[len(tones) // 2]
                t = mid
                if light:
                    if f < 0.22:
                        t = tones[-1]
                    elif f > 0.72:
                        t = tones[max(0, len(tones) // 2 - 1)]
                    if x == xr and xr - xl >= 3:
                        t = tones[0]
                    if y == ys[0] and t != tones[0]:
                        t = tones[min(len(tones) - 1, tones.index(t) + 1)] if t in tones else t
                    if y == ys[-1]:
                        t = tones[0]
            self.colour[(x, y)] = ramp_c[t]
            self.part[(x, y)] = part

    def dots(self, part, ramp, tone, points, size=2):
        self._defer(part, lambda: self._dots(part, ramp, tone, points, size))

    def _dots(self, part, ramp, tone, points, size=2):
        points = [((W - size - x) if self.mirror else x, y) for x, y in points]
        for x0, y0 in points:
            for y in range(y0, y0 + size):
                for x in range(x0, x0 + size):
                    if 0 <= x < W and 0 <= y < H:
                        self.colour[(x, y)] = RAMPS[ramp][tone]
                        self.part[(x, y)] = part

    def paint(self, part, ramp, tone, pixels):
        self._defer(part, lambda: self._paint(part, ramp, tone, pixels))

    def _paint(self, part, ramp, tone, pixels):
        for x, y in self._place(pixels):
            if 0 <= x < W and 0 <= y < H:
                self.colour[(x, y)] = RAMPS[ramp][tone]
                self.part[(x, y)] = part

    def image(self):
        self._flush()
        img = Image.new("RGBA", (W, H), (0, 0, 0, 0))
        px = img.load()
        for (x, y), c in self.colour.items():
            px[x, y] = c + (255,)
        # The silhouette takes the outline ramp, a lighter tone on the lit upper left
        # (art-style-bible.md §2.1).
        out = img.copy()
        op = out.load()
        for (x, y), c in self.colour.items():
            nbrs = [(x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)]
            if any(n not in self.colour for n in nbrs):
                op[x, y] = RAMPS["outline"][1 if (x < W // 2 and y < 60) else 0] + (255,)
        return out

    def part_map(self):
        self._flush()
        return {f"{x},{y}": p for (x, y), p in self.part.items()}



# --- the poses ---------------------------------------------------------------------------------

# Per frame: each hand and its elbow (left and right on screen); how far the crate moved (cd,
# and from the side cx: back is negative), the body's and head's drop, how high the crystals
# jump, the eye. The work stroke heaves the cargo bed up and tips it back (art-style-bible.md
# §4: "Hauler: cargo bed tipped back"): crouched under it (work_0), heaved (work_1), settling
# (work_2).
FRONT = {
    "base":   dict(l=((12, 49), (11, 58)), r=((52, 49), (53, 58))),
    "work_0": dict(l=((12, 51), (12, 61)), r=((52, 51), (52, 61)), cd=2, body=2, head=2),
    "work_1": dict(l=((12, 43), (9, 53)), r=((52, 43), (55, 53)), cd=-5, head=-1, jump=4),
    "work_2": dict(l=((12, 47), (10, 56)), r=((52, 47), (54, 56)), cd=-2, jump=1),
    "stall":  dict(l=((11, 71), (12, 64)), r=((53, 71), (52, 64)), cd=7, body=2, head=5, eye="dim"),
    "carry":  dict(l=((20, 64), (13, 62)), r=((44, 64), (51, 62)), eye="bright"),
}
SIDE = {
    "base":   dict(n=((31, 50), (40, 58))),
    "work_0": dict(n=((31, 52), (40, 61)), cd=2, body=2, head=2),
    "work_1": dict(n=((28, 45), (40, 54)), cd=-5, cx=-3, head=-1, jump=4),
    "work_2": dict(n=((30, 48), (40, 56)), cd=-2, cx=-1, jump=1),
    "stall":  dict(n=((40, 74), (39, 66)), cd=3, body=2, head=5, eye="dim"),
    "carry":  dict(n=((44, 64), (40, 66)), eye="bright"),
}
BACK = FRONT
for table in (FRONT, SIDE):
    table["idle_1"] = dict(table["base"], eye="bright")


def eye_tones(mode):
    return {"dim": (0, 0, 1), "bright": (1, 2, 2)}.get(mode, (0, 1, 2))


def draw_crystals(cv, base, jump, spots):
    """Teal Aether shards standing up out of the crate's straw, a lit facet on each; when it
    heaves they jump."""
    x0, y0 = base
    for i, (dx, h, lean, r) in enumerate(spots):
        up = jump if i % 2 == 0 else max(0, jump - 2)
        foot = (x0 + dx, y0 - up)
        tip = (x0 + dx + lean, y0 - h - up)
        cv.layer("crystals", "aether", capsule(foot, tip, r), [0, 1, 2])
        cv.paint("crystals", "steam", 3, {(round(tip[0] - 0.5), round(tip[1] + 2))})


def draw_arm(cv, shoulder, arm, dark=False):
    hand, elbow = arm
    tones = [0, 1, 2] if dark else [1, 2, 3, 4]
    cv.layer("arms", "iron", capsule(shoulder, elbow, 2.4), tones)
    cv.layer("arms", "copper", ellipse(shoulder[0], shoulder[1], 3.6, 3.4), [0, 1, 2])
    cv.layer("forearms", "iron", capsule(elbow, hand, 2.2), tones)
    cv.layer("hands", "brass", ellipse(hand[0], hand[1], 3.2, 3.0), [0, 1, 2, 3])


def draw_crate(cv, box, cd, cx=0, back=False):
    """The cargo bed: a mahogany crate, the carts' wood, planked, with brass corners and iron
    bands; from behind, a brass plaque with a crystal on it: handle with care."""
    x0, y0, x1, y1 = box
    s = lambda pts: shift(pts, cx, cd)
    cv.layer("crate", "mahogany", s(rrect(x0, y0, x1, y1, 2)), [2, 3, 5])
    for y in range(y0 + 5, y1, 5):
        cv.paint("crate", "mahogany", 1, s(rect(x0 + 1, y, x1 - 1, y)))
    for x in (x0 + 4, x1 - 4):
        cv.layer("crate", "iron", s(rect(x - 1, y0, x + 1, y1)), [1, 2, 3])
        cv.dots("crate", "brass", 2, [(x + cx, y0 + 3 + cd), (x + cx, y1 - 4 + cd)], 1)
    for x, y in ((x0, y0), (x1 - 3, y0), (x0, y1 - 3), (x1 - 3, y1 - 3)):
        cv.layer("crate", "brass", s(rect(x, y, x + 3, y + 3)), [1, 2, 3])
    cv.layer("crate", "brass", s(rect(x0, y0, x1, y0 + 1)), [1, 2, 3])
    if back:
        mx, my = (x0 + x1) // 2, (y0 + y1) // 2 + 1
        cv.layer("crate", "brass", s(rrect(mx - 6, my - 5, mx + 6, my + 5, 1)), [1, 2, 3])
        cv.layer("crate", "aether", s(capsule((mx, my + 3), (mx, my - 3), 1.6)), [0, 1, 2])
        cv.dots("crate", "iron", 1, [(mx - 5 + cx, my - 4 + cd), (mx + 4 + cx, my - 4 + cd)], 1)


def draw_front(state, back=False):
    p = (BACK if back else FRONT)[state]
    cv = Canvas("north" if back else "south", mirror=back)
    hd, bd, cd = p.get("head", 0), p.get("body", 0), p.get("cd", 0)
    o = hd + bd
    # Treads: two caterpillar blocks, iron with cleats, a copper mudguard over each; an axle
    # housing between them.
    cv.layer("treads", "iron", rect(17, 82, 47, 86), [0, 1, 2])
    cv.layer("treads", "iron", rect(28, 76, 36, 84), [1, 2, 3])
    for x0 in (4, 46):
        cv.layer("treads", "iron", rrect(x0, 74, x0 + 13, 95, 3), [0, 1, 2, 3, 4])
        for y in range(78, 94, 3):
            cv.paint("treads", "iron", 0, rect(x0 + 2, y, x0 + 11, y))
        cv.layer("treads", "copper", rrect(x0 - 1, 71, x0 + 14, 75, 2), [0, 1, 2])
        cv.dots("treads", "brass", 3, [(x0 + 2, 72), (x0 + 11, 72)], 1)
    # The cargo bed, behind it from the front, and in front of it from behind, where it covers
    # its back; the crystals stand up out of it.
    draw_crystals(cv, (32, 24 + cd), p.get("jump", 0),
                  [(-12, 10, -2, 2.2), (-4, 15, 1, 3.0), (4, 12, 2, 2.6), (12, 8, 2, 2.2), (-8, 6, -1, 1.8)])
    draw_crate(cv, (10, 22, 54, 62 if back else 50), cd, back=back)
    # Hull: cream painted armour, riveted, a copper band and a brass hatch.
    hull = ellipse(32, 66, 14, 12)
    cv.layer("body", "bone", shift(hull, 0, bd), [0, 1, 2])
    cv.layer("body", "copper", shift(rect(18, 70, 46, 73) & hull, 0, bd), [0, 1, 2])
    cv.dots("body", "brass", 3, [(x, 71 + bd) for x in (21, 26, 31, 36, 41)], 1)
    if not back:
        cv.layer("body", "brass", shift(rrect(28, 62, 36, 68, 1), 0, bd), [1, 2, 3])
        cv.paint("body", "brass", 0, shift(rect(29, 65, 35, 65), 0, bd))
    cv.dots("body", "copper", 0, [(22, 60 + bd), (42, 60 + bd), (20, 66 + bd), (44, 66 + bd)], 1)
    # Head: a small round helmet sunk into the hull, a copper brim, a teal headlamp; below the
    # brim an iron face with two little eyes.
    if not back:
        lo, mid, hi = eye_tones(p.get("eye"))
        cv.layer("head", "iron", shift(rrect(25, 52, 39, 61, 3), 0, o), [1, 2, 3])
        cv.layer("head", "aether", shift(ellipse(29, 56, 1.6, 1.8) | ellipse(35, 56, 1.6, 1.8), 0, o), [mid, hi])
        cv.paint("head", "iron", 0, shift(rect(30, 59, 34, 59), 0, o))
        cv.layer("head", "bone", shift(ellipse(32, 48, 9, 7), 0, o), [0, 1, 2])
        cv.layer("head", "copper", shift(rrect(21, 51, 43, 53, 1), 0, o), [0, 1, 2])
        cv.layer("head", "iron", shift(ellipse(32, 46, 3.8, 3.8), 0, o), [0, 1, 2, 3])
        cv.layer("head", "aether", shift(ellipse(32, 46, 2.6, 2.6), 0, o), sorted({lo, mid, hi}))
        cv.paint("head", "steam", 3 if p.get("eye") == "bright" else 2, shift({(31, 45)}, 0, o))
    # Arms: short and sturdy, copper shoulder pads, brass mitts holding the crate's corners.
    draw_arm(cv, (19, 60 + bd), p["l"])
    draw_arm(cv, (45, 60 + bd), p["r"])
    return cv


def draw_side(state, mirror=False):
    """East: the profile, front to the right. West (mirror): the same shapes mirrored and lit
    from the upper left again by the same shading rule (presser_draft.py says why)."""
    p = SIDE[state]
    cv = Canvas("west" if mirror else "east", mirror)
    hd, bd, cd, cx = p.get("head", 0), p.get("body", 0), p.get("cd", 0), p.get("cx", 0)
    o = hd + bd
    lo, mid, hi = eye_tones(p.get("eye"))
    # Treads: one long caterpillar loop, brass road wheels inside, cleats along it, a copper
    # mudguard over it.
    cv.layer("treads", "iron", rrect(5, 79, 55, 95, 7), [0, 1, 2, 3, 4])
    for x in range(8, 54, 4):
        cv.paint("treads", "iron", 0, rect(x, 79, x, 80) | rect(x, 94, x, 95))
    for wx, r in ((13, 4.5), (24, 3.6), (35, 3.6), (47, 4.5)):
        cv.layer("treads", "brass", ellipse(wx, 88, r, r), [0, 1, 2, 3])
        cv.paint("treads", "iron", 1, ellipse(wx, 88, 1.3, 1.3))
    cv.layer("treads", "copper", rrect(4, 76, 56, 80, 2), [0, 1, 2])
    # The cargo bed on its back, the crystals in it.
    draw_crystals(cv, (20 + cx, 30 + cd), p.get("jump", 0),
                  [(-9, 9, -2, 2.2), (-2, 14, 0, 3.0), (6, 10, 2, 2.4), (2, 5, 1, 1.8)])
    draw_crate(cv, (8, 28, 32, 62), cd, cx)
    cv.layer("treads", "iron", capsule((12, 62), (10, 76), 1.5) | capsule((24, 62), (22, 76), 1.5), [1, 2, 3])
    # Hull: the armour side on, the band, rivets.
    hull = ellipse(31, 66, 14, 11.5)
    cv.layer("body", "bone", shift(hull, 0, bd), [0, 1, 2])
    cv.layer("body", "copper", shift(rect(16, 70, 46, 73) & hull, 0, bd), [0, 1, 2])
    cv.dots("body", "brass", 3, [(x, 71 + bd) for x in (22, 28, 34, 40)], 1)
    # Head: forward of the hull, the brim and the lamp at its front, one eye and the face below.
    cv.layer("head", "iron", shift(rrect(40, 52, 52, 61, 3), 0, o), [1, 2, 3])
    cv.layer("head", "aether", shift(ellipse(48, 56, 1.6, 1.8), 0, o), [mid, hi])
    cv.paint("head", "iron", 0, shift(rect(47, 59, 51, 59), 0, o))
    cv.layer("head", "bone", shift(ellipse(43, 48, 8.5, 7), 0, o), [0, 1, 2])
    cv.layer("head", "copper", shift(rrect(35, 51, 55, 53, 1), 0, o), [0, 1, 2])
    cv.layer("head", "brass", shift(rrect(48, 42, 54, 50, 2), 0, o), [0, 1, 2, 3])
    cv.layer("head", "aether", shift(rect(52, 44, 53, 48), 0, o), sorted({lo, mid, hi}))
    # The near arm: up and back to the crate's front corner.
    draw_arm(cv, (34, 62 + bd), p["n"])
    return cv


def draw(facing, state):
    if facing in ("east", "west"):
        return draw_side(state, mirror=(facing == "west"))
    return draw_front(state, back=(facing == "north"))


def offsets(facing, state):
    """How far each rigid part moved from the base, for golem_set.py's compose."""
    table = SIDE if facing in ("east", "west") else (BACK if facing == "north" else FRONT)
    p = table[state]
    bd, hd = p.get("body", 0), p.get("head", 0)
    out = {"body": [0, bd], "crate": [p.get("cx", 0), p.get("cd", 0)], "head": [0, hd + bd],
           "treads": [0, 0]}
    if facing == "west":
        out = {k: (None if v is None else [-v[0], v[1]]) for k, v in out.items()}
    return out


def main(out):
    os.makedirs(out, exist_ok=True)
    for facing in FACINGS:
        for state in STATES:
            cv = draw(facing, state)
            img = cv.image()
            stem = f"draft_{facing}_{state}"
            img.save(os.path.join(out, stem + ".png"))
            key = Image.new("RGBA", (W, H), KEY + (255,))
            key.alpha_composite(img)
            key.resize((W * 8, H * 8), Image.NEAREST).convert("RGB").save(os.path.join(out, stem + "_8x.png"))
            with open(os.path.join(out, f"parts_{facing}_{state}.json"), "w") as handle:
                json.dump({"parts": cv.part_map(), "offsets": offsets(facing, state)}, handle)
    print("drafts in", out)


if __name__ == "__main__":
    main(sys.argv[1])
