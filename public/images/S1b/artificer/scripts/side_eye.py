"""Redraw the side view's eye to match the front view's, in walking right's frame 2.

The front eye is an iris about three pixels wide, dark at the top (38383f, 505548) and lighter
below (7a8264), with the eye's white (bba587) beside it and a dark lash above. The side eye was
two pixels wide with its white on the lash line, so it read as a narrow dark stroke. This
widens the iris to three pixels and puts the white behind it (toward the ear), from the
sprite's own colours. Run side_heads.py afterwards so all eight side frames take the new head.

    python side_eye.py <frame in> <frame out>
"""
import sys

from PIL import Image

LASH, DARK, MID, LIGHT, WHITE = "241e30", "38383f", "505548", "7a8264", "bba587"

# (x, y): colour, in walking right's frame 2 (64x93).
PIXELS = {
    (41, 27): LASH, (42, 27): LASH, (43, 27): LASH,
    (40, 28): WHITE, (41, 28): DARK, (42, 28): DARK, (43, 28): DARK,
    (40, 29): WHITE, (41, 29): MID, (42, 29): MID, (43, 29): MID,
    (40, 30): WHITE, (41, 30): MID, (42, 30): MID, (43, 30): MID,
    (41, 31): LIGHT, (42, 31): LIGHT, (43, 31): LIGHT,
    (42, 32): LIGHT, (43, 32): LIGHT,
}


def main(argv):
    image = Image.open(argv[0]).convert("RGBA")
    px = image.load()
    for p, hexa in PIXELS.items():
        px[p] = tuple(int(hexa[i:i + 2], 16) for i in (0, 2, 4)) + (255,)
    image.save(argv[1])
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
