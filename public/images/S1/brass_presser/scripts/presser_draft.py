"""The Brass Presser as a creature (art-style-bible.md invariant 17): a scripted, layered draft
of every frame in the three drawn facings, with a part map, so that

- each facing's base is the image-to-image start and ControlNet guide for ComfyUI, and
- every other frame is derived from the cleaned base by moving its rigid parts (compose.py),
  so its surface texture never changes between frames.

A squat brass golem: stubby iron legs with bolted feet, a body framed by two brass pillars and a
crossbeam of shoulders (the press frame), a furnace glowing in its belly, a domed head with one
teal gauge eye and a little smokestack, iron arms with copper elbows, and a brass ram with an
iron striking face that it carries in both hands and stamps with.

    python presser_draft.py <out dir>       writes draft_<facing>_<state>.png (+ _8x on the key)
                                            and parts_<facing>_<state>.json for all 21 frames

Coordinates are pixels on the 64 x 96 canvas, y down, feet on row 95.
"""
import json
import math
import os
import sys

from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
PALETTE = os.path.normpath(os.path.join(HERE, "..", "..", "..", "..", "..", "Tools", "Art", "palette.json"))
RAMPS = {k: [tuple(int(c[i:i + 2], 16) for i in (1, 3, 5)) for c in v["colours"]]
         for k, v in json.load(open(PALETTE))["ramps"].items()}
