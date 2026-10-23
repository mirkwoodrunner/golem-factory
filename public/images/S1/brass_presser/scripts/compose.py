"""Every frame of the Brass Presser from its facing's cleaned base (art-style-bible.md Â§5.1:
texture stable across frames).

    python compose.py <drafts dir> <clean dir> <out dir>

<clean dir> holds clean_<facing>.png, each facing's ComfyUI base after palette_snap.py (8x
reduced, keyed, outlined; on the draft's own 64 x 96 grid). For every frame of that facing:

1. Each pixel of the frame's draft names its part (presser_draft.py's part map). A rigid part
   (legs, body, head, ram, pack) that moved by an offset takes the cleaned base's pixel from
   where it was, if the base had that part there and the draft didn't change it (an eye that
   brightens, a fire that dims, a part the base hid): otherwise the draft's own pixel.
2. Arms and hands change shape between frames, so they are the draft's in every frame, the base
   included: the same arm never has two textures.
3. What is taken from the base is its texture, not its colour (detail_map): the draft's tone at
   that place, stepped one up or down where the render is lighter or darker than its
   neighbourhood. A part stays one material with the draft's lighting (reshade_mirror.py needs one
   ramp per part, check_art.py the light from the upper left) and gains the render's seams and
   scuffs, identical in every frame.
4. The silhouette is outlined (outline ramp), and an outline pixel the move left inside the
   body takes the draft's colour there.
"""
import json
import os
import sys

from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.normpath(os.path.join(HERE, "..", "..", "..", "..", "..", "Tools", "Art")))
import art_metrics as m  # noqa: E402

STEM = "chassis_brass_presser"
STATES = ["base", "idle_1", "work_0", "work_1", "work_2", "stall", "carry"]
RIGID = {"legs", "far_leg", "body", "head", "ram", "pack"}

# Which parts are in front of which, per facing, back to front (art-style-bible.md §5.1). What a
# golem holds or works with (its tool, the hands and forearms that hold it) is in FRONT of the
# golem: over the body seen from the south, under it seen from the north (the golem faces away,
# so its body hides what it holds; only what sticks out beside or above it shows), and over the
# body on the facing side of a profile. The upper arms hang from the shoulders, so they stay on
# top from behind. The far limbs of a profile are behind the body. A chassis draws its parts
# under these names, in any order: the draft sorts them by this list.
LAYERS = {
    "south": ["legs", "body", "head", "arms", "ram", "forearms", "hands"],
    "north": ["ram", "hands", "forearms", "legs", "body", "head", "arms"],
    "east": ["far_arm", "far_leg", "body", "pack", "legs", "head", "arms", "ram", "forearms", "hands"],
    "west": ["far_arm", "far_leg", "body", "pack", "legs", "head", "arms", "ram", "forearms", "hands"],
}


def layer_rank(facing, part):
    """Where a part is drawn in a facing, back to front; a part the list doesn't name is drawn
    in the order it was given, after the named ones (and fails nothing)."""
    order = LAYERS[facing]
    return order.index(part) if part in order else len(order)
P = m.Palette.load()
OUTLINE = P.outline()


def name(state, facing):
    return STEM + ".png" if (state, facing) == ("base", "south") else f"{STEM}_{state}_{facing}.png"


def load_parts(path):
    data = json.load(open(path))
    parts = {tuple(int(v) for v in k.split(",")): p for k, p in data["parts"].items()}
    return parts, data["offsets"]


TEXTURED = {"brass", "iron", "copper"}
DETAIL_STEP = 0.035


