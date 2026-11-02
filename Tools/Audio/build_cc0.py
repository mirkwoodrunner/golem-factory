"""The CC0 placeholders (docs/audio-design.md §6, decision 1): each slot's file, cut from a CC0
pack on OpenGameArt, made mono 16-bit 44.1 kHz WAV, its lead-in silence trimmed and its peak set
to -1 dBFS. The packs aren't committed; download them (URLs in audio_manifest.json) into one
folder, keeping each zip's own folder name, then:

    <python with PyAV and numpy> Tools/Audio/build_cc0.py <packs_dir> [out_dir]   # default godot/audio/

PyAV decodes the packs' OGG files; the system Python has neither, ComfyUI's embedded one has both.
The music isn't here: it is synthesized (synth_music.py).
"""
import json
import os
import struct
import sys
import wave

import av
import numpy as np

HERE = os.path.dirname(__file__)
RATE = 44100
PEAK = 10 ** (-1.0 / 20)


def load(path):
    resampler = av.AudioResampler(format="s16", layout="mono", rate=RATE)
    out = []
    with av.open(path) as f:
        for frame in f.decode(f.streams.audio[0]):
            for r in resampler.resample(frame):
                out.append(r.to_ndarray().reshape(-1))
        for r in resampler.resample(None):
            out.append(r.to_ndarray().reshape(-1))
    return np.concatenate(out).astype(np.float64) / 32768.0


def trim_lead(x, threshold=10 ** (-50 / 20)):
    loud = np.nonzero(np.abs(x) > threshold)[0]
    return x[max(0, loud[0] - int(RATE * 0.002)):] if loud.size else x


def fade_out(x, seconds=0.02):
    n = min(x.size, int(RATE * seconds))
    x = x.copy()
    x[-n:] *= np.linspace(1.0, 0.0, n)
    return x


def crossfaded_loop(x, start, end, fade_seconds=0.5):
    """x[start:end] as a seamless loop: the audio just past `end` is faded into its head."""
    fade = min(int(RATE * fade_seconds), x.size - end, end - start)
    out = x[start:end].copy()
    if fade > 0:
        ramp = np.linspace(0.0, 1.0, fade)
        out[:fade] = out[:fade] * ramp + x[end:end + fade] * (1.0 - ramp)
    return out


def write(path, x):
    with wave.open(path, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(np.clip(np.round(x * 32767), -32768, 32767).astype("<i2").tobytes())


def main():
    packs = sys.argv[1]
    out_dir = sys.argv[2] if len(sys.argv) > 2 else os.path.join(HERE, "..", "..", "godot", "audio")
    os.makedirs(out_dir, exist_ok=True)
    with open(os.path.join(HERE, "audio_manifest.json"), encoding="utf-8") as f:
        manifest = json.load(f)
    for slot, entry in manifest["slots"].items():
        if entry.get("generator"):
            continue
        x = load(os.path.join(packs, entry["source"]))
        if "seconds" in entry:
            start, end = entry["seconds"]
            if entry.get("loop"):
                x = crossfaded_loop(x, int(RATE * start), int(RATE * end))
            else:
                x = x[int(RATE * start):int(RATE * end)]
        if not entry.get("loop"):
            x = fade_out(trim_lead(x))
        x = x * PEAK / (np.abs(x).max() or 1.0)
        write(os.path.join(out_dir, slot + ".wav"), x)
        print(f"{slot}: {x.size / RATE:.2f} s")


if __name__ == "__main__":
    main()
