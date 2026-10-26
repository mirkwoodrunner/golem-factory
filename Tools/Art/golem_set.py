"""One golem chassis' 28 frames, from its draft script to a review grid, in one command
(docs/art-pipeline-design.md §3.2, §5.10). The Brass Presser's way, for any chassis.

    python Tools/Art/golem_set.py <set dir> run        # draft, render, pick, compose, check, review
    python Tools/Art/golem_set.py <set dir> <step>     # one step: draft render pick compose check review
    python Tools/Art/golem_set.py <set dir> install    # the approved frames into godot/art/ (the commit step)
    python Tools/Art/golem_set.py <set dir> verify     # recompose from the kept renders; must equal godot/art/

<set dir> is public/images/S1/<chassis>/ and holds chassis.json (what is the chassis' own: its
stem, review title, draft script, ramps, parts, prompts, render settings, picks, approval). The
Artificer's set (public/images/S1b/artificer/) is made the same way: its chassis.json has
"kind": "artificer" and names its own canvas ("size"), frames ("states", the first is the base),
drawn facings ("drawn"), file names ("frameName", "facingNames") and install scenarios; a golem
set leaves them out. The steps:

1. draft    the chassis' draft script writes draft_<facing>_<state>.png, its _8x on the key,
            and parts_<facing>_<state>.json, into <source>/drafts/.
2. render   each drawn facing's base (south, north, east) through ComfyUI: img2img of the 8x
            draft, the draft's lineart as the canny guide, IP-Adapter on the reference cart.
            Starts ComfyUI if it isn't up and stops it afterwards if it started it. A facing
            whose candidates are all on disk is skipped (--force renders again).
3. pick     cleans every candidate (palette_snap: key, 8x k-centroid, the ramps, outline),
            composes its base frame, scores it by check_art's measures and picks the one with
            the most texture of those that pass; chassis.json's "picks" overrides. Writes
            <clean>/clean_<f>.png, candidates_<f>.png (every candidate's base, best first: among
            passing ones the pick is taste, so look) and picks.json (every candidate's scores).
4. compose  every frame from its facing's cleaned base into <after>/ (west: east mirrored).
5. check    check_art.py over a scratch copy of godot/art/ with the frames in place, as if
            the set were replaced. Exit 1 on a FAIL.
6. review   grid.png (4x, numbered #1-28 as review.json numbers them) and review_sheet.py's
            pages; --game adds the frames as seen in the game (a scratch copy of the project
            runs the art scenario; slow).

install refuses until chassis.json has "approved" (the date the user approved the grid). It
copies the 28 frames into godot/art/, writes their manifest entries, imports them (a headless
Godot it started and stops itself; never the user's editor) and runs the art, golems and
golem-motion scenarios. It commits nothing.

Only install writes into godot/art/. Standard library and Pillow only, like the other tools.
"""
import argparse
import copy
import glob
import json
import os
import shutil
import subprocess
import sys
import tempfile
import time
import urllib.error
import urllib.request
from itertools import combinations

from PIL import Image, ImageDraw, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import art_metrics as m  # noqa: E402
import check_art  # noqa: E402
import comfy_redraw  # noqa: E402
import palette_snap  # noqa: E402
import pose_guide  # noqa: E402
import review_sheet  # noqa: E402
from art_paths import writes_into_godot_art  # noqa: E402

REPO = os.path.normpath(os.path.join(HERE, "..", ".."))
GODOT = os.path.join(REPO, "godot")
ART = os.path.join(GODOT, "art")
STATES = list(check_art.FRAME_STATES)
FACINGS = ["south", "north", "east", "west"]   # review order: rows of the grid, #1-28
DRAWN = ["south", "north", "east"]             # west is east mirrored
W, H = check_art.CHASSIS_MAX
KEY = (40, 190, 70)
KEY_HEX = "#28be46"

COMFY_URL = comfy_redraw.URL
COMFY_ROOT = "G:/AI/ComfyUI/ComfyUI_windows_portable"
COMFY_ARGS = ["-s", "ComfyUI/main.py", "--windows-standalone-build", "--disable-auto-launch",
              "--disable-dynamic-vram", "--fp8_e4m3fn-unet", "--fp8_e4m3fn-text-enc"]  # G:/AI/start_comfyui.bat's
GODOT_EXE = ("G:/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64/"
             "Godot_v4.7.2-stable_mono_win64_console.exe")

# Which parts are in front of which, per facing, back to front (art-style-bible.md §5.1). What a
# golem holds or works with (its tool, the hands and forearms that hold it) is in FRONT of the
# golem: over the body seen from the south, under it seen from the north (the golem faces away,
# so its body hides what it holds; only what sticks out beside or above it shows), and over the
# body on the facing side of a profile. The upper arms hang from the shoulders, so they stay on
# top from behind. The far limbs of a profile are behind the body. A chassis' draft names its
# parts from this list ("ram" is the held tool, whatever it is), or extends it in chassis.json.
LAYERS = {
    "south": ["legs", "body", "head", "arms", "ram", "forearms", "hands"],
    "north": ["ram", "hands", "forearms", "legs", "body", "head", "arms"],
    "east": ["far_arm", "far_leg", "body", "pack", "legs", "head", "arms", "ram", "forearms", "hands"],
    "west": ["far_arm", "far_leg", "body", "pack", "legs", "head", "arms", "ram", "forearms", "hands"],
}

