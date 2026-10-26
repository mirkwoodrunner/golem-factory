"""A west frame from an east one, still lit from the upper left (art-pipeline-design.md §5.7).

    python Tools/Art/reshade_mirror.py <east.png> <west.png> --ramps <set's ramps> [--into-art]

1. Flip horizontally about the canvas centre. The width must be even (every chassis and the
   Artificer are 64), so a BottomCenter pivot and the foot row stay put; an odd width is refused.
2. Classify each opaque pixel by its ramp and tone index (palette.json). A pixel in no ramp is
   left alone and counted.
3. Re-shade the interior: in each row, for each maximal horizontal run of one ramp (the outline
   ramp excluded), reverse the tone indices along the run. Each pixel keeps its ramp and its
   place and takes the tone of its mirror place in the run, which restores the source's
   left-to-right tone order: the highlights are back on the left. Vertical shading is untouched.
4. Re-shade the outline: in each row, swap the tones of the leftmost and rightmost outline
   pixels of each silhouette span, so a selective outline's stepped-up tone is back on the lit
   side (art-style-bible.md §2.1).
5. Measure: prints the source's and the result's edge-light scores and the untouched count, and
   exits 1 if the result scores under 0.8 x the source or under +0.02.

An asymmetric detail (a lamp on one shoulder) stays where the mirror put it, on purpose.
"""
import argparse
import os
import sys

from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import art_metrics as m  # noqa: E402
from art_paths import writes_into_godot_art  # noqa: E402

MIN_RATIO = 0.8
MIN_SCORE = 0.02


class MirrorError(Exception):
    pass


def reshade(source, palette, ramps=None):
    """The mirrored, re-shaded image and the number of opaque pixels in no ramp."""
    source = source.convert("RGBA")
    w, h = source.size
    if w % 2:
        raise MirrorError(f"the canvas is {w} wide; an odd width would move the pivot")
    flipped = source.transpose(Image.FLIP_LEFT_RIGHT)
    out = flipped.copy()
    fp, op = flipped.load(), out.load()
    untouched = 0
    for y in range(h):
        classes = []
        for x in range(w):
            p = fp[x, y]
            if p[3] < m.OPAQUE:
                classes.append(None)
                continue
            c = palette.classify(p, ramps)
            if c is None:
                untouched += 1
            classes.append(c if c else ("?", None))
        # 3: runs of one (non-outline) ramp.
        x = 0
        while x < w:
            c = classes[x]
            if c is None or c[0] in ("?", "outline"):
                x += 1
                continue
            end = x
            while end + 1 < w and classes[end + 1] is not None and classes[end + 1][0] == c[0]:
                end += 1
            ramp = palette.ramps[c[0]]
            for i in range(x, end + 1):
                tone = classes[x + end - i][1]
                op[i, y] = ramp[tone] + (fp[i, y][3],)
            x = end + 1
        # 4: the outline's two ends of each silhouette span.
        x = 0
        while x < w:
            if classes[x] is None:
                x += 1
                continue
            end = x
            while end + 1 < w and classes[end + 1] is not None:
                end += 1
            left = next((i for i in range(x, end + 1) if classes[i][0] == "outline"), None)
            right = next((i for i in range(end, x - 1, -1) if classes[i][0] == "outline"), None)
            if left is not None and right is not None and left != right:
                op[left, y], op[right, y] = fp[right, y], fp[left, y]
            x = end + 1
    return out, untouched


def main(argv):
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("east")
    parser.add_argument("west")
    parser.add_argument("--ramps", help="comma-separated ramp names (default: every ramp)")
    parser.add_argument("--palette", default=m.PALETTE_PATH)
    parser.add_argument("--into-art", action="store_true", help="allow writing under godot/art/ (the commit step)")
    args = parser.parse_args(argv)
    if writes_into_godot_art(os.path.dirname(os.path.abspath(args.west))) and not args.into_art:
        print("refused: the output is under godot/art/; pass --into-art at the commit step", file=sys.stderr)
        return 2
    palette = m.Palette.load(args.palette)
    ramps = [r.strip() for r in args.ramps.split(",")] if args.ramps else None
    if ramps and "outline" not in ramps:
        ramps.append("outline")
    source = m.load(args.east)
    try:
        result, untouched = reshade(source, palette, ramps)
    except MirrorError as error:
        print(f"refused: {error}", file=sys.stderr)
        return 2
    outline = palette.outline()
    before, after = m.edge_light_score(source, outline), m.edge_light_score(result, outline)
    before_tone, after_tone = m.edge_tone_score(source, palette), m.edge_tone_score(result, palette)
    print(f"edge-light: source {before:+.3f}, mirrored {after:+.3f}; edge-tone: source {before_tone:+.3f}, "
          f"mirrored {after_tone:+.3f}; {untouched} pixels in no ramp, left alone")
    os.makedirs(os.path.dirname(os.path.abspath(args.west)), exist_ok=True)
    result.save(args.west)
    print(f"wrote {args.west}")
    if after < MIN_SCORE or (after < MIN_RATIO * before and after_tone < MIN_RATIO * before_tone):
        print(f"failed: the mirror scores {after:+.3f}; it needs at least {MIN_SCORE:+.2f} and "
              f"{MIN_RATIO} x the source's edge light or edge tone", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
