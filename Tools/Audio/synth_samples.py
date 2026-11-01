"""Synthesized placeholder samples for the source comparison (docs/audio-design.md §9, decision 1).

Five sounds, the same five every source is sampled on: the Presser's stamp, the card snap, the
stall chime, the steam hiss (a loop) and a music loop. Standard library only (no numpy here):
noise, envelopes and tones, summed and written as 16-bit mono WAV at 44.1 kHz.

    python Tools/Audio/synth_samples.py [out_dir]     # default public/audio/samples/synth/
"""
import math
import os
import random
import struct
import sys
import wave

RATE = 44100


def silence(seconds):
    return [0.0] * int(RATE * seconds)


def mix(*tracks):
    n = max(len(t) for t in tracks)
    out = [0.0] * n
    for t in tracks:
        for i, v in enumerate(t):
            out[i] += v
    return out


def at(track, seconds, length):
    """`track` placed `seconds` into a buffer `length` seconds long."""
    out = silence(length)
    start = int(RATE * seconds)
    for i, v in enumerate(track):
        if start + i < len(out):
            out[start + i] += v
    return out


def envelope(n, attack, decay):
    """Linear attack then exponential decay, in seconds."""
    a = max(1, int(RATE * attack))
    out = []
    for i in range(n):
        if i < a:
            out.append(i / a)
        else:
            out.append(math.exp(-(i - a) / (RATE * decay)))
    return out


def tone(freq, seconds, attack=0.005, decay=0.3, partials=((1, 1.0),), detune=0.0):
    n = int(RATE * seconds)
    env = envelope(n, attack, decay)
    out = []
    for i in range(n):
        t = i / RATE
        v = sum(amp * math.sin(2 * math.pi * freq * mult * (1 + detune * k) * t)
                for k, (mult, amp) in enumerate(partials))
        out.append(v * env[i])
    return out


def noise(seconds, rng):
    return [rng.uniform(-1.0, 1.0) for _ in range(int(RATE * seconds))]


def lowpass(x, cutoff):
    """One-pole low-pass."""
    k = 1 - math.exp(-2 * math.pi * cutoff / RATE)
    y, out = 0.0, []
    for v in x:
        y += k * (v - y)
        out.append(y)
    return out


def highpass(x, cutoff):
    low = lowpass(x, cutoff)
    return [a - b for a, b in zip(x, low)]


def bandpass(x, low, high):
    return lowpass(highpass(x, low), high)


def scale(x, peak):
    m = max(abs(v) for v in x) or 1.0
    return [v * peak / m for v in x]


def apply(x, env):
    return [a * b for a, b in zip(x, env)]


# --- The five sounds ---------------------------------------------------------------------------

def stamp(rng):
    """The Presser's stamp: a low thud, a metallic clank on top, a short ring."""
    n = int(RATE * 0.45)
    thud = apply(lowpass(noise(0.45, rng), 180), envelope(n, 0.002, 0.05))
    body = tone(70, 0.45, attack=0.001, decay=0.08, partials=((1, 1.0), (2.1, 0.3)))
    clank = apply(bandpass(noise(0.45, rng), 1800, 5000), envelope(n, 0.001, 0.025))
    ring = tone(523, 0.45, attack=0.001, decay=0.12, partials=((1, 0.5), (2.76, 0.35), (5.4, 0.2)))
    return scale(mix(scale(thud, 1.0), scale(body, 0.7), scale(clank, 0.5), scale(ring, 0.18)), 0.85)


def card_snap(rng):
    """A card seating in a socket: a sharp click and a little wooden knock."""
    n = int(RATE * 0.15)
    click = apply(bandpass(noise(0.15, rng), 2500, 9000), envelope(n, 0.0005, 0.006))
    knock = tone(420, 0.15, attack=0.001, decay=0.025, partials=((1, 1.0), (2.3, 0.4)))
    second = at(scale(apply(bandpass(noise(0.05, rng), 3000, 8000),
                            envelope(int(RATE * 0.05), 0.0005, 0.004)), 0.4), 0.018, 0.15)
    return scale(mix(scale(click, 0.8), scale(knock, 0.6), second), 0.7)


