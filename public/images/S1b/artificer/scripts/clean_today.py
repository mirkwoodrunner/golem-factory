"""Clean the stray pixels from today's Artificer walk frames, keeping everything else.

The 16 frames came out of an AI generator with noise left in: the shirt's greens speckled over
the brown apron, belt and pockets, and lying along the silhouette beside the hands and the
pocket. Only the body is touched (below the collar, so the face, eyes, goggles, hair and the
collar's bits of shirt between the apron straps never are). The shirt's greens (OKLab
a <= 0.015, not a dark) form large patches: the sleeves, the back. A green clump smaller than
six pixels (8-connected) is noise, a speck of shirt where there is no shirt. Each of its pixels takes the commonest colour around it; on the
silhouette's edge, the commonest outline colour around it (the darks, L < 0.36), so the
silhouette keeps its shape.

    python public/images/S1b/artificer/scripts/clean_today.py <in dir> <out dir> [--marks <png>]

Reads `artificer_walk_*.png` from <in dir> (today's frames: source/today/) and writes the
cleaned frames to <out dir>; --marks writes a before/after sheet with every changed pixel ringed.
"""
import argparse
import glob
import os
import sys
from collections import Counter

from PIL import Image, ImageDraw

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "..", "..", "..", "..", "Tools", "Art"))
import art_metrics as m  # noqa: E402

HEAD_BOTTOM = 50     # rows above this (the head and the collar between the straps) are never touched
GREEN_A = 0.015      # OKLab a at or under: the shirt's greens
DARK_L = 0.36        # OKLab L under: the outline's darks
CLUMP = 6            # a green clump (8-connected) this big or bigger is shirt
OPAQUE = 128


def strays(image):
    """{(x, y): new RGBA} for every stray pixel of a frame."""
    px = image.load()
    w, h = image.size
    lab = {}

    def colour(p):
        if p not in lab:
            lab[p] = m.oklab(px[p][:3])
        return lab[p]

    def opaque(p):
        x, y = p
        return 0 <= x < w and 0 <= y < h and px[p][3] >= OPAQUE

    def green(p):
        return opaque(p) and p[1] >= HEAD_BOTTOM and colour(p)[1] <= GREEN_A and colour(p)[0] >= DARK_L

    def ring(p):
        x, y = p
        return [(x + dx, y + dy) for dx in (-1, 0, 1) for dy in (-1, 0, 1) if dx or dy]

    out, seen = {}, set()
    for y in range(HEAD_BOTTOM, h):
        for x in range(w):
            if (x, y) in seen or not green((x, y)):
                continue
            clump, stack = {(x, y)}, [(x, y)]
            while stack:
                for q in ring(stack.pop()):
                    if q not in clump and green(q):
                        clump.add(q)
                        stack.append(q)
            seen |= clump
            if len(clump) >= CLUMP:
                continue
            for p in clump:
                edge = any(not opaque((p[0] + dx, p[1] + dy)) for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)))
                around = [q for q in ring(p) if opaque(q) and q not in clump]
                darks = [px[q] for q in around if colour(q)[0] < DARK_L]
                pool = darks if edge and darks else [px[q] for q in around]
                if pool:
                    out[p] = Counter(pool).most_common(1)[0][0]
    return out


def clean(image):
    image = image.convert("RGBA")
    changes = strays(image)
    result = image.copy()
    rp = result.load()
    for p, c in changes.items():
        rp[p] = c
    return result, changes


def marks_sheet(pairs, path, zoom=6):
    tiles = []
    for before, after, changes in pairs:
        w, h = before.size
        tile = Image.new("RGBA", (w * zoom * 2 + zoom * 2, h * zoom), (60, 60, 70, 255))
        tile.alpha_composite(before.resize((w * zoom, h * zoom), Image.NEAREST), (0, 0))
        tile.alpha_composite(after.resize((w * zoom, h * zoom), Image.NEAREST), (w * zoom + zoom * 2, 0))
        d = ImageDraw.Draw(tile)
        for x, y in changes:
            d.rectangle((x * zoom, y * zoom, x * zoom + zoom - 1, y * zoom + zoom - 1), outline=(255, 0, 255, 255))
        tiles.append(tile)
    cols = 4
    tw, th = tiles[0].size
    sheet = Image.new("RGBA", (tw * cols, th * ((len(tiles) + cols - 1) // cols)), (60, 60, 70, 255))
    for i, t in enumerate(tiles):
        sheet.alpha_composite(t, ((i % cols) * tw, (i // cols) * th))
    sheet.save(path)


def main(argv):
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("src")
    parser.add_argument("dst")
    parser.add_argument("--marks")
    args = parser.parse_args(argv)
    os.makedirs(args.dst, exist_ok=True)
    pairs, total = [], 0
    for path in sorted(glob.glob(os.path.join(args.src, "artificer_walk_*.png"))):
        before = Image.open(path).convert("RGBA")
        after, changes = clean(before)
        after.save(os.path.join(args.dst, os.path.basename(path)))
        pairs.append((before, after, changes))
        total += len(changes)
        print(f"{os.path.basename(path)}: {len(changes)} pixels")
    print(f"clean: {total} pixels changed in {len(pairs)} frames")
    if args.marks:
        marks_sheet(pairs, args.marks)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
