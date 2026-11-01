"""The shared measures of the art tools (docs/art-pipeline-design.md §5).

check_art.py, reshade_mirror.py, review_sheet.py and the pick (§2, step 5) all import this
module, so a check and the tool it checks can never measure differently:

- sRGB to OKLab, and distances in it;
- alpha masks aligned at the feet (bottom centre), and their intersection over union;
- the edge-light score (art-style-bible.md §5.1);
- the 32 -> 24 nearest reduction at a sampling phase (art-style-bible.md §5.4);
- the palette (Tools/Art/palette.json): its ramps, and which ramp and tone a colour is;
- hue, saturation and lightness for the reserved-colour rule (§5.3), and connected pieces.

Pillow and the standard library only. Images are PIL RGBA images throughout.
"""
import colorsys
import json
import math
import os

from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
PALETTE_PATH = os.path.join(HERE, "palette.json")

# Alpha at or above this counts as opaque in a mask (check 3's foot row uses the same).
OPAQUE = 128


# --- colour ------------------------------------------------------------------------------------

def hex_to_rgb(text):
    text = text.lstrip("#")
    return tuple(int(text[i:i + 2], 16) for i in (0, 2, 4))


def rgb_to_hex(rgb):
    return "#%02x%02x%02x" % tuple(rgb[:3])


def _linear(c):
    c = c / 255.0
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


def oklab(rgb):
    """OKLab (L, a, b) of an sRGB colour given as 0-255 integers (alpha ignored)."""
    r, g, b = (_linear(v) for v in rgb[:3])
    l_ = 0.4122214708 * r + 0.5363325363 * g + 0.0514459929 * b
    m_ = 0.2119034982 * r + 0.6806995451 * g + 0.1073969566 * b
    s_ = 0.0883024619 * r + 0.2817188376 * g + 0.6299787005 * b
    l_, m_, s_ = (math.copysign(abs(v) ** (1.0 / 3.0), v) for v in (l_, m_, s_))
    return (
        0.2104542553 * l_ + 0.7936177850 * m_ - 0.0040720468 * s_,
        1.9779984951 * l_ - 2.4285922050 * m_ + 0.4505937099 * s_,
        0.0259040371 * l_ + 0.7827717662 * m_ - 0.8086757660 * s_,
    )


def lab_distance(p, q):
    return math.sqrt(sum((a - b) ** 2 for a, b in zip(p, q)))


def hsl(rgb):
    """(hue in degrees, saturation 0-1, lightness 0-1), HSL as art-style-bible.md §5.3 uses."""
    h, l, s = colorsys.rgb_to_hls(*(v / 255.0 for v in rgb[:3]))
    return h * 360.0, s, l


def hsv_hue(rgb):
    h, _, _ = colorsys.rgb_to_hsv(*(v / 255.0 for v in rgb[:3]))
    return h * 360.0


# --- pixels and masks --------------------------------------------------------------------------

def load(path):
    with Image.open(path) as image:
        return image.convert("RGBA")


def opaque_pixels(image, threshold=OPAQUE):
    """Every (x, y, rgba) with alpha >= threshold."""
    w, h = image.size
    px = image.load()
    return [(x, y, px[x, y]) for y in range(h) for x in range(w) if px[x, y][3] >= threshold]


def mean_lab(image, tint=(1.0, 1.0, 1.0), threshold=OPAQUE):
    """The mean OKLab colour of the opaque pixels, each multiplied by an RGB tint first."""
    pixels = opaque_pixels(image, threshold)
    if not pixels:
        return (0.0, 0.0, 0.0)
    total = [0.0, 0.0, 0.0]
    for _, _, rgba in pixels:
        lab = oklab(tuple(min(255.0, c * t) for c, t in zip(rgba[:3], tint)))
        for i in range(3):
            total[i] += lab[i]
    return tuple(t / len(pixels) for t in total)


def foot_mask(image, threshold=OPAQUE):
    """The opaque pixels as offsets from the bottom centre of the canvas: (dx2, dy), where
    dx2 = 2 * x - (width - 1) (doubled, so odd and even widths align without rounding) and
    dy = height - 1 - y (rows above the bottom row). Two sprites standing on their feet compare
    by these sets directly."""
    w, h = image.size
    px = image.load()
    return {(2 * x - (w - 1), h - 1 - y)
            for y in range(h) for x in range(w) if px[x, y][3] >= threshold}


def iou(a, b):
    """Intersection over union of two masks (sets). Two empty masks overlap 1.0."""
    if not a and not b:
        return 1.0
    return len(a & b) / float(len(a | b))


def lowest_opaque_row(image, threshold=OPAQUE):
    w, h = image.size
    px = image.load()
    for y in range(h - 1, -1, -1):
        if any(px[x, y][3] >= threshold for x in range(w)):
            return y
    return None


def connected_pieces(mask):
    """The number of 4-connected pieces in a set of (x, y)."""
    seen, pieces = set(), 0
    for start in mask:
        if start in seen:
            continue
        pieces += 1
        stack = [start]
        seen.add(start)
        while stack:
            x, y = stack.pop()
            for n in ((x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)):
                if n in mask and n not in seen:
                    seen.add(n)
                    stack.append(n)
    return pieces


def xy_mask(image, threshold=OPAQUE):
    w, h = image.size
    px = image.load()
    return {(x, y) for y in range(h) for x in range(w) if px[x, y][3] >= threshold}


# --- the edge-light score ----------------------------------------------------------------------

