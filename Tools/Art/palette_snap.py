"""The scripted cleanup of a redrawn frame (docs/art-pipeline-design.md §2 step 7, §5.3).

    python Tools/Art/palette_snap.py <in.png> <out.png> --ramps outline,mahogany,brass
        [--key "#28be46" [--key-tolerance 0.12] [--key-holes 256]] [--reduce 8] [--alpha 128]
        [--shadow-rows 114,119] [--outline] [--fixes <fixes.json>] [--max-change 0.10]
        [--canvas 64,96] [--into-art]

In order:
0. --key: a ComfyUI render has no alpha. Every pixel within --key-tolerance (OKLab) of the key
   colour that is connected to the canvas edge through such pixels becomes transparent: the flat
   background the init was composited on. A key-coloured pixel inside the body stays, unless
   --key-holes N: an enclosed key-coloured region of at least N pixels (the background seen
   through a gap in the body) is cleared too.
1. --reduce N: N x to 1 x by k-centroid, per N x N block: split the block's opaque colours in
   two by a two-means pass and keep the larger cluster's most common colour. That keeps a
   one-pixel line a nearest pick can drop. A block is opaque when most of its pixels are.
2. Alpha to 0 or 255 at --alpha, except in --shadow-rows (first,last).
3. Every opaque pixel to the nearest colour of the named ramps, in OKLab.
4. --outline: an opaque pixel next to transparency whose OKLab lightness was under 0.2 before
   the snap becomes the outline ramp's nearest tone: the carts' black-to-brown softening, as a
   tool. (Judged on the colour before step 3, which would have moved it onto another ramp.)
5. --fixes: this file's scripted fixes from a set's fixes.json, applied last; each is a
   rectangle and a ramp tone ("brass:3") or "transparent", and an off-palette tone is refused.
6. --canvas W,H: the result placed on a W x H canvas, centred, with its lowest opaque row on
   the bottom row (a BottomCenter sprite's feet, art-style-bible.md invariant 1). Refuses a body
   that doesn't fit.
7. Prints the pixels each step changed, and exits 1 if step 3 moved more than --max-change of
   the opaque pixels further than FAR_MOVE in OKLab (a render is never exactly on the palette,
   so a near move doesn't count; a far one means the redraw left it: back to ComfyUI).

fixes.json: {"<out file name>": [{"rect": [x, y, w, h], "tone": "brass:3"},
                                  {"rect": [x, y, w, h], "ramp": "mahogany"}, ...], ...}
A "ramp" fix moves every opaque, non-outline pixel of its rectangle to that ramp's nearest tone.

It writes under godot/art/ only with --into-art (the commit step, §5).
"""
import argparse
import json
import os
import sys
from collections import Counter

from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import art_metrics as m  # noqa: E402
from art_paths import writes_into_godot_art  # noqa: E402

OUTLINE_LIGHTNESS = 0.2


class SnapError(Exception):
    pass


def key_out(image, key, tolerance=0.12, holes=0):
    """The flat background, transparent: pixels near `key` in OKLab, flood-filled from the edge,
    and (holes > 0) every enclosed region of such pixels at least `holes` pixels big: the
    background seen through a gap in the body (a press's frame, the space between legs). A
    smaller key-coloured patch inside the body stays. Returns the number of pixels cleared."""
    px = image.load()
    w, h = image.size
    target = m.oklab(key)
    cache = {}

    def near(p):
        rgb = tuple(p[:3])
        if rgb not in cache:
            cache[rgb] = m.lab_distance(m.oklab(rgb), target) <= tolerance
        return cache[rgb]

    def region(start, seen):
        found, stack = [], [start]
        while stack:
            x, y = stack.pop()
            if (x, y) in seen or not (0 <= x < w and 0 <= y < h):
                continue
            p = px[x, y]
            if p[3] != 0 and not near(p):
                continue
            seen.add((x, y))
            found.append((x, y))
            stack.extend(((x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)))
        return found

    seen = set()
    clear = []
    for start in [(x, y) for x in range(w) for y in (0, h - 1)] + [(x, y) for y in range(h) for x in (0, w - 1)]:
        clear.extend(region(start, seen))
    if holes > 0:
        for y in range(h):
            for x in range(w):
                if (x, y) not in seen and px[x, y][3] != 0 and near(px[x, y]):
                    found = region((x, y), seen)
                    if len(found) >= holes:
                        clear.extend(found)
    cleared = 0
    for x, y in clear:
        if px[x, y][3] != 0:
            px[x, y] = (0, 0, 0, 0)
            cleared += 1
    return cleared