def detail_map(clean, base_parts, radius=2):
    """The render's texture, as a tone step per pixel: +1 where the cleaned render is lighter
    than its part's neighbourhood by DETAIL_STEP in OKLab, -1 where darker, else 0. The draft
    keeps the shading and the light direction; the render adds only its high-pass detail (seams,
    scuffs, rivet glints), so the result can't drift off the light check."""
    cp = clean.load()
    w, h = clean.size
    light = {}
    for y in range(h):
        for x in range(w):
            if cp[x, y][3] >= 128 and tuple(cp[x, y][:3]) not in OUTLINE:
                light[(x, y)] = m.oklab(cp[x, y])[0]
    out = {}
    for (x, y), lv in light.items():
        part = base_parts.get((x, y))
        # A part's border keeps the draft's tone: there the render's own shading (a shadow
        # side, a rim light) passes the high-pass, and a mirrored texture would carry it to the
        # lit side.
        if any(base_parts.get((x + dx, y + dy)) != part for dx in (-1, 0, 1) for dy in (-1, 0, 1)):
            continue
        near = [light[(nx, ny)] for ny in range(y - radius, y + radius + 1) for nx in range(x - radius, x + radius + 1)
                if (nx, ny) in light and base_parts.get((nx, ny)) == part]
        mean = sum(near) / len(near)
        if lv - mean > DETAIL_STEP:
            out[(x, y)] = 1
        elif mean - lv > DETAIL_STEP:
            out[(x, y)] = -1
    return out


def compose(draft_dir, clean, facing, state):
    draft = m.load(os.path.join(draft_dir, f"draft_{facing}_{state}.png"))
    base_draft = m.load(os.path.join(draft_dir, f"draft_{facing}_base.png"))
    parts, offsets = load_parts(os.path.join(draft_dir, f"parts_{facing}_{state}.json"))
    base_parts, _ = load_parts(os.path.join(draft_dir, f"parts_{facing}_base.json"))
    dp, bp, cp = draft.load(), base_draft.load(), clean.load()
    detail = detail_map(clean, base_parts)
    w, h = draft.size
    out = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    op = out.load()
    taken = 0
    for (x, y), part in parts.items():
        d = dp[x, y]
        if d[3] < 128:
            continue
        colour = d
        off = offsets.get(part)
        if part in RIGID and off is not None:
            sx, sy = x - off[0], y - off[1]
            if (0 <= sx < w and 0 <= sy < h and base_parts.get((sx, sy)) == part and cp[sx, sy][3] >= 128
                    and bp[sx, sy][:3] == d[:3] and tuple(d[:3]) not in OUTLINE):
                ramp = P.classify(d[:3])
                step = detail.get((sx, sy), 0)
                if ramp and ramp[0] in TEXTURED and step:
                    tones = P.ramps[ramp[0]]
                    colour = tones[max(0, min(len(tones) - 1, ramp[1] + step))] + (255,)
                    taken += 1
        op[x, y] = colour
    # The silhouette in the outline ramp; an outline pixel left inside takes the draft's colour.
    final = out.copy()
    fp = final.load()
    for y in range(h):
        for x in range(w):
            if op[x, y][3] == 0:
                continue
            edge = any(not (0 <= nx < w and 0 <= ny < h) or op[nx, ny][3] == 0
                       for nx, ny in ((x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)))
            if edge:
                if tuple(op[x, y][:3]) not in OUTLINE:
                    fp[x, y] = dp[x, y] if tuple(dp[x, y][:3]) in OUTLINE else P.ramps["outline"][0] + (255,)
            elif tuple(op[x, y][:3]) in OUTLINE and tuple(dp[x, y][:3]) not in OUTLINE:
                fp[x, y] = dp[x, y]
    # Every frame stands on the bottom row (check_art.py check 13: one foot row for all 28):
    # a carried golem with its legs tucked is moved down to it.
    low = m.lowest_opaque_row(final)
    if low is not None and low < h - 1:
        moved = Image.new("RGBA", (w, h), (0, 0, 0, 0))
        moved.paste(final, (0, h - 1 - low))
        final = moved
    return final, taken


def main(draft_dir, clean_dir, out_dir):
    os.makedirs(out_dir, exist_ok=True)
    for facing in ("south", "north", "east", "west"):
        # West's texture is east's, mirrored with it (presser_draft.py draws west as east mirrored).
        source = "east" if facing == "west" else facing
        clean = m.load(os.path.join(clean_dir, f"clean_{source}.png"))
        if facing == "west":
            clean = clean.transpose(Image.FLIP_LEFT_RIGHT)
        for state in STATES:
            image, taken = compose(draft_dir, clean, facing, state)
            image.save(os.path.join(out_dir, name(state, facing)))
            print(f"{name(state, facing)}: {taken} pixels from the cleaned base")


if __name__ == "__main__":
    main(*sys.argv[1:4])
