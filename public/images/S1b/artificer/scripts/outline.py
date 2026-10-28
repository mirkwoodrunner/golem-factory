"""Darken the Artificer's outline so the figure stands off the wooden floor.

In-game the sprite is drawn at about half size, where its brown edge melts into the floor's
browns. This paints the silhouette's outer two pixels (by 4-neighbour distance from a
transparent pixel) one near-black plum; two, so the line survives the half-size nearest-neighbour
scaling. The inside of the figure is unchanged.

    python outline.py <in dir> <out dir>

Reads and writes `artificer_walk_*.png`. Run it last, after clean_today, side_eye and side_heads.
"""
import glob
import os
import sys

from PIL import Image

COLOUR = (0x1C, 0x14, 0x22)
WIDTH = 2
OPAQUE = 128


def outline(image):
    image = image.convert("RGBA")
    px = image.load()
    w, h = image.size
    solid = {(x, y) for x in range(w) for y in range(h) if px[x, y][3] >= OPAQUE}
    edge = {(x, y) for x, y in solid
            for dx in range(-WIDTH, WIDTH + 1) for dy in range(-WIDTH, WIDTH + 1)
            if abs(dx) + abs(dy) <= WIDTH and (x + dx, y + dy) not in solid}
    for p in edge:
        px[p] = COLOUR + (255,)
    return image, len(edge)


def main(argv):
    src, dst = argv
    os.makedirs(dst, exist_ok=True)
    for path in sorted(glob.glob(os.path.join(src, "artificer_walk_*.png"))):
        image, n = outline(Image.open(path))
        image.save(os.path.join(dst, os.path.basename(path)))
        print(f"{os.path.basename(path)}: {n} pixels")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
