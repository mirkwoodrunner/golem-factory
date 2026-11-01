"""A set of environment sprites (floor, walls, props) redrawn in ComfyUI from today's art
(docs/art-pipeline-design.md §2, the S2 row of §4).

    python Tools/Art/env_set.py <set dir> source            # 8x inits from today's art, seam masks
    python Tools/Art/env_set.py <set dir> run [--only a,b]  # the render: THE USER RUNS THIS (§1.4)
    python Tools/Art/env_set.py <set dir> pick              # clean every candidate, one sheet a file
    python Tools/Art/env_set.py <set dir> review            # today's room beside the picks' room
    python Tools/Art/env_set.py <set dir> install           # only once set.json says "approved"

`<set dir>/set.json` names each file, its kind, prompt and ramps, and may name a `reference`
(a file in godot/art/) whose style IP-Adapter carries into the redraw, for the set or per file;
`review: "row"` lays the set's sprites out in a row on the floor instead of the room corner.
`picks` records the chosen
candidate per file (by its number on the file's sheet; 0 keeps today's file, which `install`
then leaves alone) and `approved` the user's pass. Working
images stay in the set dir (gitignored); only `install` writes into godot/art/.

Kinds:
- `tile`: seamless on all four sides. Each candidate gets a second pass: rolled by half, so its
  old edges meet in a cross in the middle, and only that cross redrawn (comfy_redraw --mask).
- `strip`: seamless left to right (a wall run, a kerb): the same, with only the vertical band.
- `sprite`: stands on the floor; rendered on the key colour, which the cleanup clears.

`run` starts ComfyUI if it isn't up and stops it afterwards (golem_set.Comfy). Run it in your
own terminal: from Claude Code the low-memory reaper kills it.
"""
import argparse
import json
import os
import shutil
import sys
import time

from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import art_metrics as m  # noqa: E402
import comfy_redraw  # noqa: E402
import palette_snap  # noqa: E402
from golem_set import ART, KEY, Comfy, SetError  # noqa: E402

SCALE = 8
KINDS = ("tile", "strip", "sprite")
DEFAULTS = {
    "promptPrefix": "pixel art, cozy top-down game tile, steampunk artificer's workshop, ",
    "promptSuffix": ", muted warm browns, soft light from above",
    "negative": "3d render, realistic, photo, blurry, text, watermark, black outline, people",
    "denoise": [0.35, 0.5, 0.65],
    "seeds": 3,
    "seedBase": 515151,
    "seamDenoise": 0.6,
    "seamWidth": 6,          # 1x pixels either side of the seam that the second pass redraws
    "reference": None,       # a godot/art/ name: IP-Adapter carries its style into every redraw
    "ipadapterModel": "ip-adapter-plus_sdxl_vit-h.safetensors",
    "clipVision": "CLIP-ViT-H-14-laion2B-s32B-b79K.safetensors",
    "ipadapterWeight": 0.5,
    "review": "room",
    "picks": {},
    "approved": False,
}
# What a file's own entry in set.json may override; its "negativeExtra" is added to the negative.
PER_FILE = ("denoise", "seeds", "seedBase", "seamDenoise")
PER_FILE_REFERENCE = ("reference", "ipadapterWeight")
# The game's view at 1280x720 and the default zoom: half a viewport is 10 cells of 64 px.
PLAY_SCALE = 360 / 640


class EnvSet:
    def __init__(self, root):
        self.root = os.path.abspath(root)
        with open(os.path.join(self.root, "set.json"), encoding="utf-8") as handle:
            self.config = {**DEFAULTS, **json.load(handle)}
        self.files = self.config["files"]
        for name, spec in self.files.items():
            if spec.get("kind") not in KINDS:
                raise SetError(f"{name}: kind must be one of {KINDS}")

    def __getattr__(self, key):
        try:
            return self.config[key]
        except KeyError:
            raise AttributeError(key)

    def setting(self, name, key):
        """A file's own value for a PER_FILE or PER_FILE_REFERENCE key, else the set's."""
        return self.files[name].get(key, self.config[key])

    def negative_for(self, name):
        extra = self.files[name].get("negativeExtra")
        return self.negative + ", " + extra if extra else self.negative

    def dir(self, *parts):
        path = os.path.join(self.root, *parts)
        os.makedirs(os.path.dirname(path) if os.path.splitext(path)[1] else path, exist_ok=True)
        return path

    def today(self, name):
        return m.load(os.path.join(ART, name + ".png"))

    def candidates(self, name):
        """[(label, raw path)] in sheet order: every denoise over every seed, the seamed one for a tile."""
        out = []
        seam = self.setting(name, "seamDenoise")
        for d, seed in comfy_redraw.candidates(self.setting(name, "denoise"), self.setting(name, "seeds"),
                                               self.setting(name, "seedBase")):
            raw = os.path.join(self.root, "raw", name, f"{name}_d{d:.2f}_s{seed}.png")
            if self.files[name]["kind"] in ("tile", "strip"):
                raw = os.path.join(self.root, "raw", name + "_seam", f"{os.path.basename(raw)[:-4]}_seam_d{seam:.2f}_s{seed}.png")
            out.append((f"d{d:.2f} s{seed}", raw))
        return out

    def save(self):
        """Back to set.json: its own keys and the picks, never the defaults it didn't set."""
        path = os.path.join(self.root, "set.json")
        with open(path, encoding="utf-8") as handle:
            own = set(json.load(handle)) | {"picks", "approved"}
        with open(path, "w", encoding="utf-8", newline="\n") as handle:
            json.dump({k: v for k, v in self.config.items() if k in own}, handle, indent=2)
            handle.write("\n")


