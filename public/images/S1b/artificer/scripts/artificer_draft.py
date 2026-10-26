"""The Artificer (set S1b): a scripted, layered draft of the walk cycle, with a part map, the
golems' way (Tools/Art/golem_set.py, art-pipeline-design.md §3.2), so that

- each drawn direction's frame 0 is the image-to-image start and ControlNet guide for ComfyUI, and
- the other frames are derived from the cleaned base by moving its rigid parts (the head and the
  body bob with the step), while the arms and legs are drawn fresh in every frame.

The character is today's (the user's call, 2026-10-25): goggles pushed up on messy plum-brown
hair, green eyes, a sage shirt, an oxblood leather apron with a brass gear on its pocket, slate
trousers and leather boots. The head is traced pixel for pixel from today's frame 0 of each
direction (source/today/, copies of godot/art/'s artificer_walk_*.png as of commit 906d389;
PNGs under public/images/ are ignored, so restore them with `git show 906d389:godot/art/<name>`),
recoloured into the palette; the body, arms and legs are drawn here,
so the walk is a real four-frame step and nothing flickers between frames.

Frame 0 is standing (ArtificerWalkAnimation.StandingFrameIndex) and the first step of the
cycle: 0 standing, 1 near foot forward, 2 passing, 3 far foot forward.

    python artificer_draft.py <out dir>   writes draft_<facing>_<state>.png (+ _8x on the key)
                                          and parts_<facing>_<state>.json

Facings are the golems' (south = down, north = up, east = right, west = left); states are walk_0
to walk_3. Coordinates are pixels on the 64 x 93 canvas, y down, feet on row 92.
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
TODAY = os.path.join(HERE, "..", "source", "today")
# Back to front, per facing. From the side the far arm and leg are behind the body, the apron
# over the tops of the legs, the near arm over everything but the head. From the front and the
# back the arms hang beside the body, over its edges.
LAYERS = {
    "south": ["legs", "body", "arms", "head"],
    "north": ["arms", "legs", "body", "head"],   # from behind the body hides the arms' inner edge
    "east": ["far_arm", "far_leg", "legs", "body", "arms", "head"],
    "west": ["far_arm", "far_leg", "legs", "body", "arms", "head"],
}
W, H = 64, 93
KEY = (40, 190, 70)
STATES = ["walk_0", "walk_1", "walk_2", "walk_3"]
FACINGS = ["south", "north", "east", "west"]   # west is east mirrored
RIGID = ["head", "body"]

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




# --- today's head, traced ------------------------------------------------------------------------

def hls(c):
    import colorsys
    h, l, s = colorsys.rgb_to_hls(*[v / 255 for v in c])
    return h * 360, l, s


def recolour(c):
    """Today's colour, as a ramp and tone: skin stays skin, the plum hair hair, the oxblood
    strap and hair streaks leather, the sage lenses and eyes cloth, the beige glints bone."""
    h, l, s = hls(c)
    if l < 0.125 and s < 0.2:
        ramp = "outline"
    elif (h < 40 or h > 350) and l > 0.38:
        ramp = "skin"
    elif 25 <= h < 50:
        ramp = "bone" if l > 0.5 else "mahogany"
    elif 50 <= h < 120:
        ramp = "cloth"
    elif s < 0.1:
        ramp = "iron"
    elif (h >= 350 or h < 15) and s >= 0.4:
        ramp = "leather"
    else:
        ramp = "hair"
    tones = RAMPS[ramp]
    lum = lambda t: 0.3 * t[0] + 0.59 * t[1] + 0.11 * t[2]
    tone = min(range(len(tones)), key=lambda i: abs(lum(tones[i]) - lum(c)))
    return ramp, tone


def trace_head(cv, name, below, dy=0):
    """Today's head from source/today/<name>.png: every opaque pixel above row `below`."""
    img = Image.open(os.path.join(TODAY, name + ".png")).convert("RGBA")
    groups = {}
    for y in range(below):
        for x in range(img.width):
            p = img.getpixel((x, y))
            if p[3] >= 128:
                groups.setdefault(recolour(p[:3]), set()).add((x, y + dy))
    for (ramp, tone), pts in groups.items():
        cv.paint("head", ramp, tone, pts)


# --- the poses ---------------------------------------------------------------------------------