def place_on_canvas(image, size):
    """The opaque body centred on a size[0] x size[1] canvas, its lowest opaque row on the
    bottom row."""
    box = image.getbbox()
    if box is None:
        raise SnapError("nothing opaque to place")
    body = image.crop(box)
    if body.width > size[0] or body.height > size[1]:
        raise SnapError(f"the body is {body.width}x{body.height}, larger than the {size[0]}x{size[1]} canvas")
    out = Image.new("RGBA", size, (0, 0, 0, 0))
    out.paste(body, ((size[0] - body.width) // 2, size[1] - body.height))
    return out


def k_centroid_block(pixels, alpha_threshold=128):
    """One N x N block's colour: transparent unless most pixels are opaque; otherwise the most
    common colour of the larger of two clusters (two-means in OKLab, seeded by the darkest and
    lightest colours)."""
    opaque = [p for p in pixels if p[3] >= alpha_threshold]
    if len(opaque) * 2 < len(pixels):
        return (0, 0, 0, 0)
    colours = Counter(tuple(p[:3]) for p in opaque)
    if len(colours) == 1:
        return tuple(next(iter(colours))) + (255,)
    labs = {c: m.oklab(c) for c in colours}
    ordered = sorted(colours, key=lambda c: labs[c][0])
    centres = [labs[ordered[0]], labs[ordered[-1]]]
    for _ in range(8):
        groups = ([], [])
        for c in colours:
            d0 = m.lab_distance(labs[c], centres[0])
            d1 = m.lab_distance(labs[c], centres[1])
            groups[0 if d0 <= d1 else 1].append(c)
        new = []
        for g, old in zip(groups, centres):
            weight = sum(colours[c] for c in g)
            new.append(tuple(sum(labs[c][i] * colours[c] for c in g) / weight for i in range(3)) if weight else old)
        if new == centres:
            break
        centres = new
    sizes = [sum(colours[c] for c in g) for g in groups]
    big = groups[0] if sizes[0] >= sizes[1] else groups[1]
    best = max(big, key=lambda c: (colours[c], -labs[c][0]))
    return best + (255,)


def reduce(image, factor, alpha_threshold=128):
    w, h = image.size
    if w % factor or h % factor:
        raise SnapError(f"{w}x{h} isn't a multiple of --reduce {factor}")
    out = Image.new("RGBA", (w // factor, h // factor))
    src, dst = image.load(), out.load()
    for by in range(h // factor):
        for bx in range(w // factor):
            block = [src[bx * factor + x, by * factor + y] for y in range(factor) for x in range(factor)]
            dst[bx, by] = k_centroid_block(block, alpha_threshold)
    return out


def hard_alpha(image, threshold, shadow_rows=None):
    px = image.load()
    w, h = image.size
    changed = 0
    for y in range(h):
        if shadow_rows and shadow_rows[0] <= y <= shadow_rows[1]:
            continue
        for x in range(w):
            r, g, b, a = px[x, y]
            if 0 < a < 255:
                px[x, y] = (r, g, b, 255) if a >= threshold else (0, 0, 0, 0)
                changed += 1
    return changed


# A snap that moves a pixel further than this (OKLab) counts toward --max-change: a render is
# never exactly on the palette, so "moved at all" would refuse every one; "moved far" is what
# says the redraw left the palette (a colour no ramp is near).
FAR_MOVE = 0.08


def snap(image, palette, ramps, far=FAR_MOVE):
    """Every opaque pixel to its nearest ramp colour. Returns (changed, opaque, moved far)."""
    px = image.load()
    w, h = image.size
    cache, changed, opaque, distant = {}, 0, 0, 0
    for y in range(h):
        for x in range(w):
            r, g, b, a = px[x, y]
            if a == 0:
                continue
            opaque += 1
            rgb = (r, g, b)
            if rgb not in cache:
                nearest = palette.nearest(rgb, ramps)
                cache[rgb] = (nearest, m.lab_distance(m.oklab(rgb), m.oklab(nearest)) > far)
            nearest, is_far = cache[rgb]
            if nearest != rgb:
                px[x, y] = nearest + (a,)
                changed += 1
                distant += is_far
    return changed, opaque, distant


def outline_candidates(image):
    """The opaque pixels next to transparency (or the canvas edge) darker than OKLab 0.2, with
    their colour: read BEFORE the snap, which would otherwise have moved a black edge onto the
    nearest dark tone of some other ramp."""
    px = image.load()
    w, h = image.size
    edge = []
    for y in range(h):
        for x in range(w):
            if px[x, y][3] == 0:
                continue
            near_clear = any(not (0 <= nx < w and 0 <= ny < h) or px[nx, ny][3] == 0
                             for nx, ny in ((x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)))
            if near_clear and m.oklab(px[x, y])[0] < OUTLINE_LIGHTNESS:
                edge.append((x, y))
    return [(x, y, px[x, y]) for x, y in edge]


def soften_outline(image, palette, candidates):
    if "outline" not in palette.ramps:
        raise SnapError("--outline needs an outline ramp in palette.json")
    px = image.load()
    for x, y, original in candidates:
        px[x, y] = palette.nearest(original, ["outline"]) + (px[x, y][3],)
    return len(candidates)


def parse_tone(text, palette):
    if text == "transparent":
        return (0, 0, 0, 0)
    if ":" not in text:
        raise SnapError(f"a fix's tone is '<ramp>:<index>' or 'transparent', not '{text}'")
    ramp, index = text.split(":", 1)
    if ramp not in palette.ramps or not index.isdigit() or int(index) >= len(palette.ramps[ramp]):
        raise SnapError(f"'{text}' isn't a tone of the palette")
    return palette.ramps[ramp][int(index)] + (255,)


def apply_fixes(image, fixes, palette):
    """Each fix is a rectangle and either a "tone" (painted flat) or a "ramp": every opaque pixel
    in the rectangle that isn't outline moves to that ramp's nearest tone, so a part is one
    material and keeps its shading (a highlight the snap put on a neighbouring ramp comes back,
    which reshade_mirror.py needs: it re-shades within one ramp's runs)."""
    px = image.load()
    w, h = image.size
    changed = 0
    outline = palette.outline()
    for fix in fixes:
        x0, y0, fw, fh = fix["rect"]
        if "ramp" in fix:
            if fix["ramp"] not in palette.ramps:
                raise SnapError(f"'{fix['ramp']}' isn't a ramp of the palette")
            for y in range(max(0, y0), min(h, y0 + fh)):
                for x in range(max(0, x0), min(w, x0 + fw)):
                    p = px[x, y]
                    if p[3] == 0 or tuple(p[:3]) in outline:
                        continue
                    nearest = palette.nearest(p[:3], [fix["ramp"]]) + (p[3],)
                    if nearest != p:
                        px[x, y] = nearest
                        changed += 1
            continue
        colour = parse_tone(fix["tone"], palette)
        for y in range(max(0, y0), min(h, y0 + fh)):
            for x in range(max(0, x0), min(w, x0 + fw)):
                if px[x, y] != colour:
                    px[x, y] = colour
                    changed += 1
    return changed


def run(image, palette, ramps, reduce_by=None, alpha=128, shadow_rows=None, outline=False,
        fixes=None, max_change=0.10, log=print, key=None, key_tolerance=0.12, canvas=None, key_holes=0):
    """The whole cleanup on one image; returns (image, counts). Raises SnapError when the snap
    moved more than max_change of the opaque pixels."""
    image = image.convert("RGBA")
    counts = {}
    if key is not None:
        counts["key"] = key_out(image, key, key_tolerance, key_holes)
    if reduce_by and reduce_by > 1:
        image = reduce(image, reduce_by, alpha)
        counts["reduce"] = image.size[0] * image.size[1]
    counts["alpha"] = hard_alpha(image, alpha, shadow_rows)
    edge = outline_candidates(image) if outline else []
    counts["snap"], opaque, counts["snap far"] = snap(image, palette, ramps)
    counts["outline"] = soften_outline(image, palette, edge) if outline else 0
    if canvas:
        image = place_on_canvas(image, canvas)
    counts["fixes"] = apply_fixes(image, fixes, palette) if fixes else 0
    for step, n in counts.items():
        log(f"  {step}: {n} pixels")
    if opaque and counts["snap far"] / opaque > max_change:
        raise SnapError(f"the snap moved {counts['snap far']} of {opaque} opaque pixels more than {FAR_MOVE} in OKLab "
                        f"({counts['snap far'] / opaque:.0%} > {max_change:.0%}): the redraw is off-palette")
    return image, counts


def main(argv):
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("input")
    parser.add_argument("output")
    parser.add_argument("--ramps", required=True, help="comma-separated ramp names from palette.json")
    parser.add_argument("--reduce", type=int)
    parser.add_argument("--alpha", type=int, default=128)
    parser.add_argument("--shadow-rows", help="first,last")
    parser.add_argument("--outline", action="store_true")
    parser.add_argument("--fixes")
    parser.add_argument("--max-change", type=float, default=0.10)
    parser.add_argument("--key", help="the render's flat background colour, #rrggbb")
    parser.add_argument("--key-tolerance", type=float, default=0.12)
    parser.add_argument("--key-holes", type=int, default=0,
                        help="also clear enclosed key-coloured regions of at least this many pixels")
    parser.add_argument("--canvas", help="W,H: place the body on this canvas, feet on the bottom row")
    parser.add_argument("--palette", default=m.PALETTE_PATH)
    parser.add_argument("--into-art", action="store_true", help="allow writing under godot/art/ (the commit step)")
    args = parser.parse_args(argv)

    if writes_into_godot_art(os.path.dirname(os.path.abspath(args.output))) and not args.into_art:
        print("refused: the output is under godot/art/; pass --into-art at the commit step", file=sys.stderr)
        return 2
    palette = m.Palette.load(args.palette)
    ramps = [r.strip() for r in args.ramps.split(",") if r.strip()]
    unknown = [r for r in ramps if r not in palette.ramps]
    if unknown:
        print(f"unknown ramps: {', '.join(unknown)}", file=sys.stderr)
        return 2
    shadow = tuple(int(v) for v in args.shadow_rows.split(",")) if args.shadow_rows else None
    fixes = None
    if args.fixes:
        with open(args.fixes, encoding="utf-8") as handle:
            fixes = json.load(handle).get(os.path.basename(args.output), [])
    try:
        image, _ = run(m.load(args.input), palette, ramps, args.reduce, args.alpha, shadow,
                       args.outline, fixes, args.max_change,
                       key=m.hex_to_rgb(args.key) if args.key else None, key_tolerance=args.key_tolerance,
                       key_holes=args.key_holes,
                       canvas=tuple(int(v) for v in args.canvas.split(",")) if args.canvas else None)
    except SnapError as error:
        print(f"failed: {error}", file=sys.stderr)
        return 1
    os.makedirs(os.path.dirname(os.path.abspath(args.output)), exist_ok=True)
    image.save(args.output)
    print(f"wrote {args.output} ({image.size[0]}x{image.size[1]})")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