W, H = 64, 96
KEY = (40, 190, 70)
STATES = ["base", "idle_1", "work_0", "work_1", "work_2", "stall", "carry"]
FACINGS = ["south", "north", "east", "west"]
# Parts that move rigidly between frames: compose.py copies the cleaned base's pixels for them,
# shifted by the frame's offset. Arms and hands change shape, so they are always the draft's.
RIGID = ["legs", "body", "head", "ram", "pack"]


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

    DROP = 4  # everything above the legs sits this much lower: a squat golem

    def __init__(self, mirror=False):
        self.colour = {}
        self.part = {}
        self.mirror = mirror  # west: the east profile's shapes mirrored, then shaded as ever

    def _drop(self, part, pixels):
        dy = 0 if part == "legs" else self.DROP
        return {((W - 1 - x) if self.mirror else x, y + dy) for x, y in pixels}

    def layer(self, part, ramp, pixels, tones=None, flat=None, light=True):
        ramp_c = RAMPS[ramp]
        tones = tones or list(range(len(ramp_c)))
        pixels = {(x, y) for x, y in self._drop(part, pixels) if 0 <= x < W and 0 <= y < H}
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
        points = [((W - size - x) if self.mirror else x, y + (0 if part == "legs" else self.DROP)) for x, y in points]
        for x0, y0 in points:
            for y in range(y0, y0 + size):
                for x in range(x0, x0 + size):
                    if 0 <= x < W and 0 <= y < H:
                        self.colour[(x, y)] = RAMPS[ramp][tone]
                        self.part[(x, y)] = part

    def paint(self, part, ramp, tone, pixels):
        for x, y in self._drop(part, pixels):
            if 0 <= x < W and 0 <= y < H:
                self.colour[(x, y)] = RAMPS[ramp][tone]
                self.part[(x, y)] = part

    def image(self):
        img = Image.new("RGBA", (W, H), (0, 0, 0, 0))
        px = img.load()
        for (x, y), c in self.colour.items():
            px[x, y] = c + (255,)
        # Interior lines: where two parts meet, the front one's edge takes its ramp's darkest
        # tone (art-style-bible.md §2.1); the silhouette takes the outline ramp.
        out = img.copy()
        op = out.load()
        for (x, y), c in self.colour.items():
            nbrs = [(x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)]
            if any(n not in self.colour for n in nbrs):
                op[x, y] = RAMPS["outline"][1 if (x < W // 2 and y < 60) else 0] + (255,)
        return out

    def part_map(self):
        return {f"{x},{y}": p for (x, y), p in self.part.items()}


def _unused_edge_lines(cv, part, pixels, ramp):
    """A part's own border, inside the silhouette, in its ramp's darkest tone: so a part reads
    against the part behind it."""
    pixels = {(x, y) for x, y in pixels if 0 <= x < W and 0 <= y < H}
    for x, y in pixels:
        if cv.part.get((x, y)) != part:
            continue
        for n in ((x + 1, y), (x, y + 1), (x - 1, y), (x, y - 1)):
            if n not in pixels:
                cv.colour[(x, y)] = RAMPS[ramp][0]
                break


# --- the poses ---------------------------------------------------------------------------------

# Per frame: the ram's top-left, the hands, the elbows, the head's and body's drop, the legs'
# tuck, the eye and fire mode. South and north share them; east has its own (profile).
FRONT = {
    "base":   dict(ram=(17, 70), hands=[(13, 75), (50, 75)], elbows=[(3, 66), (60, 66)]),
    "work_0": dict(ram=(17, 6), hands=[(17, 12), (46, 12)], elbows=[(2, 34), (61, 34)]),
    "work_1": dict(ram=(17, 80), hands=[(15, 84), (48, 84)], elbows=[(2, 70), (61, 70)]),
    "work_2": dict(ram=(17, 58), hands=[(14, 63), (49, 63)], elbows=[(2, 58), (61, 58)]),
    "stall":  dict(ram=(17, 80), hands=[(16, 82), (47, 82)], elbows=[(5, 72), (58, 72)], head=4, body=2, eye="dim", fire="dim"),
    "carry":  dict(ram=(17, 60), hands=[(18, 65), (45, 65)], elbows=[(9, 63), (54, 63)], legs=(-6, 3)),
}
SIDE = {
    "base":   dict(ram=(41, 66), hand=(43, 71), elbow=(36, 64), far_hand=(46, 69), far_elbow=(40, 63)),
    "work_0": dict(ram=(33, 6), hand=(37, 12), elbow=(42, 34), far_hand=(38, 12), far_elbow=(43, 34)),
    "work_1": dict(ram=(43, 80), hand=(46, 83), elbow=(42, 68), far_hand=(49, 83), far_elbow=(46, 68)),
    "work_2": dict(ram=(43, 56), hand=(46, 61), elbow=(34, 62), far_hand=(49, 61), far_elbow=(38, 61)),
    "stall":  dict(ram=(43, 80), hand=(45, 82), elbow=(38, 72), far_hand=(48, 82), far_elbow=(42, 72), head=4, body=2, eye="dim"),
    "carry":  dict(ram=(37, 60), hand=(39, 65), elbow=(31, 62), far_hand=(42, 64), far_elbow=(35, 61), legs=(-6, 0)),
}
for table in (FRONT, SIDE):
    table["idle_1"] = dict(table["base"], eye="bright")


def fire_tones(mode):
    return {"dim": (0, 1, 1), "bright": (1, 2, 3)}.get(mode, (1, 2, 3))


def eye_tones(mode):
    return {"dim": (0, 0, 1), "bright": (1, 2, 2)}.get(mode, (0, 1, 2))


def draw_front(state, back=False):
    p = FRONT[state]
    cv = Canvas()
    hd, bd = p.get("head", 0), p.get("body", 0)
    lt_dy, lt_dx = p.get("legs", (0, 0))
    # Legs and feet (iron), bolted soles.
    for sx, flip in ((0, 1), (1, -1)):
        lx = 13 if sx == 0 else 42
        leg = shift(rect(lx, 79, lx + 8, 89), lt_dx * flip, lt_dy)
        foot = shift(rrect(lx - 4 if sx == 0 else lx - 1, 88, lx + 9 if sx == 0 else lx + 12, 95, 2), lt_dx * flip, lt_dy)
        cv.layer("legs", "iron", leg, [1, 2, 3, 4])
        cv.layer("legs", "iron", foot, [0, 1, 2, 3])
        cv.dots("legs", "copper", 1, [(lx - 2 + lt_dx * flip if sx == 0 else lx + 9 + lt_dx * flip, 92 + lt_dy)], 2)
    ram = p["ram"]
    ram_px = shift(rrect(0, 0, 29, 11, 2), *ram)
    if back:
        # From behind, the ram is in front of the body: drawn first, mostly hidden.
        cv.layer("ram", "brass", ram_px, [0, 1, 2, 3])
    # Body: two brass pillars, an iron belly plate, a crossbeam of shoulders (the press frame).
    body = shift(rrect(10, 54, 53, 74, 5) | rrect(16, 70, 47, 82, 4), 0, bd)
    cv.layer("body", "iron", body, [1, 2, 3, 4])
    for x0 in (10, 47):
        cv.layer("body", "brass", shift(rrect(x0, 54, x0 + 6, 74, 2), 0, bd), [0, 1, 2, 3])
        cv.dots("body", "copper", 2 if x0 == 10 else 0, [(x0 + 2, 60 + bd), (x0 + 2, 67 + bd)], 2)
    cv.layer("body", "brass", shift(rrect(2, 50, 61, 59, 4), 0, bd), [0, 1, 2, 3])
    cv.dots("body", "iron", 3, [(5, 54 + bd), (56, 54 + bd)], 2)
    cv.paint("body", "iron", 0, shift(rect(18, 80, 45, 80), 0, bd))
    if back:
        # The back: a round boiler hatch with vent slats, rivets round it.
        hatch = shift(ellipse(31.5, 68, 7, 7), 0, bd)
        cv.layer("body", "iron", hatch, [0, 1, 2, 3])
        for y in range(64, 73, 3):
            cv.paint("body", "iron", 0, shift(rect(27, y, 36, y), 0, bd))
        cv.dots("body", "copper", 1, [(21, 62 + bd), (41, 62 + bd), (21, 76 + bd), (41, 76 + bd)], 2)
    else:
        # The belly furnace: an iron rim, glowing grate, two bars.
        lo, mid, hi = fire_tones(p.get("fire"))
        cv.layer("body", "iron", shift(rrect(23, 61, 40, 75, 3), 0, bd), [0, 1, 2], light=False, flat=1)
        cv.paint("body", "fire", lo, shift(rrect(25, 63, 38, 73, 2), 0, bd))
        cv.paint("body", "fire", mid, shift(rrect(27, 65, 36, 72, 2), 0, bd))
        cv.paint("body", "fire", hi, shift(rect(29, 68, 34, 71), 0, bd))
        for x in (29, 34):
            cv.paint("body", "iron", 0, shift(rect(x, 63, x, 73), 0, bd))
        cv.dots("body", "copper", 1, [(19, 77 + bd), (43, 77 + bd)], 2)
    # Head: a brass dome with an iron jaw, one gauge eye, a smokestack.
    head = shift(ellipse(31.5, 41, 10, 10), 0, hd + bd)
    stack = shift(rect(28, 25, 34, 32), 0, hd + bd)
    cv.layer("head", "iron", stack, [1, 2, 3, 4])
    cv.layer("head", "copper", shift(rect(27, 25, 35, 26), 0, hd + bd), [0, 1, 2])
    cv.layer("head", "brass", head, [0, 1, 2, 3])
    cv.layer("head", "iron", shift(rrect(24, 46, 39, 52, 2), 0, hd + bd), [1, 2, 3, 4])
    if back:
        cv.layer("head", "brass", shift(rrect(25, 34, 38, 44, 2), 0, hd + bd), [0, 1, 2])
        cv.paint("head", "iron", 0, shift(rect(25, 39, 38, 39), 0, hd + bd))
        cv.dots("head", "iron", 1, [(27, 36 + hd + bd), (35, 36 + hd + bd), (27, 41 + hd + bd), (35, 41 + hd + bd)], 2)
    else:
        lo, mid, hi = eye_tones(p.get("eye"))
        cv.layer("head", "iron", shift(ellipse(31.5, 40, 6.5, 6.5), 0, hd + bd), [0, 1, 2, 3])
        cv.paint("head", "aether", lo, shift(ellipse(31.5, 40, 4.5, 4.5), 0, hd + bd))
        cv.paint("head", "aether", mid, shift(ellipse(31.5, 40, 3.2, 3.2), 0, hd + bd))
        cv.paint("head", "aether" if p.get("eye") != "bright" else "steam", hi if p.get("eye") != "bright" else 3,
                 shift(rect(29, 37, 30, 38), 0, hd + bd))
        for x in (26, 30, 34):
            cv.paint("head", "iron", 0, shift(rect(x, 50, x + 1, 51), 0, hd + bd))
    # Arms (iron, copper elbows) from the shoulders to the hands.
    shoulders = [(6, 57 + bd), (57, 57 + bd)]
    for s, e, h in zip(shoulders, p["elbows"], p["hands"]):
        cv.layer("arms", "iron", capsule(s, e, 4.2) | capsule(e, h, 3.6), [1, 2, 3, 4])
        cv.layer("arms", "copper", ellipse(e[0], e[1], 3.2, 3.2), [0, 1, 2])
    if not back:
        cv.layer("ram", "brass", ram_px, [0, 1, 2, 3])
        cv.paint("ram", "iron", 1, shift(rect(1, 9, 28, 11), *ram))
        cv.paint("ram", "iron", 3, shift(rect(1, 9, 6, 9), *ram))
        cv.dots("ram", "copper", 1, [(ram[0] + 4, ram[1] + 3), (ram[0] + 24, ram[1] + 3)], 2)
    for h in p["hands"]:
        cv.layer("arms", "brass", ellipse(h[0], h[1], 4.0, 4.0), [0, 1, 2, 3])
    return cv


def draw_side(state, mirror=False):
    """East: the profile, front to the right. West (mirror): the same shapes mirrored and lit
    from the upper left again by the same shading rule, which is what reshade_mirror.py does to
    a finished frame; drawn here because a profile's front (eye lens, ram, hand) isn't the
    mirror of its back, so re-shading the finished east frame run by run can't put the light back
    (+0.05 against a source's +0.11)."""
    p = SIDE[state]
    cv = Canvas(mirror)
    hd, bd = p.get("head", 0), p.get("body", 0)
    lt_dy, lt_dx = p.get("legs", (0, 0))
    # Far arm, far leg (darker), then the body, the pack, the near leg, the head, the ram, the near arm.
    cv.layer("arms", "iron", capsule((30, 56 + bd), p["far_elbow"], 3.6) | capsule(p["far_elbow"], p["far_hand"], 3.2), [0, 1, 2])
    cv.layer("legs", "iron", shift(rect(24, 79, 31, 89), lt_dx, lt_dy), [0, 1, 2])
    cv.layer("legs", "iron", shift(rrect(22, 88, 36, 95, 2), lt_dx, lt_dy), [0, 1, 2])
    body = shift(rrect(19, 54, 44, 82, 5), 0, bd)
    cv.layer("body", "iron", body, [1, 2, 3, 4])
    cv.layer("body", "brass", shift(rrect(19, 54, 26, 82, 2), 0, bd), [0, 1, 2, 3])
    cv.layer("body", "brass", shift(rrect(18, 51, 44, 59, 3), 0, bd), [0, 1, 2, 3])
    cv.dots("body", "copper", 2, [(22, 62 + bd), (22, 70 + bd), (22, 78 + bd)], 2)
    # The furnace seen edge-on: its rim stands out of the belly, the glow on its front.
    lo, mid, hi = fire_tones(p.get("fire"))
    cv.layer("body", "iron", shift(rect(41, 61, 46, 74), 0, bd), [0, 1, 2], light=False, flat=1)
    cv.paint("body", "fire", mid, shift(rect(44, 63, 46, 72), 0, bd))
    cv.paint("body", "fire", hi, shift(rect(45, 66, 46, 70), 0, bd))
    # The boiler pack on its back.
    cv.layer("pack", "copper", shift(rrect(12, 58, 20, 78, 3), 0, bd), [0, 1, 2])
    for y in range(62, 76, 4):
        cv.paint("pack", "iron", 0, shift(rect(13, y, 18, y), 0, bd))
    cv.layer("legs", "iron", shift(rect(30, 79, 38, 89), lt_dx, lt_dy), [1, 2, 3, 4])
    cv.layer("legs", "iron", shift(rrect(29, 88, 44, 95, 2), lt_dx, lt_dy), [0, 1, 2, 3])
    cv.dots("legs", "copper", 1, [(40 + lt_dx, 92 + lt_dy)], 2)
    # Head in profile: the dome, the jaw, the eye's lens standing out in front, the stack behind.
    cv.layer("head", "iron", shift(rect(24, 25, 30, 32), 0, hd + bd), [1, 2, 3, 4])
    cv.layer("head", "copper", shift(rect(23, 25, 31, 26), 0, hd + bd), [0, 1, 2])
    cv.layer("head", "brass", shift(ellipse(32, 41, 10, 11), 0, hd + bd), [0, 1, 2, 3])
    cv.layer("head", "iron", shift(rrect(27, 46, 42, 53, 2), 0, hd + bd), [1, 2, 3, 4])
    lo, mid, hi = eye_tones(p.get("eye"))
    cv.layer("head", "iron", shift(rrect(38, 35, 44, 45, 2), 0, hd + bd), [0, 1, 2, 3])
    cv.paint("head", "aether", lo, shift(rect(41, 36, 44, 44), 0, hd + bd))
    cv.paint("head", "aether", mid, shift(rect(42, 37, 44, 43), 0, hd + bd))
    cv.paint("head", "aether" if p.get("eye") != "bright" else "steam", hi if p.get("eye") != "bright" else 3,
             shift(rect(43, 38, 44, 39), 0, hd + bd))
    ram = p["ram"]
    cv.layer("ram", "brass", shift(rrect(0, 0, 13, 11, 2), *ram), [0, 1, 2, 3])
    cv.paint("ram", "iron", 1, shift(rect(1, 9, 12, 11), *ram))
    cv.dots("ram", "copper", 1, [(ram[0] + 5, ram[1] + 3)], 2)
    cv.layer("arms", "iron", capsule((32, 56 + bd), p["elbow"], 4.2) | capsule(p["elbow"], p["hand"], 3.6), [1, 2, 3, 4])
    cv.layer("arms", "copper", ellipse(p["elbow"][0], p["elbow"][1], 3.2, 3.2), [0, 1, 2])
    cv.layer("arms", "brass", ellipse(p["hand"][0], p["hand"][1], 4.0, 4.0), [0, 1, 2, 3])
    return cv


def draw(facing, state):
    if facing in ("east", "west"):
        return draw_side(state, mirror=(facing == "west"))
    return draw_front(state, back=(facing == "north"))


def offsets(facing, state):
    """How far each rigid part moved from the base, for compose.py."""
    table = SIDE if facing in ("east", "west") else FRONT
    b, p = table["base"], table[state]
    ldy, ldx = p.get("legs", (0, 0))
    bd, hd = p.get("body", 0), p.get("head", 0)
    out = {"body": [0, bd], "pack": [0, bd], "head": [0, hd + bd], "legs": [ldx, ldy],
           "ram": [p["ram"][0] - b["ram"][0], p["ram"][1] - b["ram"][1]]}
    if facing in ("south", "north") and ldx:
        out["legs"] = None  # the two legs move apart, not as one: the draft's legs are used
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
