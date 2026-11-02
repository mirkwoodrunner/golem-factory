"""The listening sheet (docs/audio-design.md §7): every slot's file, one after another, in one WAV,
and a text index of when each starts. Loops play long enough to hear the seam; the music plays
layer 1 alone, then with each further layer stacked, as the factory grows.

    python Tools/Audio/review_sheet.py <audio_dir> <out.wav>
"""
import os
import struct
import sys
import wave

RATE = 44100
GAP = 0.8
ORDER = [
    "ui.card-snap", "ui.engage", "ui.patent", "ui.claim", "ui.place", "ui.demolish", "ui.collect",
    "ui.stall-chime",
    "work.stamp", "work.fast-stamp", "work.tug", "work.heave", "work.lunge", "work.launch",
    "amb.clock", "amb.workshop", "amb.steam",
    "music.tower-fanfare",
]
LOOP_SECONDS = {"amb.workshop": 8.0, "amb.steam": 8.0}


def read(path):
    with wave.open(path) as w:
        data = w.readframes(w.getnframes())
    return [v / 32768.0 for v in struct.unpack("<%dh" % (len(data) // 2), data)]


def looped(x, seconds):
    n = int(RATE * seconds)
    return [x[i % len(x)] for i in range(n)]


def main():
    audio, out = sys.argv[1], sys.argv[2]
    sheet, index = [], []

    def add(label, x):
        index.append(f"{len(sheet) / RATE:6.1f} s  {label}")
        sheet.extend(x)
        sheet.extend([0.0] * int(RATE * GAP))

    for slot in ORDER:
        x = read(os.path.join(audio, slot + ".wav"))
        if slot == "amb.clock":
            # As it plays in the game: once per game second.
            x = sum(([*x, *[0.0] * (RATE - len(x))] for _ in range(4)), [])
        add(slot, looped(x, LOOP_SECONDS[slot]) if slot in LOOP_SECONDS else x)

    layers = [read(os.path.join(audio, f"music.layer.{i}.wav")) for i in range(1, 5)]
    for count in range(1, 5):
        stack = [sum(layer[i] for layer in layers[:count]) for i in range(len(layers[0]))]
        add("music: layer 1" if count == 1 else f"music: layers 1-{count}", stack)

    with wave.open(out, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(b"".join(struct.pack("<h", int(max(-1.0, min(1.0, v)) * 32767)) for v in sheet))
    with open(os.path.splitext(out)[0] + ".txt", "w", encoding="utf-8") as f:
        f.write("\n".join(index) + "\n")
    print("\n".join(index))


if __name__ == "__main__":
    main()