DEFAULTS = {
    "dirs": {"source": "source", "raw": "raw", "work": "work", "clean": "clean", "after": "after"},
    "rigid": ["legs", "far_leg", "body", "head", "ram", "pack"],
    "textured": ["brass", "iron", "copper"],
    "reference": "stall_scrap.png",
    "promptPrefix": "pixel art, cozy game sprite, ",
    "promptSuffix": (", warm brown outlines, lit from the upper left, bright highlights on the left side, "
                     "shadows on the right side, detailed, flat green background"),
    "negative": ("3d render, realistic, photo, blurry, text, watermark, black outline, purple, red, blue, "
                 "noise, grime, human, knight"),
    "denoise": [0.35, 0.45, 0.55],
    "seeds": 3,
    "seedBase": 5000,
    "controlModel": "control-lora-canny-rank128.safetensors",
    "controlStrength": 0.7,
    "controlEnd": 0.8,
    "ipadapterModel": "ip-adapter-plus_sdxl_vit-h.safetensors",
    "clipVision": "CLIP-ViT-H-14-laion2B-s32B-b79K.safetensors",
    "ipadapterWeight": 0.5,
    "picks": {},
}
REQUIRED = ("stem", "title", "draft", "ramps", "prompts")
KINDS = ("chassis", "artificer")
DETAIL_STEP = 0.035


class SetError(Exception):
    pass


# --- the set -----------------------------------------------------------------------------------

class GolemSet:
    def __init__(self, root, config):
        self.root = os.path.abspath(root)
        missing = [k for k in REQUIRED if k not in config]
        if missing:
            raise SetError(f"chassis.json lacks {', '.join(missing)}")
        self.kind = config.get("kind", "chassis")
        if self.kind not in KINDS:
            raise SetError(f"chassis.json's kind is {self.kind}, not one of {', '.join(KINDS)}")
        # A golem's canvas, frames and facings are check_art's; the Artificer's set names its own.
        self.size = tuple(config.get("size", (W, H)))
        self.states = list(config.get("states", STATES))
        self.base = self.states[0]
        self.drawn = [f for f in DRAWN if f in config.get("drawn", DRAWN)]
        self.facings = [f for f in FACINGS if f in self.drawn or (f == "west" and "east" in self.drawn)]
        absent = [f for f in self.drawn if f not in config["prompts"]]
        if absent:
            raise SetError(f"chassis.json's prompts lack {', '.join(absent)}")
        self.config = {**copy.deepcopy(DEFAULTS), **config}
        self.config["dirs"] = {**DEFAULTS["dirs"], **config.get("dirs", {})}
        if writes_into_godot_art(self.root):
            raise SetError("a set's folder is public/images/<set>/<chassis>/, never under godot/art/")
        layers = copy.deepcopy(LAYERS)
        for facing, order in config.get("layers", {}).items():
            layers[facing] = list(order)
        self.layers = layers

    @classmethod
    def load(cls, root):
        path = os.path.join(root, "chassis.json")
        if not os.path.exists(path):
            raise SetError(f"no chassis.json in {root}")
        with open(path, encoding="utf-8") as handle:
            return cls(root, json.load(handle))

    def __getattr__(self, key):
        try:
            return self.__dict__["config"][key]
        except KeyError:
            raise AttributeError(key) from None

    def dir(self, kind, *parts):
        return os.path.join(self.root, self.config["dirs"][kind], *parts)

    @property
    def drafts(self):
        return self.dir("source", "drafts")

    def name(self, state, facing):
        template = self.config.get("frameName")
        if template is None:
            return check_art.frame_name(self.stem, state, facing)
        # e.g. "artificer_walk_{facing}_{index}" with facingNames {"east": "right", ...}
        return template.format(stem=self.stem, state=state, index=self.states.index(state),
                               facing=self.config.get("facingNames", {}).get(facing, facing)) + ".png"

    def frames(self):
        """Every frame in review order: south, north, east, west; then the states in order
        (a golem's 28: base, idle_1, ... carry)."""
        return [self.name(s, f) for f in self.facings for s in self.states]

    def candidates(self, facing):
        frame = f"base_{facing}"
        plan = comfy_redraw.candidates([float(d) for d in self.denoise], int(self.seeds), int(self.seedBase))
        return [(f"d{d:.2f}_s{seed}", self.dir("raw", frame, f"{frame}_d{d:.2f}_s{seed}.png")) for d, seed in plan]


def layer_rank(facing, part, layers=None):
    """Where a part is drawn in a facing, back to front; a part the list doesn't name is drawn
    in the order it was given, after the named ones (and fails nothing)."""
    order = (layers or LAYERS)[facing]
    return order.index(part) if part in order else len(order)


# --- 1. draft ----------------------------------------------------------------------------------