# From the side, per frame: the body's drop (down on the contact frames), each leg's hip, knee and
# ankle (n the near leg, f the far one), each arm's elbow and hand (swinging against the legs).
SIDE = {
    "walk_0": dict(bd=0, n=((36, 70), (37, 79), (37, 87)), f=((32, 70), (31, 79), (30, 87)),
                   na=((32, 60), (33, 71)), fa=((31, 60), (31, 71))),
    "walk_1": dict(bd=1, n=((36, 70), (40, 78), (44, 86)), f=((32, 70), (28, 79), (23, 84)),
                   na=((28, 59), (23, 67)), fa=((36, 59), (42, 66))),
    "walk_2": dict(bd=0, n=((36, 70), (37, 79), (36, 87)), f=((32, 70), (35, 77), (31, 83)),
                   na=((32, 60), (32, 71)), fa=((31, 60), (31, 71))),
    "walk_3": dict(bd=1, n=((36, 70), (32, 79), (26, 84)), f=((32, 70), (37, 78), (42, 86)),
                   na=((37, 59), (43, 66)), fa=((29, 59), (24, 67))),
}


def draw_leg(cv, part, leg, dark):
    hip, knee, ankle = leg
    tones = [0, 1, 2] if dark else [0, 1, 2, 3]
    cv.layer(part, "slate", capsule(hip, knee, 3.6) | capsule(knee, ankle, 3.2), tones)
    # The boot: a leather ankle and a foot pointing forward; a foot lifted off the floor (the
    # trailing toe, the passing foot) tips its toe down.
    x, y = ankle
    boot = rrect(x - 3, y - 2, x + 3, y + 3, 2) | rrect(x - 3, y + 1, x + 6, y + 5, 2)
    cv.layer(part, "leather", {(bx, by) for bx, by in boot if by <= 92},
             [0, 1, 2] if dark else [0, 1, 2, 3])


def draw_arm(cv, part, shoulder, arm, dark, sleeve=True):
    elbow, hand = arm
    if not sleeve:
        # From the front and the back the sleeve is part of the shirt (draw_front); the bare
        # arm comes out from under its hem.
        cv.layer(part, "skin", capsule(shoulder, elbow, 2.6) | capsule(elbow, hand, 2.6)
                 | ellipse(hand[0], hand[1], 3.2, 3.2), [1, 2, 3, 3])
        return
    # A puffed short sleeve, then the bare arm from above the elbow, and the hand. Skin is
    # mostly today's light tone; its shadow edge steps down, so mirrored to walk left the arm
    # still darkens away from the light (check 14).
    cuff = (shoulder[0] + (elbow[0] - shoulder[0]) * 0.7, shoulder[1] + (elbow[1] - shoulder[1]) * 0.7)
    cv.layer(part, "skin", capsule(cuff, elbow, 2.8) | capsule(elbow, hand, 2.6) | ellipse(hand[0], hand[1], 3.2, 3.2),
             [1, 2, 2] if dark else [1, 2, 3, 3])
    cv.layer(part, "cloth", ellipse(shoulder[0], shoulder[1] + 3, 5.2, 6) | capsule(shoulder, cuff, 4.2),
             [0, 1, 2] if dark else [0, 1, 2, 3])