# --- source ------------------------------------------------------------------------------------

def init_image(es, name):
    """Today's sprite at 8x, nearest-neighbour; a sprite's transparency filled with the key."""
    image = es.today(name)
    big = image.resize((image.width * SCALE, image.height * SCALE), Image.NEAREST)
    if es.files[name]["kind"] == "sprite":
        ground = Image.new("RGBA", big.size, KEY + (255,))
        ground.alpha_composite(big)
        big = ground
    return big.convert("RGB")


def seam_mask(size, kind, width):
    """White where the second pass redraws: the cross (tile) or the vertical band (strip) through
    the middle of the image rolled by half."""
    w, h = size
    mask = Image.new("RGB", size, (0, 0, 0))
    draw = ImageDraw.Draw(mask)
    draw.rectangle((w // 2 - width, 0, w // 2 + width - 1, h - 1), fill=(255, 255, 255))
    if kind == "tile":
        draw.rectangle((0, h // 2 - width, w - 1, h // 2 + width - 1), fill=(255, 255, 255))
    return mask


def roll(image, kind):
    """The image moved by half its size, wrapping: a tile both ways, a strip only sideways."""
    w, h = image.size
    dx, dy = w // 2, (h // 2 if kind == "tile" else 0)
    out = Image.new(image.mode, image.size)
    for ox in (0, -w):
        for oy in (0, -h):
            out.paste(image, (dx + ox, dy + oy))
    return out


def reference_image(es, name, init_size):
    """The file's reference on the key colour, at the largest whole scale up to 4x that fits
    inside its render (comfy_redraw refuses a larger one); None when it has none."""
    ref_name = es.setting(name, "reference")
    if not ref_name:
        return None
    ref = m.load(os.path.join(ART, ref_name if ref_name.endswith(".png") else ref_name + ".png"))
    scale = max(1, min(4, init_size[0] // ref.width, init_size[1] // ref.height))
    ground = Image.new("RGBA", ref.size, KEY + (255,))
    ground.alpha_composite(ref)
    return ground.resize((ref.width * scale, ref.height * scale), Image.NEAREST).convert("RGB")


def step_source(es, log=print):
    for name, spec in es.files.items():
        init = init_image(es, name)
        init.save(es.dir("source", f"{name}_8x.png"))
        ref = reference_image(es, name, init.size)
        if ref is not None:
            ref.save(es.dir("source", f"{name}_ref.png"))
        if spec["kind"] in ("tile", "strip"):
            size = (es.today(name).width * SCALE, es.today(name).height * SCALE)
            seam_mask(size, spec["kind"], es.seamWidth * SCALE).save(es.dir("source", f"{name}_seam_mask.png"))
    log(f"source: {len(es.files)} inits in {es.dir('source')}")


# --- run ---------------------------------------------------------------------------------------

def redraw(es, name, init, out, denoise, seeds, seed_base, mask=None):
    spec = es.files[name]
    args = ["--init", init, "--out", out, "--prompt", es.promptPrefix + spec["prompt"] + es.promptSuffix,
            "--negative", es.negative_for(name), "--denoise", ",".join(str(d) for d in denoise),
            "--seeds", str(seeds), "--seed-base", str(seed_base)]
    if mask:
        args += ["--mask", mask]
    if es.setting(name, "reference"):
        args += ["--reference", os.path.join(es.root, "source", f"{name}_ref.png"),
                 "--ipadapter-model", es.ipadapterModel, "--clip-vision", es.clipVision,
                 "--ipadapter-weight", str(es.setting(name, "ipadapterWeight"))]
    if comfy_redraw.main(args) != 0:
        raise SetError(f"comfy_redraw failed for {name}")


def step_run(es, only=None, log=print):
    names = [n for n in es.files if not only or n in only]
    if not os.path.isdir(os.path.join(es.root, "source")):
        step_source(es, log)
    started = time.time()
    with Comfy(log):
        for name in names:
            spec = es.files[name]
            raw_dir = es.dir("raw", name)
            denoise, seeds, seed_base, seam = (es.setting(name, k) for k in PER_FILE)
            redraw(es, name, os.path.join(es.root, "source", f"{name}_8x.png"), raw_dir,
                   denoise, seeds, seed_base)
            if spec["kind"] not in ("tile", "strip"):
                continue
            # The second pass, per candidate: roll it, then redraw only the seam.
            mask = os.path.join(es.root, "source", f"{name}_seam_mask.png")
            for d, seed in comfy_redraw.candidates(denoise, seeds, seed_base):
                stem = f"{name}_d{d:.2f}_s{seed}"
                rolled = es.dir("raw", name + "_seam", "rolled", stem + ".png")
                roll(Image.open(os.path.join(raw_dir, stem + ".png")).convert("RGB"), spec["kind"]).save(rolled)
                out = es.dir("raw", name + "_seam", stem)
                redraw(es, name, rolled, out, [seam], 1, seed, mask)
                made = os.path.join(out, f"{stem}_d{seam:.2f}_s{seed}.png")
                shutil.move(made, os.path.join(es.root, "raw", name + "_seam", f"{stem}_seam_d{seam:.2f}_s{seed}.png"))
                shutil.rmtree(out, ignore_errors=True)
            log(f"run: {name} done")
    log(f"run: {len(names)} files in {(time.time() - started) / 60:.1f} min")


# --- pick --------------------------------------------------------------------------------------

def clean(es, name, raw_path):
    """A candidate at 1x on the set's ramps; (image, the share of pixels the snap moved far)."""
    spec = es.files[name]
    sprite = spec["kind"] == "sprite"
    image, counts = palette_snap.run(m.load(raw_path), m.Palette.load(), spec["ramps"], reduce_by=SCALE,
                                     key=KEY if sprite else None, key_holes=256 if sprite else 0,
                                     outline=sprite, max_change=1.0, log=lambda *_: None)
    opaque = sum(1 for p in image.getdata() if p[3] >= 128) or 1
    return image, counts.get("snap far", 0) / opaque


def tiled(image, kind, z):
    """How a tile or strip meets itself in the game: 3x3 tiles, or a strip four wide."""
    nx, ny = (3, 3) if kind == "tile" else (4, 1) if kind == "strip" else (1, 1)
    out = Image.new("RGBA", (image.width * nx, image.height * ny))
    for i in range(nx):
        for j in range(ny):
            out.alpha_composite(image, (i * image.width, j * image.height))
    return out.resize((out.width * z, out.height * z), Image.NEAREST)


def on_floor(image, z):
    floor = m.load(os.path.join(ART, "floor_tile.png"))
    w, h = image.width + 16, image.height + 8
    ground = Image.new("RGBA", (w, h))
    for x in range(0, w, floor.width):
        for y in range(0, h, floor.height):
            ground.alpha_composite(floor, (x, y))
    ground.alpha_composite(image, (8, h - image.height - 4))
    return ground.resize((w * z, h * z), Image.NEAREST)


def sheet(es, name, entries, z):
    """Today's sprite, then each candidate numbered, in the game's arrangement."""
    kind = es.files[name]["kind"]
    view = (lambda im: tiled(im, kind, z)) if kind != "sprite" else (lambda im: on_floor(im, z))
    tiles = [("today", view(es.today(name)))] + [(label, view(im)) for label, im in entries]
    tw = max(t.width for _, t in tiles)
    th = max(t.height for _, t in tiles)
    cols = 4
    rows = (len(tiles) + cols - 1) // cols
    out = Image.new("RGBA", (cols * (tw + 12), rows * (th + 26)), (52, 48, 56, 255))
    draw = ImageDraw.Draw(out)
    for i, (label, t) in enumerate(tiles):
        x, y = (i % cols) * (tw + 12), (i // cols) * (th + 26)
        out.alpha_composite(t, (x, y + 20))
        draw.text((x + 2, y + 4), label, fill=(255, 230, 120, 255))
    return out


def step_pick(es, log=print):
    for name in es.files:
        entries, scored = [], []
        for i, (label, raw) in enumerate(es.candidates(name), start=1):
            if not os.path.exists(raw):
                continue
            image, far = clean(es, name, raw)
            image.save(es.dir("clean", name, f"{i:02d}.png"))
            entries.append((f"#{i} {label} far {far:.0%}", image))
            scored.append((far, i))
        if not entries:
            log(f"pick: {name}: no candidates yet (run first)")
            continue
        z = 4 if es.files[name]["kind"] != "tile" else 2
        sheet(es, name, entries, z).save(es.dir(f"candidates_{name}.png"))
        auto = min(scored)[1]
        es.picks.setdefault(name, auto)
        log(f"pick: {name}: {len(entries)} candidates, palette fit best #{auto}, picked #{es.picks[name]}")
    es.save()


# --- review ------------------------------------------------------------------------------------

def picked(es, name):
    number = es.picks.get(name)
    path = os.path.join(es.root, "clean", name, f"{number:02d}.png") if number else None
    return m.load(path) if path and os.path.exists(path) else es.today(name)


def room(get, width=8, depth=5):
    """A corner of the workshop from the set's files: the north wall over the floor, props on it."""
    wall, floor = get("wall_segment_nw"), get("floor_tile")
    out = Image.new("RGBA", (width * 64, wall.height + depth * 64), (30, 24, 20, 255))
    for x in range(width):
        for y in range(depth):
            out.alpha_composite(floor, (x * 64, wall.height - 32 + y * 64))
        out.alpha_composite(wall, (x * 64, 0))
    props = [n for n in ("prop_crate", "prop_barrel", "prop_shelf", "prop_hearth", "prop_workbench", "prop_tool_rack")]
    for i, name in enumerate(props):
        sprite = get(name)
        x = 40 + i * 76
        out.alpha_composite(sprite, (x, wall.height + 40 - sprite.height + (i % 2) * 70))
    return out


def row(get, names):
    """The set's sprites side by side on the floor, feet on one line."""
    floor = get("floor_tile")
    sprites = [get(n) for n in names]
    height = max(s.height for s in sprites) + 24
    width = sum(s.width + 16 for s in sprites) + 16
    out = Image.new("RGBA", (width, height))
    for x in range(0, width, floor.width):
        for y in range(0, height, floor.height):
            out.alpha_composite(floor, (x, y))
    x = 16
    for s in sprites:
        out.alpha_composite(s, (x, height - 12 - s.height))
        x += s.width + 16
    return out


def at_play_size(image):
    """As the game draws it at 1280x720 and the default zoom (nearest, like the renderer)."""
    return image.resize((max(1, round(image.width * PLAY_SCALE)), max(1, round(image.height * PLAY_SCALE))),
                        Image.NEAREST)


def step_review(es, log=print):
    if es.review == "row":
        names = [n for n, spec in es.files.items() if spec["kind"] == "sprite"]
        before, after = row(es.today, names), row(lambda n: picked(es, n) if n in es.files else es.today(n), names)
    else:
        before = room(es.today)
        after = room(lambda n: picked(es, n) if n in es.files else es.today(n))
    z = 2
    small_b, small_a = at_play_size(before), at_play_size(after)
    out = Image.new("RGBA", (before.width * z * 2 + 24, (before.height + small_b.height) * z + 66), (52, 48, 56, 255))
    draw = ImageDraw.Draw(out)
    out.alpha_composite(before.resize((before.width * z, before.height * z), Image.NEAREST), (0, 30))
    out.alpha_composite(after.resize((after.width * z, after.height * z), Image.NEAREST), (before.width * z + 24, 30))
    draw.text((4, 8), "today", fill=(255, 230, 120, 255))
    draw.text((before.width * z + 28, 8), "picks: " + ", ".join(f"{n} #{es.picks.get(n)}" for n in es.files),
              fill=(255, 230, 120, 255))
    y = before.height * z + 60
    draw.text((4, y - 22), "at play size (1280x720, default zoom), shown 2x", fill=(255, 230, 120, 255))
    out.alpha_composite(small_b.resize((small_b.width * z, small_b.height * z), Image.NEAREST), (0, y))
    out.alpha_composite(small_a.resize((small_a.width * z, small_a.height * z), Image.NEAREST), (before.width * z + 24, y))
    path = es.dir("review.png")
    out.save(path)
    log(f"review: {path}")


# --- install -----------------------------------------------------------------------------------

def step_install(es, log=print):
    if not es.approved:
        raise SetError("set.json isn't approved: the user passes the review first")
    for name in es.files:
        if name not in es.picks:
            raise SetError(f"{name} has no pick")
        if es.picks[name] == 0:
            log(f"install: {name} kept as it is")
            continue
        image = picked(es, name)
        if image.size != es.today(name).size:
            raise SetError(f"{name}: {image.size}, today's is {es.today(name).size}")
        image.save(os.path.join(ART, name + ".png"))
        log(f"install: {name}")


def main(argv):
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("set")
    parser.add_argument("step", choices=("source", "run", "pick", "review", "install"))
    parser.add_argument("--only", help="comma-separated file names (run)")
    args = parser.parse_args(argv)
    try:
        es = EnvSet(args.set)
        if args.step == "source":
            step_source(es)
        elif args.step == "run":
            step_run(es, set(args.only.split(",")) if args.only else None)
        elif args.step == "pick":
            step_pick(es)
        elif args.step == "review":
            step_review(es)
        else:
            step_install(es)
    except SetError as error:
        print(f"env_set: {error}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
