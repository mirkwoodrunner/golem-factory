"""The music: four stacking layers and the Clock Tower fanfare (docs/audio-design.md §4, decision 2).

One key (D minor), one tempo (90 bpm), one length (4 bars, 10.67 s), so the layers loop together
and any set of them sounds finished. Layer 1 always plays; the others fade in as the factory
grows (AudioRules.MusicLayers). Standard library only, reusing synth_samples' building blocks.

    python Tools/Audio/synth_music.py [out_dir]     # default godot/audio/
"""
import math
import os
import random
import sys

sys.path.insert(0, os.path.dirname(__file__))
from synth_samples import (RATE, at, bandpass, envelope, apply, mix, noise, scale,  # noqa: E402
                           tone, write_wav)

BEAT = 60 / 90
BAR = BEAT * 4
LENGTH = BAR * 4

# Dm, Bb, F, C: a falling, unhurried loop.
CHORDS = [
    (146.83, 174.61, 220.00),
    (116.54, 146.83, 174.61),
    (130.81, 174.61, 220.00),
    (130.81, 164.81, 196.00),
]
ROOTS = [73.42, 58.27, 87.31, 65.41]
ARPS = [
    (587.33, 698.46, 880.00, 698.46),
    (466.16, 587.33, 698.46, 587.33),
    (523.25, 698.46, 880.00, 698.46),
    (523.25, 659.25, 783.99, 659.25),
]
# The counter-melody: one long note or two per bar, high and sparse.
MELODY = [
    [(0, 2, 1174.66), (2, 2, 1046.50)],
    [(0, 3, 932.33), (3, 1, 880.00)],
    [(0, 2, 880.00), (2, 2, 1046.50)],
    [(0, 4, 987.77)],
]

MUSIC_BOX = ((1, 1.0), (3, 0.25), (5.2, 0.08))
CELESTA = ((1, 1.0), (2, 0.35), (4.1, 0.1))


def wrap(track):
    """Folds anything past the loop's end back onto its start, so a tail rings across the seam."""
    n = int(RATE * LENGTH)
    out = track[:n] + [0.0] * max(0, n - len(track))
    for i, v in enumerate(track[n:]):
        out[i % n] += v
    return out


def place(note, start):
    """A note at `start` seconds in a buffer the loop's length plus a tail, for wrap()."""
    return at(note, start, LENGTH + 3.0)


def layer1():
    """The pad and the music-box arpeggio: calm on its own."""
    tracks = []
    for bar, (chord, arp) in enumerate(zip(CHORDS, ARPS)):
        start = bar * BAR
        for f in chord:
            pad = tone(f, BAR + 1.0, attack=0.5, decay=1.8, partials=((1, 1.0), (2, 0.2)), detune=0.002)
            tracks.append(place(scale(pad, 0.10), start))
        for step in range(8):
            note = tone(arp[step % 4], BEAT * 1.5, attack=0.002, decay=0.3, partials=MUSIC_BOX)
            tracks.append(place(scale(note, 0.14), start + step * BEAT / 2))
    return wrap(mix(*tracks))


def layer2():
    """A soft plucked bass on the downbeats: the factory has a pulse."""
    tracks = []
    for bar, root in enumerate(ROOTS):
        for beat, mult in ((0, 1.0), (2, 1.5), (3, 1.0)):
            note = tone(root * mult, BEAT * 1.5, attack=0.004, decay=0.35, partials=((1, 1.0), (2, 0.5), (3, 0.15)))
            tracks.append(place(scale(note, 0.30 if beat == 0 else 0.2), bar * BAR + beat * BEAT))
    return wrap(mix(*tracks))


def layer3(rng):
    """Clockwork percussion: a wooden tick on every beat, a lighter tock off it, a ratchet per bar."""
    tracks = []
    tick_len = 0.08
    for bar in range(4):
        for beat in range(4):
            t0 = bar * BAR + beat * BEAT
            n = int(RATE * tick_len)
            tick = mix(tone(1800, tick_len, attack=0.0005, decay=0.012, partials=((1, 1.0), (2.4, 0.3))),
                       apply(bandpass(noise(tick_len, rng), 2000, 7000), envelope(n, 0.0005, 0.006)))
            tracks.append(place(scale(tick, 0.16), t0))
            tock = tone(1200, tick_len, attack=0.0005, decay=0.01, partials=((1, 1.0), (2.7, 0.2)))
            tracks.append(place(scale(tock, 0.07), t0 + BEAT / 2))
        # A little ratchet on the last beat of the bar: four quick clicks.
        for k in range(4):
            n = int(RATE * 0.03)
            click = apply(bandpass(noise(0.03, rng), 3000, 9000), envelope(n, 0.0003, 0.004))
            tracks.append(place(scale(click, 0.06), bar * BAR + 3.5 * BEAT + k * BEAT / 8))
    return wrap(mix(*tracks))


def layer4():
    """The counter-melody on a celesta: the big factory sings."""
    tracks = []
    for bar, notes in enumerate(MELODY):
        for beat, beats, f in notes:
            note = tone(f, beats * BEAT + 1.0, attack=0.01, decay=0.9, partials=CELESTA, detune=0.001)
            tracks.append(place(scale(note, 0.11), bar * BAR + beat * BEAT))
    return wrap(mix(*tracks))


def fanfare():
    """A Clock Tower stage: a rising D-major arpeggio on bells, then a held chord. About 3.5 s."""
    bell = ((1, 1.0), (2.0, 0.4), (2.76, 0.25), (5.4, 0.1))
    tracks = []
    for k, f in enumerate((293.66, 369.99, 440.00, 587.33)):
        tracks.append(at(scale(tone(f, 2.5, attack=0.003, decay=0.6, partials=bell), 0.25), k * 0.16, 4.0))
    for f in (293.66, 369.99, 440.00, 587.33, 739.99):
        held = tone(f, 3.2, attack=0.02, decay=1.4, partials=((1, 1.0), (2, 0.3), (3, 0.12)), detune=0.002)
        tracks.append(at(scale(held, 0.12), 0.7, 4.0))
    return mix(*tracks)


def normalized(x, peak_db=-3.0):
    return scale(x, 10 ** (peak_db / 20))


def main():
    out_dir = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(__file__), "..", "..", "godot", "audio")
    os.makedirs(out_dir, exist_ok=True)
    rng = random.Random("music")
    layers = [layer1(), layer2(), layer3(rng), layer4()]
    # One gain for all four, set by their sum, so the stack never clips and the balance holds.
    total = mix(*layers)
    gain = 10 ** (-3.0 / 20) / (max(abs(v) for v in total) or 1.0)
    for i, layer in enumerate(layers, start=1):
        write_wav(os.path.join(out_dir, f"music.layer.{i}.wav"), [v * gain for v in layer])
    write_wav(os.path.join(out_dir, "music.tower-fanfare.wav"), normalized(fanfare()))
    print(f"wrote 4 layers and the fanfare to {out_dir}")


if __name__ == "__main__":
    main()
