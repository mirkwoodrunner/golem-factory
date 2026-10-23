"""The scripted cleanup of a redrawn frame (docs/art-pipeline-design.md §2 step 7, §5.3).

    python Tools/Art/palette_snap.py <in.png> <out.png> --ramps outline,mahogany,brass
        [--reduce 8] [--alpha 128] [--shadow-rows 114,119] [--outline]
        [--fixes <fixes.json>] [--max-change 0.10] [--into-art]

In order:
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
6. Prints the pixels each step changed, and exits 1 if step 3 moved more than --max-change of
   the opaque pixels (the redraw was off-palette: back to ComfyUI).

fixes.json: {"<out file name>": [{"rect": [x, y, w, h], "tone": "brass:3"}, ...], ...}

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


def snap(image, palette, ramps):
    px = image.load()
    w, h = image.size
    cache, changed, opaque = {}, 0, 0
    for y in range(h):
        for x in range(w):
            r, g, b, a = px[x, y]
            if a == 0:
                continue
            opaque += 1
            rgb = (r, g, b)
            if rgb not in cache:
                cache[rgb] = palette.nearest(rgb, ramps)
            if cache[rgb] != rgb:
                px[x, y] = cache[rgb] + (a,)
                changed += 1
    return changed, opaque


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
    px = image.load()
    w, h = image.size
    changed = 0
    for fix in fixes:
        colour = parse_tone(fix["tone"], palette)
        x0, y0, fw, fh = fix["rect"]
        for y in range(max(0, y0), min(h, y0 + fh)):
            for x in range(max(0, x0), min(w, x0 + fw)):
                if px[x, y] != colour:
                    px[x, y] = colour
                    changed += 1
    return changed


def run(image, palette, ramps, reduce_by=None, alpha=128, shadow_rows=None, outline=False,
        fixes=None, max_change=0.10, log=print):
    """The whole cleanup on one image; returns (image, counts). Raises SnapError when the snap
    moved more than max_change of the opaque pixels."""
    image = image.convert("RGBA")
    counts = {}
    if reduce_by and reduce_by > 1:
        image = reduce(image, reduce_by, alpha)
        counts["reduce"] = image.size[0] * image.size[1]
    counts["alpha"] = hard_alpha(image, alpha, shadow_rows)
    edge = outline_candidates(image) if outline else []
    counts["snap"], opaque = snap(image, palette, ramps)
    counts["outline"] = soften_outline(image, palette, edge) if outline else 0
    counts["fixes"] = apply_fixes(image, fixes, palette) if fixes else 0
    for step, n in counts.items():
        log(f"  {step}: {n} pixels")
    if opaque and counts["snap"] / opaque > max_change:
        raise SnapError(f"the snap moved {counts['snap']} of {opaque} opaque pixels "
                        f"({counts['snap'] / opaque:.0%} > {max_change:.0%}): the redraw is off-palette")
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
                       args.outline, fixes, args.max_change)
    except SnapError as error:
        print(f"failed: {error}", file=sys.stderr)
        return 1
    os.makedirs(os.path.dirname(os.path.abspath(args.output)), exist_ok=True)
    image.save(args.output)
    print(f"wrote {args.output} ({image.size[0]}x{image.size[1]})")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
