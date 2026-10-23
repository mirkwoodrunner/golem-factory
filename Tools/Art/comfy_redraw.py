"""One frame's redraw candidates from the local ComfyUI (docs/art-pipeline-design.md §2 step 4,
§5.4). Standard library only; it talks to the server over its HTTP API and never touches
G:/AI/ComfyUI. ComfyUI must already be running (G:/AI/start_comfyui.bat); stop it when the set
that needed it is done.

    python Tools/Art/comfy_redraw.py --init <8x.png> --out public/images/<set>/raw/<frame>/
        [--prompt "..."] [--denoise 0.35,0.5,0.65] [--seeds 4]
        [--guide <guide_8x.png> --control-model control-lora-canny-rank128.safetensors
         --control-strength 0.8 --control-end 0.8 [--guide-preprocess canny]]
        [--reference <cleaned_south_base_8x.png> --ipadapter-model ip-adapter-plus_sdxl_vit-h.safetensors
         --clip-vision CLIP-ViT-H-14-laion2B-s32B-b79K.safetensors --ipadapter-weight 0.7]
        [--txt2img --width 512 --height 768]
    python Tools/Art/comfy_redraw.py ... --dry-run     # print the workflow, queue nothing

The base workflow: LoadImage of --init, VAEEncode, KSampler (LCM, 8 steps, cfg 1.5,
sgm_uniform) at each --denoise and seed, SDXL with the LCM LoRA and Pixel Art XL at 1.2,
VAEDecode, SaveImage. --txt2img starts from an EmptyLatentImage instead (denoise 1).

--guide adds ControlNet: LoadImage of the guide (pose_guide.py's lineart, already white edges
on black, so no preprocessor by default; --guide-preprocess canny runs comfyui_controlnet_aux's
CannyEdgePreprocessor on it first), ControlNetLoader of --control-model, and
ControlNetApplyAdvanced on both the positive and the negative conditioning at
--control-strength, from 0 to --control-end of the steps, with the VAE (this ComfyUI's
ControlNetApplyAdvanced takes one).

--reference adds IP-Adapter (ComfyUI_IPAdapter_plus): IPAdapterModelLoader of
--ipadapter-model, CLIPVisionLoader of --clip-vision, and IPAdapterAdvanced on the model at
--ipadapter-weight (weight type "style transfer", embeds "V only", as the smoke test that
proved the install used), with the cleaned south base as its image.

6 GB of VRAM (§1.4): batch size 1; one ControlNet; no default loads a model the command line
didn't name for ControlNet or IP-Adapter; the guide and the reference may not be larger than the
render. On running out of memory, lower --ipadapter-weight first, never drop the guide.

Measured on this machine (2026-10-23): about 11 s an image warm, about 6 minutes for the first
after a start (SDXL loads from the HDD).

It refuses an --out under godot/art/, a --guide without --control-model, and a --reference
without --ipadapter-model and --clip-vision.
"""
import argparse
import hashlib
import json
import os
import struct
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
import uuid

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
from art_paths import writes_into_godot_art  # noqa: E402

URL = "http://127.0.0.1:8188"
CHECKPOINT = "sd_xl_base_1.0.safetensors"
VAE = "sdxl-vae-fp16-fix.safetensors"
LCM_LORA = "lcm-lora-sdxl.safetensors"
PIXEL_LORA = "pixel-art-xl.safetensors"
PIXEL_STRENGTH = 1.2
STEPS = 8
CFG = 1.5
SEED_BASE = 424242
DEFAULT_PROMPT = ("pixel art, cozy top-down game sprite, brass and mahogany, warm brown outlines, "
                  "lit from the upper left, plain background")
DEFAULT_NEGATIVE = "3d render, realistic, photo, blurry, text, watermark, black outline"


class RedrawError(Exception):
    pass


