#!/usr/bin/env python3
"""Condition the Artificer walk-cycle concept sheet into sliced, engine-ready frames.

The source (ConceptArt/artificer_walk_4dir_sheet.png) is an AI render *in* a pixel-art style,
not pixel art: 768x1362 with 101k distinct colours and no recoverable native grid -- edge energy
on the best-fitting lattice is 1.16x the mean, i.e. noise (a true 4x upscale would read ~4x).
So there is nothing to "un-upscale"; the job is to pick a native resolution and resample to it
cleanly. Target is the project's own convention: PPU 64, one cell = 64px, chassis art is 64x96.

Pipeline, in order:
  1. border flood-fill (tolerant) keys the blue backdrop out to alpha, rather than a global
     colour key -- the goggles and the coat shadows are blue-adjacent and a key eats them.
  2. connected components -> the 16 sprites; everything smaller is dropped, which is also what
     removes the generator's watermark sparkle in the bottom-right.
  3. ONE scale factor for all 16 frames (never per-frame fit) so the character does not change
     size between frames of its own walk.
  4. premultiplied-alpha BOX downscale, then a hard alpha threshold: averaging straight RGBA
     drags the backdrop blue into every silhouette edge, and a soft edge at PPU 64 reads as
     grime rather than as anti-aliasing.
  5. one shared adaptive palette across the whole sheet, so the four directions cannot drift
     apart in hue.
  6. alignment: horizontal centre is taken from the HEAD/TORSO band, not the full bbox, because
     a swinging arm moves the bbox centre and the character would visibly shimmy in place.
     Vertically the frames keep their offsets relative to a per-row baseline, which preserves
     the walk's bob instead of flattening it.

Outputs to ConceptArt/artificer_walk/ (deliberately NOT Assets/, so Unity does not import a
concept-art derivative as though it were a production asset):
  frames/artificer_walk_<dir>_<n>.png  -- 16 individual frames
  artificer_walk_sheet_<W>x<H>.png     -- uniform grid sheet, Unity "Grid By Cell Size"-ready
"""

import collections
import os
import sys

from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, "..", ".."))
SRC = os.path.join(ROOT, "ConceptArt", "artificer_walk_4dir_sheet.png")
OUT_DIR = os.path.join(ROOT, "ConceptArt", "artificer_walk")

CELL_W, CELL_H = 64, 96      # matches chassis_*.png / PPU 64
FLOOR_MARGIN = 3             # px of empty cell below the lowest foot in a row
BG_TOLERANCE = 42            # sum-of-channel distance from the sampled backdrop
ALPHA_CUTOFF = 128           # hard on/off; no semi-transparent fringe at this PPU
PALETTE_COLORS = 32
# Verified by eye against the rendered montage, not assumed: the sheet's two profile rows both
# read as "side view" at a glance but face OPPOSITE ways -- row 1 faces right, row 3 faces left.
# They are also independently drawn rather than mirrored, so neither is a flip of the other.
ROW_NAMES = ["down", "right", "up", "left"]


def load_mask(img):
    """Border flood fill -> set of background pixel indices."""
    w, h = img.size
    px = img.load()
    seeds = [(0, 0), (w - 1, 0), (0, h - 1), (w - 1, h - 1)]
    ref = [px[s] for s in seeds]

    def near_bg(c):
        return any(
            abs(c[0] - r[0]) + abs(c[1] - r[1]) + abs(c[2] - r[2]) <= BG_TOLERANCE for r in ref
        )

    bg = bytearray(w * h)
    dq = collections.deque()
    for x in range(w):
        for y in (0, h - 1):
            if not bg[y * w + x] and near_bg(px[x, y]):
                bg[y * w + x] = 1
                dq.append((x, y))
    for y in range(h):
        for x in (0, w - 1):
            if not bg[y * w + x] and near_bg(px[x, y]):
                bg[y * w + x] = 1
                dq.append((x, y))
    while dq:
        x, y = dq.popleft()
        for nx, ny in ((x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)):
            if 0 <= nx < w and 0 <= ny < h and not bg[ny * w + nx] and near_bg(px[nx, ny]):
                bg[ny * w + nx] = 1
                dq.append((nx, ny))
    return bg