def step_draft(gs, log=print):
    script = os.path.join(gs.root, gs.draft)
    os.makedirs(gs.drafts, exist_ok=True)
    result = subprocess.run([sys.executable, script, gs.drafts], capture_output=True, text=True)
    if result.returncode != 0:
        raise SetError(f"{gs.draft} failed:\n{result.stderr[-2000:]}")
    missing = [f"draft_{f}_{s}" for f in gs.facings for s in gs.states
               if not os.path.exists(os.path.join(gs.drafts, f"draft_{f}_{s}.png"))
               or not os.path.exists(os.path.join(gs.drafts, f"parts_{f}_{s}.json"))]
    if missing:
        raise SetError(f"{gs.draft} didn't write {', '.join(missing[:4])}{' ...' if len(missing) > 4 else ''}")
    log(f"draft: {len(gs.frames())} frames in {gs.drafts}")
    if gs.kind == "chassis":
        check_draft_silhouette(gs, log)


SILHOUETTE_MARGIN = 0.02


def check_draft_silhouette(gs, log=print):
    """The pick step holds the south base to SILHOUETTE_MAX_IOU against every other chassis'
    sprite, today's included, where check_art only notes an overlap with one not yet replaced. So
    measure the draft here, before a render: over the bound stops the run; within the margin
    under it warns, since a render can move the outline a pixel or two (the Scavenger's wider
    bucket, 0.52 against the Hauler, cost a render)."""
    draft = m.foot_mask(m.load(os.path.join(gs.drafts, "draft_south_base.png")))
    stems = check_art.Context.from_repo().chassis_stems
    overlaps = {s: m.iou(draft, m.foot_mask(m.load(os.path.join(ART, s + ".png")))) for s in stems if s != gs.stem}
    if not overlaps:
        return
    worst = max(overlaps, key=overlaps.get)
    bound = check_art.SILHOUETTE_MAX_IOU
    if overlaps[worst] > bound:
        raise SetError(f"the south base draft's silhouette overlaps {worst} at {overlaps[worst]:.2f} "
                       f"(at most {bound:.2f}): change the draft before rendering")
    if overlaps[worst] > bound - SILHOUETTE_MARGIN:
        log(f"draft: WARN south silhouette {overlaps[worst]:.2f} against {worst}, within {SILHOUETTE_MARGIN} "
            f"of {bound:.2f}: a render may push it over")


# --- 2. render ---------------------------------------------------------------------------------

def keyed(image, size, scale):
    """The image on the flat key colour at size, enlarged by nearest-neighbour: ComfyUI has no alpha."""
    canvas = Image.new("RGBA", size, KEY + (255,))
    canvas.alpha_composite(image, (0, size[1] - image.height))
    return canvas.resize((size[0] * scale, size[1] * scale), Image.NEAREST).convert("RGB")


def prepare_render(gs):
    """The guides (each base draft's lineart, 8x) and the IP-Adapter reference (the cart, 4x)."""
    source = gs.dir("source")
    poses = os.path.join(source, "identity_poses.json")
    os.makedirs(source, exist_ok=True)
    with open(poses, "w", encoding="utf-8", newline="\n") as handle:
        json.dump({"base": {"parts": []}}, handle)
    for facing in gs.drawn:
        out = os.path.join(source, f"guide_{facing}")
        if pose_guide.main([os.path.join(gs.drafts, f"draft_{facing}_{gs.base}.png"), "--poses", poses,
                            "--frame", "base", "--out", out]) != 0:
            raise SetError(f"pose_guide failed for {facing}")
    cart = m.load(os.path.join(ART, gs.reference))
    keyed(cart, cart.size, 4).save(os.path.join(source, "ref_cart_4x.png"))


def comfy_up(url=COMFY_URL):
    try:
        urllib.request.urlopen(url + "/system_stats", timeout=3).read()
        return True
    except (urllib.error.URLError, OSError):
        return False


class Comfy:
    """ComfyUI for the length of a with-block: started if it isn't up, stopped afterwards only if
    this started it (the user's own server is left alone)."""

    def __init__(self, log=print, wait=900):
        self.log, self.wait, self.proc = log, wait, None

    def __enter__(self):
        if comfy_up():
            self.log("render: ComfyUI already up; leaving it running afterwards")
            return self
        python = os.path.join(COMFY_ROOT, "python_embeded", "python.exe")
        if not os.path.exists(python):
            raise SetError(f"no ComfyUI at {COMFY_ROOT}")
        self.logfile = open(os.path.join(tempfile.gettempdir(), "golem_set_comfyui.log"), "w")
        self.proc = subprocess.Popen([python] + COMFY_ARGS, cwd=COMFY_ROOT, stdout=self.logfile,
                                     stderr=subprocess.STDOUT)
        self.log(f"render: started ComfyUI (pid {self.proc.pid}); log in {self.logfile.name}")
        deadline = time.time() + self.wait
        while not comfy_up():
            if self.proc.poll() is not None:
                raise SetError(f"ComfyUI exited ({self.proc.returncode}); see {self.logfile.name}")
            if time.time() > deadline:
                self.__exit__(None, None, None)
                raise SetError("ComfyUI didn't come up in time")
            time.sleep(5)
        return self

    def __exit__(self, *_):
        if self.proc is None:
            return False
        subprocess.run(["taskkill", "/T", "/F", "/PID", str(self.proc.pid)], capture_output=True)
        self.proc.wait(timeout=60)
        self.logfile.close()
        self.log("render: stopped ComfyUI")
        self.proc = None
        return False


