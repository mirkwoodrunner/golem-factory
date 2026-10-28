"""Give each side view one head, the same in all four walk frames.

Today's side frames were generated one by one, so the face changes from frame to frame (a hair
strand over the eye in one, marks by the mouth in another, a rounder cheek). This takes one
frame's head (everything above the collar) and puts it into the other three frames, placed
where that frame's own head was (found by matching, so the walk's bob is kept), and leaves the
body untouched. A facing's head can also be the other facing's, mirrored.

    python side_heads.py <in dir> <out dir> right=2 left=0 [--marks <png>]
    python side_heads.py <in dir> <out dir> right=2 left=mirror:2

`right=2`: walking right takes frame 2's head. `left=mirror:2`: walking left takes walking
right's frame 2 head, mirrored.
"""
import argparse
import os
import sys

from PIL import Image

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "..", "..", "..", "..", "Tools", "Art"))
import art_metrics as m  # noqa: E402

GREEN_A = 0.015      # OKLab a at or under: the shirt's greens (the collar)
DARK_L = 0.36
OPAQUE = 128
CHIN_FROM = 38       # the collar is searched for from this row down (above it, the green eyes)
SHIFT = 5            # how far (px) a frame's head may sit from the chosen one's


def frame(src, facing, i):
    return Image.open(os.path.join(src, f"artificer_walk_{facing}_{i}.png")).convert("RGBA")


def collar_row(image):
    """The first row (from CHIN_FROM) holding two or more shirt-green pixels: the head ends above it."""
    px = image.load()
    w, h = image.size
    for y in range(CHIN_FROM, h):
        greens = 0
        for x in range(w):
            p = px[x, y]
            if p[3] >= OPAQUE:
                L, a, _ = m.oklab(p[:3])
                greens += a <= GREEN_A and L >= DARK_L
        if greens >= 2:
            return y
    raise ValueError("no collar found")


def head(image):
    """The head: the frame's rows above its collar, the rest transparent."""
    cut = collar_row(image)
    out = Image.new("RGBA", image.size, (0, 0, 0, 0))
    out.paste(image.crop((0, 0, image.width, cut)), (0, 0))
    return out, cut


def mismatch(a, b, dx, dy, rows):
    """How unlike head `a`, moved by (dx, dy), is frame `b` over a's rows."""
    pa, pb = a.load(), b.load()
    w, h = a.size
    cost = 0.0
    for y in range(rows):
        for x in range(w):
            p = pa[x, y]
            tx, ty = x + dx, y + dy
            q = pb[tx, ty] if 0 <= tx < w and 0 <= ty < h else (0, 0, 0, 0)
            if (p[3] >= OPAQUE) != (q[3] >= OPAQUE):
                cost += 1
            elif p[3] >= OPAQUE:
                cost += min(1.0, m.lab_distance(m.oklab(p[:3]), m.oklab(q[:3])) * 4)
    return cost


def place(chosen, cut, target):
    """(dx, dy) that best lays the chosen head over the target frame's head."""
    best = None
    for dy in range(-SHIFT, SHIFT + 1):
        for dx in range(-SHIFT, SHIFT + 1):
            c = mismatch(chosen, target, dx, dy, cut)
            if best is None or c < best[0]:
                best = (c, dx, dy)
    return best[1], best[2]


def swap_head(chosen, cut, target):
    dx, dy = place(chosen, cut, target)
    own = collar_row(target)
    out = target.copy()
    px = out.load()
    # Clear the frame's own head (above its collar, and above where the chosen head ends).
    for y in range(max(own, cut + dy)):
        for x in range(out.width):
            px[x, y] = (0, 0, 0, 0)
    out.alpha_composite(chosen, (max(dx, 0), max(dy, 0)), (max(-dx, 0), max(-dy, 0)))
    return out, (dx, dy, own, cut + dy)


def main(argv):
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("src")
    parser.add_argument("dst")
    parser.add_argument("choices", nargs="+", help="facing=frame or facing=mirror:frame")
    parser.add_argument("--marks")
    args = parser.parse_args(argv)
    os.makedirs(args.dst, exist_ok=True)
    other = {"right": "left", "left": "right"}
    rows = []
    for choice in args.choices:
        facing, spec = choice.split("=")
        if spec.startswith("mirror:"):
            chosen, cut = head(frame(args.src, other[facing], int(spec[7:])))
            chosen = chosen.transpose(Image.FLIP_LEFT_RIGHT)
        else:
            chosen, cut = head(frame(args.src, facing, int(spec)))
        before, after = [], []
        for i in range(4):
            target = frame(args.src, facing, i)
            result, (dx, dy, own, end) = swap_head(chosen, cut, target)
            result.save(os.path.join(args.dst, f"artificer_walk_{facing}_{i}.png"))
            print(f"{facing} {i}: head moved ({dx}, {dy}); own collar row {own}, chosen head ends {end}")
            before.append(target)
            after.append(result)
        rows += [before, after]
    if args.marks:
        z, crop = 6, (0, 0, 64, 93)
        tw, th = (crop[2] - crop[0]) * z, (crop[3] - crop[1]) * z
        sheet = Image.new("RGBA", (tw * 4 + 30, th * len(rows) + 10 * len(rows)), (60, 60, 70, 255))
        for r, row in enumerate(rows):
            for i, im in enumerate(row):
                sheet.alpha_composite(im.crop(crop).resize((tw, th), Image.NEAREST), (i * (tw + 10), r * (th + 10)))
        sheet.save(args.marks)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
