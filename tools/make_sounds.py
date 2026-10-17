#!/usr/bin/env python3
"""Synthesises AVABungelingBay's sound effects and tunes into ../src/AVABungelingBay/Sounds as 16-bit mono WAV.

Nothing is sampled from any game: every effect is built here from square, triangle and noise voices, and the
tunes are new compositions: a patrol march over the beat of the rotor, and a fanfare for the title.
Run it from anywhere:
    python3 tools/make_sounds.py
"""
import math
import os
import random
import struct
import wave



RATE = 22050
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "src", "AVABungelingBay", "Sounds")
RNG = random.Random(1984)


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




def main():
    os.makedirs(OUT, exist_ok=True)
    for f in os.listdir(OUT):
        if f.endswith(".wav"):
            os.remove(os.path.join(OUT, f))
    # The helicopter.
    write("Gun.wav", mix(tone(0.05, 1600, 600, "noise", attack=0.001, release=1.5), tone(0.04, 300, 150, "square", release=1.2, gain=0.5)), 0.16)
    write("BombDrop.wav", tone(0.5, 1800, 500, "sine", attack=0.005, release=0.3, gain=0.5), 0.16)
    write("Explode.wav", mix(tone(0.5, 1800, 120, "noise", attack=0.001, release=0.9), tone(0.4, 140, 40, "sine", release=0.8, gain=0.8)), 0.42)
    write("Splash.wav", tone(0.45, 3000, 700, "noise", attack=0.01, release=0.8, gain=0.7), 0.28)
    write("FactoryDown.wav", cat(mix(tone(1.4, 2200, 60, "noise", attack=0.002, release=0.5), tone(1.2, 110, 25, "sine", release=0.6, gain=0.9)),
                                 *[tone(0.08, hz(n), kind="pulse", release=0.3) for n in ["C5", "E5", "G5", "C6"]]), 0.55)
    write("GunDown.wav", mix(tone(0.3, 1500, 200, "noise", attack=0.001, release=1.0), tone(0.2, 400, 120, "square", release=1.0, gain=0.5)), 0.32)
    write("JetDown.wav", cat(tone(0.4, 1400, 200, "saw", release=0.5, gain=0.6), mix(tone(0.4, 2000, 100, "noise", release=0.8), tone(0.3, 160, 50, "sine", release=0.8))), 0.36)
    write("Clang.wav", mix(tone(0.08, 2400, 1800, "square", attack=0.001, release=1.5, gain=0.4), tone(0.06, 3000, 1000, "noise", release=1.5, gain=0.4)), 0.16)
    write("Hurt.wav", mix(tone(0.12, 600, 200, "square", attack=0.001, release=1.0, gain=0.6), tone(0.1, 2500, 500, "noise", release=1.2, gain=0.6)), 0.3)
    write("FlakFire.wav", mix(tone(0.12, 260, 90, "square", attack=0.001, release=1.2, gain=0.5), tone(0.1, 1200, 300, "noise", release=1.4, gain=0.5)), 0.18)
    write("Burst.wav", mix(tone(0.3, 1400, 200, "noise", attack=0.001, release=1.0), tone(0.2, 180, 70, "sine", release=1.0, gain=0.6)), 0.26)
    write("JetUp.wav", tone(1.0, 300, 1400, "saw", attack=0.2, vibrato=0.02, vib_rate=30, release=0.6, gain=0.4), 0.22)
    write("JetFire.wav", cat(*[cat(tone(0.03, 900, 400, "square", attack=0.001, release=1.0, gain=0.5), silence(0.03)) for _ in range(3)]), 0.18)
    # The battleship.
    write("ShipLaunched.wav", cat(*[tone(0.5, 180, kind="saw", vibrato=0.01, release=0.2, gain=0.6) for _ in range(2)], tone(1.0, 140, kind="saw", vibrato=0.01, release=0.5, gain=0.6)), 0.4)
    write("ShipSunk.wav", cat(mix(tone(1.6, 2000, 50, "noise", attack=0.002, release=0.4), tone(1.4, 120, 20, "sine", release=0.5)), tone(1.2, 900, 120, "sine", vibrato=0.1, vib_rate=5, release=0.6, gain=0.4)), 0.55)
    write("Shell.wav", cat(tone(0.25, 2400, 600, "sine", release=0.2, gain=0.3), mix(tone(0.4, 1600, 100, "noise", release=0.8), tone(0.3, 120, 40, "sine", release=0.8))), 0.36)
    write("Crash.wav", cat(tone(0.6, 900, 120, "saw", vibrato=0.15, vib_rate=12, release=0.4, gain=0.6), mix(tone(1.0, 2200, 60, "noise", release=0.6), tone(0.8, 120, 30, "sine", release=0.6))), 0.5)
    write("Land.wav", cat(*[tone(0.06, hz(n), kind="tri", release=0.3) for n in ["G4", "C5", "E5"]]), 0.24)
    write("TakeOff.wav", tone(0.6, 200, 520, "saw", attack=0.05, vibrato=0.2, vib_rate=16, release=0.5, gain=0.4), 0.2)
    write("Load.wav", tone(0.04, 700, 900, "square", attack=0.001, release=1.0, gain=0.4), 0.1)
    write("Warning.wav", cat(*[tone(0.35, 600, 900, "square", release=0.1, gain=0.5) for _ in range(3)]), 0.24)
    write("ExtraLife.wav", cat(*[tone(0.07, hz(n), kind="pulse", release=0.3) for n in ["G5", "B5", "D6", "G6", "D6", "G6"]]), 0.3)

    # The patrol march (an original composition), with the rotor chopping underneath.
    e = 0.5
    lead = [("D5", 1), ("A4", e), ("D5", e), ("F5", 1), ("E5", e), ("D5", e), ("C5", 1), ("A4", 1), ("D5", 2),
            ("F5", 1), ("G5", e), ("F5", e), ("E5", 1), ("C5", e), ("E5", e), ("D5", 1), ("C#5", 1), ("D5", 2)]
    bass = [("D3", e), ("D3", e), ("A2", e), ("A2", e)] * 4 + [("D3", e), ("D3", e), ("C3", e), ("C3", e), ("A#2", e), ("A#2", e), ("A2", e), ("A2", e)] * 2
    march = tune(150, lead * 2, bass * 2, kind="pulse")
    beat = 60.0 / 150
    chop = cat(*[cat(tone(beat / 4, 120, 80, "noise", attack=0.002, release=2.0, gain=0.35), silence(beat / 4)) for _ in range(len(march) * 2 // int(RATE * beat) + 2)])
    write("Music.wav", mix(march, chop[: len(march)]), 0.26)
    fanfare = [("D5", 0.5), ("D5", 0.5), ("D5", 0.5), ("A5", 1.5), ("G5", 0.5), ("F5", 0.5), ("E5", 0.5), ("D5", 1), ("A4", 1),
               ("D5", 0.5), ("F5", 0.5), ("A5", 0.5), ("D6", 2), ("C6", 0.5), ("A5", 1), ("D6", 3)]
    fbass = [("D3", 2), ("A2", 2), ("D3", 2), ("A2", 2), ("D3", 2), ("F2", 1), ("A2", 2), ("D3", 3)]
    write("TitleTune.wav", cat(tune(120, fanfare, fbass, kind="square"), silence(0.8)), 0.34)
    write("Cleared.wav", tune(150, [("A4", 0.5), ("D5", 0.5), ("F5", 0.5), ("A5", 0.5), ("D6", 2)], [("D3", 1), ("A2", 1), ("D3", 2)], kind="pulse"), 0.36)
    write("Victory.wav", tune(150, [("D5", 0.5), ("F#5", 0.5), ("A5", 0.5), ("D6", 1.5), ("C#6", 0.5), ("D6", 0.5), ("E6", 0.5), ("F#6", 3)],
                              [("D3", 1), ("A2", 1), ("B2", 1), ("G2", 1), ("A2", 1), ("D3", 2.5)], kind="pulse"), 0.4)
    write("GameOver.wav", tune(66, [("A4", 1), ("F4", 1), ("E4", 1), ("C#4", 1), ("D4", 4)], [("D3", 2), ("A#2", 2), ("A2", 2), ("D2", 2)], kind="tri"), 0.42)


if __name__ == "__main__":
    main()
