"""Turn the front and back walks' in-between frames into passing poses, and keep a standing pose.

Today's walk goes stand, step, stand, step: frames 0 and 2 are both the standing pose. This
copies each facing's frame 0 to `artificer_stand_<facing>.png` (what the game shows when the
Artificer stands still), then, in the front (down) and back (up) walks, lifts one foot three
pixels in frame 0 and the other foot in frame 2, so the legs pass each other. A foot is lifted
by moving its columns' lowest rows up, which shortens the shin as a bent knee would; nothing is
drawn. The side views' legs are one block in those frames, so they are left as they are.

    python walk_feet.py <in dir> <out dir>

Run it last, after clean_today, side_eye, side_heads and outline, on the 16 walk frames.
"""
import os
import shutil
import sys

from PIL import Image

# facing: {frame: (first column, last column, first row moved, pixels lifted)}, 64x93 frames.
# The legs of the front and back views stand apart; columns 18-34 hold the figure's right leg
# from the front (left on screen), 35-50 the other.
LIFTS = {
    "down": {0: (18, 34, 87, 3), 2: (35, 50, 87, 3)},
    "up": {0: (35, 50, 86, 3), 2: (16, 34, 86, 3)},
}
FACINGS = ("down", "left", "right", "up")


def lift(image, x0, x1, y0, dy):
    """Move columns x0..x1, rows y0 to the bottom, up by dy; the rows they leave are cleared."""
    image = image.convert("RGBA")
    source = image.copy().load()
    px = image.load()
    h = image.height
    for x in range(x0, x1 + 1):
        for y in range(y0 - dy, h):
            px[x, y] = source[x, y + dy] if y + dy < h else (0, 0, 0, 0)
    return image


def main(argv):
    src, dst = argv
    os.makedirs(dst, exist_ok=True)
    for facing in FACINGS:
        shutil.copyfile(os.path.join(src, f"artificer_walk_{facing}_0.png"),
                        os.path.join(dst, f"artificer_stand_{facing}.png"))
        for i in range(4):
            name = f"artificer_walk_{facing}_{i}.png"
            image = Image.open(os.path.join(src, name)).convert("RGBA")
            if i in LIFTS.get(facing, {}):
                image = lift(image, *LIFTS[facing][i])
                print(f"{name}: foot lifted")
            image.save(os.path.join(dst, name))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
