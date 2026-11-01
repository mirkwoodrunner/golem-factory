"""Drafts for Phase 6's FX sprites (docs/game-feel-design.md §5): the shapes ComfyUI redraws through
env_set.py, in the palette's ramps. Writes <set dir>/drafts/<name>.png.

    python public/images/S7/scripts/fx_draft.py
"""
import math
import os

from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "..", "drafts")

FIRE = ["#7a2e14", "#c4561f", "#f0a040", "#ffe08a"]
DUST = ["#6e584e", "#b0a096", "#c1ab98", "#d0bfaa"]
BRASS = ["#7d7153", "#967850", "#c49544", "#e2c27a"]
STEAM = ["#6e584e", "#b0a096", "#ded6cc", "#faf6ee"]


def spark():
    """A burst of sparks off a stamp: a hot white-gold core, four rays, a few flecks."""
    im = Image.new("RGBA", (24, 24))
    d = ImageDraw.Draw(im)
    c = 11.5
    for angle, length in ((15, 10), (100, 8), (200, 10), (290, 7), (55, 6), (150, 6), (245, 5), (335, 6)):
        a = math.radians(angle)
        for r in range(2, length + 1):
            colour = FIRE[3] if r < 4 else FIRE[2] if r < 7 else FIRE[1]
            x, y = c + math.cos(a) * r, c + math.sin(a) * r
            d.point((round(x), round(y)), fill=colour)
    d.ellipse((9, 9, 14, 14), fill=FIRE[2])
    d.ellipse((10, 10, 13, 13), fill=FIRE[3])
    for x, y in ((3, 5), (20, 3), (21, 19), (4, 20)):
        d.point((x, y), fill=FIRE[1])
    return im


def build_dust():
    """The dust a building raises as it lands or comes down: three soft lumps on a flat base."""
    im = Image.new("RGBA", (32, 24))
    d = ImageDraw.Draw(im)
    for box, shade in (((2, 10, 14, 22), 1), ((10, 4, 24, 20), 2), ((18, 9, 30, 22), 1), ((6, 14, 26, 23), 2)):
        d.ellipse(box, fill=DUST[shade])
    for box in ((12, 6, 18, 11), (20, 11, 25, 15), (5, 13, 9, 16)):
        d.ellipse(box, fill=DUST[3])
    d.line((4, 22, 27, 22), fill=DUST[0])
    return im


def pop_glint():
    """A glint as a good lands in a crate: a four-pointed brass star."""
    im = Image.new("RGBA", (16, 16))
    d = ImageDraw.Draw(im)
    d.polygon([(8, 1), (9, 7), (15, 8), (9, 9), (8, 15), (7, 9), (1, 8), (7, 7)], fill=BRASS[2])
    d.polygon([(8, 4), (9, 7), (12, 8), (9, 9), (8, 12), (7, 9), (4, 8), (7, 7)], fill=BRASS[3])
    d.point((8, 8), fill="#faf6ee")
    return im


def main():
    os.makedirs(OUT, exist_ok=True)
    for name, make in (("fx_spark", spark), ("fx_build_dust", build_dust), ("fx_pop_glint", pop_glint)):
        make().save(os.path.join(OUT, name + ".png"))
        print("draft:", name)


if __name__ == "__main__":
    main()
