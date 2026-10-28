"""The Artificer cranking at the Hand-Crank Bench, and the bench's wheel turning, from their own pixels.

Drawing a cranking arm (by hand or by rotating the sprite's own) never sat in the figure, so
nothing is drawn: the Artificer's standing pose rocks instead. Above the waist the figure
moves round a two-pixel loop (forward, forward and down, down, back; straight down for the
front and back views), and the head and shoulders dip one pixel more as he leans in. The legs
stay planted. Each frame is re-outlined (outline.py) so the shifted silhouette keeps its edge.

The bench's wheel turns by two brass glints travelling round its rim, 45 degrees a frame, in
the wheel's own lightest gold; turning the wheel's pixels dragged the tabletop and the gear
housing into the spokes.

    python crank_frames.py <art dir> <out dir>

Reads `artificer_stand_<facing>.png` (walk_feet.py's output) and `hand_crank_bench.png`; writes
`artificer_crank_<facing>_<0..3>.png` and `hand_crank_bench_turn_<0..3>.png`. Run it last.
"""
import math
import os
import sys

from PIL import Image

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "..", "..", "..", "..", "Tools", "Art"))
import art_metrics as m  # noqa: E402
import outline  # noqa: E402
import side_heads  # noqa: E402

FRAMES = 4
# facing: (waist row: rows above it move, the body's loop of (dx, dy))
BODY = {
    "right": (80, [(0, 0), (2, 0), (2, 2), (0, 2)]),
    "left": (80, [(0, 0), (-2, 0), (-2, 2), (0, 2)]),
    "down": (82, [(0, 0), (0, 1), (0, 2), (0, 1)]),
    "up": (77, [(0, 0), (0, 1), (0, 2), (0, 1)]),
}
NOD = [0, 1, 1, 0]   # the head and shoulders dip a further pixel as he leans in

# The bench's wheel (hand_crank_bench.png, 94x82): its centre and radii, the rim band (as a
# fraction of those radii) the glints run in, and the gear housing in front of its lower left.
WHEEL = (16.8, 15.5, 11.6, 14.2)
RIM = (0.78, 1.08)
HOUSING = (13, 16)   # x below, y above: not the rim
GLINT = math.radians(34)


def shift(image, rows, dx, dy):
    """Move the rows above `rows` by (dx, dy); the rows below keep their pixels unless covered."""
    out = image.copy()
    src, px = image.load(), out.load()
    w = image.width
    for y in range(rows):
        for x in range(w):
            px[x, y] = (0, 0, 0, 0)
    for y in range(rows):
        for x in range(w):
            if src[x, y][3] >= outline.OPAQUE and 0 <= x + dx < w:
                px[x + dx, y + dy] = src[x, y]
    return out


def crank(stand, facing, k):
    waist, loop = BODY[facing]
    dx, dy = loop[k]
    figure = shift(stand, waist, dx, dy)
    if NOD[k]:
        neck = side_heads.collar_row(stand) + dy
        figure = shift(figure, neck, 0, NOD[k])
    return outline.outline(figure)[0]


def brass(p):
    if p[3] < 128:
        return False
    L, a, b = m.oklab(p[:3])
    return b > 0.06 and a < 0.03 and L > 0.45


def bench_turn(bench, k):
    image = bench.copy()
    px = image.load()
    light = max({px[x, y][:3] for y in range(image.height) for x in range(image.width) if brass(px[x, y])},
                key=lambda c: m.oklab(c)[0])
    cx, cy, rx, ry = WHEEL
    step = 2 * math.pi / 8
    for y in range(int(cy + ry) + 2):
        for x in range(int(cx + rx) + 2):
            u, v = (x + 0.5 - cx) / rx, (y + 0.5 - cy) / ry
            if not RIM[0] <= math.hypot(u, v) <= RIM[1] or not brass(px[x, y]):
                continue
            if x < HOUSING[0] and y > HOUSING[1]:
                continue
            t = math.atan2(v, u)
            for glint in (step * k, step * k + math.pi):
                if abs((t - glint + math.pi) % (2 * math.pi) - math.pi) < GLINT:
                    px[x, y] = light + (255,)
                    break
    return image


def main(argv):
    src, dst = argv
    os.makedirs(dst, exist_ok=True)
    for facing in BODY:
        stand = Image.open(os.path.join(src, f"artificer_stand_{facing}.png")).convert("RGBA")
        for k in range(FRAMES):
            crank(stand, facing, k).save(os.path.join(dst, f"artificer_crank_{facing}_{k}.png"))
    bench = Image.open(os.path.join(src, "hand_crank_bench.png")).convert("RGBA")
    for k in range(FRAMES):
        bench_turn(bench, k).save(os.path.join(dst, f"hand_crank_bench_turn_{k}.png"))
    print(f"crank: {len(BODY) * FRAMES} Artificer frames, {FRAMES} bench frames")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
