"""Synthesise Slender's own ring sounds (no samples, nothing licensed). 24 kHz mono 16-bit WAV, about 2.5 s, loopable."""
import math
import struct
import wave
from pathlib import Path

RATE = 24000
OUT = Path(__file__).parent


def silence(sec):
    return [0.0] * int(sec * RATE)


def tone(freqs, sec, attack=0.01, release=0.05, decay=None, partials=None, am=None):
    n = int(sec * RATE)
    out = []
    for i in range(n):
        t = i / RATE
        v = 0.0
        for f in freqs:
            for mult, amp in (partials or [(1.0, 1.0)]):
                v += amp * math.sin(2 * math.pi * f * mult * t)
        v /= len(freqs) * sum(a for _, a in (partials or [(1.0, 1.0)]))
        env = min(1.0, t / attack) if attack else 1.0
        env *= min(1.0, (sec - t) / release) if release else 1.0
        if decay:
            env *= math.exp(-t / decay)
        if am:
            env *= 0.55 + 0.45 * math.sin(2 * math.pi * am * t)
        out.append(v * env)
    return out


def sweep(f0, f1, sec):
    n = int(sec * RATE)
    out, phase = [], 0.0
    for i in range(n):
        t = i / RATE
        f = f0 + (f1 - f0) * (t / sec) ** 1.6
        phase += 2 * math.pi * f / RATE
        env = min(1.0, t / 0.03) * min(1.0, (sec - t) / 0.08)
        out.append((math.sin(phase) + 0.25 * math.sin(2 * phase)) / 1.25 * env)
    return out


def mix(*parts):
    n = max(len(p) for p in parts)
    return [sum(p[i] for p in parts if i < len(p)) for i in range(n)]


def at(start, part):
    return silence(start) + part


def bell(f, sec, decay):
    return tone([f], sec, attack=0.002, release=0.05, decay=decay, partials=[(1.0, 1.0), (2.76, 0.45), (5.4, 0.2), (8.93, 0.08)])


def mallet(f, sec):
    return tone([f], sec, attack=0.002, release=0.04, decay=0.18, partials=[(1.0, 1.0), (4.0, 0.3), (9.2, 0.06)])


SOUNDS = {
    "classic": lambda: tone([400, 450], 0.4) + silence(0.2) + tone([400, 450], 0.4) + silence(1.5),
    "bell": lambda: tone([880, 1320], 1.2, attack=0.005, release=0.1, am=20, partials=[(1.0, 1.0), (2.0, 0.3)]) + silence(1.3),
    "chime": lambda: mix(at(0.0, bell(1318.5, 1.6, 0.5)), at(0.35, bell(1046.5, 1.6, 0.5)), at(0.7, bell(784.0, 1.8, 0.6))) + silence(0.2),
    "marimba": lambda: mix(at(0.0, mallet(523.3, 0.5)), at(0.14, mallet(659.3, 0.5)), at(0.28, mallet(784.0, 0.5)),
                           at(0.42, mallet(1046.5, 0.8)), at(0.9, mallet(784.0, 0.5)), at(1.04, mallet(1046.5, 0.9))) + silence(0.5),
    "soft pulse": lambda: sum((tone([660], 0.22, attack=0.06, release=0.12) + silence(0.18) for _ in range(3)), []) + silence(1.3),
    "digital": lambda: sum((tone([1000], 0.07, attack=0.003, release=0.01, partials=[(1, 1), (3, 0.3)]) + silence(0.04)
                            + tone([1250], 0.07, attack=0.003, release=0.01, partials=[(1, 1), (3, 0.3)]) + silence(0.04) for _ in range(4)), [])
                       + silence(1.6),
    "rise": lambda: sweep(520, 940, 0.55) + silence(0.15) + sweep(520, 940, 0.55) + silence(1.25),
}


def write(name, samples):
    peak = max(abs(s) for s in samples) or 1.0
    gain = 0.7 / peak
    path = OUT / f"{name.replace(' ', '-')}.wav"
    with wave.open(str(path), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(b"".join(struct.pack("<h", int(max(-1, min(1, s * gain)) * 32767)) for s in samples))
    return path


for name, make in SOUNDS.items():
    p = write(name, make())
    print(f"{p.name}: {p.stat().st_size // 1024} KB")
