"""Score each ComfyUI candidate for a facing's base by what compose.py would make of it.

    python pick_base.py <drafts dir> <raw dir> <clean out dir> <facing>

Each candidate is cleaned (palette_snap: key, 8x k-centroid reduce, the set's ramps, outline),
its feet checked on row 95, then composed into the base frame and scored by check_art.py's
measures: silhouette overlap with the other chassis' south bases (south only), mean lightness,
the Stalled shift, the stopped tints' spread, the edge-light score, colours, and how many pixels
the render changed from the draft (its texture; zero means the draft came back unchanged).
"""
import glob
import os
import sys
from itertools import combinations

HERE = os.path.dirname(os.path.abspath(__file__))
ART_TOOLS = os.path.normpath(os.path.join(HERE, "..", "..", "..", "..", "..", "Tools", "Art"))
sys.path.insert(0, ART_TOOLS)
sys.path.insert(0, HERE)
import art_metrics as m  # noqa: E402
import check_art  # noqa: E402
import palette_snap  # noqa: E402
import compose  # noqa: E402

RAMPS = ["outline", "brass", "iron", "copper", "aether", "fire", "steam"]
KEY = (40, 190, 70)


def clean(path):
    image, _ = palette_snap.run(m.load(path), m.Palette.load(), RAMPS, reduce_by=8, key=KEY, key_holes=256,
                                outline=True, max_change=1.0, log=lambda *_: None)
    return image


def main(draft_dir, raw_dir, out_dir, facing):
    ctx = check_art.Context.from_repo()
    art = os.path.join(ART_TOOLS, "..", "..", "godot", "art")
    others = [m.foot_mask(m.load(os.path.join(art, s + ".png"))) for s in ctx.chassis_stems if s != compose.STEM]
    draft = m.load(os.path.join(draft_dir, f"draft_{facing}_base.png"))
    os.makedirs(out_dir, exist_ok=True)
    for path in sorted(glob.glob(os.path.join(raw_dir, "*.png"))):
        c = clean(path)
        c.save(os.path.join(out_dir, os.path.basename(path)))
        low = m.lowest_opaque_row(c)
        img, taken = compose.compose(draft_dir, c, facing, "base")
        sil = max(m.iou(m.foot_mask(img), o) for o in others) if facing == "south" else float("nan")
        lit = m.mean_lab(img)
        t = {k: m.mean_lab(img, ctx.tints[k]) for k in check_art.MOOD_TINTS_CHECKED}
        apart = min(m.lab_distance(t[a], t[b]) for a, b in combinations(t, 2))
        edge = m.edge_light_score(img, ctx.palette.outline())
        changed = sum(1 for x, y, p in m.opaque_pixels(img) if p != draft.getpixel((x, y)))
        cols = len({p[:3] for *_, p in m.opaque_pixels(img)})
        print(f"{os.path.basename(path)}: feet {low} sil {sil:.2f} L {lit[0]:.2f} stall {m.lab_distance(lit, t['Stalled']):.3f} "
              f"apart {apart:.3f} edge {edge:+.3f} cols {cols} texture {changed} px (taken {taken})")


if __name__ == "__main__":
    main(*sys.argv[1:5])
