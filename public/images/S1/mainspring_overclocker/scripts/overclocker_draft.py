"""The Mainspring Overclocker as a creature (art-style-bible.md invariant 17): a scripted, layered
draft of every frame in the three drawn facings, with a part map, so that

- each facing's base is the image-to-image start and ControlNet guide for ComfyUI, and
- every other frame is derived from the cleaned base by moving its rigid parts
  (Tools/Art/golem_set.py's compose step),
  so its surface texture never changes between frames.

The Watchmaker (the user's call, 2026-10-24): what the Overclocker does, not the roster's cut
speed aura (progression-design.md §6). It is the late game's precision assembler, the one chassis
that runs the 3-input recipes and Repeat, and it runs hot. So: a hunched brass clockmaker with a
jeweller's loupe for its teal eye, a flyball governor spinning on its head, a pressure gauge in
the red on its chest, a copper work apron, its mainspring coiled in a cage on its back, and three
thin arms, one per input, holding tweezers, a hammer and a screwdriver. Its work stroke brings
all three down together; the governor's balls fly out as it speeds up and fall when it stalls.
Copied from the Clockwork Scavenger's scavenger_draft.py (art-pipeline-design.md §3.2); the
shapes and poses are the Overclocker's, the drawing rules the same.

    python overclocker_draft.py <out dir>   writes draft_<facing>_<state>.png (+ _8x on the key)
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
# The draw order per facing is the chassis' own (chassis.json "layers"): the tools are in front of
# the body from the south and hidden behind it from the north.
LAYERS = json.load(open(os.path.join(HERE, "..", "chassis.json")))["layers"]
W, H = 64, 96
KEY = (40, 190, 70)
STATES = ["base", "idle_1", "work_0", "work_1", "work_2", "stall", "carry"]
FACINGS = ["south", "north", "east", "west"]
# Parts that move rigidly between frames: golem_set.py's compose copies the cleaned base's pixels for them,
# shifted by the frame's offset. Arms, tools and the governor change shape, so they are always the draft's.
RIGID = ["legs", "far_leg", "body", "apron", "cage", "head"]


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

# Per frame, for each of the three arms (tweezers, hammer, screwdriver): the hand, its elbow and
# which way the tool points; the governor's spread (gov: how far its balls fly out, 0 hanging to
# 1 flat out); the head's and body's drop; a lifted leg; the eye. The work stroke: the tools
# raised (work_0), brought down together on the piece in front (work_1), lifted (work_2).
FRONT = {
    "base":   dict(tw=((11, 62), (10, 53), (0.2, 1)), hm=((53, 61), (55, 52), (0, -1)),
                   sd=((47, 74), (49, 67), (0, 1)), gov=0.25),
    "work_0": dict(tw=((10, 43), (8, 51), (0.4, -1)), hm=((54, 38), (56, 47), (0.2, -1)),
                   sd=((51, 58), (52, 64), (0.3, -1)), gov=0.7, head=-1),
    "work_1": dict(tw=((24, 70), (14, 60), (0.6, 1)), hm=((41, 66), (51, 58), (-0.7, 0.8)),
                   sd=((35, 77), (44, 71), (-0.2, 1)), gov=1.0, body=2, head=2),
    "work_2": dict(tw=((15, 60), (10, 54), (0.3, 1)), hm=((50, 55), (55, 50), (-0.2, -1)),
                   sd=((45, 71), (49, 66), (-0.1, 1)), gov=0.6, body=1),
    "stall":  dict(tw=((14, 74), (13, 62), (0, 1)), hm=((50, 74), (52, 62), (0, 1)),
                   sd=((45, 80), (48, 72), (0, 1)), gov=-0.4, head=4, body=2, eye="dim"),
    "carry":  dict(tw=((19, 58), (11, 55), (0.5, 0.6)), hm=((45, 56), (53, 54), (-0.5, -0.6)),
                   sd=((40, 66), (47, 64), (-0.4, 1)), gov=0.25, step=True),
}
SIDE = {
    "base":   dict(tw=((44, 64), (38, 57), (0.5, 1)), hm=((40, 60), (33, 55), (0.3, -1)),
                   sd=((40, 72), (34, 67), (0.4, 1)), gov=0.25),
    "work_0": dict(tw=((44, 46), (40, 53), (0.5, -1)), hm=((41, 40), (36, 48), (0.3, -1)),
                   sd=((43, 58), (36, 62), (0.5, -1)), gov=0.7, head=-1),
    "work_1": dict(tw=((50, 72), (42, 62), (0.4, 1)), hm=((48, 66), (39, 59), (0.8, 0.6)),
                   sd=((47, 76), (39, 70), (0.3, 1)), gov=1.0, body=2, head=2),
    "work_2": dict(tw=((46, 61), (39, 56), (0.5, 1)), hm=((43, 54), (36, 52), (0.4, -0.6)),
                   sd=((43, 70), (36, 66), (0.4, 1)), gov=0.6, body=1),
    "stall":  dict(tw=((42, 75), (38, 64), (0.1, 1)), hm=((38, 74), (33, 64), (0, 1)),
                   sd=((37, 80), (33, 72), (0, 1)), gov=-0.4, head=4, body=2, eye="dim"),
    "carry":  dict(tw=((42, 58), (37, 56), (0.6, -0.4)), hm=((39, 56), (33, 54), (0.6, -0.4)),
                   sd=((39, 66), (34, 63), (0.6, 0.2)), gov=0.25, step=True),
}
# North: the golem faces away, so its tools and the piece it works are in front of it, further
# up the floor: drawn a little higher than from the south and behind its body (chassis.json
# "layers"). The whole view is drawn mirrored (draw_front), so each tool stays in its own hand.
BACK = {}
for state, p in FRONT.items():
    q = dict(p)
    for arm in ("tw", "hm", "sd"):
        (hx, hy), (ex, ey), point = p[arm]
        q[arm] = ((hx, hy - 6), (ex, ey - 3), point)
    BACK[state] = q
for table in (FRONT, SIDE, BACK):
    table["idle_1"] = dict(table["base"], eye="bright")


# The head sits this far down into its shoulders: a hunched craftsman. On a neck it read as a
# lollipop.
HUNCH = 5


def eye_tones(mode):
    return {"dim": (0, 0, 1), "bright": (1, 2, 2)}.get(mode, (0, 1, 2))


def unit(v):
    n = math.hypot(v[0], v[1]) or 1
    return v[0] / n, v[1] / n


def draw_tool(cv, kind, hand, point, part="tools"):
    """Tweezers (two brass prongs), a hammer (iron head, brass face) or a screwdriver (brass
    grip, iron shaft), held in the hand and pointing along `point`."""
    ux, uy = unit(point)
    vx, vy = -uy, ux
    x, y = hand
    if kind == "tw":
        for side in (-1, 1):
            root = (x + vx * 1.2 * side, y + vy * 1.2 * side)
            tip = (x + ux * 8 + vx * 0.4 * side, y + uy * 8 + vy * 0.4 * side)
            cv.layer(part, "brass", capsule(root, tip, 0.9), [1, 2, 3])
    elif kind == "hm":
        end = (x + ux * 7, y + uy * 7)
        cv.layer(part, "iron", capsule(hand, end, 1.1), [1, 2, 3])
        cv.layer(part, "iron", capsule((end[0] - vx * 3.5, end[1] - vy * 3.5), (end[0] + vx * 3.5, end[1] + vy * 3.5), 2.0), [0, 1, 2, 3, 4])
        cv.layer(part, "brass", ellipse(end[0] + vx * 3.5, end[1] + vy * 3.5, 1.6, 1.6), [1, 2, 3])
    else:
        mid = (x + ux * 3, y + uy * 3)
        cv.layer(part, "brass", capsule(hand, mid, 1.6), [0, 1, 2])
        cv.layer(part, "iron", capsule(mid, (x + ux * 9, y + uy * 9), 0.8), [2, 3, 4])


def draw_arm(cv, kind, shoulder, arm, upper="arms", fore="forearms", hands="hands", tools="tools", dark=False):
    (hand, elbow, point) = arm
    tones = [0, 1, 2] if dark else [1, 2, 3, 4]
    cv.layer(upper, "iron", capsule(shoulder, elbow, 1.8), tones)
    cv.layer(fore, "iron", capsule(elbow, hand, 1.8), tones)
    cv.layer(upper, "brass", ellipse(elbow[0], elbow[1], 2.2, 2.2), [0, 1, 2])
    draw_tool(cv, kind, hand, point, tools)
    cv.layer(hands, "brass", ellipse(hand[0], hand[1], 2.6, 2.6), [0, 1, 2] if dark else [0, 1, 2, 3])


def draw_governor(cv, top, spread, part="governor"):
    """A flyball governor: an iron spindle up from the head, a brass collar on top, two arms
    hanging from it to brass balls that fly out as it spins faster (spread 1) and droop when it
    stops (spread below 0), and links from the balls down to a sliding collar: the diamond that
    says governor (without them it read as an arrowhead)."""
    x, y = top
    cv.layer(part, "iron", capsule((x, y), (x, y - 19), 1.0), [1, 2, 3])
    cv.layer(part, "brass", ellipse(x, y - 19, 1.8, 1.8), [1, 2, 3])
    angle = 0.35 + 1.05 * spread  # from the spindle, in radians
    for side in (-1, 1):
        ball = (x + side * math.sin(angle) * 10, y - 19 + math.cos(angle) * 10)
        cv.layer(part, "iron", capsule((x, y - 19), ball, 1.2), [1, 2, 3])
        cv.layer(part, "iron", capsule(ball, (x, y - 7), 0.9), [1, 2, 3])
        cv.layer(part, "brass", ellipse(ball[0], ball[1], 3.0, 3.0), [0, 1, 2, 3])
    cv.layer(part, "brass", rect(x - 2, y - 7, x + 2, y - 6), [0, 1, 2])


def spiral(cx, cy, r0, r1, turns, squash=1.0):
    out = set()
    steps = int(turns * 80)
    for i in range(steps + 1):
        t = i / steps
        a = t * turns * 2 * math.pi
        r = r0 + (r1 - r0) * t
        out.add((round(cx + r * math.cos(a)), round(cy + r * math.sin(a) * squash)))
    return out


def draw_front(state, back=False):
    p = (BACK if back else FRONT)[state]
    # From behind, the golem's right is on the screen's right: the back is the front's shapes
    # mirrored, then shaded from the upper left as ever.
    cv = Canvas("north" if back else "south", mirror=back)
    hd, bd = p.get("head", 0), p.get("body", 0)
    o = hd + bd + HUNCH
    # Legs: short, a little bowed, brass knees, broad iron feet. Carried, its right leg lifts.
    for hip, knee, foot, lift in (((26, 70), (23, 81), (25, 91), 5 if p.get("step") else 0),
                                  ((38, 70), (41, 81), (39, 91), 0)):
        k, f = (knee[0], knee[1] - lift), (foot[0], foot[1] - lift)
        cv.layer("legs", "iron", capsule(hip, k, 2.0) | capsule(k, f, 1.8), [1, 2, 3, 4])
        cv.layer("legs", "brass", ellipse(k[0], k[1], 2.5, 2.5), [0, 1, 2, 3])
        cv.layer("legs", "iron", rrect(f[0] - 4, f[1] + 1, f[0] + 3, f[1] + 4, 2), [0, 1, 2, 3])
    # The mainspring in its cage: from behind, the whole back; from the front, hidden.
    if back:
        cv.layer("cage", "iron", shift(ellipse(32, 55, 11, 11), 0, bd), [0, 1, 2, 3])
        cv.layer("cage", "steam", shift(ellipse(32, 55, 8.6, 8.6), 0, bd), [0, 1])
        cv.paint("cage", "brass", 2, shift(spiral(32, 55, 1.5, 8, 3.2), 0, bd))
        cv.paint("cage", "iron", 1, shift(rect(32, 44, 32, 66) | rect(21, 55, 43, 55), 0, bd))
        cv.layer("cage", "brass", shift(ellipse(32, 55, 2, 2), 0, bd), [1, 2, 3])
    # Body: a hunched brass barrel, gear shoulders, a copper apron; on the chest, a pressure
    # gauge with its needle in the red.
    cv.layer("body", "brass", shift(ellipse(32, 57, 10, 12.5), 0, bd), [0, 1, 2, 3])
    for sx in (19, 45):
        cv.layer("body", "brass", shift(ellipse(sx, 47, 5, 5), 0, bd), [0, 1, 2, 3])
        cv.paint("body", "iron", 1, shift(ellipse(sx, 47, 1.6, 1.6), 0, bd))
        cv.dots("body", "brass", 0, [(sx - 1, 41 + bd), (sx + 4, 46 + bd), (sx - 1, 51 + bd), (sx - 6, 46 + bd)], 2)
    if not back:
        cv.layer("apron", "copper", shift(rrect(24, 58, 40, 72, 3) & ellipse(32, 60, 10, 13), 0, bd), [0, 1, 2])
        cv.paint("apron", "copper", 0, shift(rect(26, 65, 38, 65) | rect(32, 65, 32, 72), 0, bd))
        cv.paint("apron", "brass", 2, shift({(25, 59), (39, 59)}, 0, bd))
        # Below the hunched head, so the head never hides it.
        cv.layer("body", "iron", shift(ellipse(32, 54, 5.4, 4.6), 0, bd), [1, 2, 3])
        cv.layer("body", "steam", shift(ellipse(32, 54, 4.0, 3.3), 0, bd), [1, 2, 3])
        cv.paint("body", "fire", 1, shift({(35, 52), (36, 53), (36, 54)}, 0, bd))
        cv.paint("body", "fire", 0, shift({(32, 55), (33, 54), (34, 53), (35, 53)}, 0, bd))
        cv.dots("body", "iron", 1, [(25, 52 + bd), (38, 52 + bd)], 1)
    else:
        cv.layer("apron", "copper", shift(rect(30, 42, 34, 46), 0, bd), [0, 1, 2])
    # Head: a brass dome, hunched low and forward, a neck collar; from the front the loupe on its
    # right eye (the teal lens) on a hinged arm, a small left eye, a grille for a mouth.
    cv.layer("head", "iron", shift(rect(28, 38, 36, 42), 0, o), [1, 2, 3, 4])
    cv.layer("head", "brass", shift(ellipse(32, 31, 8.5, 8), 0, o), [0, 1, 2, 3])
    cv.paint("head", "brass", 0, shift(rect(24, 31, 40, 31) & ellipse(32, 31, 8.5, 8), 0, o))
    cv.dots("head", "iron", 1, [(26, 26 + o), (37, 26 + o)], 1)
    if not back:
        lo, mid, hi = eye_tones(p.get("eye"))
        cv.layer("head", "iron", shift(capsule((24, 25), (26, 30), 0.9), 0, o), [1, 2])
        cv.layer("head", "iron", shift(ellipse(27, 32, 4.6, 4.6), 0, o), [0, 1, 2, 3])
        cv.layer("head", "aether", shift(ellipse(27, 32, 3.3, 3.3), 0, o), sorted({lo, mid, hi}))
        cv.paint("head", "aether" if p.get("eye") != "bright" else "steam", hi if p.get("eye") != "bright" else 3,
                 shift(rect(26, 31, 26, 31), 0, o))
        cv.paint("head", "aether", mid, shift(rect(36, 31, 37, 32), 0, o))
        cv.layer("head", "iron", shift(rrect(29, 36, 35, 38, 1), 0, o), [0, 1])
    draw_governor(cv, (32, 23 + o), p["gov"])
    # Three arms: tweezers on its right shoulder (screen left), the hammer on its left, the
    # screwdriver from its left hip.
    draw_arm(cv, "tw", (19, 47 + bd), p["tw"])
    draw_arm(cv, "hm", (45, 47 + bd), p["hm"])
    draw_arm(cv, "sd", (41, 64 + bd), p["sd"])
    return cv


def draw_side(state, mirror=False):
    """East: the profile, front to the right. West (mirror): the same shapes mirrored and lit
    from the upper left again by the same shading rule (presser_draft.py says why)."""
    p = SIDE[state]
    cv = Canvas("west" if mirror else "east", mirror)
    hd, bd = p.get("head", 0), p.get("body", 0)
    o = hd + bd + HUNCH
    lift = 5 if p.get("step") else 0
    # The far arms (darker): the hammer from the far shoulder, the screwdriver from the far hip.
    draw_arm(cv, "hm", (29, 47 + bd), p["hm"], "far_arm", "far_arm", "far_arm", "far_arm", dark=True)
    draw_arm(cv, "sd", (31, 64 + bd), p["sd"], "far_arm", "far_arm", "far_arm", "far_arm", dark=True)
    # Legs: the far leg (darker), the near leg; feet forward.
    cv.layer("far_leg", "iron", capsule((28, 70), (26, 81), 2.0) | capsule((26, 81), (28, 91), 1.8), [0, 1, 2])
    cv.layer("far_leg", "iron", rrect(23, 91, 33, 95, 2), [0, 1, 2])
    k, f = (35, 81 - lift), (33, 91 - lift)
    cv.layer("legs", "iron", capsule((31, 70), k, 2.0) | capsule(k, f, 1.8), [1, 2, 3, 4])
    cv.layer("legs", "brass", ellipse(k[0], k[1], 2.5, 2.5), [0, 1, 2, 3])
    cv.layer("legs", "iron", rrect(29, 91 - lift, 40, 95 - lift, 2), [0, 1, 2, 3])
    # The mainspring's cage on its back: a drum, its coil's edge in brass bands.
    cv.layer("cage", "iron", shift(rrect(13, 43, 21, 66, 3), 0, bd), [0, 1, 2, 3, 4])
    for y in (47, 51, 55, 59, 63):
        cv.paint("cage", "brass", 2, shift(rect(15, y, 19, y), 0, bd))
    # Body: the barrel side on, hunched forward; the apron hanging in front; the gauge's rim.
    cv.layer("body", "brass", shift(ellipse(30, 57, 10.5, 12.5), 0, bd), [0, 1, 2, 3])
    cv.layer("body", "brass", shift(ellipse(30, 47, 5, 5), 0, bd), [0, 1, 2, 3])
    cv.paint("body", "iron", 1, shift(ellipse(30, 47, 1.6, 1.6), 0, bd))
    cv.layer("apron", "copper", shift(rrect(34, 58, 42, 72, 3), 0, bd), [0, 1, 2])
    cv.paint("apron", "copper", 0, shift(rect(36, 64, 42, 64), 0, bd))
    cv.layer("body", "iron", shift(rrect(39, 44, 43, 52, 1), 0, bd), [1, 2, 3])
    cv.paint("body", "fire", 1, shift({(42, 46), (42, 47)}, 0, bd))
    # Head: the dome forward of the body; the loupe's tube out in front with the lens at its end,
    # an iron rim round it so the outline lands on the rim, not the lens (the Scavenger's west
    # light check).
    cv.layer("head", "iron", shift(rect(32, 38, 38, 42), 0, o), [1, 2, 3, 4])
    cv.layer("head", "brass", shift(ellipse(37, 31, 8.5, 8.5), 0, o), [0, 1, 2, 3])
    cv.paint("head", "brass", 0, shift(rect(29, 31, 45, 31) & ellipse(37, 31, 8.5, 8.5), 0, o))
    cv.dots("head", "iron", 1, [(33, 26 + o)], 1)
    lo, mid, hi = eye_tones(p.get("eye"))
    cv.layer("head", "iron", shift(capsule((39, 24), (43, 28), 0.9), 0, o), [1, 2])
    cv.layer("head", "iron", shift(rrect(42, 27, 51, 37, 2), 0, o), [0, 1, 2, 3])
    cv.layer("head", "aether", shift(rect(47, 29, 49, 35), 0, o), sorted({lo, mid, hi}))
    cv.paint("head", "aether" if p.get("eye") != "bright" else "steam", hi if p.get("eye") != "bright" else 3,
             shift(rect(47, 30, 47, 30), 0, o))
    cv.layer("head", "iron", shift(rrect(40, 36, 44, 38, 1), 0, o), [0, 1])
    draw_governor(cv, (36, 23 + o), p["gov"])
    # The near arm: the tweezers.
    draw_arm(cv, "tw", (31, 47 + bd), p["tw"])
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
    out = {"body": [0, bd], "apron": [0, bd], "cage": [0, bd], "head": [0, hd + bd],
           "legs": [0, 0], "far_leg": [0, 0]}
    if p.get("step"):
        out["legs"] = None  # one leg lifts, not both: the draft's legs are used
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