def stall_chime(rng):
    """Two bell strikes, falling a minor third: something needs you."""
    bell = ((1, 1.0), (2.0, 0.45), (2.76, 0.3), (5.4, 0.12))
    first = tone(988, 1.0, attack=0.002, decay=0.35, partials=bell)
    second = tone(831, 1.2, attack=0.002, decay=0.45, partials=bell)
    return scale(mix(at(first, 0.0, 1.5), at(second, 0.22, 1.5)), 0.6)


def steam_hiss(rng):
    """Four seconds of hiss that loops: shaped noise with a slow flutter, crossfaded end to start."""
    seconds, fade = 4.0, 0.5
    raw = bandpass(noise(seconds + fade, rng), 2500, 9000)
    flutter = [0.8 + 0.2 * math.sin(2 * math.pi * 0.7 * i / RATE)
               * math.sin(2 * math.pi * 0.25 * i / RATE) for i in range(len(raw))]
    x = apply(raw, flutter)
    n, f = int(RATE * seconds), int(RATE * fade)
    # Crossfade the tail into the head so the loop has no seam.
    out = x[:n]
    for i in range(f):
        w = i / f
        out[i] = out[i] * w + x[n + i] * (1 - w)
    return scale(out, 0.35)


def music_loop(rng):
    """Eight seconds at 90 bpm in D minor: a soft pad and a music-box arpeggio. Loops cleanly."""
    beat = 60 / 90
    length = beat * 12  # 3 bars of 4: 8 s
    chords = [(146.83, 174.61, 220.0), (116.54, 146.83, 174.61), (130.81, 164.81, 196.0)]  # Dm Bb C
    arps = [(587.33, 698.46, 880.0, 698.46), (466.16, 587.33, 698.46, 587.33),
            (523.25, 659.25, 783.99, 659.25)]
    tracks = []
    for bar, (chord, arp) in enumerate(zip(chords, arps)):
        start = bar * beat * 4
        for f in chord:
            pad = tone(f, beat * 4, attack=0.4, decay=1.6, partials=((1, 1.0), (2, 0.2)), detune=0.002)
            tracks.append(at(scale(pad, 0.12), start, length))
        for step in range(8):
            note = tone(arp[step % 4], beat, attack=0.002, decay=0.25,
                        partials=((1, 1.0), (3, 0.25), (5.2, 0.08)))
            tracks.append(at(scale(note, 0.16), start + step * beat / 2, length))
    return scale(mix(*tracks), 0.6)


SOUNDS = {
    "work.presser.stamp": stamp,
    "ui.card-snap": card_snap,
    "ui.stall-chime": stall_chime,
    "amb.steam": steam_hiss,
    "music.layer.1": music_loop,
}


def write_wav(path, samples):
    with wave.open(path, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(b"".join(struct.pack("<h", int(max(-1.0, min(1.0, v)) * 32767)) for v in samples))


def main():
    out_dir = sys.argv[1] if len(sys.argv) > 1 else "public/audio/samples/synth"
    os.makedirs(out_dir, exist_ok=True)
    sheet = []
    for slot, make in SOUNDS.items():
        samples = make(random.Random(slot))
        write_wav(os.path.join(out_dir, slot + ".wav"), samples)
        # On the sheet a loop plays twice, so its seam can be heard.
        sheet += samples + (samples if slot in ("amb.steam", "music.layer.1") else []) + silence(0.8)
    write_wav(os.path.join(out_dir, "_sheet.wav"), sheet)
    print(f"wrote {len(SOUNDS)} sounds and _sheet.wav to {out_dir}")


if __name__ == "__main__":
    main()