def build_workflow(init_image=None, prompt=DEFAULT_PROMPT, negative=DEFAULT_NEGATIVE, denoise=0.5,
                   seed=SEED_BASE, txt2img=False, width=512, height=768,
                   guide_image=None, control_model=None, control_strength=0.8, control_end=0.8,
                   guide_preprocess="none",
                   reference_image=None, ipadapter_model=None, clip_vision=None, ipadapter_weight=0.7,
                   ipadapter_weight_type="style transfer",
                   checkpoint=CHECKPOINT, vae=VAE, prefix="redraw"):
    """The API-format workflow for one candidate. Image arguments are names the server knows
    (as /upload/image returned them); width and height are the render size (the empty latent's
    for --txt2img, the canny preprocessor's resolution either way). Pure, so the tests need no
    server."""
    if guide_image and not control_model:
        raise RedrawError("--guide needs --control-model")
    if reference_image and not (ipadapter_model and clip_vision):
        raise RedrawError("--reference needs --ipadapter-model and --clip-vision")
    if not txt2img and not init_image:
        raise RedrawError("image to image needs --init (or pass --txt2img)")

    wf = {
        "1": {"class_type": "CheckpointLoaderSimple", "inputs": {"ckpt_name": checkpoint}},
        "2": {"class_type": "VAELoader", "inputs": {"vae_name": vae}},
        "3": {"class_type": "LoraLoader", "inputs": {"model": ["1", 0], "clip": ["1", 1],
              "lora_name": LCM_LORA, "strength_model": 1.0, "strength_clip": 1.0}},
        "4": {"class_type": "LoraLoader", "inputs": {"model": ["3", 0], "clip": ["3", 1],
              "lora_name": PIXEL_LORA, "strength_model": PIXEL_STRENGTH, "strength_clip": PIXEL_STRENGTH}},
        "5": {"class_type": "CLIPTextEncode", "inputs": {"clip": ["4", 1], "text": prompt}},
        "6": {"class_type": "CLIPTextEncode", "inputs": {"clip": ["4", 1], "text": negative}},
    }
    model = ["4", 0]
    positive, negative_cond = ["5", 0], ["6", 0]

    if txt2img:
        wf["7"] = {"class_type": "EmptyLatentImage", "inputs": {"width": width, "height": height, "batch_size": 1}}
        latent = ["7", 0]
        denoise = 1.0
    else:
        wf["40"] = {"class_type": "LoadImage", "inputs": {"image": init_image}}
        wf["41"] = {"class_type": "VAEEncode", "inputs": {"pixels": ["40", 0], "vae": ["2", 0]}}
        latent = ["41", 0]

    if reference_image:
        wf["20"] = {"class_type": "IPAdapterModelLoader", "inputs": {"ipadapter_file": ipadapter_model}}
        wf["21"] = {"class_type": "CLIPVisionLoader", "inputs": {"clip_name": clip_vision}}
        wf["22"] = {"class_type": "LoadImage", "inputs": {"image": reference_image}}
        wf["23"] = {"class_type": "IPAdapterAdvanced", "inputs": {
            "model": model, "ipadapter": ["20", 0], "image": ["22", 0], "clip_vision": ["21", 0],
            "weight": ipadapter_weight, "weight_type": ipadapter_weight_type, "combine_embeds": "concat",
            "start_at": 0.0, "end_at": 1.0, "embeds_scaling": "V only"}}
        model = ["23", 0]

    if guide_image:
        wf["30"] = {"class_type": "LoadImage", "inputs": {"image": guide_image}}
        control = ["30", 0]
        if guide_preprocess == "canny":
            wf["31"] = {"class_type": "CannyEdgePreprocessor", "inputs": {
                "image": ["30", 0], "low_threshold": 100, "high_threshold": 200,
                "resolution": max(width, height)}}
            control = ["31", 0]
        elif guide_preprocess != "none":
            raise RedrawError(f"--guide-preprocess is none or canny, not {guide_preprocess}")
        wf["32"] = {"class_type": "ControlNetLoader", "inputs": {"control_net_name": control_model}}
        wf["33"] = {"class_type": "ControlNetApplyAdvanced", "inputs": {
            "positive": positive, "negative": negative_cond, "control_net": ["32", 0], "image": control,
            "strength": control_strength, "start_percent": 0.0, "end_percent": control_end, "vae": ["2", 0]}}
        positive, negative_cond = ["33", 0], ["33", 1]

    wf["8"] = {"class_type": "KSampler", "inputs": {
        "model": model, "positive": positive, "negative": negative_cond, "latent_image": latent,
        "seed": seed, "steps": STEPS, "cfg": CFG, "sampler_name": "lcm", "scheduler": "sgm_uniform",
        "denoise": denoise}}
    wf["9"] = {"class_type": "VAEDecode", "inputs": {"samples": ["8", 0], "vae": ["2", 0]}}
    wf["10"] = {"class_type": "SaveImage", "inputs": {"images": ["9", 0], "filename_prefix": prefix}}
    return wf


