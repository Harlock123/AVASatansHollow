#!/usr/bin/env python3
"""Synthesises AVASatansHollow's sound effects and tunes into ../src/AVASatansHollow/Sounds as 16-bit mono WAV.

Nothing is sampled from any arcade machine: every effect is built here from square, triangle and
noise voices, and the tunes are new compositions. Run it from anywhere:
    python3 tools/make_sounds.py
"""
import math
import os
import random
import struct
import wave

RATE = 22050
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "src", "AVASatansHollow", "Sounds")
RNG = random.Random(1982)


def write(name, samples, volume=0.7):
    peak = max(1e-9, max(abs(s) for s in samples))
    with wave.open(os.path.join(OUT, name), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(b"".join(struct.pack("<h", int(s / peak * volume * 32767)) for s in samples))


def voice(kind, phase):
    p = phase % 1.0
    if kind == "square":
        return 1.0 if p < 0.5 else -1.0
    if kind == "pulse":
        return 1.0 if p < 0.25 else -1.0
    if kind == "tri":
        return 4 * p - 1 if p < 0.5 else 3 - 4 * p
    if kind == "saw":
        return 2 * p - 1
    if kind == "sine":
        return math.sin(2 * math.pi * p)
    return RNG.uniform(-1, 1)


def tone(seconds, f0, f1=None, kind="square", attack=0.004, release=1.0, vibrato=0.0, vib_rate=8.0, gain=1.0):
    """A note gliding from f0 to f1 (exponentially), with a short attack and a shaped decay."""
    n = int(RATE * seconds)
    f1 = f1 or f0
    out, phase, held = [], 0.0, 0.0
    for i in range(n):
        t = i / RATE
        f = f0 * (f1 / f0) ** (i / max(1, n - 1))
        if vibrato:
            f *= 1 + vibrato * math.sin(2 * math.pi * vib_rate * t)
        phase += f / RATE
        if kind == "noise":
            # Noise re-sampled at the voice's rate: a lower 'pitch' sounds rougher.
            if int(phase) != int(phase - f / RATE):
                held = RNG.uniform(-1, 1)
            s = held
        else:
            s = voice(kind, phase)
        env = min(1.0, t / attack) * (1 - i / n) ** release
        out.append(s * env * gain)
    return out


def silence(seconds):
    return [0.0] * int(RATE * seconds)


def cat(*parts):
    out = []
    for p in parts:
        out.extend(p)
    return out


def mix(*parts):
    n = max(len(p) for p in parts)
    out = [0.0] * n
    for p in parts:
        for i, v in enumerate(p):
            out[i] += v
    return out


NOTES = {"C": 0, "D": 2, "E": 4, "F": 5, "G": 7, "A": 9, "B": 11}


def hz(note):
    n = NOTES[note[0]]
    rest = note[1:]
    while rest and rest[0] in "#b":
        n += 1 if rest[0] == "#" else -1
        rest = rest[1:]
    return 440.0 * 2 ** ((12 * (int(rest) + 1) + n - 69) / 12)


def tune(bpm, lead, bass, kind="pulse"):
    """Two voices: a lead and a bass line, as (note, beats) lists; '-' is a rest."""
    beat = 60.0 / bpm

    def line(notes, k, gain, release):
        out = []
        for note, beats in notes:
            d = beats * beat
            out.extend(silence(d) if note == "-" else tone(d, hz(note), kind=k, release=release, gain=gain))
        return out

    return mix(line(lead, kind, 0.55, 0.6), line(bass, "tri", 0.6, 0.3))


def seq(bpm, notes, kind="pulse", gain=0.55, release=0.6):
    beat = 60.0 / bpm
    out = []
    for note, beats in notes:
        d = beats * beat
        out.extend(silence(d) if note == "-" else tone(d, hz(note), kind=kind, release=release, gain=gain))
    return out




def laugh(syllables=4, base=90):
    """The Devil's laugh: deep 'ha' after 'ha', each a growl falling in pitch, with a breathy edge."""
    out = []
    for k in range(syllables):
        n = int(RATE * 0.2)
        f0 = base * (1.15 - 0.06 * k)
        phase = 0.0
        for i in range(n):
            t = i / RATE
            f = f0 * (1 - 0.25 * t / 0.2)
            phase += f / RATE
            saw = 2 * (phase % 1) - 1
            env = math.sin(math.pi * min(1, t / 0.2)) ** 0.7
            out.append(env * (0.7 * saw * (0.6 + 0.4 * math.sin(2 * math.pi * 650 * t)) + 0.15 * (RNG.random() * 2 - 1)))
        out.extend(silence(0.07))
    return out


def main():
    os.makedirs(OUT, exist_ok=True)
    for f in os.listdir(OUT):
        if f.endswith(".wav"):
            os.remove(os.path.join(OUT, f))
    # The cannon's bolt; the demons' fireballs, a soft whoosh.
    write("Fire.wav", mix(tone(0.12, 1400, 500, "square", release=1.0), tone(0.06, 3500, 1500, "noise", release=1.0, gain=0.3)), 0.24)
    write("Fireball.wav", mix(tone(0.18, 2000, 600, "noise", release=0.8), tone(0.15, 300, 200, "tri", release=0.8, gain=0.5)), 0.2)
    write("Hit.wav", mix(tone(0.22, 3000, 200, "noise", release=0.9), tone(0.18, 500, 120, "saw", release=0.8, gain=0.6)), 0.38)
    write("ImpHit.wav", mix(tone(0.18, 3500, 300, "noise", release=0.9), tone(0.16, 1400, 2600, "square", release=0.7, gain=0.5)), 0.36)
    # A swoop: a falling, leathery whistle.
    write("Dive.wav", mix(tone(0.8, 1100, 300, "tri", release=0.4, vibrato=0.06, vib_rate=9), tone(0.8, 800, 400, "noise", release=0.4, gain=0.15)), 0.22)
    write("Shield.wav", tone(1.5, 300, 700, "sine", attack=0.02, release=0.3, vibrato=0.05, vib_rate=22), 0.34)
    write("ShieldBlock.wav", tone(0.08, 2200, 1800, "tri", release=0.6), 0.26)
    # The bridge: a chime for a piece, a clunk to pick it up, a thud to lay it, and a fanfare when it's done.
    write("PieceReady.wav", cat(*[tone(0.1, hz(n), kind="tri", release=0.5) for n in ["D5", "A5", "D6"]]), 0.34)
    write("PickUp.wav", cat(tone(0.06, 300, 200, "square", release=0.8), tone(0.08, 600, 900, "pulse", release=0.6)), 0.3)
    write("LayPiece.wav", mix(tone(0.25, 140, 60, "square", release=0.8), tone(0.15, 900, 300, "noise", release=0.8, gain=0.5)), 0.4)
    write("BridgeDone.wav", tune(170, [("D5", 0.5), ("F5", 0.5), ("A5", 0.5), ("D6", 1.5)], [("D3", 1), ("A2", 2)]), 0.42)
    write("Cross.wav", tone(1.2, 900, 120, "saw", release=0.5, vibrato=0.08, vib_rate=6), 0.3)
    # The Devil: a rumbling rise, grunts when he's hit, his laugh, the whirr of a thrown pitchfork, and his end.
    write("DevilRise.wav", mix(tone(2.4, 40, 90, "saw", attack=0.4, release=0.4, vibrato=0.1, vib_rate=5), tone(2.4, 300, 900, "noise", attack=0.4, release=0.5, gain=0.4)), 0.5)
    write("DevilHit.wav", mix(tone(0.14, 180, 90, "square", release=0.8), tone(0.1, 2500, 800, "noise", release=0.9, gain=0.5)), 0.38)
    write("DevilLaugh.wav", laugh(), 0.42)
    write("Pitchfork.wav", tone(0.7, 600, 300, "tri", release=0.3, vibrato=0.3, vib_rate=14), 0.24)
    write("DevilDown.wav", mix(tone(3.0, 4000, 40, "noise", release=0.6), tone(3.0, 120, 20, "saw", release=0.5, gain=0.8),
                               cat(*[tone(0.22, f, f * 0.5, "square", release=0.6, gain=0.5) for f in [700, 590, 500, 420, 350, 300, 250, 210]])), 0.55)
    write("Death.wav", cat(mix(tone(0.9, 3500, 90, "noise", release=0.6), tone(0.9, 420, 40, "saw", release=0.6, gain=0.7)),
                           *[tone(0.14, hz(n), kind="tri", release=0.4) for n in ["A4", "F4", "D4", "A3"]]), 0.5)
    write("WaveStart.wav", cat(*[tone(0.08, hz(n), kind="pulse", release=0.4) for n in ["D4", "F4", "A4"]]), 0.32)
    write("ExtraLife.wav", cat(*[tone(0.07, hz(n), kind="pulse", release=0.3) for n in ["D5", "F#5", "A5", "D6", "A5", "D6"]]), 0.4)

    # The Hollow: a low rumble of lava, bubbling, with a far-off wind. Loops (four seconds, whole cycles).
    n = int(RATE * 4)
    rumble, low = [], 0.0
    rng = random.Random(3)
    for i in range(n):
        t = i / RATE
        low += (rng.random() * 2 - 1 - low) * 0.02
        bubble = 0.5 + 0.5 * math.sin(2 * math.pi * 2 * t) * math.sin(2 * math.pi * 0.75 * t)
        rumble.append(2.5 * low + 0.25 * bubble * math.sin(2 * math.pi * 49 * t) + 0.04 * math.sin(2 * math.pi * (330 + 25 * math.sin(2 * math.pi * 0.25 * t)) * t))
    write("Music.wav", rumble, 0.3)

    # The title: a dark, whirling tarantella in D minor (an original tune).
    lead = [("D5", 0.5), ("A4", 0.5), ("D5", 0.5), ("F5", 0.5), ("E5", 0.5), ("D5", 0.5),
            ("C#5", 0.5), ("A4", 0.5), ("C#5", 0.5), ("E5", 0.5), ("D5", 0.5), ("C#5", 0.5),
            ("D5", 0.5), ("F5", 0.5), ("A5", 0.5), ("G5", 0.5), ("F5", 0.5), ("E5", 0.5),
            ("F5", 0.5), ("E5", 0.5), ("D5", 0.5), ("C#5", 0.5), ("A4", 0.5), ("C#5", 0.5),
            ("D5", 0.5), ("A4", 0.5), ("D5", 0.5), ("F5", 0.5), ("A5", 0.5), ("D6", 0.5),
            ("C#6", 0.5), ("A5", 0.5), ("E5", 0.5), ("C#5", 0.5), ("E5", 0.5), ("A5", 0.5),
            ("Bb5", 0.5), ("A5", 0.5), ("G5", 0.5), ("F5", 0.5), ("E5", 0.5), ("C#5", 0.5),
            ("D5", 3)]
    bass = [("D3", 1.5), ("A2", 1.5), ("A2", 1.5), ("E3", 1.5), ("D3", 1.5), ("F3", 1.5), ("A2", 1.5), ("A2", 1.5),
            ("D3", 1.5), ("F3", 1.5), ("A2", 1.5), ("C#3", 1.5), ("G2", 1.5), ("A2", 1.5), ("D3", 3)]
    write("TitleTune.wav", cat(tune(200, lead, bass, kind="pulse"), silence(1.2)), 0.42)
    write("StartTune.wav", tune(160, [("A4", 0.5), ("D5", 0.5), ("F5", 0.5), ("E5", 0.5), ("C#5", 0.5), ("D5", 2)], [("D3", 1), ("A2", 1), ("D3", 2)], kind="pulse"), 0.45)
    write("GameOver.wav", tune(76, [("A4", 1), ("F4", 1), ("E4", 1), ("C#4", 1), ("D4", 4)], [("D3", 2), ("Bb2", 2), ("A2", 2), ("D2", 2)], kind="tri"), 0.45)


if __name__ == "__main__":
    main()
