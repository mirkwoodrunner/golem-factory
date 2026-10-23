"""A frame's ControlNet guide and image-to-image start, from a cleaned base
(docs/art-pipeline-design.md §2 step 3, §5.6).

    python Tools/Art/pose_guide.py <cleaned_base.png> --poses public/images/<set>/poses.json
        --frame work_1 --out public/images/<set>/source/<frame>/ [--scale 8]

poses.json gives, per frame, the parts that move and what to hide:

    {
      "work_1": {
        "parts": [
          {"rect": [20, 10, 24, 30], "translate": [0, 6]},
          {"ramps": ["brass"], "rect": [0, 0, 64, 40], "rotate": 15, "pivot": [32, 40]},
          {"colours": ["#5ed6c8"], "scale": 1.5, "pivot": [30, 20]}
        ],
        "hide": [[26, 14, 12, 8]]
      }
    }

A part selects pixels by a rectangle, by ramps (palette.json), by exact colours, or a
rectangle and one of the others together. Its transform, applied in this order about its
`pivot` pixel (default: the selection's centre): `scale`, then `rotate` (degrees, clockwise on
screen; quarter turns are exact), then `translate` [dx, dy]. Every part is taken from the base
before any is pasted back, so parts don't pick each other up; the holes they leave are
transparent; `hide` rectangles are cleared last (the face masked out for a north base).

It writes two files at --scale, both enlarged by nearest-neighbour:
- guide.png: the lineart, white on black: the silhouette and every boundary between two ramps
  (a colour in no ramp counts as its nearest ramp);
- init.png: the moved colour image.

It refuses an --out under godot/art/.
"""
import argparse
import json
import math
import os
import sys

from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import art_metrics as m  # noqa: E402
from art_paths import writes_into_godot_art  # noqa: E402


class PoseError(Exception):
    pass


def _select(image, part, palette):
    w, h = image.size
    px = image.load()
    x0, y0, rw, rh = part.get("rect", [0, 0, w, h])
    ramps = part.get("ramps")
    colours = {m.hex_to_rgb(c) for c in part.get("colours", [])}
    if ramps:
        unknown = [r for r in ramps if r not in palette.ramps]
        if unknown:
            raise PoseError(f"unknown ramps {unknown}")
        colours |= palette.colours(ramps)
    if "rect" not in part and not colours:
        raise PoseError("a part selects by rect, ramps or colours")
    chosen = {}
    for y in range(max(0, y0), min(h, y0 + rh)):
        for x in range(max(0, x0), min(w, x0 + rw)):
            p = px[x, y]
            if p[3] == 0:
                continue
            if colours and tuple(p[:3]) not in colours:
                continue
            chosen[(x, y)] = p
    return chosen


def _transform(pixels, part, size):
    """The part's pixels after scale, rotate, translate, mapped back (inverse nearest) so a
    rotation or scale leaves no holes inside the part."""
    if not pixels:
        return {}
    xs = [x for x, _ in pixels]
    ys = [y for _, y in pixels]
    cx, cy = part.get("pivot", [(min(xs) + max(xs)) / 2.0, (min(ys) + max(ys)) / 2.0])
    scale = float(part.get("scale", 1.0))
    angle = float(part.get("rotate", 0.0))
    dx, dy = part.get("translate", [0, 0])
    quarter = angle % 90 == 0
    if scale == 1.0 and (angle == 0 or quarter):
        out = {}
        turns = int(round(angle / 90.0)) % 4
        for (x, y), p in pixels.items():
            # Centre-of-pixel arithmetic, so a quarter turn about a pixel keeps it in place.
            rx, ry = x - cx, y - cy
            for _ in range(turns):
                rx, ry = -ry, rx  # clockwise on screen (y down)
            out[(int(round(cx + rx + dx)), int(round(cy + ry + dy)))] = p
        return out
    w, h = size
    rad = math.radians(angle)
    cos, sin = math.cos(rad), math.sin(rad)
    out = {}
    for ty in range(h):
        for tx in range(w):
            ux, uy = tx - dx - cx, ty - dy - cy
            sx = (cos * ux + sin * uy) / scale + cx
            sy = (-sin * ux + cos * uy) / scale + cy
            key = (int(round(sx)), int(round(sy)))
            if key in pixels:
                out[(tx, ty)] = pixels[key]
    return out


def pose(base, frame, palette):
    """The moved colour image of one frame at 1x."""
    base = base.convert("RGBA")
    w, h = base.size
    out = base.copy()
    op = out.load()
    parts = frame.get("parts", [])
    taken = [_select(base, part, palette) for part in parts]
    for chosen in taken:
        for (x, y) in chosen:
            op[x, y] = (0, 0, 0, 0)
    for part, chosen in zip(parts, taken):
        for (x, y), p in _transform(chosen, part, base.size).items():
            if 0 <= x < w and 0 <= y < h:
                op[x, y] = p
    for x0, y0, rw, rh in frame.get("hide", []):
        for y in range(max(0, y0), min(h, y0 + rh)):
            for x in range(max(0, x0), min(w, x0 + rw)):
                op[x, y] = (0, 0, 0, 0)
    return out


def lineart(image, palette):
    """White on black: an opaque pixel next to transparency (the silhouette) or next to a pixel
    of another ramp (a material boundary)."""
    w, h = image.size
    px = image.load()
    cache = {}

    def ramp(p):
        rgb = tuple(p[:3])
        if rgb not in cache:
            c = palette.classify(rgb) or palette.classify(palette.nearest(rgb))
            cache[rgb] = c[0] if c else None
        return cache[rgb]

    out = Image.new("RGB", (w, h), (0, 0, 0))
    lp = out.load()
    for y in range(h):
        for x in range(w):
            p = px[x, y]
            if p[3] < m.OPAQUE:
                continue
            mine = ramp(p)
            for nx, ny in ((x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)):
                if not (0 <= nx < w and 0 <= ny < h) or px[nx, ny][3] < m.OPAQUE or ramp(px[nx, ny]) != mine:
                    lp[x, y] = (255, 255, 255)
                    break
    return out


def make(base, poses, frame_name, palette, scale=8):
    if frame_name not in poses:
        raise PoseError(f"poses.json has no frame '{frame_name}'")
    moved = pose(base, poses[frame_name], palette)
    guide = lineart(moved, palette)
    size = (base.size[0] * scale, base.size[1] * scale)
    return guide.resize(size, Image.NEAREST), moved.resize(size, Image.NEAREST)


def main(argv):
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("base")
    parser.add_argument("--poses", required=True)
    parser.add_argument("--frame", required=True)
    parser.add_argument("--out", required=True)
    parser.add_argument("--scale", type=int, default=8)
    parser.add_argument("--palette", default=m.PALETTE_PATH)
    args = parser.parse_args(argv)
    if writes_into_godot_art(args.out):
        print("refused: --out is under godot/art/; guides go in public/images/<set>/source/", file=sys.stderr)
        return 2
    with open(args.poses, encoding="utf-8") as handle:
        poses = json.load(handle)
    try:
        guide, init = make(m.load(args.base), poses, args.frame, m.Palette.load(args.palette), args.scale)
    except PoseError as error:
        print(f"failed: {error}", file=sys.stderr)
        return 1
    os.makedirs(args.out, exist_ok=True)
    guide.save(os.path.join(args.out, "guide.png"))
    init.save(os.path.join(args.out, "init.png"))
    print(f"wrote guide.png and init.png ({guide.size[0]}x{guide.size[1]}) in {args.out}")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
