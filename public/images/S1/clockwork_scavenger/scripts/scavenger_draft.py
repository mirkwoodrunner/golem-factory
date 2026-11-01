"""The Clockwork Scavenger as a creature (art-style-bible.md invariant 17): a scripted, layered
draft of every frame in the three drawn facings, with a part map, so that

- each facing's base is the image-to-image start and ControlNet guide for ComfyUI, and
- every other frame is derived from the cleaned base by moving its rigid parts
  (Tools/Art/golem_set.py's compose step),
  so its surface texture never changes between frames.

A rickety copper golem on a tripod (digital-design.md's roster): two splayed front legs and a
third behind, thin iron legs with copper knees, a small copper kettle of a body with a brass
band, a round copper bulb head with one big teal eye and a little antenna, a brass scrap bin on
its back, and a long iron arm ending in a brass grabber claw that it reaches down and grips with.
Copied from the Brass Presser's presser_draft.py (art-pipeline-design.md §3.2); the shapes and
poses are the Scavenger's, the drawing rules the same.

    python scavenger_draft.py <out dir>     writes draft_<facing>_<state>.png (+ _8x on the key)
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
# The draw order per facing is the chassis' own (chassis.json "layers"): the tripod's third leg
# is behind the body from the south and in front of it from the north.
LAYERS = json.load(open(os.path.join(HERE, "..", "chassis.json")))["layers"]
W, H = 64, 96
KEY = (40, 190, 70)
STATES = ["base", "idle_1", "work_0", "work_1", "work_2", "stall", "carry"]
FACINGS = ["south", "north", "east", "west"]
# Parts that move rigidly between frames: golem_set.py's compose copies the cleaned base's pixels for them,
# shifted by the frame's offset. Arms, hands and the claw change shape, so they are always the draft's.
RIGID = ["legs", "far_leg", "body", "head", "pack"]


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

# Per frame: the claw hand, its elbow, which way the claw points, whether it is open and holds a
# piece of scrap; the free hand and elbow; the head's and body's drop; a lifted front leg; the
# eye. South and north share most of it; east has its own (profile).
FRONT = {
    "base":   dict(claw=(11, 74), elbow=(10, 61), point=(0, 1), open=True,
                   hand=(52, 70), hand_elbow=(54, 60)),
    "work_0": dict(claw=(6, 84), elbow=(6, 66), point=(-0.3, 1), open=True,
                   hand=(53, 66), hand_elbow=(55, 57), body=2, head=1),
    "work_1": dict(claw=(9, 87), elbow=(7, 70), point=(0, 1), open=False,
                   hand=(53, 68), hand_elbow=(55, 58), body=3, head=1),
    "work_2": dict(claw=(13, 70), elbow=(9, 60), point=(0.2, 1), open=False, scrap=True,
                   hand=(52, 70), hand_elbow=(54, 60), body=1),
    "stall":  dict(claw=(13, 82), elbow=(12, 68), point=(0.2, 1), open=True,
                   hand=(51, 78), hand_elbow=(53, 66), head=4, body=2, eye="dim"),
    "carry":  dict(claw=(24, 64), elbow=(13, 62), point=(0.6, 0.8), open=False, scrap=True,
                   hand=(42, 66), hand_elbow=(52, 61), step=True),
}
SIDE = {
    "base":   dict(claw=(45, 72), elbow=(39, 61), point=(0.3, 1), open=True,
                   hand=(41, 70), hand_elbow=(36, 61)),
    "work_0": dict(claw=(53, 80), elbow=(45, 64), point=(0.6, 0.9), open=True,
                   hand=(43, 68), hand_elbow=(37, 60), body=2, head=1),
    "work_1": dict(claw=(52, 87), elbow=(46, 70), point=(0.2, 1), open=False,
                   hand=(42, 70), hand_elbow=(36, 61), body=3, head=1),
    "work_2": dict(claw=(47, 66), elbow=(41, 60), point=(0.3, 1), open=False, scrap=True,
                   hand=(41, 70), hand_elbow=(36, 61), body=1),
    "stall":  dict(claw=(42, 80), elbow=(39, 68), point=(0.1, 1), open=True,
                   hand=(38, 78), hand_elbow=(35, 67), head=4, body=2, eye="dim"),
    "carry":  dict(claw=(45, 62), elbow=(41, 64), point=(0.3, 1), open=False, scrap=True,
                   hand=(43, 66), hand_elbow=(37, 62), step=True),
}
# North: the golem faces away, so the claw and what it holds are in front of it, further up the
# floor: drawn a little higher on screen than from the south and behind its body (chassis.json
# "layers"). The arms still hang at its sides, where they show (tucked in behind the bucket, the
# user saw arms cut off at the elbow, first review). The whole view is drawn mirrored
# (draw_front), so the claw arm, the antenna and the scrap are on the golem's own side.
BACK = {}
for state, p in FRONT.items():
    q = dict(p)
    q["claw"] = (p["claw"][0], p["claw"][1] - 6)
    q["elbow"] = (p["elbow"][0], p["elbow"][1] - 3)
    BACK[state] = q
for table in (FRONT, SIDE, BACK):
    table["idle_1"] = dict(table["base"], eye="bright")


def eye_tones(mode):
    return {"dim": (0, 0, 1), "bright": (1, 2, 2)}.get(mode, (0, 1, 2))


def draw_claw(cv, wrist, point, opened, scrap, part="claw", tones=(0, 1, 2, 3)):
    """A brass wrist collar and three prongs along `point`, spread when open, together when
    closed; a lump of scrap between them when it holds one."""
    px, py = point
    n = math.hypot(px, py) or 1
    px, py = px / n, py / n
    if scrap:
        tip = (wrist[0] + px * 8, wrist[1] + py * 8)
        cv.layer(part, "iron", ellipse(tip[0], tip[1], 4.2, 3.6), [1, 2, 3, 4])
        cv.dots(part, "copper", 1, [(int(tip[0]) - 1, int(tip[1]) - 1)], 2)
    spread = 0.62 if opened else 0.22
    for side in (-1, 0, 1):
        a = side * spread
        dx, dy = px * math.cos(a) - py * math.sin(a), px * math.sin(a) + py * math.cos(a)
        length = 7.5 if side == 0 else 8.5
        mid = (wrist[0] + dx * length * 0.6, wrist[1] + dy * length * 0.6)
        # Each side prong bends back in at its tip: a claw, not a fork.
        bend = -side * (0.5 if opened else 0.9)
        ex, ey = dx * math.cos(bend) - dy * math.sin(bend), dx * math.sin(bend) + dy * math.cos(bend)
        tip = (mid[0] + ex * length * 0.45, mid[1] + ey * length * 0.45)
        cv.layer(part, "brass", capsule(wrist, mid, 1.9) | capsule(mid, tip, 1.6), [0, 1, 2, 3])
    cv.layer(part, "brass", ellipse(wrist[0], wrist[1], 3.4, 3.4), list(tones))


def front_legs(cv, p, back):
    """Two front legs splayed out from the hips, copper knees, small iron feet; the third leg
    stands straight down behind (south) or in front (north)."""
    step = p.get("step")
    for side in (-1, 1):
        lift = 5 if (step and side == -1) else 0
        hip = (32 + side * 6, 68)
        knee = (32 + side * 15, 79 - lift)
        foot = (32 + side * 21, 92 - lift)
        cv.layer("legs", "iron", capsule(hip, knee, 2.8), [1, 2, 3, 4])
        cv.layer("legs", "iron", capsule(knee, foot, 2.5), [1, 2, 3, 4])
        cv.layer("legs", "copper", ellipse(knee[0], knee[1], 3.4, 3.4), [0, 1, 2])
        cv.layer("legs", "iron", rrect(foot[0] - 5, foot[1], foot[0] + 4, foot[1] + 3, 1), [0, 1, 2, 3])
    third = rect(30, 70, 34, 90)
    tones = [1, 2, 3, 4] if back else [0, 1, 2]
    cv.layer("far_leg", "iron", third, tones)
    cv.layer("far_leg", "copper", ellipse(32, 80, 3.2, 3.2), [0, 1, 2])
    cv.layer("far_leg", "iron", rrect(27, 91, 37, 95, 1), [0, 1, 2, 3] if back else [0, 1, 2])


def draw_front(state, back=False):
    p = (BACK if back else FRONT)[state]
    # From behind, the golem's right is on the screen's right: the back is the front's shapes
    # mirrored, then shaded from the upper left as ever.
    cv = Canvas("north" if back else "south", mirror=back)
    hd, bd = p.get("head", 0), p.get("body", 0)
    front_legs(cv, p, back)
    # Body: a small copper kettle, a brass band round its middle, an iron hip disc under it.
    cv.layer("body", "iron", shift(rrect(23, 64, 41, 71, 3), 0, bd), [1, 2, 3, 4])
    cv.layer("body", "copper", shift(ellipse(32, 55, 12, 11.5), 0, bd), [0, 1, 2])
    cv.layer("body", "brass", shift(rect(21, 55, 43, 58) & ellipse(32, 55, 12, 11.5), 0, bd), [0, 1, 2, 3])
    cv.dots("body", "iron", 1, [(25, 56 + bd), (37, 56 + bd)], 2)
    if not back:
        # A little iron grille on its chest.
        cv.layer("body", "iron", shift(rrect(28, 47, 36, 52, 1), 0, bd), [1, 2, 3])
        for x in (30, 33):
            cv.paint("body", "iron", 0, shift(rect(x, 48, x, 51), 0, bd))
        cv.dots("body", "brass", 2, [(29, 61 + bd), (34, 61 + bd)], 2)
    # The scrap bin (an iron bucket, brass rim and straps) on its back, centred: from the north it
    # is the whole back; from the south (behind the body) its rim shows behind the neck and its
    # scrap either side of the head, so it reads as a backpack. On one shoulder only, the user saw
    # a box beside the head (first review). It is no wider than the body: wider, the south
    # silhouette overlapped today's Hauler at 0.52 (at most 0.50; this is 0.48).
    cv.layer("pack", "iron", shift(rrect(21, 38, 43, 60, 3), 0, bd), [0, 1, 2, 3, 4])
    cv.layer("pack", "brass", shift(rect(21, 38, 43, 40), 0, bd), [1, 2, 3])
    cv.layer("pack", "brass", shift(rrect(17, 32, 22, 39, 1), 0, bd), [0, 1, 2, 3])
    cv.layer("pack", "copper", shift(rrect(42, 30, 47, 39, 1), 0, bd), [0, 1, 2])
    if back:
        for x in (25, 38):
            cv.paint("pack", "brass", 1, shift(rect(x, 42, x + 1, 62), 0, bd))
    # Head: a round copper bulb on an iron neck, one big eye, a little antenna with a brass tip.
    o = hd + bd
    cv.layer("head", "iron", shift(rect(29, 39, 35, 44), 0, o), [1, 2, 3, 4])
    cv.layer("head", "iron", shift(capsule((36, 22), (41, 13), 1.3), 0, o), [1, 2, 3])
    cv.layer("head", "brass", shift(ellipse(41.5, 12.5, 2.6, 2.6), 0, o), [1, 2, 3])
    cv.layer("head", "copper", shift(ellipse(32, 30, 10.5, 10), 0, o), [0, 1, 2])
    if back:
        cv.layer("head", "iron", shift(rrect(27, 27, 37, 34, 2), 0, o), [1, 2, 3])
        for y in (29, 32):
            cv.paint("head", "iron", 0, shift(rect(28, y, 36, y), 0, o))
        cv.dots("head", "brass", 2, [(24, 30 + o), (38, 30 + o)], 2)
    else:
        lo, mid, hi = eye_tones(p.get("eye"))
        cv.layer("head", "iron", shift(ellipse(32, 30, 7, 7), 0, o), [0, 1, 2, 3])
        cv.paint("head", "aether", lo, shift(ellipse(32, 30, 5.2, 5.2), 0, o))
        cv.paint("head", "aether", mid, shift(ellipse(32, 30, 3.6, 3.6), 0, o))
        cv.paint("head", "aether" if p.get("eye") != "bright" else "steam", hi if p.get("eye") != "bright" else 3,
                 shift(rect(29, 27, 30, 28), 0, o))
        cv.dots("head", "brass", 2, [(22, 29 + o), (41, 29 + o)], 2)
    # Arms: thin iron, copper elbows. The golem's right arm (screen left) ends in the claw.
    shoulders = [(21, 50 + bd), (43, 50 + bd)]
    cv.layer("arms", "iron", capsule(shoulders[0], p["elbow"], 2.8), [1, 2, 3, 4])
    cv.layer("forearms", "iron", capsule(p["elbow"], p["claw"], 2.5), [1, 2, 3, 4])
    cv.layer("arms", "copper", ellipse(p["elbow"][0], p["elbow"][1], 3.0, 3.0), [0, 1, 2])
    cv.layer("arms", "iron", capsule(shoulders[1], p["hand_elbow"], 2.8), [1, 2, 3, 4])
    cv.layer("forearms", "iron", capsule(p["hand_elbow"], p["hand"], 2.5), [1, 2, 3, 4])
    cv.layer("arms", "copper", ellipse(p["hand_elbow"][0], p["hand_elbow"][1], 3.0, 3.0), [0, 1, 2])
    cv.layer("hands", "brass", ellipse(p["hand"][0], p["hand"][1], 3.2, 3.2), [0, 1, 2, 3])
    draw_claw(cv, p["claw"], p["point"], p["open"], p.get("scrap"))
    return cv


def draw_side(state, mirror=False):
    """East: the profile, front to the right. West (mirror): the same shapes mirrored and lit
    from the upper left again by the same shading rule (presser_draft.py says why)."""
    p = SIDE[state]
    cv = Canvas("west" if mirror else "east", mirror)
    hd, bd = p.get("head", 0), p.get("body", 0)
    o = hd + bd
    lift = 5 if p.get("step") else 0
    # The far arm (darker), behind everything.
    cv.layer("far_arm", "iron", capsule((30, 50 + bd), p["hand_elbow"], 2.5) | capsule(p["hand_elbow"], p["hand"], 2.3), [0, 1, 2])
    cv.layer("far_arm", "brass", ellipse(p["hand"][0], p["hand"][1], 2.8, 2.8), [0, 1, 2])
    # Legs: the far front leg (darker), the near front leg, and the third leg reaching back.
    cv.layer("far_leg", "iron", capsule((34, 68), (39, 79), 2.5) | capsule((39, 79), (42, 92), 2.3), [0, 1, 2])
    cv.layer("far_leg", "iron", rrect(37, 91, 46, 95, 1), [0, 1, 2])
    for hip, knee, foot, dl in (((33, 68), (44, 78 - lift), (49, 92 - lift), lift), ((28, 68), (19, 79), (13, 92), 0)):
        cv.layer("legs", "iron", capsule(hip, knee, 2.8), [1, 2, 3, 4])
        cv.layer("legs", "iron", capsule(knee, foot, 2.5), [1, 2, 3, 4])
        cv.layer("legs", "copper", ellipse(knee[0], knee[1], 3.4, 3.4), [0, 1, 2])
        cv.layer("legs", "iron", rrect(foot[0] - 5, foot[1], foot[0] + 4, foot[1] + 3, 1), [0, 1, 2, 3])
    # Body: the kettle side on, a brass band, the hip disc.
    cv.layer("body", "iron", shift(rrect(25, 64, 38, 71, 3), 0, bd), [1, 2, 3, 4])
    cv.layer("body", "copper", shift(ellipse(31, 55, 10.5, 11.5), 0, bd), [0, 1, 2])
    cv.layer("body", "brass", shift(rect(20, 55, 42, 58) & ellipse(31, 55, 10.5, 11.5), 0, bd), [0, 1, 2, 3])
    cv.dots("body", "iron", 1, [(36, 56 + bd)], 2)
    # The scrap bin on its back, scrap over its rim. A battered iron bucket with a brass rim:
    # in brass, it was the brightest thing on the profile's back and failed the west frames'
    # light check (the back is the lit edge from the east, the shadowed one from the west).
    cv.layer("pack", "iron", shift(rrect(11, 40, 23, 62, 2), 0, bd), [0, 1, 2, 3, 4])
    cv.layer("pack", "brass", shift(rect(11, 40, 23, 42), 0, bd), [1, 2, 3])
    cv.paint("pack", "brass", 1, shift(rect(16, 44, 17, 61), 0, bd))
    cv.layer("pack", "brass", shift(rrect(12, 35, 17, 41, 1), 0, bd), [0, 1, 2, 3])
    cv.layer("pack", "copper", shift(rrect(18, 33, 23, 41, 1), 0, bd), [0, 1, 2])
    # Head in profile: the bulb, the neck, the eye's lens standing out in front, the antenna behind.
    cv.layer("head", "iron", shift(rect(29, 39, 35, 44), 0, o), [1, 2, 3, 4])
    cv.layer("head", "iron", shift(capsule((27, 22), (22, 13), 1.3), 0, o), [1, 2, 3])
    cv.layer("head", "brass", shift(ellipse(21.5, 12.5, 2.6, 2.6), 0, o), [1, 2, 3])
    cv.layer("head", "copper", shift(ellipse(33, 30, 10, 10), 0, o), [0, 1, 2])
    lo, mid, hi = eye_tones(p.get("eye"))
    cv.layer("head", "iron", shift(rrect(38, 23, 47, 37, 3), 0, o), [0, 1, 2, 3])
    # The lens is shaded like any part, lit on its upper left: flat, it was the darkest thing on
    # the west profile's lit edge and failed the mirror's light check (+0.11 against +0.17).
    cv.layer("head", "aether", shift(rect(42, 25, 46, 35), 0, o), sorted({lo, mid, hi}))
    cv.paint("head", "aether" if p.get("eye") != "bright" else "steam", hi if p.get("eye") != "bright" else 3,
             shift(rect(43, 27, 44, 28), 0, o))
    cv.dots("head", "brass", 2, [(26, 31 + o)], 2)
    # The near arm and its claw.
    cv.layer("arms", "iron", capsule((32, 50 + bd), p["elbow"], 2.8), [1, 2, 3, 4])
    cv.layer("forearms", "iron", capsule(p["elbow"], p["claw"], 2.5), [1, 2, 3, 4])
    cv.layer("forearms", "copper", ellipse(p["elbow"][0], p["elbow"][1], 3.0, 3.0), [0, 1, 2])
    draw_claw(cv, p["claw"], p["point"], p["open"], p.get("scrap"))
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
    out = {"body": [0, bd], "pack": [0, bd], "head": [0, hd + bd], "legs": [0, 0], "far_leg": [0, 0]}
    if p.get("step"):
        out["legs"] = None  # one front leg lifts, not both: the draft's legs are used
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