def render_args(gs, facing):
    source = gs.dir("source")
    prompt = gs.promptPrefix + gs.prompts[facing] + gs.promptSuffix
    return ["--init", os.path.join(gs.drafts, f"draft_{facing}_{gs.base}_8x.png"),
            "--guide", os.path.join(source, f"guide_{facing}", "guide.png"),
            "--out", gs.dir("raw", f"base_{facing}"),
            "--prompt", prompt, "--negative", gs.negative,
            "--denoise", ",".join(str(d) for d in gs.denoise), "--seeds", str(gs.seeds),
            "--seed-base", str(gs.seedBase),
            "--control-model", gs.controlModel, "--control-strength", str(gs.controlStrength),
            "--control-end", str(gs.controlEnd),
            "--reference", os.path.join(source, "ref_cart_4x.png"),
            "--ipadapter-model", gs.ipadapterModel, "--clip-vision", gs.clipVision,
            "--ipadapter-weight", str(gs.ipadapterWeight)]


def step_render(gs, force=False, dry_run=False, log=print):
    prepare_render(gs)
    todo = [f for f in gs.drawn if force or not all(os.path.exists(p) for _, p in gs.candidates(f))]
    if not todo:
        log("render: every candidate is on disk (--force to render again)")
        return
    if dry_run:
        for facing in todo:
            comfy_redraw.main(render_args(gs, facing) + ["--dry-run"])
        return
    started = time.time()
    with Comfy(log):
        for facing in todo:
            if comfy_redraw.main(render_args(gs, facing)) != 0:
                raise SetError(f"comfy_redraw failed for {facing}")
    log(f"render: {len(todo)} facings in {(time.time() - started) / 60:.1f} min")


# --- 3. pick -----------------------------------------------------------------------------------

def clean(gs, path):
    image, _ = palette_snap.run(m.load(path), m.Palette.load(), gs.ramps, reduce_by=8, key=KEY, key_holes=256,
                                outline=True, max_change=1.0, log=lambda *_: None)
    return image


def score(gs, facing, cleaned, others, ctx, draft):
    """check_art's measures on the base frame composed from one cleaned candidate. The silhouette,
    the lightness and the mood tints are a golem's (checks 8-10); the Artificer has none of them
    (from behind it is mostly dark hair, as today's frame is)."""
    image, taken = compose_frame(gs, cleaned, facing, gs.base)
    lit = m.mean_lab(image)
    golem = gs.kind == "chassis"
    tints = {k: m.mean_lab(image, ctx.tints[k]) for k in check_art.MOOD_TINTS_CHECKED} if golem else None
    s = {
        "feet": m.lowest_opaque_row(cleaned),
        "silhouette": (max((m.iou(m.foot_mask(image), o) for o in others), default=0.0)
                       if golem and facing == "south" else None),
        "lightness": lit[0],
        "stalledShift": m.lab_distance(lit, tints["Stalled"]) if golem else None,
        "tintsApart": min(m.lab_distance(tints[a], tints[b]) for a, b in combinations(tints, 2)) if golem else None,
        "edgeLight": m.edge_light_score(image, ctx.palette.outline()),
        "texture": sum(1 for x, y, p in m.opaque_pixels(image) if p != draft.getpixel((x, y))),
        "taken": taken,
    }
    s["fails"] = failures(s, gs.size[1], golem)
    return s, image


def failures(s, height=H, golem=True):
    """What keeps a candidate from being picked: check_art's bounds (§5.1), the feet on the
    bottom row, and a render that changed nothing. A measure that is None doesn't apply."""
    out = []
    if s["feet"] != height - 1:
        out.append(f"feet on row {s['feet']}")
    if s["silhouette"] is not None and s["silhouette"] > check_art.SILHOUETTE_MAX_IOU:
        out.append(f"silhouette {s['silhouette']:.2f}")
    if golem and s["lightness"] < check_art.BODY_MIN_LIGHTNESS:
        out.append(f"lightness {s['lightness']:.2f}")
    if s["stalledShift"] is not None and s["stalledShift"] < check_art.STALLED_MIN_SHIFT:
        out.append(f"stalled shift {s['stalledShift']:.3f}")
    if s["tintsApart"] is not None and s["tintsApart"] < check_art.STOPPED_TINTS_MIN_APART:
        out.append(f"tints apart {s['tintsApart']:.3f}")
    if s["edgeLight"] < check_art.EDGE_LIGHT_MIN:
        out.append(f"edge light {s['edgeLight']:+.3f}")
    if s["taken"] == 0:
        out.append("no texture")
    return out


def best(scored):
    """The candidate to pick from {label: scores}: of those that fail nothing, the one with the
    most render texture taken into the frame (the Presser's lesson: plainer than the carts is
    the risk), the edge light breaking ties. None when every candidate fails."""
    passing = [(s["taken"], s["edgeLight"], label) for label, s in scored.items() if not s["fails"]]
    return max(passing)[2] if passing else None


