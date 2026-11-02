"""Checks the sound files against their data (docs/audio-design.md §6). Standard library only.

- every slot in godot/data/audio.json has its file, and every file in godot/audio/ has a slot;
- every slot is in Tools/Audio/audio_manifest.json, with its pack (and the pack its licence) or
  its generator, and the manifest names no slot the data lacks;
- every file is 16-bit mono 44.1 kHz WAV with its peak at or under -1 dBFS;
- a loop's end runs into its start without a click.

    python Tools/Audio/verify_audio.py        # exit 0 when all hold
"""
import json
import math
import os
import struct
import sys
import wave

HERE = os.path.dirname(os.path.abspath(__file__))
GODOT = os.path.join(HERE, "..", "..", "godot")
AUDIO = os.path.join(GODOT, "audio")
PEAK_LIMIT_DB = -1.0 + 0.01  # a hair of rounding over the -1 dBFS the tools normalize to
CLICK_LIMIT = 0.08           # largest sample jump across a loop's seam, as a share of full scale


def samples(path):
    with wave.open(path) as w:
        shape = (w.getnchannels(), w.getsampwidth(), w.getframerate())
        data = w.readframes(w.getnframes())
    return shape, struct.unpack("<%dh" % (len(data) // 2), data) if shape[1] == 2 else ()


def main():
    problems = []
    with open(os.path.join(GODOT, "data", "audio.json"), encoding="utf-8") as f:
        slots = json.load(f)
    with open(os.path.join(HERE, "audio_manifest.json"), encoding="utf-8") as f:
        manifest = json.load(f)

    files = {name[:-4] for name in os.listdir(AUDIO) if name.endswith(".wav")}
    for name, slot in slots.items():
        if slot.get("path") != f"res://audio/{name}.wav":
            problems.append(f"{name}: path is {slot.get('path')}, not res://audio/{name}.wav")
        if name not in files:
            problems.append(f"{name}: no file in godot/audio/")
        entry = manifest["slots"].get(name)
        if entry is None:
            problems.append(f"{name}: not in audio_manifest.json")
        elif not entry.get("generator"):
            pack = entry.get("source", "").split("/")[0]
            if pack not in manifest["packs"] or not manifest["packs"][pack].get("licence"):
                problems.append(f"{name}: its pack '{pack}' has no licence in the manifest")
    for name in sorted(files - set(slots)):
        problems.append(f"godot/audio/{name}.wav: no slot in audio.json")
    for name in sorted(set(manifest["slots"]) - set(slots)):
        problems.append(f"audio_manifest.json: {name} is no slot in audio.json")

    for name in sorted(files & set(slots)):
        (channels, width, rate), x = samples(os.path.join(AUDIO, name + ".wav"))
        if (channels, width, rate) != (1, 2, 44100):
            problems.append(f"{name}: {channels} ch, {width * 8}-bit, {rate} Hz; wants mono 16-bit 44100")
            continue
        peak = max(abs(v) for v in x) / 32768.0 if x else 0.0
        peak_db = 20 * math.log10(peak) if peak > 0 else -120.0
        if peak_db > PEAK_LIMIT_DB:
            problems.append(f"{name}: peaks at {peak_db:.2f} dBFS, over -1")
        if slots[name].get("loop") and x:
            seam = abs(x[-1] - x[0]) / 32768.0
            if seam > CLICK_LIMIT:
                problems.append(f"{name}: its loop jumps {seam:.2f} of full scale at the seam")

    for p in problems:
        print(p)
    print(f"{len(slots)} slots, {len(files)} files: " + ("ok" if not problems else f"{len(problems)} problems"))
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main())