def components(bg, w, h, min_area=400):
    """Label opaque regions; return [(area, x0, y0, x1, y1, pixels)] largest first."""
    seen = bytearray(w * h)
    out = []
    for sy in range(h):
        for sx in range(w):
            i = sy * w + sx
            if bg[i] or seen[i]:
                continue
            dq = collections.deque([(sx, sy)])
            seen[i] = 1
            pix = []
            x0 = x1 = sx
            y0 = y1 = sy
            while dq:
                x, y = dq.popleft()
                pix.append((x, y))
                x0, x1 = min(x0, x), max(x1, x)
                y0, y1 = min(y0, y), max(y1, y)
                for nx, ny in ((x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)):
                    j = ny * w + nx
                    if 0 <= nx < w and 0 <= ny < h and not bg[j] and not seen[j]:
                        seen[j] = 1
                        dq.append((nx, ny))
            if len(pix) >= min_area:
                out.append((len(pix), x0, y0, x1, y1, pix))
    out.sort(key=lambda c: -c[0])
    return out


def group_rows(sprites, tol=80):
    """Cluster the 16 sprites into 4 rows by vertical centre, then order left-to-right."""
    by_y = sorted(sprites, key=lambda s: (s[2] + s[4]) / 2)
    rows, cur = [], [by_y[0]]
    for s in by_y[1:]:
        if (s[2] + s[4]) / 2 - (cur[-1][2] + cur[-1][4]) / 2 > tol:
            rows.append(cur)
            cur = []
        cur.append(s)
    rows.append(cur)
    return [sorted(r, key=lambda s: (s[1] + s[3]) / 2) for r in rows]


def report(rows):
    print(f"{len(rows)} rows")
    for ri, row in enumerate(rows):
        print(f"  row {ri}: {len(row)} sprites")
        for area, x0, y0, x1, y1, _ in row:
            print(f"    bbox ({x0:3d},{y0:4d})-({x1:3d},{y1:4d})  {x1-x0+1:3d}x{y1-y0+1:3d}  area {area}")
    tall = max(y1 - y0 + 1 for row in rows for _, _, y0, _, y1, _ in row)
    wide = max(x1 - x0 + 1 for row in rows for _, x0, _, x1, _, _ in row)
    print(f"tallest {tall}px, widest {wide}px")
    usable_h = CELL_H - FLOOR_MARGIN - 1
    print(f"scale to fit {CELL_W}x{CELL_H} cell: {usable_h/tall:.4f} (h) / {CELL_W/wide:.4f} (w)")


def cutout(img, bg, comp, pad=0):
    """Crop one sprite to RGBA, its own component's pixels opaque and nothing else."""
    _, x0, y0, x1, y1, pix = comp
    w, h = x1 - x0 + 1 + 2 * pad, y1 - y0 + 1 + 2 * pad
    out = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    src = img.load()
    dst = out.load()
    for x, y in pix:
        c = src[x, y]
        dst[x - x0 + pad, y - y0 + pad] = (c[0], c[1], c[2], 255)
    return out


def downscale(rgba, scale):
    """Premultiplied BOX resample, then a hard alpha cut so edges stay crisp."""
    w, h = rgba.size
    tw, th = max(1, round(w * scale)), max(1, round(h * scale))
    px = rgba.load()
    pm = Image.new("RGBA", (w, h))
    pmp = pm.load()
    for y in range(h):
        for x in range(w):
            r, g, b, a = px[x, y]
            f = a / 255
            pmp[x, y] = (round(r * f), round(g * f), round(b * f), a)
    pm = pm.resize((tw, th), Image.BOX)
    out = Image.new("RGBA", (tw, th), (0, 0, 0, 0))
    sp, op = pm.load(), out.load()
    for y in range(th):
        for x in range(tw):
            r, g, b, a = sp[x, y]
            if a < ALPHA_CUTOFF:
                continue
            f = a / 255
            op[x, y] = (min(255, round(r / f)), min(255, round(g / f)), min(255, round(b / f)), 255)
    return out