def contact_sheet(draft, composed, scored, auto, override=None, z=3):
    """The draft, then every candidate's composed base, best first, labelled: choosing among
    candidates that all pass is taste (the Presser's three were picked by eye), so the pick
    step shows them and chassis.json's "picks" records a choice."""
    order = sorted(composed, key=lambda k: (bool(scored[k]["fails"]), -scored[k]["taken"], -scored[k]["edgeLight"]))
    cw, ch, label = draft.width * z, draft.height * z, 30
    sheet = Image.new("RGBA", ((len(order) + 1) * (cw + 6) + 6, ch + label + 12), (60, 45, 35, 255))
    draw = ImageDraw.Draw(sheet)
    font = ImageFont.load_default()
    sheet.alpha_composite(draft.resize((cw, ch), Image.NEAREST), (6, 6))
    draw.text((6, ch + 8), "draft", fill=(230, 220, 200), font=font)
    for i, k in enumerate(order, start=1):
        x = 6 + i * (cw + 6)
        sheet.alpha_composite(composed[k].resize((cw, ch), Image.NEAREST), (x, 6))
        s = scored[k]
        mark = " PICK" if k == (override or auto) else (" auto" if k == auto else "")
        draw.text((x, ch + 8), f"{k}{mark}", fill=(255, 120, 100) if s["fails"] else (230, 220, 200), font=font)
        draw.text((x, ch + 20), "FAILS" if s["fails"] else f"tex {s['taken']} edge {s['edgeLight']:+.2f}",
                  fill=(150, 140, 125), font=font)
    return sheet


def step_pick(gs, log=print):
    ctx = check_art.Context.from_repo()
    others = [m.foot_mask(m.load(os.path.join(ART, s + ".png"))) for s in ctx.chassis_stems if s != gs.stem]
    report = {}
    os.makedirs(gs.dir("clean"), exist_ok=True)
    for facing in gs.drawn:
        draft = m.load(os.path.join(gs.drafts, f"draft_{facing}_{gs.base}.png"))
        scored, cleaned, composed = {}, {}, {}
        for label, path in gs.candidates(facing):
            if not os.path.exists(path):
                raise SetError(f"no render {os.path.relpath(path, gs.root)}; run the render step")
            cleaned[label] = clean(gs, path)
            out = gs.dir("work", f"base_{facing}", os.path.basename(path))
            os.makedirs(os.path.dirname(out), exist_ok=True)
            cleaned[label].save(out)
            scored[label], composed[label] = score(gs, facing, cleaned[label], others, ctx, draft)
        auto = best(scored)
        sheet = contact_sheet(draft, composed, scored, auto, gs.picks.get(facing))
        sheet.save(os.path.join(gs.root, f"candidates_{facing}.png"))
        chosen = gs.picks.get(facing, auto)
        if chosen is None:
            raise SetError(f"every {facing} candidate fails ({'; '.join(sorted({f for s in scored.values() for f in s['fails']}))}): "
                           "change the draft or the render settings, or name a pick in chassis.json")
        if chosen not in cleaned:
            raise SetError(f"chassis.json picks {chosen} for {facing}, which isn't a candidate")
        cleaned[chosen].save(gs.dir("clean", f"clean_{facing}.png"))
        report[facing] = {"picked": chosen, "auto": auto, "candidates": scored}
        log(f"pick: {facing} {chosen}" + ("" if chosen == auto else f" (chassis.json; auto would pick {auto})"))
        for label, s in scored.items():
            log(f"    {label}: taken {s['taken']:4d}  edge {s['edgeLight']:+.3f}  L {s['lightness']:.2f}"
                + (f"  sil {s['silhouette']:.2f}" if s["silhouette"] is not None else "")
                + (f"  FAILS {', '.join(s['fails'])}" if s["fails"] else ""))
    with open(os.path.join(gs.root, "picks.json"), "w", encoding="utf-8", newline="\n") as handle:
        json.dump(report, handle, indent=2)
        handle.write("\n")


# --- 4. compose --------------------------------------------------------------------------------

def load_parts(path):
    with open(path, encoding="utf-8") as handle:
        data = json.load(handle)
    parts = {tuple(int(v) for v in k.split(",")): p for k, p in data["parts"].items()}
    return parts, data["offsets"]


def detail_map(clean_image, base_parts, outline, radius=2):
    """The render's texture, as a tone step per pixel: +1 where the cleaned render is lighter
    than its part's neighbourhood by DETAIL_STEP in OKLab, -1 where darker, else 0. The draft
    keeps the shading and the light direction; the render adds only its high-pass detail (seams,
    scuffs, rivet glints), so the result can't drift off the light check."""
    cp = clean_image.load()
    w, h = clean_image.size
    light = {}
    for y in range(h):
        for x in range(w):
            if cp[x, y][3] >= 128 and tuple(cp[x, y][:3]) not in outline:
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


