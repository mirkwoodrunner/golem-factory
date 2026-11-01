"""AI placeholder samples from Stable Audio Open in the local ComfyUI (docs/audio-design.md §9,
decision 1). The same five sounds the synthesized and CC0 samples cover.

Needs, in G:/AI/ComfyUI/ComfyUI_windows_portable/ComfyUI/models/:
    checkpoints/stable-audio-open-1.0.safetensors   (huggingface.co/stabilityai/stable-audio-open-1.0,
                                                     model.safetensors renamed; gated: accept its terms)
    text_encoders/t5_base.safetensors               (huggingface.co/google-t5/t5-base, model.safetensors renamed)
and ComfyUI running (G:/AI/start_comfyui.bat). The user runs this in their own terminal (renders
in Claude's shell get killed for memory), then stops ComfyUI:

    python Tools/Audio/comfy_audio_samples.py [--seed N]

Writes public/audio/samples/ai/<slot>.flac, then each again with --seed for a second take if wanted.
"""
import argparse
import json
import os
import shutil
import sys
import time
import urllib.request

COMFY = "http://127.0.0.1:8188"
ROOT = "G:/AI/ComfyUI/ComfyUI_windows_portable/ComfyUI"
OUT = os.path.join(os.path.dirname(__file__), "..", "..", "public", "audio", "samples", "ai")
NEGATIVE = "low quality, noise, distortion, voice, speech, vocals, clipping"

# slot: (seconds, prompt)
SOUNDS = {
    "work.presser.stamp": (1.5, "a single heavy mechanical press stamping metal, one clank, short, dry, "
                                "clockwork machine, foley, close"),
    "ui.card-snap": (1.0, "a single crisp click of a brass card snapping into a slot, short, clean, ui sound"),
    "ui.stall-chime": (2.5, "two soft bell chimes falling in pitch, gentle alert, music box bell, clean"),
    "amb.steam": (8.0, "steady steam hissing from a boiler pipe, continuous, loopable, ambience, no music"),
    "music.layer.1": (16.0, "calm cozy music box and soft pad, d minor, 90 bpm, gentle loopable "
                             "background music for a clockwork workshop game"),
}


def workflow(seconds, prompt, seed, prefix):
    return {
        "1": {"class_type": "CheckpointLoaderSimple", "inputs": {"ckpt_name": "stable-audio-open-1.0.safetensors"}},
        "2": {"class_type": "CLIPLoader", "inputs": {"clip_name": "t5_base.safetensors", "type": "stable_audio"}},
        "3": {"class_type": "CLIPTextEncode", "inputs": {"text": prompt, "clip": ["2", 0]}},
        "4": {"class_type": "CLIPTextEncode", "inputs": {"text": NEGATIVE, "clip": ["2", 0]}},
        "5": {"class_type": "ConditioningStableAudio", "inputs": {
            "positive": ["3", 0], "negative": ["4", 0], "seconds_start": 0.0, "seconds_total": seconds}},
        "6": {"class_type": "EmptyLatentAudio", "inputs": {"seconds": seconds, "batch_size": 1}},
        "7": {"class_type": "KSampler", "inputs": {
            "model": ["1", 0], "positive": ["5", 0], "negative": ["5", 1], "latent_image": ["6", 0],
            "seed": seed, "steps": 50, "cfg": 5.0, "sampler_name": "dpmpp_3m_sde_gpu",
            "scheduler": "exponential", "denoise": 1.0}},
        "8": {"class_type": "VAEDecodeAudio", "inputs": {"samples": ["7", 0], "vae": ["1", 2]}},
        "9": {"class_type": "SaveAudio", "inputs": {"audio": ["8", 0], "filename_prefix": prefix}},
    }


def call(path, body=None):
    data = json.dumps(body).encode() if body is not None else None
    req = urllib.request.Request(COMFY + path, data=data, headers={"Content-Type": "application/json"})
    with urllib.request.urlopen(req, timeout=30) as r:
        return json.loads(r.read())


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--seed", type=int, default=7)
    args = ap.parse_args()

    missing = [p for p in ("models/checkpoints/stable-audio-open-1.0.safetensors",
                           "models/text_encoders/t5_base.safetensors")
               if not os.path.exists(os.path.join(ROOT, p))]
    if missing:
        sys.exit("missing: " + ", ".join(missing) + " (see this file's header)")
    try:
        call("/system_stats")
    except OSError:
        sys.exit("ComfyUI isn't running: start G:/AI/start_comfyui.bat first")

    os.makedirs(OUT, exist_ok=True)
    for slot, (seconds, prompt) in SOUNDS.items():
        prefix = f"golem_audio/{slot}_s{args.seed}"
        prompt_id = call("/prompt", {"prompt": workflow(seconds, prompt, args.seed, prefix)})["prompt_id"]
        print(f"{slot}: queued ({seconds} s)", flush=True)
        while True:
            history = call(f"/history/{prompt_id}")
            if prompt_id in history:
                break
            time.sleep(2)
        entry = history[prompt_id]
        if entry.get("status", {}).get("status_str") == "error":
            sys.exit(f"{slot}: ComfyUI reported an error; see its console")
        for output in entry["outputs"].values():
            for audio in output.get("audio", []):
                src = os.path.join(ROOT, "output", audio.get("subfolder", ""), audio["filename"])
                name = slot + (".flac" if args.seed == 7 else f"_s{args.seed}.flac")
                shutil.copyfile(src, os.path.join(OUT, name))
                print(f"{slot}: wrote {name}", flush=True)
    print("done; stop ComfyUI when finished")


if __name__ == "__main__":
    main()