def candidates(denoise, seeds, seed_base=SEED_BASE, txt2img=False):
    """(denoise, seed) for every candidate: a denoise sweep over seeds (txt2img: denoise 1)."""
    levels = [1.0] if txt2img else denoise
    return [(d, seed_base + i) for d in levels for i in range(seeds)]


def png_size(path):
    with open(path, "rb") as handle:
        head = handle.read(24)
    if head[:8] != b"\x89PNG\r\n\x1a\n":
        raise RedrawError(f"{path} isn't a PNG")
    return struct.unpack(">II", head[16:24])


def check_sizes(render, guide=None, reference=None):
    """The guide and the reference at the render size, never larger (§5.4, 6 GB)."""
    for label, path in (("--guide", guide), ("--reference", reference)):
        if path:
            w, h = png_size(path)
            if w > render[0] or h > render[1]:
                raise RedrawError(f"{label} is {w}x{h}, larger than the {render[0]}x{render[1]} render")


def validate_out(out):
    if writes_into_godot_art(out):
        raise RedrawError("--out is under godot/art/; raw candidates go in public/images/<set>/raw/")


# --- the server ----------------------------------------------------------------------------------

def _post(url, path, payload):
    req = urllib.request.Request(url + path, data=json.dumps(payload).encode(),
                                 headers={"Content-Type": "application/json"})
    with urllib.request.urlopen(req, timeout=60) as r:
        return json.load(r)


def _get(url, path):
    with urllib.request.urlopen(url + path, timeout=60) as r:
        return json.load(r)


def upload_name(role, data):
    """The name an image is uploaded under: its role and a hash of its bytes. Never the file's
    own name: the server overwrites by name, and pose_guide.py writes init.png and guide.png in
    every frame's folder, so an init and a reference both called init.png were one image."""
    return f"gf-{role}-{hashlib.sha1(data).hexdigest()[:16]}.png"


def upload(url, path, role="image"):
    boundary = uuid.uuid4().hex
    with open(path, "rb") as handle:
        data = handle.read()
    name = upload_name(role, data)
    body = (f"--{boundary}\r\nContent-Disposition: form-data; name=\"image\"; filename=\"{name}\"\r\n"
            f"Content-Type: image/png\r\n\r\n").encode() + data + \
           f"\r\n--{boundary}\r\nContent-Disposition: form-data; name=\"overwrite\"\r\n\r\ntrue\r\n--{boundary}--\r\n".encode()
    req = urllib.request.Request(url + "/upload/image", data=body,
                                 headers={"Content-Type": f"multipart/form-data; boundary={boundary}"})
    with urllib.request.urlopen(req, timeout=60) as r:
        return json.load(r)["name"]


