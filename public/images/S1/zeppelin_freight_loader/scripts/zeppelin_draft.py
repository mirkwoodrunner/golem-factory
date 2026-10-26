"""The Zeppelin Freight Loader as a creature (art-style-bible.md invariant 17): a scripted, layered
draft of every frame in the three drawn facings, with a part map, so that

- each facing's base is the image-to-image start and ControlNet guide for ComfyUI, and
- every other frame is derived from the cleaned base by moving its rigid parts
  (Tools/Art/golem_set.py's compose step),
  so its surface texture never changes between frames.

The roster's "massive industrial behemoth" that "packs bulk shipments onto outgoing trade
zeppelins" (digital-design.md), which "wears its balloon" (art-style-bible.md §5.1), and whose
verb is the Freight Link, launching its output to a far mast (progression-design.md §6): a huge
iron creature, great brass shoulders and a small head sunk between them, short thick legs, big
brass fists hugging a netted bundle of crates, a little canvas blimp tethered over its back. Its
work stroke crouches and flings the bundle up towards the mast; stalled, it drops the bundle and
the blimp sags. Copied from the Mainspring Overclocker's overclocker_draft.py
(art-pipeline-design.md §3.2); the shapes and poses are the Zeppelin's, the drawing rules the
same.

    python zeppelin_draft.py <out dir>   writes draft_<facing>_<state>.png (+ _8x on the key)
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
# The draw order per facing is the chassis' own (chassis.json "layers"): the bundle it holds is in
# front of it from the south and hidden behind it from the north; the blimp is behind it from the
# south and over it from the north.
LAYERS = json.load(open(os.path.join(HERE, "..", "chassis.json")))["layers"]
W, H = 64, 96
KEY = (40, 190, 70)
STATES = ["base", "idle_1", "work_0", "work_1", "work_2", "stall", "carry"]
FACINGS = ["south", "north", "east", "west"]
# Parts that move rigidly between frames: golem_set.py's compose copies the cleaned base's pixels for them,
# shifted by the frame's offset. Arms, the bundle and the ropes change shape, so they are always the draft's.
RIGID = ["legs", "far_leg", "body", "head", "balloon"]


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

# Per frame: each hand and its elbow (left and right on screen; from the side, n the near arm and
# f the far one); where the freight bundle is (cargo, None once thrown); the body's, head's and
# balloon's drop; the eye. The work stroke is the launch (art-style-bible.md §4: "Zeppelin:
# crouched, launch"): crouched with the bundle low (work_0), flung up towards the mast (work_1),
# arms still up as it flies (work_2). Stalled, the bundle is dropped and the blimp sags.
FRONT = {
    "base":   dict(l=((21, 67), (9, 60)), r=((43, 67), (55, 60)), cargo=(32, 67)),
    "work_0": dict(l=((21, 73), (9, 65)), r=((43, 73), (55, 65)), cargo=(32, 74), body=3, head=3, bal=1),
    "work_1": dict(l=((21, 33), (10, 44)), r=((43, 33), (54, 44)), cargo=(32, 24), body=-1, head=-1, bal=-2),
    "work_2": dict(l=((15, 40), (7, 50)), r=((49, 40), (57, 50)), cargo=None, bal=-1),
    "stall":  dict(l=((10, 82), (8, 70)), r=((54, 82), (56, 70)), cargo=(32, 87), body=3, head=6, bal=5, eye="dim"),
    "carry":  dict(l=((22, 62), (10, 58)), r=((42, 62), (54, 58)), cargo=(32, 62), eye="bright"),
}
SIDE = {
    "base":   dict(n=((47, 66), (40, 60)), f=((44, 70), (35, 62)), cargo=(48, 67)),
    "work_0": dict(n=((47, 72), (40, 65)), f=((44, 75), (35, 66)), cargo=(48, 73), body=3, head=3, bal=1),
    "work_1": dict(n=((42, 31), (44, 44)), f=((39, 33), (39, 45)), cargo=(40, 22), body=-1, head=-1, bal=-2),
    "work_2": dict(n=((52, 42), (46, 52)), f=((49, 44), (41, 52)), cargo=None, bal=-1),
    "stall":  dict(n=((44, 82), (40, 70)), f=((40, 82), (35, 70)), cargo=(50, 88), body=3, head=6, bal=5, eye="dim"),
    "carry":  dict(n=((46, 62), (41, 58)), f=((43, 64), (36, 58)), cargo=(47, 62), eye="bright"),
}
BACK = {}
for state, p in FRONT.items():
    q = dict(p)
    # From behind, what it holds is in front of it, so a little further up the floor.
    if p["cargo"] is not None and state != "work_1":
        q["cargo"] = (p["cargo"][0], p["cargo"][1] - 4)
    BACK[state] = q
for table in (FRONT, SIDE, BACK):
    table["idle_1"] = dict(table["base"], eye="bright")


def eye_tones(mode):
    return {"dim": (0, 0, 1), "bright": (1, 2, 2)}.get(mode, (0, 1, 2))


def draw_cargo(cv, at, w=11, h=7):
    """The freight bundle: mahogany crates roped together, knotted on top."""
    if at is None:
        return
    x, y = at
    cv.layer("cargo", "mahogany", rrect(x - w, y - h, x + w, y + h, 2), [2, 3, 5])
    cv.paint("cargo", "mahogany", 1, rect(x - 3, y - h, x - 3, y + h) | rect(x + 4, y - h, x + 4, y + h))
    cv.layer("cargo", "bone", rect(x - w, y - 4, x + w, y - 3) | rect(x - w, y + 2, x + w, y + 3), [0, 1, 2])
    cv.layer("cargo", "bone", rect(x, y - h, x + 1, y + h), [0, 1, 2])
    cv.layer("cargo", "bone", ellipse(x, y - h - 1, 2.2, 1.8), [1, 2])


def draw_arm(cv, shoulder, arm, dark=False, part=None):
    """A behemoth's arm: a thick iron upper arm, a heavier forearm, a big brass fist."""
    hand, elbow = arm
    tones = [0, 1, 2] if dark else [1, 2, 3, 4]
    upper, fore, hands = (part, part, part) if part else ("arms", "forearms", "hands")
    cv.layer(upper, "iron", capsule(shoulder, elbow, 3.4), tones)
    cv.layer(fore, "iron", capsule(elbow, hand, 4.0), tones)
    cv.layer(fore, "copper", ellipse(elbow[0], elbow[1], 3.0, 3.0), [0, 1] if dark else [0, 1, 2])
    cv.layer(hands, "brass", ellipse(hand[0], hand[1], 4.2, 4.0), [0, 1, 2] if dark else [0, 1, 2, 3])