def torso_centre(rgba):
    """Horizontal centre of the top 45% of the silhouette -- head and shoulders, which do not
    swing. Using the full bbox centre instead makes the sprite shimmy as arms extend."""
    w, h = rgba.size
    px = rgba.load()
    band = max(1, int(h * 0.45))
    xs = [x for y in range(band) for x in range(w) if px[x, y][3] >= ALPHA_CUTOFF]
    if not xs:
        xs = [w // 2]
    return (min(xs) + max(xs)) / 2


def quantise(sheet, colors=PALETTE_COLORS):
    """One adaptive palette for every frame at once; transparency parked on a sentinel."""
    sentinel = (255, 0, 255)
    flat = Image.new("RGB", sheet.size, sentinel)
    flat.paste(sheet, (0, 0), sheet)
    q = flat.convert("P", palette=Image.ADAPTIVE, colors=colors + 1).convert("RGB")
    out = Image.new("RGBA", sheet.size, (0, 0, 0, 0))
    sp, qp, op = sheet.load(), q.load(), out.load()
    for y in range(sheet.size[1]):
        for x in range(sheet.size[0]):
            if sp[x, y][3] >= ALPHA_CUTOFF:
                r, g, b = qp[x, y]
                op[x, y] = (r, g, b, 255)
    return out


def write_preview(sheet, n_rows, path, zoom=3, ms=180):
    """An animated strip -- frame i of every direction, side by side -- so the cycle can be
    judged as motion. Reviewing a walk as 16 stills hides exactly the faults that matter
    (drift, a dead frame, feet that skate)."""
    frames = []
    for ci in range(4):
        strip = Image.new("RGBA", (CELL_W * n_rows, CELL_H), (72, 82, 96, 255))
        for ri in range(n_rows):
            cell = sheet.crop((ci * CELL_W, ri * CELL_H, (ci + 1) * CELL_W, (ri + 1) * CELL_H))
            strip.alpha_composite(cell, (ri * CELL_W, 0))
        frames.append(strip.resize((strip.size[0] * zoom, strip.size[1] * zoom), Image.NEAREST)
                      .convert("P", palette=Image.ADAPTIVE))
    frames[0].save(path, save_all=True, append_images=frames[1:], duration=ms, loop=0)


def main():
    img = Image.open(SRC).convert("RGB")
    w, h = img.size
    bg = load_mask(img)
    comps = components(bg, w, h)
    sprites = comps[:16]
    dropped = comps[16:]
    print(f"components kept {len(sprites)}, dropped {len(dropped)} "
          f"(areas {[c[0] for c in dropped][:6]})")
    rows = group_rows(sprites)

    if "--report" in sys.argv:
        report(rows)
        return

    tall = max(y1 - y0 + 1 for row in rows for _, _, y0, _, y1, _ in row)
    scale = (CELL_H - FLOOR_MARGIN - 1) / tall

    frames_dir = os.path.join(OUT_DIR, "frames")
    os.makedirs(frames_dir, exist_ok=True)
    sheet = Image.new("RGBA", (CELL_W * 4, CELL_H * len(rows)), (0, 0, 0, 0))

    for ri, row in enumerate(rows):
        baseline = max(c[4] for c in row)  # lowest foot in this row, in source px
        for ci, comp in enumerate(row):
            small = downscale(cutout(img, bg, comp), scale)
            # bob: how far this frame's feet sit above the row's lowest, at target scale
            lift = round((baseline - comp[4]) * scale)
            cx = torso_centre(small)
            ox = round(CELL_W / 2 - cx)
            oy = CELL_H - FLOOR_MARGIN - small.size[1] - lift
            cell = Image.new("RGBA", (CELL_W, CELL_H), (0, 0, 0, 0))
            cell.alpha_composite(small, (max(0, ox), max(0, oy)))
            sheet.alpha_composite(cell, (ci * CELL_W, ri * CELL_H))

    sheet = quantise(sheet)

    for ri in range(len(rows)):
        name = ROW_NAMES[ri] if ri < len(ROW_NAMES) else f"row{ri}"
        for ci in range(4):
            frame = sheet.crop((ci * CELL_W, ri * CELL_H, (ci + 1) * CELL_W, (ri + 1) * CELL_H))
            frame.save(os.path.join(frames_dir, f"artificer_walk_{name}_{ci}.png"))

    sheet_path = os.path.join(OUT_DIR, f"artificer_walk_sheet_{CELL_W}x{CELL_H}.png")
    sheet.save(sheet_path)
    preview_path = os.path.join(OUT_DIR, "artificer_walk_preview.gif")
    write_preview(sheet, len(rows), preview_path)
    print(f"wrote {sheet_path} ({sheet.size[0]}x{sheet.size[1]}, cell {CELL_W}x{CELL_H}), "
          f"{4*len(rows)} frames in {frames_dir}, preview {preview_path}")


if __name__ == "__main__":
    main()