def edge_light_score(image, outline=None, threshold=OPAQUE, min_span=3):
    """art-style-bible.md §5.1: over the rows of a sprite, the mean OKLab lightness of the first
    non-outline pixel inside each row's left silhouette edge, minus that of the last one inside
    the right edge. Positive means lit from the left.

    `outline` is a set of RGB tuples (the outline ramp). Without it, the silhouette's own edge
    pixel counts as the outline, and the pixel next to it is read. Rows narrower than
    `min_span` opaque pixels, or with nothing inside the outline, are skipped."""
    w, h = image.size
    px = image.load()
    total, rows = 0.0, 0
    for y in range(h):
        xs = [x for x in range(w) if px[x, y][3] >= threshold]
        if len(xs) < min_span:
            continue
        left, right = xs[0], xs[-1]
        lx = _inside(px, y, left, right, 1, outline, threshold)
        rx = _inside(px, y, right, left, -1, outline, threshold)
        if lx is None or rx is None or lx >= rx:
            continue
        total += oklab(px[lx, y])[0] - oklab(px[rx, y])[0]
        rows += 1
    return total / rows if rows else 0.0


def edge_tone_score(image, palette, threshold=OPAQUE, min_span=3):
    """art-style-bible.md §5.1, for a mirrored frame against its source: like
    edge_light_score, but each edge pixel counts as its tone's place in its own ramp (0 the
    darkest, 1 the lightest), not its lightness. A mirror can put a different material on the
    lit edge (the apron rather than the shirt); this asks only whether each material is lit on
    the left. Rows whose edge pixels are in no ramp are skipped."""
    w, h = image.size
    px = image.load()
    outline = palette.outline()
    names = [n for n in palette.ramps if n != "outline"]
    total, rows = 0.0, 0
    for y in range(h):
        xs = [x for x in range(w) if px[x, y][3] >= threshold]
        if len(xs) < min_span:
            continue
        left, right = xs[0], xs[-1]
        lx = _inside(px, y, left, right, 1, outline, threshold)
        rx = _inside(px, y, right, left, -1, outline, threshold)
        if lx is None or rx is None or lx >= rx:
            continue
        a, b = palette.classify(px[lx, y], names), palette.classify(px[rx, y], names)
        if a is None or b is None:
            continue
        total += _place(palette, a) - _place(palette, b)
        rows += 1
    return total / rows if rows else 0.0


def _place(palette, ramp_tone):
    ramp, tone = ramp_tone
    return tone / max(1, len(palette.ramps[ramp]) - 1)


def _inside(px, y, start, stop, step, outline, threshold):
    if outline is None:
        x = start + step
        return x if (x - stop) * step <= 0 and px[x, y][3] >= threshold else None
    x = start
    while (x - stop) * step <= 0:
        rgba = px[x, y]
        if rgba[3] >= threshold and tuple(rgba[:3]) not in outline:
            return x
        x += step
    return None


# --- goods at 24 -------------------------------------------------------------------------------

def reduce_phase(image, phase, size=24):
    """The nearest-neighbour reduction of a square icon (32 -> 24 by default) at a sampling
    phase 0..3: destination pixel i reads source pixel floor((i + phase / 4) * src / size).
    For 32 -> 24 that is (4 * i + phase) // 3, so each phase drops a different column (and
    row) in every four, as an icon landing on a different screen pixel would."""
    src = image.size[0]
    out = Image.new("RGBA", (size, size))
    sp, dp = image.load(), out.load()
    for y in range(size):
        sy = _phase_index(y, src, size, phase)
        for x in range(size):
            dp[x, y] = sp[_phase_index(x, src, size, phase), sy]
    return out


def _phase_index(i, src, size, phase):
    """Which source index destination index i samples at a phase: the sample point moves by a
    quarter of a destination pixel per phase."""
    return min(src - 1, int(math.floor((i + phase / 4.0) * src / float(size))))


# --- the palette -------------------------------------------------------------------------------

class Palette:
    """Tools/Art/palette.json: named ramps, dark to light, and the reserved-colour limits."""

    def __init__(self, data):
        self.data = data
        self.ramps = {name: [hex_to_rgb(c) for c in ramp["colours"]]
                      for name, ramp in data["ramps"].items()}
        self.reserved = data.get("reserved", {})
        self.colour_counts = data.get("colourCounts", {})

    @classmethod
    def load(cls, path=PALETTE_PATH):
        with open(path, encoding="utf-8") as handle:
            return cls(json.load(handle))

    def colours(self, names=None):
        names = names or list(self.ramps)
        return {c for n in names for c in self.ramps[n]}

    def classify(self, rgb, names=None):
        """(ramp, tone index) of an exact palette colour, the first named ramp that holds it;
        None for a colour in no ramp."""
        rgb = tuple(rgb[:3])
        for name in names or list(self.ramps):
            ramp = self.ramps[name]
            if rgb in ramp:
                return name, ramp.index(rgb)
        return None

    def nearest(self, rgb, names=None):
        """The nearest palette colour of the named ramps, in OKLab."""
        target = oklab(rgb)
        best, best_d = None, None
        for colour in sorted(self.colours(names)):
            d = lab_distance(target, oklab(colour))
            if best_d is None or d < best_d:
                best, best_d = colour, d
        return best

    def outline(self):
        return set(self.ramps.get("outline", []))


def is_reserved_red(rgb, limits):
    h, s, l = hsl(rgb)
    lo, hi = limits["hue"]
    in_hue = h >= lo or h <= hi if lo > hi else lo <= h <= hi
    return in_hue and s > limits["saturationAbove"] and l > limits["lightnessAbove"]


def is_reserved_blue(rgb, limits):
    h, s, _ = hsl(rgb)
    lo, hi = limits["hue"]
    return lo <= h <= hi and s > limits["saturationAbove"]
