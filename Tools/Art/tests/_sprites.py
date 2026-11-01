"""Small synthetic sprites for the tool tests: no ComfyUI, no godot/art/."""
from PIL import Image

try:
    from . import _art  # noqa: F401
except ImportError:
    import _art  # noqa: F401

import art_metrics as m

PALETTE = m.Palette.load()
OUTLINE = PALETTE.ramps["outline"][0]
BRASS = PALETTE.ramps["brass"]


def lit_body(width=16, height=24, left=3, right=12, top=4, ramp=None, lit_from="left"):
    """A standing body on the bottom row: an outline box whose interior runs the ramp's tones
    from light on the lit side to dark on the other (horizontal shading only)."""
    ramp = ramp or BRASS
    image = Image.new("RGBA", (width, height), (0, 0, 0, 0))
    px = image.load()
    for y in range(top, height):
        for x in range(left, right + 1):
            if x in (left, right) or y in (top, height - 1):
                px[x, y] = OUTLINE + (255,)
                continue
            t = (x - left - 1) / max(1, right - left - 2)  # 0 at the left, 1 at the right
            if lit_from == "left":
                t = 1 - t
            px[x, y] = ramp[min(len(ramp) - 1, int(t * len(ramp)))] + (255,)
    return image


def block(width, height, rects, colour, size=None):
    """Rectangles of one colour on a transparent canvas."""
    image = Image.new("RGBA", (width, height), (0, 0, 0, 0))
    px = image.load()
    for x0, y0, w, h in rects:
        for y in range(y0, y0 + h):
            for x in range(x0, x0 + w):
                px[x, y] = tuple(colour[:3]) + (255,)
    return image