def compose_frame(gs, clean_image, facing, state):
    """One frame from its facing's cleaned base (art-style-bible.md §5.1: texture stable across
    frames):

    1. Each pixel of the frame's draft names its part. A rigid part that moved by an offset takes
       the cleaned base's texture from where it was, if the base had that part there and the
       draft didn't change it (an eye that brightens, a fire that dims, a part the base hid):
       otherwise the draft's own pixel.
    2. Arms and hands change shape between frames, so they are the draft's in every frame.
    3. What is taken is texture, not colour (detail_map): the draft's tone, stepped one up or
       down where the render is lighter or darker than its neighbourhood.
    4. The silhouette is outlined, and an outline pixel the move left inside takes the draft's
       colour; every frame is moved down to stand on the bottom row (check 13)."""
    palette = m.Palette.load()
    outline = palette.outline()
    rigid, textured = set(gs.rigid), set(gs.textured)
    draft = m.load(os.path.join(gs.drafts, f"draft_{facing}_{state}.png"))
    base_draft = m.load(os.path.join(gs.drafts, f"draft_{facing}_{gs.base}.png"))
    parts, offsets = load_parts(os.path.join(gs.drafts, f"parts_{facing}_{state}.json"))
    base_parts, _ = load_parts(os.path.join(gs.drafts, f"parts_{facing}_{gs.base}.json"))
    dp, bp, cp = draft.load(), base_draft.load(), clean_image.load()
    detail = detail_map(clean_image, base_parts, outline)
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
        if part in rigid and off is not None:
            sx, sy = x - off[0], y - off[1]
            if (0 <= sx < w and 0 <= sy < h and base_parts.get((sx, sy)) == part and cp[sx, sy][3] >= 128
                    and bp[sx, sy][:3] == d[:3] and tuple(d[:3]) not in outline):
                ramp = palette.classify(d[:3])
                step = detail.get((sx, sy), 0)
                if ramp and ramp[0] in textured and step:
                    tones = palette.ramps[ramp[0]]
                    colour = tones[max(0, min(len(tones) - 1, ramp[1] + step))] + (255,)
                    taken += 1
        op[x, y] = colour
    final = out.copy()
    fp = final.load()
    for y in range(h):
        for x in range(w):
            if op[x, y][3] == 0:
                continue
            edge = any(not (0 <= nx < w and 0 <= ny < h) or op[nx, ny][3] == 0
                       for nx, ny in ((x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)))
            if edge:
                if tuple(op[x, y][:3]) not in outline:
                    fp[x, y] = dp[x, y] if tuple(dp[x, y][:3]) in outline else palette.ramps["outline"][0] + (255,)
            elif tuple(op[x, y][:3]) in outline and tuple(dp[x, y][:3]) not in outline:
                fp[x, y] = dp[x, y]
    low = m.lowest_opaque_row(final)
    if low is not None and low < h - 1:
        moved = Image.new("RGBA", (w, h), (0, 0, 0, 0))
        moved.paste(final, (0, h - 1 - low))
        final = moved
    return final, taken


def step_compose(gs, out_dir=None, log=print):
    out_dir = out_dir or gs.dir("after")
    if writes_into_godot_art(out_dir):
        raise SetError("compose writes to the set's folder; install copies into godot/art/")
    os.makedirs(out_dir, exist_ok=True)
    total = 0
    for facing in gs.facings:
        # West's texture is east's, mirrored with it (the draft draws west as east mirrored).
        source = "east" if facing == "west" else facing
        path = gs.dir("clean", f"clean_{source}.png")
        if not os.path.exists(path):
            raise SetError(f"no {os.path.relpath(path, gs.root)}; run the pick step")
        cleaned = m.load(path)
        if facing == "west":
            cleaned = cleaned.transpose(Image.FLIP_LEFT_RIGHT)
        for state in gs.states:
            image, taken = compose_frame(gs, cleaned, facing, state)
            image.save(os.path.join(out_dir, gs.name(state, facing)))
            total += taken
    log(f"compose: {len(gs.frames())} frames in {out_dir} ({total} pixels of render texture)")
    return out_dir


# --- 5. check ----------------------------------------------------------------------------------

def manifest_entries(gs, manifest):
    """The frames' manifest entries: for a golem, the south base's entry, as replaced (owner
    comfyui, the set's ramps), per frame; west names the east frame it mirrors. The Artificer's
    frames keep their own entries (direction, frame, readers), as replaced."""
    if gs.kind == "artificer":
        return artificer_entries(gs, manifest)
    base = manifest.get(gs.name("base", "south"))
    if base is None:
        raise SetError(f"art_manifest.json has no {gs.name('base', 'south')}")
    rel = set_source(gs)
    out = {}
    for facing in gs.facings:
        for state in gs.states:
            name = gs.name(state, facing)
            e = copy.deepcopy(manifest.get(name, base))
            e.update({"set": base.get("set", "S1"), "kind": "standing", "size": [W, H], "pivot": "bottom-center",
                      "footRow": H - 1, "owner": "comfyui", "palette": list(gs.ramps), "chassis": gs.stem,
                      "state": state, "facing": facing, "source": rel})
            if (state, facing) != ("base", "south"):
                e["readers"] = ["GolemFrames.FileName", "GolemFrames.Resolve", "GolemNode"]
            if facing == "west":
                e["mirroredFrom"] = gs.name(state, "east")
            else:
                e.pop("mirroredFrom", None)
            out[name] = e
    return out


def set_source(gs):
    try:
        return os.path.relpath(gs.root, REPO).replace(os.sep, "/") + "/"
    except ValueError:  # another drive: only a test's scratch set
        return gs.root.replace(os.sep, "/") + "/"


def artificer_entries(gs, manifest):
    out = {}
    for facing in gs.facings:
        for state in gs.states:
            name = gs.name(state, facing)
            if name not in manifest:
                raise SetError(f"art_manifest.json has no {name}")
            e = copy.deepcopy(manifest[name])
            e.update({"size": list(gs.size), "footRow": gs.size[1] - 1, "owner": "comfyui",
                      "palette": list(gs.ramps), "source": set_source(gs)})
            if facing == "west":
                e["mirroredFrom"] = gs.name(state, "east")
            else:
                e.pop("mirroredFrom", None)
            out[name] = e
    return out