# Nose-on, the blimp drifts to its right on its ropes, off the golem's middle: centred, the
# envelope sat where the Overclocker's governor is and the two outlines overlapped 0.50.
BLIMP_X = 43


def draw_balloon_end(cv, dy, tail):
    """The blimp end on: a round bone canvas envelope with a brass band, fins out to the sides
    and up, a brass nose cap (from the front) or the copper tail cone (from behind), a little
    mahogany gondola slung under it."""
    x, y = BLIMP_X, 18 + dy
    fins = ellipse(x - 15, y, 4, 2.2) | ellipse(x + 15, y, 4, 2.2) | ellipse(x, y - 12, 2.2, 4)
    cv.layer("balloon", "copper", fins, [0, 1, 2])
    cv.layer("balloon", "bone", ellipse(x, y, 12, 10), [0, 1, 2])
    cv.paint("balloon", "brass", 1, rect(x - 12, y + 3, x + 12, y + 3) & ellipse(x, y, 12, 10))
    cv.layer("balloon", "mahogany", rrect(x - 5, y + 9, x + 5, y + 13, 1), [2, 3, 5])
    cv.paint("balloon", "brass", 2, rect(x - 4, y + 9, x + 4, y + 9))
    if tail:
        cv.layer("balloon", "copper", ellipse(x, y, 3.6, 3.6), [0, 1, 2])
    else:
        cv.layer("balloon", "brass", ellipse(x, y, 3.2, 3.0), [1, 2, 3])
    cv.dots("balloon", "steam", 3, [(x - 6, y - 6)], 2)