def render(url, workflow, out_path, timeout=1500):
    try:
        prompt_id = _post(url, "/prompt", {"prompt": workflow})["prompt_id"]
    except urllib.error.HTTPError as error:
        raise RedrawError("rejected: " + error.read().decode(errors="replace")[:2000])
    started = time.time()
    while True:
        history = _get(url, "/history/" + prompt_id).get(prompt_id)
        if history:
            status = history.get("status", {})
            if status.get("status_str") == "error":
                raise RedrawError("failed: " + json.dumps(status.get("messages", []))[:2000])
            if status.get("completed"):
                break
        if time.time() - started > timeout:
            raise RedrawError("timed out")
        time.sleep(1)
    image = history["outputs"]["10"]["images"][0]
    query = urllib.parse.urlencode({k: image[k] for k in ("filename", "subfolder", "type")})
    with urllib.request.urlopen(f"{url}/view?{query}", timeout=60) as r, open(out_path, "wb") as handle:
        handle.write(r.read())
    return time.time() - started


def main(argv):
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--init")
    parser.add_argument("--out", required=True)
    parser.add_argument("--prompt", default=DEFAULT_PROMPT)
    parser.add_argument("--negative", default=DEFAULT_NEGATIVE)
    parser.add_argument("--denoise", default="0.35,0.5,0.65")
    parser.add_argument("--seeds", type=int, default=4)
    parser.add_argument("--seed-base", type=int, default=SEED_BASE)
    parser.add_argument("--guide")
    parser.add_argument("--control-model")
    parser.add_argument("--control-strength", type=float, default=0.8)
    parser.add_argument("--control-end", type=float, default=0.8)
    parser.add_argument("--guide-preprocess", default="none", choices=("none", "canny"))
    parser.add_argument("--reference")
    parser.add_argument("--ipadapter-model")
    parser.add_argument("--clip-vision")
    parser.add_argument("--ipadapter-weight", type=float, default=0.7)
    parser.add_argument("--ipadapter-weight-type", default="style transfer")
    parser.add_argument("--txt2img", action="store_true")
    parser.add_argument("--width", type=int, default=512)
    parser.add_argument("--height", type=int, default=768)
    parser.add_argument("--url", default=URL)
    parser.add_argument("--dry-run", action="store_true")
    args = parser.parse_args(argv)

    try:
        validate_out(args.out)
        if args.guide and not args.control_model:
            raise RedrawError("--guide needs --control-model")
        if args.reference and not (args.ipadapter_model and args.clip_vision):
            raise RedrawError("--reference needs --ipadapter-model and --clip-vision")
        size = (args.width, args.height) if args.txt2img or not args.init else png_size(args.init)
        check_sizes(size, args.guide, args.reference)
        denoise = [float(d) for d in args.denoise.split(",") if d.strip()]
        plan = candidates(denoise, args.seeds, args.seed_base, args.txt2img)
        frame = os.path.basename(os.path.normpath(args.out))
        names = {}
        if not args.dry_run:
            for key, path in (("init", None if args.txt2img else args.init), ("guide", args.guide), ("reference", args.reference)):
                if path:
                    names[key] = upload(args.url, path, key)
        else:
            names = {k: os.path.basename(p) for k, p in (("init", args.init), ("guide", args.guide),
                                                          ("reference", args.reference)) if p}
        os.makedirs(args.out, exist_ok=True)
        for d, seed in plan:
            wf = build_workflow(names.get("init"), args.prompt, args.negative, d, seed, args.txt2img,
                                size[0], size[1], names.get("guide"), args.control_model,
                                args.control_strength, args.control_end, args.guide_preprocess,
                                names.get("reference"), args.ipadapter_model, args.clip_vision,
                                args.ipadapter_weight, args.ipadapter_weight_type, prefix=f"redraw-{frame}")
            target = os.path.join(args.out, f"{frame}_d{d:.2f}_s{seed}.png")
            if args.dry_run:
                print(json.dumps(wf, indent=2))
                return 0
            seconds = render(args.url, wf, target)
            print(f"wrote {target} ({seconds:.0f} s)", flush=True)
    except RedrawError as error:
        print(f"refused: {error}", file=sys.stderr)
        return 2
    except urllib.error.URLError as error:
        print(f"no ComfyUI at {args.url} ({error.reason}); start G:/AI/start_comfyui.bat", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