def step_check(gs, frames_dir=None, log=print):
    """check_art over a scratch copy of godot/art/ with the frames in place, as if replaced.
    Returns the number of FAILs on this set's frames."""
    frames_dir = frames_dir or gs.dir("after")
    scratch = tempfile.mkdtemp(prefix="golem-check-")
    try:
        art = os.path.join(scratch, "art")
        shutil.copytree(ART, art, ignore=shutil.ignore_patterns("*.import"))
        manifest = check_art.load_manifest()
        manifest.update(manifest_entries(gs, manifest))
        names = gs.frames()
        for n in names:
            shutil.copy(os.path.join(frames_dir, n), os.path.join(art, n))
        report = check_art.Checker(manifest, art, check_art.Context.from_repo(), names).run()
        mine = sorted({(lv, c, r, msg) for lv, c, r, msg in report.findings if r in names or r.startswith(gs.stem)})
        for lv, c, r, msg in mine:
            if lv != "INFO":
                log(f"check: {lv} [{c}] {r}: {msg}")
        fails = sum(1 for f in mine if f[0] == "FAIL")
        warns = sum(1 for f in mine if f[0] == "WARN")
        log(f"check: {fails} FAIL, {warns} WARN on {gs.stem}")
        return fails
    finally:
        shutil.rmtree(scratch, ignore_errors=True)


# --- 6. review ---------------------------------------------------------------------------------