def draw_ropes(cv, pairs, part="ropes"):
    """Hemp ropes, one flat tone: lit bone ones on the far edge of a profile read as light from
    the right (the west launch frame failed check 14)."""
    for a, b in pairs:
        cv.layer(part, "mahogany", capsule(a, b, 1.6), [3, 5], light=False)


def draw_front(state, back=False):
    p = (BACK if back else FRONT)[state]
    cv = Canvas("north" if back else "south", mirror=back)
    hd, bd, ld = p.get("head", 0), p.get("body", 0), p.get("bal", 0)
    o = hd + bd
    # Legs: short and thick, brass knee plates, broad feet.
    for hip, foot in (((24, 76), (22, 90)), ((40, 76), (42, 90))):
        cv.layer("legs", "iron", capsule(hip, foot, 4.6), [1, 2, 3, 4])
        cv.layer("legs", "brass", ellipse(foot[0] + (hip[0] - foot[0]) * 0.4, 83, 3.4, 3.0), [0, 1, 2, 3])
        cv.layer("legs", "iron", rrect(foot[0] - 7, 89, foot[0] + 6, 95, 2), [0, 1, 2, 3])
    # The blimp, tethered over its back, and the ropes down to its shoulders.
    draw_balloon_end(cv, ld, tail=back)
    draw_ropes(cv, [((BLIMP_X - 3, 31 + ld), (17, 41 + bd)), ((BLIMP_X + 2, 31 + ld), (48, 41 + bd))])
    # Body: a huge chest, narrow at the waist; great brass shoulders; a copper chest plate, a
    # belt with a brass buckle. From behind, the winch drum its ropes wind on.
    chest = ellipse(32, 54, 18, 13) | rrect(22, 56, 42, 76, 4)
    cv.layer("body", "iron", shift(chest, 0, bd), [0, 1, 2, 3, 4])
    for sx in (14, 50):
        cv.layer("body", "brass", shift(ellipse(sx, 46, 8, 7), 0, bd), [0, 1, 2, 3])
        cv.dots("body", "brass", 0, [(sx - 3, 48 + bd), (sx + 2, 48 + bd)], 1)
    cv.layer("body", "copper", shift(rect(21, 70, 43, 73) & chest, 0, bd), [0, 1, 2])
    if not back:
        cv.layer("body", "copper", shift(rrect(23, 48, 41, 62, 3), 0, bd), [0, 1, 2])
        cv.dots("body", "brass", 3, [(25, 50 + bd), (38, 50 + bd), (25, 59 + bd), (38, 59 + bd)], 1)
        cv.layer("body", "brass", shift(rect(30, 70, 34, 73), 0, bd), [1, 2, 3])
    else:
        cv.layer("body", "iron", shift(rrect(23, 46, 41, 56, 3), 0, bd), [1, 2, 3])
        cv.layer("body", "brass", shift(rect(25, 48, 39, 54), 0, bd), [1, 2, 3])
        for x in range(26, 39, 3):
            cv.paint("body", "brass", 0, shift(rect(x, 48, x, 54), 0, bd))
        cv.dots("body", "iron", 1, [(24, 62 + bd), (39, 62 + bd)], 2)
    # Head: small for its bulk, sunk between the shoulders, a heavy brass brow, two teal eyes and
    # an iron jaw.
    cv.layer("head", "iron", shift(ellipse(32, 41, 8, 7), 0, o), [0, 1, 2, 3])
    cv.layer("head", "brass", shift(rrect(23, 37, 41, 40, 1), 0, o), [1, 2, 3])
    if not back:
        lo, mid, hi = eye_tones(p.get("eye"))
        cv.layer("head", "aether", shift(rect(27, 42, 29, 43) | rect(35, 42, 37, 43), 0, o), [mid, hi])
        cv.layer("head", "iron", shift(rrect(28, 45, 36, 48, 1), 0, o), [0, 1])
        cv.paint("head", "iron", 0, shift({(30, 46), (32, 46), (34, 46)}, 0, o))
    # Arms round the bundle, the bundle in front of its belly.
    draw_arm(cv, (14, 48 + bd), (p["l"][0], p["l"][1]))
    draw_arm(cv, (50, 48 + bd), (p["r"][0], p["r"][1]))
    draw_cargo(cv, p["cargo"])
    return cv


