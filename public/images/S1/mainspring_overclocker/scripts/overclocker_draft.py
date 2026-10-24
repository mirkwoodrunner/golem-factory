"""The Mainspring Overclocker as a creature (art-style-bible.md invariant 17): a scripted, layered
draft of every frame in the three drawn facings, with a part map, so that

- each facing's base is the image-to-image start and ControlNet guide for ComfyUI, and
- every other frame is derived from the cleaned base by moving its rigid parts
  (Tools/Art/golem_set.py's compose step),
  so its surface texture never changes between frames.

A thin clockwork butler (digital-design.md's roster): a tall narrow brass body in a waistcoat
with a clock face on its chest, iron coat-tails, a white collar and bow tie, a small brass head
under an iron top hat with a teal monocle and a moustache, a brass wind-up key in its back, thin
iron legs and arms. One hand holds a tuning-fork staff, which it strikes on the floor (its quick
stamp, the harmonic wave); a towel hangs over the other arm.
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
# The draw order per facing is the chassis' own (chassis.json "layers"): the staff is in front of
# the body from the south and hidden behind it from the north.
LAYERS = json.load(open(os.path.join(HERE, "..", "chassis.json")))["layers"]
W, H = 64, 96
KEY = (40, 190, 70)
STATES = ["base", "idle_1", "work_0", "work_1", "work_2", "stall", "carry"]
FACINGS = ["south", "north", "east", "west"]
# Parts that move rigidly between frames: golem_set.py's compose copies the cleaned base's pixels for them,
# shifted by the frame's offset. Arms, hands and the staff change shape, so they are always the draft's.
RIGID = ["legs", "far_leg", "tails", "body", "key", "head"]


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

# Per frame: the staff hand (grip), its elbow and the staff's lean (radians; positive tilts its
# top to the screen's left); the towel hand and elbow; the head's and body's drop; a lifted leg;
# the eye. The work stroke: the staff raised (work_0), struck on the floor (work_1), lifted
# back (work_2); stalled it droops outward, carried it is held upright and close (across the
# body, it hid the clock face and its fork crossed the face). South and north share it; east
# has its own (profile).
FRONT = {
    "base":   dict(grip=(15, 62), elbow=(17, 54), lean=0.0, hand=(37, 62), hand_elbow=(46, 55)),
    "work_0": dict(grip=(13, 46), elbow=(12, 53), lean=0.3, hand=(37, 61), hand_elbow=(46, 54)),
    "work_1": dict(grip=(16, 70), elbow=(16, 58), lean=0.0, hand=(37, 64), hand_elbow=(46, 57),
                   body=2, head=1),
    "work_2": dict(grip=(15, 64), elbow=(16, 55), lean=0.1, hand=(37, 63), hand_elbow=(46, 56), body=1),
    "stall":  dict(grip=(16, 72), elbow=(18, 60), lean=0.25, hand=(44, 72), hand_elbow=(45, 60),
                   head=3, body=2, eye="dim"),
    "carry":  dict(grip=(19, 58), elbow=(16, 54), lean=-0.05, hand=(37, 62), hand_elbow=(46, 55), step=True),
}
SIDE = {
    "base":   dict(grip=(44, 62), elbow=(38, 56), lean=0.0, hand=(36, 62), hand_elbow=(29, 56)),
    "work_0": dict(grip=(44, 47), elbow=(40, 55), lean=-0.25, hand=(36, 61), hand_elbow=(29, 55)),
    "work_1": dict(grip=(48, 71), elbow=(41, 60), lean=-0.15, hand=(36, 64), hand_elbow=(29, 58),
                   body=2, head=1),
    "work_2": dict(grip=(45, 64), elbow=(39, 57), lean=-0.05, hand=(36, 63), hand_elbow=(29, 57), body=1),
    "stall":  dict(grip=(45, 72), elbow=(39, 61), lean=-0.3, hand=(34, 70), hand_elbow=(29, 60),
                   head=3, body=2, eye="dim"),
    "carry":  dict(grip=(43, 56), elbow=(38, 54), lean=0.05, hand=(36, 62), hand_elbow=(29, 56), step=True),
}
# North: the golem faces away, so the staff is in front of it, further up the floor: drawn a
# little higher than from the south and behind its body (chassis.json "layers"). The whole view
# is drawn mirrored (draw_front), so the staff stays in the golem's own right hand (the
# Scavenger's review).
BACK = {}
for state, p in FRONT.items():
    q = dict(p)
    q["grip"] = (p["grip"][0], p["grip"][1] - 6)
    q["elbow"] = (p["elbow"][0], p["elbow"][1] - 3)
    BACK[state] = q
for table in (FRONT, SIDE, BACK):
    table["idle_1"] = dict(table["base"], eye="bright")


def eye_tones(mode):
    return {"dim": (0, 0, 1), "bright": (1, 2, 2)}.get(mode, (0, 1, 2))


def draw_staff(cv, grip, lean, part="staff"):
    """An iron staff through the hand, its butt below, a brass tuning fork on top: a cross bar
    and two tines along the staff."""
    ux, uy = -math.sin(lean), -math.cos(lean)  # up the staff
    vx, vy = math.cos(lean), -math.sin(lean)   # across it
    top = (grip[0] + ux * 24, grip[1] + uy * 24)
    butt = (grip[0] - ux * 24, grip[1] - uy * 24)
    cv.layer(part, "iron", capsule(butt, top, 1.4), [1, 2, 3, 4])
    for side in (-1, 1):
        root = (top[0] + vx * 4 * side, top[1] + vy * 4 * side)
        tip = (root[0] + ux * 11, root[1] + uy * 11)
        cv.layer(part, "brass", capsule(root, tip, 1.5), [0, 1, 2, 3])
    cv.layer(part, "brass", capsule((top[0] - vx * 4, top[1] - vy * 4), (top[0] + vx * 4, top[1] + vy * 4), 1.7), [0, 1, 2, 3])
    cv.layer(part, "brass", ellipse(top[0] - ux * 1.5, top[1] - uy * 1.5, 2.2, 2.2), [1, 2, 3])


def draw_hand(cv, at, part="hands"):
    cv.layer(part, "brass", ellipse(at[0], at[1], 2.8, 2.8), [0, 1, 2, 3])


def draw_head(cv, p, o, back=False):
    """A small brass oval on an iron neck, under an iron top hat with a brass band; from the
    front and side a teal monocle and a moustache."""
    cv.layer("head", "iron", shift(rect(30, 32, 34, 37), 0, o), [1, 2, 3, 4])
    cv.layer("head", "brass", shift(ellipse(32, 25, 7.5, 8.5), 0, o), [0, 1, 2, 3])
    cv.layer("head", "iron", shift(rect(23, 15, 41, 17) | rrect(26, 3, 38, 16, 1), 0, o), [0, 1, 2, 3, 4])
    cv.paint("head", "brass", 2, shift(rect(26, 12, 38, 13), 0, o))
    if back:
        return
    lo, mid, hi = eye_tones(p.get("eye"))
    cv.layer("head", "iron", shift(ellipse(35.5, 23.5, 3.8, 3.8), 0, o), [0, 1, 2])
    cv.layer("head", "aether", shift(ellipse(35.5, 23.5, 2.6, 2.6), 0, o), sorted({lo, mid, hi}))
    cv.paint("head", "aether" if p.get("eye") != "bright" else "steam", hi if p.get("eye") != "bright" else 3,
             shift(rect(34, 22, 34, 22), 0, o))


def draw_front(state, back=False):
    p = (BACK if back else FRONT)[state]
    # From behind, the golem's right is on the screen's right: the back is the front's shapes
    # mirrored, then shaded from the upper left as ever.
    cv = Canvas("north" if back else "south", mirror=back)
    hd, bd = p.get("head", 0), p.get("body", 0)
    o = hd + bd
    # Legs: thin iron, brass knees, polished iron shoes. Carried, its right leg lifts.
    for x, lift in ((28, 5 if p.get("step") else 0), (36, 0)):
        knee = (x, 78 - lift)
        cv.layer("legs", "iron", capsule((x, 64), knee, 2.0) | capsule(knee, (x, 90 - lift), 1.8), [1, 2, 3, 4])
        cv.layer("legs", "brass", ellipse(knee[0], knee[1], 2.6, 2.6), [0, 1, 2, 3])
        cv.layer("legs", "iron", rrect(x - 4, 90 - lift, x + 3, 95 - lift, 2), [0, 1, 2, 3])
    # The coat-tails: iron, from the waist to the knees, either side; from behind, the coat's back.
    tails = capsule((28, 52), (26, 74), 2.6) | capsule((36, 52), (38, 74), 2.6)
    if back:
        tails |= rrect(26, 44, 38, 70, 3)
    cv.layer("tails", "iron", shift(tails, 0, bd), [0, 1, 2, 3, 4])
    # The wind-up key in its back: a brass bow either side of the body, an iron hub between.
    key = ellipse(17, 50, 4.5, 5.5) | ellipse(47, 50, 4.5, 5.5) | rect(20, 48, 44, 52)
    cv.layer("key", "brass", shift(key, 0, bd), [0, 1, 2, 3])
    for cx in (17, 47):
        cv.paint("key", "iron", 0, shift(ellipse(cx, 50, 1.6, 2.5), 0, bd))
    if back:
        cv.layer("key", "iron", shift(ellipse(32, 50, 3.6, 3.6), 0, bd), [1, 2, 3, 4])
    # Body: a tall narrow brass waistcoat, shoulder caps, a clock face on the chest, buttons,
    # the collar and bow tie.
    cv.layer("body", "brass", shift(rrect(26, 38, 38, 66, 4), 0, bd), [0, 1, 2, 3])
    cv.layer("body", "brass", shift(ellipse(25, 43, 3.2, 3.2) | ellipse(39, 43, 3.2, 3.2), 0, bd), [1, 2, 3])
    if not back:
        cv.layer("body", "iron", shift(ellipse(32, 50, 6.2, 6.2), 0, bd), [1, 2, 3])
        cv.layer("body", "steam", shift(ellipse(32, 50, 4.6, 4.6), 0, bd), [1, 2, 3])
        cv.paint("body", "iron", 0, shift(rect(32, 47, 32, 50) | rect(33, 50, 34, 50), 0, bd))
        cv.dots("body", "iron", 1, [(31, 58 + bd), (31, 62 + bd)], 2)
        cv.layer("body", "steam", shift(rect(28, 36, 36, 39), 0, bd), [1, 2, 3])
        cv.layer("body", "iron", shift(ellipse(29.5, 38, 2.2, 1.6) | ellipse(34.5, 38, 2.2, 1.6), 0, bd), [0, 1, 2])
    draw_head(cv, p, o, back)
    if not back:
        # The right eye, small, and a moustache.
        lo, mid, hi = eye_tones(p.get("eye"))
        cv.paint("head", "aether", mid, shift(rect(27, 23, 29, 24), 0, o))
        cv.layer("head", "iron", shift(capsule((28, 29), (36, 29), 1.2), 0, o), [0, 1])
    # Arms: thin iron. The golem's right hand (screen left) holds the staff; the left is folded
    # at its waist with a towel over the forearm.
    shoulders = [(25, 44 + bd), (39, 44 + bd)]
    cv.layer("arms", "iron", capsule(shoulders[0], p["elbow"], 2.2), [1, 2, 3, 4])
    cv.layer("forearms", "iron", capsule(p["elbow"], p["grip"], 2.0), [1, 2, 3, 4])
    cv.layer("arms", "iron", capsule(shoulders[1], p["hand_elbow"], 2.2), [1, 2, 3, 4])
    cv.layer("forearms", "iron", capsule(p["hand_elbow"], p["hand"], 2.0), [1, 2, 3, 4])
    ex, ey = p["hand_elbow"]
    cv.layer("hands", "steam", rrect(ex - 3, ey + 1, ex + 2, ey + 11, 1), [1, 2, 3])
    draw_hand(cv, p["hand"])
    draw_staff(cv, p["grip"], p["lean"])
    draw_hand(cv, p["grip"])
    return cv


def draw_side(state, mirror=False):
    """East: the profile, front to the right. West (mirror): the same shapes mirrored and lit
    from the upper left again by the same shading rule (presser_draft.py says why)."""
    p = SIDE[state]
    cv = Canvas("west" if mirror else "east", mirror)
    hd, bd = p.get("head", 0), p.get("body", 0)
    o = hd + bd
    lift = 5 if p.get("step") else 0
    # The far arm (darker) with the towel, behind everything.
    cv.layer("far_arm", "iron", capsule((30, 45 + bd), p["hand_elbow"], 2.0) | capsule(p["hand_elbow"], p["hand"], 1.9), [0, 1, 2])
    ex, ey = p["hand_elbow"]
    cv.layer("far_arm", "steam", rrect(ex - 2, ey + 1, ex + 2, ey + 10, 1), [0, 1, 2])
    # Legs: the far leg (darker) a step back, the near leg; shoes pointing forward.
    cv.layer("far_leg", "iron", capsule((30, 64), (29, 78), 2.1) | capsule((29, 78), (28, 89), 2.0), [0, 1, 2])
    cv.layer("far_leg", "iron", rrect(24, 89, 34, 95, 2), [0, 1, 2])
    knee = (34, 78 - lift)
    cv.layer("legs", "iron", capsule((33, 64), knee, 2.3) | capsule(knee, (34, 89 - lift), 2.1), [1, 2, 3, 4])
    cv.layer("legs", "brass", ellipse(knee[0], knee[1], 2.6, 2.6), [0, 1, 2, 3])
    cv.layer("legs", "iron", rrect(30, 89 - lift, 41, 95 - lift, 2), [0, 1, 2, 3])
    # The coat-tails behind.
    cv.layer("tails", "iron", shift(capsule((26, 50), (19, 78), 3.4), 0, bd), [0, 1, 2, 3, 4])
    # The key: a shaft out of its back, the bow face on.
    cv.layer("key", "iron", shift(capsule((25, 49), (16, 49), 1.5), 0, bd), [1, 2, 3])
    cv.layer("key", "brass", shift(ellipse(13, 43, 3.5, 5) | ellipse(13, 55, 3.5, 5) | rect(11, 47, 15, 51), 0, bd), [0, 1, 2, 3])
    for cy in (43, 55):
        cv.paint("key", "iron", 0, shift(ellipse(13, cy, 1.3, 2.3), 0, bd))
    # Body in profile: the waistcoat, buttons down the front, the collar and the bow tie.
    cv.layer("body", "brass", shift(rrect(25, 38, 39, 66, 4), 0, bd), [0, 1, 2, 3])
    cv.dots("body", "iron", 1, [(37, 56 + bd), (37, 61 + bd)], 2)
    cv.layer("body", "steam", shift(rect(32, 36, 37, 39), 0, bd), [1, 2, 3])
    cv.layer("body", "iron", shift(ellipse(39, 38, 1.8, 1.8), 0, bd), [0, 1, 2])
    draw_head(cv, p, o)
    cv.layer("head", "iron", shift(capsule((35, 29), (40, 29), 1.2), 0, o), [0, 1])
    # The near arm and the staff in front.
    cv.layer("arms", "iron", capsule((32, 45 + bd), p["elbow"], 2.2), [1, 2, 3, 4])
    cv.layer("forearms", "iron", capsule(p["elbow"], p["grip"], 2.0), [1, 2, 3, 4])
    draw_staff(cv, p["grip"], p["lean"])
    draw_hand(cv, p["grip"])
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
    out = {"body": [0, bd], "tails": [0, bd], "key": [0, bd], "head": [0, hd + bd],
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