def grid(gs, frames_dir, out):
    """Every frame at 4x on the floor, a row per facing and a column per state, numbered as
    review.json numbers them, with today's sprite beside each row (a golem's south base; the
    Artificer's own base frame of that facing)."""
    z = 4
    cell_size = (gs.size[0] * z, gs.size[1] * z)
    pad, label = 8, 18
    bg, ink, dim = (24, 20, 18), (230, 220, 200), (150, 140, 125)
    floor = Image.open(os.path.join(ART, "floor_tile.png")).convert("RGBA").resize((64 * z, 64 * z), Image.NEAREST)

    def cell(path):
        img = Image.new("RGBA", cell_size)
        for y in range(0, cell_size[1], floor.height):
            img.paste(floor, (0, y))
        sprite = Image.open(path).convert("RGBA")
        big = sprite.resize((sprite.width * z, sprite.height * z), Image.NEAREST)
        img.alpha_composite(big, ((cell_size[0] - big.width) // 2, cell_size[1] - big.height))
        return img

    font = ImageFont.load_default()
    cols = 1 + len(gs.states)
    sheet = Image.new("RGBA", (pad + cols * (cell_size[0] + pad), 30 + len(gs.facings) * (cell_size[1] + label + pad)), bg + (255,))
    draw = ImageDraw.Draw(sheet)
    draw.text((pad, 8), f"{gs.title} - #numbers are review.json's; left column: today's sprite", fill=ink, font=font)
    number = 1
    for r, facing in enumerate(gs.facings):
        y = 30 + r * (cell_size[1] + label + pad)
        own = os.path.join(ART, gs.name(gs.base, facing))
        if gs.kind == "artificer" and os.path.exists(own):
            sheet.alpha_composite(cell(own), (pad, y))
            caption = "today"
        else:
            sheet.alpha_composite(cell(os.path.join(ART, gs.name(gs.base, "south"))), (pad, y))
            caption = "today" if facing == "south" else f"today ({facing}: none drawn)"
        draw.text((pad, y + cell_size[1] + 2), caption, fill=dim, font=font)
        for c, state in enumerate(gs.states):
            x = pad + (c + 1) * (cell_size[0] + pad)
            sheet.alpha_composite(cell(os.path.join(frames_dir, gs.name(state, facing))), (x, y))
            draw.text((x, y + cell_size[1] + 2), f"#{number}  {state} {facing}", fill=ink, font=font)
            number += 1
    sheet.convert("RGB").save(out)
    return out


def scratch_project(gs, frames_dir):
    """A scratch copy of the Godot project and Tools/Art with the frames and their manifest
    entries in place, imported; returns its root. The caller removes it."""
    root = tempfile.mkdtemp(prefix="golem-game-")
    shutil.copytree(GODOT, os.path.join(root, "godot"), ignore=shutil.ignore_patterns(".godot", "bin", "obj"))
    shutil.copytree(HERE, os.path.join(root, "Tools", "Art"), ignore=shutil.ignore_patterns("__pycache__"))
    for n in gs.frames():
        shutil.copy(os.path.join(frames_dir, n), os.path.join(root, "godot", "art", n))
    import_project(os.path.join(root, "godot"), gs.frames())
    return root


def import_project(project, names, timeout=600):
    """Imports the project in a headless Godot this starts and stops itself, until every named
    file has its .import (the editor's import doesn't quit on its own)."""
    proc = subprocess.Popen([GODOT_EXE, "--headless", "--path", project, "--import"],
                            stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    deadline = time.time() + timeout
    try:
        while not all(os.path.exists(os.path.join(project, "art", n + ".import")) for n in names):
            if proc.poll() is not None:
                break
            if time.time() > deadline:
                raise SetError("Godot's import didn't finish in time")
            time.sleep(3)
        time.sleep(10 if proc.poll() is None else 0)
    finally:
        if proc.poll() is None:
            subprocess.run(["taskkill", "/T", "/F", "/PID", str(proc.pid)], capture_output=True)
            proc.wait(timeout=60)
    missing = [n for n in names if not os.path.exists(os.path.join(project, "art", n + ".import"))]
    if missing:
        raise SetError(f"Godot didn't import {', '.join(missing[:3])}")


def scenario(project, name, extra=(), timeout=900):
    """Runs one scenario; returns (passed, its verdict line)."""
    result = subprocess.run([GODOT_EXE, "--path", project, "--fixed-fps", "60", "--", "--scenario", name, *extra],
                            capture_output=True, text=True, timeout=timeout)
    lines = [ln for ln in result.stdout.splitlines() if ln.startswith(f"[scenario {name}]")]
    return result.returncode == 0, (lines[-1] if lines else f"[scenario {name}] no verdict (exit {result.returncode})")


def step_review(gs, game=False, log=print):
    after = gs.dir("after")
    log(f"review: {grid(gs, after, os.path.join(gs.root, 'grid.png'))}")
    game_after = None
    if game:
        game_after = os.path.join(gs.root, "after_game")
        shutil.rmtree(game_after, ignore_errors=True)
        root = scratch_project(gs, after)
        try:
            build = subprocess.run([GODOT_EXE, "--headless", "--path", os.path.join(root, "godot"), "--build-solutions", "--quit"],
                                   capture_output=True, text=True, timeout=900)
            if build.returncode != 0:
                raise SetError("the scratch project didn't build")
            ok, line = scenario(os.path.join(root, "godot"), "art", [f"--capture-dir={game_after}"])
            log(f"review: {line}")
        finally:
            shutil.rmtree(root, ignore_errors=True)
    for path in review_sheet.write(gs.title, gs.frames(), ART, after, gs.root, game_after=game_after):
        log(f"review: {path}")


# --- install, verify -------------------------------------------------------------------------

def step_install(gs, log=print):
    if not gs.config.get("approved"):
        raise SetError("chassis.json has no \"approved\" date: the user approves the grid before install")
    after = gs.dir("after")
    if step_check(gs, after, log) > 0:
        raise SetError("the frames fail check_art; nothing installed")
    for n in gs.frames():
        shutil.copy(os.path.join(after, n), os.path.join(ART, n))
    manifest = check_art.load_manifest()
    manifest.update(manifest_entries(gs, manifest))
    with open(check_art.MANIFEST_PATH, "w", encoding="utf-8", newline="\n") as handle:
        json.dump(manifest, handle, indent=2)
        handle.write("\n")
    log(f"install: {len(gs.frames())} frames into {ART}, their manifest entries written")
    import_project(GODOT, gs.frames())
    log("install: imported")
    failed = []
    for name in gs.config.get("scenarios", ["art", "golems", "golem-motion"]):
        ok, line = scenario(GODOT, name)
        log(f"install: {line}")
        if not ok:
            failed.append(name)
    if failed:
        raise SetError(f"installed, but {', '.join(failed)} failed")
    log("install: done; look at the game, then commit (git add retries: root CLAUDE.md)")


def step_verify(gs, log=print):
    """Recompose from the renders on disk (pick, with chassis.json's picks) into a scratch folder
    and compare with godot/art/: an installed set must reproduce pixel for pixel."""
    scratch = tempfile.mkdtemp(prefix="golem-verify-")
    try:
        step_pick(gs, log=lambda *_: None)
        out = step_compose(gs, scratch, log=lambda *_: None)
        drifted = []
        for n in gs.frames():
            installed = os.path.join(ART, n)
            if not os.path.exists(installed):
                drifted.append(f"{n} (not installed)")
                continue
            a, b = m.load(os.path.join(out, n)), m.load(installed)
            if a.size != b.size or a.tobytes() != b.tobytes():
                drifted.append(n)
        for n in drifted:
            log(f"verify: DRIFT {n}")
        log(f"verify: {len(gs.frames()) - len(drifted)} of {len(gs.frames())} reproduce")
        return len(drifted)
    finally:
        shutil.rmtree(scratch, ignore_errors=True)


STEPS = ["draft", "render", "pick", "compose", "check", "review"]


def main(argv):
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("set")
    parser.add_argument("step", choices=STEPS + ["run", "install", "verify"])
    parser.add_argument("--force", action="store_true", help="render: again, even with every candidate on disk")
    parser.add_argument("--dry-run", action="store_true", help="render: print the workflows, queue nothing")
    parser.add_argument("--game", action="store_true", help="review: add the frames as seen in the game (slow)")
    args = parser.parse_args(argv)
    try:
        gs = GolemSet.load(args.set)
        steps = STEPS if args.step == "run" else [args.step]
        for step in steps:
            if step == "draft":
                step_draft(gs)
            elif step == "render":
                step_render(gs, args.force, args.dry_run)
                if args.dry_run:
                    return 0
            elif step == "pick":
                step_pick(gs)
            elif step == "compose":
                step_compose(gs)
            elif step == "check":
                if step_check(gs) > 0 and args.step == "check":
                    return 1
            elif step == "review":
                step_review(gs, args.game)
        if args.step == "install":
            step_install(gs)
        elif args.step == "verify":
            return 1 if step_verify(gs) else 0
    except SetError as error:
        print(f"golem_set: {error}", file=sys.stderr)
        return 2
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