def draw_side(state, mirror=False):
    """East: the profile, front to the right. West (mirror): the same shapes mirrored and lit
    from the upper left again by the same shading rule (presser_draft.py says why)."""
    p = SIDE[state]
    cv = Canvas("west" if mirror else "east", mirror)
    hd, bd, ld = p.get("head", 0), p.get("body", 0), p.get("bal", 0)
    o = hd + bd
    lo, mid, hi = eye_tones(p.get("eye"))
    # The far arm (darker), the far leg, the near leg.
    draw_arm(cv, (28, 49 + bd), p["f"], dark=True, part="far_arm")
    cv.layer("far_leg", "iron", capsule((26, 76), (25, 90), 4.4), [0, 1, 2])
    cv.layer("far_leg", "iron", rrect(19, 89, 33, 95, 2), [0, 1, 2])
    cv.layer("legs", "iron", capsule((34, 76), (35, 90), 4.6), [1, 2, 3, 4])
    cv.layer("legs", "brass", ellipse(35, 83, 3.2, 3.0), [0, 1, 2, 3])
    cv.layer("legs", "iron", rrect(29, 89, 44, 95, 2), [0, 1, 2, 3])
    # The blimp side on, nose forward: the envelope, its rings, the tail fins behind.
    y = 18 + ld
    cv.layer("balloon", "copper", capsule((5, y - 9), (12, y - 2), 1.8) | capsule((5, y + 9), (12, y + 2), 1.8), [0, 1, 2])
    cv.layer("balloon", "bone", ellipse(28, y, 21, 9), [0, 1, 2])
    for x in (16, 28, 40):
        cv.paint("balloon", "brass", 1, rect(x, y - 9, x, y + 9) & ellipse(28, y, 21, 9))
    cv.layer("balloon", "brass", ellipse(47, y, 3, 3), [1, 2, 3])
    cv.layer("balloon", "mahogany", rrect(22, y + 8, 34, y + 12, 1), [2, 3, 5])
    cv.paint("balloon", "brass", 2, rect(23, y + 8, 33, y + 8))
    cv.dots("balloon", "steam", 3, [(22, y - 6)], 2)
    draw_ropes(cv, [((24, 30 + ld), (22, 46 + bd)), ((32, 30 + ld), (31, 42 + bd))])
    # Body: the chest side on, hunched forward; the winch on its back; the shoulder.
    chest = ellipse(31, 56, 14, 15) | rrect(24, 60, 40, 76, 4)
    cv.layer("body", "iron", shift(chest, 0, bd), [0, 1, 2, 3, 4])
    cv.layer("body", "brass", shift(rrect(14, 48, 20, 60, 2), 0, bd), [1, 2, 3])
    cv.layer("body", "copper", shift(rect(24, 70, 42, 73) & chest, 0, bd), [0, 1, 2])
    cv.layer("body", "copper", shift(rrect(38, 50, 44, 62, 2), 0, bd), [0, 1, 2])
    cv.layer("body", "brass", shift(ellipse(32, 48, 7.5, 6.5), 0, bd), [0, 1, 2, 3])
    # Head: forward of the chest, low; the brow, the eye, the jaw.
    cv.layer("head", "iron", shift(ellipse(43, 44, 7, 6.5), 0, o), [0, 1, 2, 3])
    cv.layer("head", "brass", shift(rrect(38, 39, 51, 42, 1), 0, o), [1, 2, 3])
    cv.layer("head", "aether", shift(rect(47, 44, 49, 45), 0, o), [mid, hi])
    cv.layer("head", "iron", shift(rrect(43, 47, 50, 50, 1), 0, o), [0, 1])
    # The near arm and the bundle it holds.
    draw_arm(cv, (32, 49 + bd), p["n"])
    draw_cargo(cv, p["cargo"], w=7, h=7)
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
    out = {"body": [0, bd], "head": [0, hd + bd], "balloon": [0, p.get("bal", 0)],
           "legs": [0, 0], "far_leg": [0, 0]}
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