def shift_leg(leg, bd):
    hip, knee, ankle = leg
    return (hip[0], hip[1] + bd), (knee[0], knee[1] + bd // 2), ankle


def shift_arm(arm, bd):
    return tuple((x, y + bd) for x, y in arm)


def draw_side(state, mirror=False):
    p = SIDE[state]
    cv = Canvas("west" if mirror else "east", mirror)
    bd = p["bd"]
    draw_arm(cv, "far_arm", (33, 48 + bd), shift_arm(p["fa"], bd), dark=True)
    draw_leg(cv, "far_leg", shift_leg(p["f"], bd), dark=True)
    draw_leg(cv, "legs", shift_leg(p["n"], bd), dark=False)
    # The body: the sage shirt, the oxblood apron over its front and down past the hips, the
    # belt and its brass buckle, the bib pocket with a screwdriver, the big pocket with its brass
    # gear, a pouch on the back hip, the neck strap.
    b = lambda pts: shift(pts, 0, bd)
    shirt = ellipse(35, 52, 9, 10) | rrect(28, 52, 42, 70, 5)
    cv.layer("body", "cloth", b(shirt), [0, 1, 2, 3])
    apron = ellipse(41, 54, 6, 9) | rrect(35, 45, 45, 62, 3) | rrect(30, 60, 47, 77, 4)
    cv.layer("body", "leather", b(apron), [0, 1, 2, 3, 4])
    # The belt and seam lines stop short of the apron's front edge: mirrored to walk left, that
    # edge is the lit one, and a dark line there reads as shadow on the light side (check 14).
    front = {(x, y) for x, y in apron if (x + 1, y) not in apron}
    cv.paint("body", "leather", 0, b((rect(29, 61, 47, 62) & apron) - front))
    cv.layer("body", "brass", b(rect(42, 60, 44, 63)), [1, 2, 3])
    cv.layer("body", "iron", b(rect(40, 45, 41, 50)), [2, 3, 4])
    cv.layer("body", "leather", b(rrect(37, 48, 44, 55, 1)), [1, 2, 3])
    cv.layer("body", "leather", b(rrect(35, 65, 45, 74, 2)), [1, 2, 3])
    cv.paint("body", "leather", 0, b(rect(35, 65, 45, 65)))
    cv.layer("body", "brass", b(ellipse(41, 70, 2.8, 2.8)), [1, 2, 3])
    cv.paint("body", "brass", 0, b({(41, 70)}))
    cv.layer("body", "leather", b(rrect(26, 63, 32, 71, 2)), [0, 1, 2, 3])
    cv.layer("body", "leather", b(capsule((30, 43), (37, 47), 0.9)), [0, 1])
    # Today's head, traced, bobbing with the body.
    trace_head(cv, "artificer_walk_right_0", 43, dy=bd)
    draw_arm(cv, "arms", (32, 48 + bd), shift_arm(p["na"], bd), dark=False)
    return cv


# From the front and the back, per frame: the body's drop, each leg's hip, knee and ankle (l on
# the viewer's left, r on the right), each arm's elbow and hand. A stepping foot lifts its heel
# (the ankle rises); the hands swing against the feet, the forward one lower.
FRONT = {
    "walk_0": dict(bd=0, l=((28, 71), (28, 81), (28, 87)), r=((36, 71), (36, 81), (36, 87)),
                   la=((21, 57), (21, 65)), ra=((43, 57), (43, 65))),
    "walk_1": dict(bd=1, l=((28, 71), (27, 81), (27, 87)), r=((36, 71), (36, 80), (37, 84)),
                   la=((21, 56), (22, 63)), ra=((43, 57), (42, 67))),
    "walk_2": dict(bd=0, l=((28, 71), (28, 81), (28, 87)), r=((36, 71), (36, 81), (36, 86)),
                   la=((21, 57), (21, 65)), ra=((43, 57), (43, 65))),
    "walk_3": dict(bd=1, l=((28, 71), (28, 80), (27, 84)), r=((36, 71), (37, 81), (37, 87)),
                   la=((21, 57), (22, 67)), ra=((43, 56), (42, 63))),
}


def draw_leg_front(cv, leg, bd):
    hip, knee, ankle = leg
    hip, knee = (hip[0], hip[1] + bd), (knee[0], knee[1] + bd // 2)
    cv.layer("legs", "slate", capsule(hip, knee, 3.6) | capsule(knee, ankle, 3.2), [0, 1, 2, 3])
    x, y = ankle
    boot = rrect(x - 3, y - 2, x + 3, y + 5, 2)
    cv.layer("legs", "leather", {(bx, by) for bx, by in boot if by <= 92}, [0, 1, 2, 3])


def draw_front(state, back=False):
    """Walking down (the south) or up (the north, from behind)."""
    p = FRONT[state]
    cv = Canvas("north" if back else "south")
    bd = p["bd"]
    b = lambda pts: shift(pts, 0, bd)
    # From behind the feet swap sides with the hands: the character's left is the viewer's left.
    draw_leg_front(cv, p["l"], bd)
    draw_leg_front(cv, p["r"], bd)
    # The shirt: a narrow torso, shoulders sloping from the neck, and the short puffed sleeves
    # as part of it, so they shade with the shirt rather than sitting on it as separate pads.
    shirt = (ellipse(32, 48, 8, 4) | rrect(24, 47, 40, 70, 4)
             | ellipse(22, 51, 3.4, 4) | ellipse(42, 51, 3.4, 4))
    # The neck, from under the chin (or the hair) into the collar, shaded under the head.
    cv.layer("body", "skin", b(rect(30, 42, 35, 47)), [1, 2, 2, 3])
    cv.paint("body", "skin", 1, b(rect(30, 42, 35, 43)))
    if back:
        # The back: the sage shirt, the apron's straps crossed over it, the trouser seat under
        # the belt, the belt with its tie and a pouch on each hip.
        cv.layer("body", "slate", b(rrect(23, 62, 41, 76, 3)), [0, 1, 2, 3])
        cv.layer("body", "cloth", b(shirt - rect(22, 63, 42, 80)), [0, 1, 2, 3])
        cv.layer("body", "leather", b(capsule((25, 46), (38, 61), 1.1)), [0, 1, 2])
        cv.layer("body", "leather", b(capsule((39, 46), (26, 61), 1.1)), [0, 1, 2])
        cv.layer("body", "leather", b(rrect(22, 60, 42, 63, 1)), [0, 1, 2, 3])
        cv.layer("body", "leather", b(rrect(30, 59, 34, 64, 2)), [1, 2, 3])
        cv.paint("body", "leather", 0, b({(32, 61), (32, 62)}))
        # The pouches sit on the back of the hips, inside the hanging hands.
        for x0 in (24, 35):
            cv.layer("body", "leather", b(rrect(x0, 62, x0 + 5, 70, 2)), [0, 1, 2, 3])
            cv.paint("body", "leather", 0, b(rect(x0 + 1, 64, x0 + 4, 64)))
            cv.layer("body", "brass", b(rect(x0 + 2, 65, x0 + 3, 66)), [1, 2, 3])
    else:
        # The front: the sage shirt, the oxblood apron (its bib, and the skirt down over the
        # thighs), the belt line, the bib pocket with a screwdriver, the big pocket with its
        # brass gear, the neck straps, the apron's ties at the waist.
        cv.layer("body", "cloth", b(shirt), [0, 1, 2, 3])
        # The skirt flares from the waist to just above the knees.
        skirt = {(x, y) for y in range(58, 79) for x in range(64)
                 if abs(x - 32) <= 9 + (y - 58) * 3 // 20 and not (y >= 77 and abs(x - 32) > 9 + (y - 58) * 3 // 20 - (y - 76))}
        apron = rrect(25, 46, 39, 62, 2) | skirt
        cv.layer("body", "leather", b(apron), [0, 1, 2, 3, 4])
        edges = {(x, y) for x, y in apron if (x - 1, y) not in apron or (x + 1, y) not in apron}
        cv.paint("body", "leather", 0, b((rect(22, 60, 42, 61) & apron) - edges))
        cv.layer("body", "leather", b(rrect(27, 49, 35, 56, 1)), [1, 2, 3])
        cv.layer("body", "iron", b(rect(33, 46, 34, 51)), [2, 3, 4])
        cv.layer("body", "leather", b(rrect(27, 65, 37, 73, 2)), [2, 3, 4])
        cv.paint("body", "leather", 1, b(rect(28, 65, 36, 65)))
        cv.layer("body", "brass", b(ellipse(32, 70, 2.8, 2.8)), [1, 2, 3])
        cv.paint("body", "brass", 0, b({(32, 70)}))
        cv.layer("body", "leather", b(capsule((26, 46), (28, 43), 0.8) | capsule((38, 46), (36, 43), 0.8)), [0, 1])
        cv.layer("body", "leather", b(rect(20, 59, 22, 62) | rect(42, 59, 44, 62)), [0, 1, 2])
    # From behind the arms sit a pixel further in, so the body hides their inner edge.
    tuck = 1 if back else 0
    la = tuple((x + tuck, y) for x, y in shift_arm(p["la"], bd))
    ra = tuple((x - tuck, y) for x, y in shift_arm(p["ra"], bd))
    draw_arm(cv, "arms", (22 + tuck, 54 + bd), la, dark=False, sleeve=False)
    draw_arm(cv, "arms", (42 - tuck, 54 + bd), ra, dark=False, sleeve=False)
    trace_head(cv, "artificer_walk_up_0" if back else "artificer_walk_down_0", 43, dy=bd)
    return cv


def draw(facing, state):
    if facing in ("south", "north"):
        return draw_front(state, back=(facing == "north"))
    return draw_side(state, mirror=(facing == "west"))


def offsets(facing, state):
    """How far each rigid part moved from frame 0, for golem_set.py's compose."""
    bd = (FRONT if facing in ("south", "north") else SIDE)[state]["bd"]
    return {"body": [0, bd], "head": [0, bd]}


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
