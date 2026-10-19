#!/usr/bin/env python3
"""Procedural audio for Ricochet Depths (clean room, all original synthesis).

Usage:  python3 tools/gen.py            # render masters/*.wav, encode sfx/ music/ *.mp3, write manifest.json
Needs:  numpy, scipy, lame on PATH.
Every sound is built from oscillators, noise and filters defined in this file. No samples are loaded.
"""
import json, os, subprocess, sys
import numpy as np
import scipy.signal as ss
from scipy.io import wavfile

SR = 32000
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
RNG = np.random.default_rng(20261019)

# ---------------------------------------------------------------- primitives

def n_of(d): return max(1, int(round(d * SR)))
def tt(d): return np.arange(n_of(d)) / SR
def noise(d): return RNG.uniform(-1, 1, n_of(d))

def expenv(d, tau, attack=0.002):
    t = tt(d)
    e = np.exp(-t / tau)
    a = n_of(attack)
    if a > 1: e[:a] *= np.linspace(0, 1, a)
    return e

def adsr(d, a, dcy, s, r):
    n = n_of(d); e = np.zeros(n)
    ia, idc, ir = n_of(a), n_of(dcy), n_of(r)
    ia = min(ia, n); e[:ia] = np.linspace(0, 1, ia)
    j = min(n, ia + idc); e[ia:j] = np.linspace(1, s, j - ia)
    e[j:] = s
    if ir < n: e[n - ir:] *= np.linspace(1, 0, ir)
    return e

def glide(f0, f1, d, curve='exp'):
    x = np.linspace(0, 1, n_of(d))
    if curve == 'exp': return f0 * (f1 / f0) ** x
    return f0 + (f1 - f0) * x

def osc(freq, d=None, shape='sine', phase=0.0):
    f = np.full(n_of(d), float(freq)) if np.isscalar(freq) else np.asarray(freq, float)
    ph = 2 * np.pi * np.cumsum(f) / SR + phase
    if shape == 'sine': return np.sin(ph)
    if shape == 'tri': return 2 / np.pi * np.arcsin(np.sin(ph))
    if shape == 'square':  # band-limited-ish: sum of odd harmonics under nyquist
        out = np.zeros_like(ph)
        for k in range(1, 40, 2):
            m = (k * f) < SR / 2.2
            out += m * np.sin(k * ph) / k
        return out * 4 / np.pi
    if shape == 'saw':
        out = np.zeros_like(ph)
        for k in range(1, 60):
            m = (k * f) < SR / 2.2
            if not m.any(): break
            out += m * np.sin(k * ph) / k
        return out * 2 / np.pi
    raise ValueError(shape)

def _sos(kind, fc, order=2, q=None):
    nyq = SR / 2
    if kind == 'band':
        lo, hi = fc
        return ss.butter(order, [max(10, lo) / nyq, min(hi, nyq * 0.95) / nyq], btype='band', output='sos')
    return ss.butter(order, min(fc, nyq * 0.95) / nyq, btype=kind, output='sos')

def lp(x, fc, order=2): return ss.sosfilt(_sos('low', fc, order), x)
def hp(x, fc, order=2): return ss.sosfilt(_sos('high', fc, order), x)
def bp(x, lo, hi, order=2): return ss.sosfilt(_sos('band', (lo, hi), order), x)

def sweep_lp(x, f0, f1):
    """Time-varying one-pole lowpass (cutoff glides f0->f1)."""
    fc = glide(f0, f1, len(x) / SR)[:len(x)]
    a = np.exp(-2 * np.pi * fc / SR)
    y = np.zeros_like(x); z = 0.0
    for i in range(len(x)):
        z = (1 - a[i]) * x[i] + a[i] * z
        y[i] = z
    return y

def mix(*xs):
    n = max(len(x) for x in xs)
    out = np.zeros(n)
    for x in xs: out[:len(x)] += x
    return out

def place(buf, x, at):
    i = int(round(at * SR))
    if i >= len(buf): return buf
    j = min(len(buf), i + len(x))
    buf[i:j] += x[:j - i]
    return buf

def pad(x, d):
    n = n_of(d)
    return np.concatenate([x, np.zeros(max(0, n - len(x)))])[:n]

def sat(x, drive=2.0): return np.tanh(x * drive) / np.tanh(drive)

def fade(x, fin=0.001, fout=0.01):
    x = x.copy()
    a, b = n_of(fin), n_of(fout)
    x[:a] *= np.linspace(0, 1, a)
    x[-b:] *= np.linspace(1, 0, b)
    return x

def norm(x, peak_db):
    x = x - np.mean(x)
    m = np.max(np.abs(x)) or 1.0
    return x / m * 10 ** (peak_db / 20)

# Our own bronze partial set (ratios to nominal, relative amp, decay scale).
BELL_PARTIALS = [(0.5, 0.55, 1.6), (1.0, 1.0, 1.0), (1.21, 0.5, 0.7), (1.49, 0.35, 0.55),
                 (2.03, 0.45, 0.45), (2.61, 0.25, 0.3), (3.17, 0.18, 0.22), (4.23, 0.12, 0.15)]

def bell(f, d, tau=0.8, bright=1.0, detune=0.0, partials=BELL_PARTIALS):
    t = tt(d); out = np.zeros_like(t)
    for r, amp, dk in partials:
        fr = f * r
        if fr > SR / 2.3: continue
        beat = 1 + detune * RNG.uniform(-1, 1)
        a = amp * (bright if r > 1.5 else 1.0)
        out += a * np.sin(2 * np.pi * fr * beat * t + RNG.uniform(0, 6.28)) * np.exp(-t / (tau * dk))
        if detune:  # beating twin partial (cracked bell)
            out += 0.5 * a * np.sin(2 * np.pi * fr * (1 + detune) * t) * np.exp(-t / (tau * dk))
    strike = hp(noise(0.01), 2000) * expenv(0.01, 0.002) * 0.3
    return mix(out * expenv(d, 1e9, 0.001), strike)

def fm(fc, ratio, index, d, tau):
    t = tt(d)
    mod = np.sin(2 * np.pi * fc * ratio * t) * index * np.exp(-t / (tau * 0.5))
    return np.sin(2 * np.pi * fc * t + mod) * np.exp(-t / tau)

def crackle(d, density, lo=1500, hi=9000, decay=None):
    """Sparse random impulses through a band filter (fire, sparks, debris)."""
    n = n_of(d); x = np.zeros(n)
    k = int(density * d)
    idx = RNG.integers(0, n, k)
    x[idx] = RNG.uniform(-1, 1, k) * RNG.uniform(0.3, 1, k)
    y = bp(x, lo, hi) * 6
    if decay: y *= expenv(d, decay, 0.001)
    return y

def thud(f0, f1, d, tau, click=0.3):
    body = osc(glide(f0, f1, d), shape='sine') * expenv(d, tau, 0.001)
    c = lp(noise(0.015), 3000) * expenv(0.015, 0.003) * click
    return mix(body, c)

def note(name):
    names = {'C': 0, 'C#': 1, 'Db': 1, 'D': 2, 'D#': 3, 'Eb': 3, 'E': 4, 'F': 5, 'F#': 6, 'Gb': 6,
             'G': 7, 'G#': 8, 'Ab': 8, 'A': 9, 'A#': 10, 'Bb': 10, 'B': 11}
    p, o = name[:-1], int(name[-1])
    return 440.0 * 2 ** ((names[p] + 12 * (o + 1) - 69) / 12)

# ---------------------------------------------------------------- SFX recipes
# Each returns a float array; finalize() normalises to the listed peak.

def ball_launch(v):
    f = [1180, 1320, 1050][v]
    ping = osc(glide(f * 0.8, f, 0.02), shape='sine')
    ping = np.concatenate([ping, osc(f, 0.14, 'sine')]) * expenv(0.16, 0.035, 0.001)
    ping += 0.25 * osc(f * 2.76, 0.16) * expenv(0.16, 0.015)
    puff = bp(noise(0.08), 900, 4000) * expenv(0.08, 0.018, 0.003) * 0.5
    return mix(ping * 0.7, puff)

def ball_wall(v):
    f = [210, 240, 190][v]
    body = osc(glide(f * 1.4, f, 0.08), shape='tri') * expenv(0.08, 0.014, 0.0005)
    tick = bp(noise(0.03), 1500 + 300 * v, 5000) * expenv(0.03, 0.004, 0.0003)
    return mix(body * 0.8, tick * 0.7)

def ball_hit(v):
    f0 = [720, 640, 800, 580][v]
    tone = osc(glide(f0, f0 * 0.38, 0.09), shape='square') * expenv(0.09, 0.018, 0.0005)
    tone = lp(tone, 4000)
    body = thud(180 + 20 * v, 90, 0.13, 0.035, click=0)
    snap = hp(noise(0.02), 2500) * expenv(0.02, 0.003, 0.0002)
    return sat(mix(tone * 0.45, body * 0.9, snap * 0.8), 1.6)

def ball_crit(v):
    hit = ball_hit(v) * 1.1
    ring = bell([note('A5'), note('D6')][v], 0.4, tau=0.12, bright=1.3)
    zing = osc(glide(2600, 3400, 0.08)) * expenv(0.08, 0.03) * 0.25
    return mix(hit, ring * 0.7, zing)

def ball_catch(v):
    a, b = (note('D6'), note('A6')) if v == 0 else (note('F6'), note('C7'))
    x = np.zeros(n_of(0.18))
    place(x, osc(a, 0.1, 'tri') * expenv(0.1, 0.03, 0.001), 0)
    place(x, osc(b, 0.13, 'tri') * expenv(0.13, 0.045, 0.001), 0.045)
    return x + 0.15 * hp(noise(0.18), 6000) * expenv(0.18, 0.02)

def ball_return(v):
    d = 0.28
    w = bp(noise(d), 600, 5000) * adsr(d, 0.2, 0.02, 0.6, 0.06)
    w = sweep_lp(w, 800, 6000)
    tone = osc(glide(500, 1100, d), shape='sine') * adsr(d, 0.18, 0.03, 0.3, 0.07) * 0.25
    return mix(w, tone)

def el_fire(v):
    d = 0.45
    cr = crackle(d, 260 + 80 * v, 1800, 9000, decay=0.16)
    whoomp = lp(noise(d), 500) * adsr(d, 0.02, 0.1, 0.3, 0.25) * 1.4
    whoomp = sweep_lp(whoomp, 1500, 200)
    hiss = bp(noise(d), 3000, 8000) * expenv(d, 0.08, 0.01) * 0.25
    lowt = thud(140, 60, d, 0.08, 0) * 0.6
    return mix(cr * 0.9, whoomp, hiss, lowt)

def el_freeze(v):
    d = 0.55; x = np.zeros(n_of(d))
    base = [note('D7'), note('E7')][v]
    for k, r in enumerate([1.0, 1.34, 1.79, 2.23, 2.71]):
        at = 0.012 * k + RNG.uniform(0, 0.01)
        place(x, osc(base * r * 0.5, d - at) * expenv(d - at, 0.05 + 0.04 * RNG.random(), 0.0005) * 0.5, at)
    shimmer = hp(noise(d), 7000) * expenv(d, 0.12, 0.003) * 0.35
    crunch = bp(noise(0.05), 2000, 7000) * expenv(0.05, 0.008) * 0.8
    return mix(x, shimmer, crunch)

def el_lightning(v):
    d = 0.4; n = n_of(d)
    jit = np.repeat(RNG.uniform(-1, 1, n // 160 + 1), 160)[:n]
    f = 140 * (1 + v * 0.2) * (1 + 0.6 * jit)
    zap = osc(f, shape='saw') * expenv(d, 0.09, 0.0005)
    zap = bp(zap, 300, 6000)
    gate = (RNG.random(n // 200 + 1) > 0.3).repeat(200)[:n]
    cr = crackle(d, 900, 2500, 11000, decay=0.12)
    snap = hp(noise(0.01), 3000) * 1.5
    return sat(mix(zap * gate * 0.8, cr * 0.7, snap), 2.2)

def el_poison(v):
    d = 0.45; x = np.zeros(n_of(d))
    for k in range(5 + v):
        at = 0.05 * k + RNG.uniform(0, 0.03)
        f0 = RNG.uniform(420, 800) * (1 + 0.1 * k)
        bd = 0.06
        place(x, osc(glide(f0, f0 * 2.2, bd)) * expenv(bd, 0.03, 0.004) * (0.9 - 0.08 * k), at)
    goo = lp(noise(d), 700) * expenv(d, 0.12, 0.01) * 0.7
    return mix(lp(x, 3000), goo)

def el_bomb(v):
    d = 1.0
    body = sweep_lp(noise(d), 2800, 100) * expenv(d, 0.2, 0.001) * 1.6
    sub = osc(glide(95 - 10 * v, 32, 0.6)) * expenv(0.6, 0.25, 0.002) * 1.3
    debris = crackle(d, 120, 800, 5000, decay=0.35) * 0.6
    crack = hp(noise(0.02), 1000) * expenv(0.02, 0.004) * 1.2
    return sat(mix(body, sub, debris, crack), 1.6)

def el_laser(v):
    d = 0.5; t = tt(d)
    f = glide([1500, 1200][v], [700, 900][v], d) * (1 + 0.01 * np.sin(2 * np.pi * 38 * t))
    a = osc(f, shape='saw'); b = osc(f * 1.007, shape='saw')
    beam = lp(mix(a, b), 5000) * adsr(d, 0.01, 0.05, 0.6, 0.25)
    sub = osc(f / 4) * adsr(d, 0.005, 0.1, 0.3, 0.2) * 0.5
    return mix(beam * 0.6, sub, hp(noise(0.03), 4000) * expenv(0.03, 0.006))

def el_quake(v):
    d = 0.9; t = tt(d)
    rum = lp(noise(d), 160, 4) * 4 * (0.6 + 0.4 * np.sin(2 * np.pi * 11 * t)) * adsr(d, 0.02, 0.2, 0.6, 0.5)
    sub = osc(glide(55, 38, d)) * adsr(d, 0.01, 0.2, 0.5, 0.5)
    crunch = crackle(d, 160, 300, 3000, decay=0.4) * 0.8
    return sat(mix(rum, sub, crunch), 2.0)

def en_step(v):
    d = 0.12
    t = thud([78, 70, 86][v], 50, d, 0.035, click=0)
    dust = lp(noise(d), 900) * expenv(d, 0.025, 0.002) * 0.6
    return mix(t, dust)

def en_windup(v):
    d = 0.6
    creak_f = glide(90, 150, d) * (1 + 0.04 * np.sin(2 * np.pi * 23 * tt(d)))
    creak = bp(osc(creak_f, shape='saw'), 200, 2000) * adsr(d, 0.35, 0.1, 0.8, 0.12)
    hiss = sweep_lp(bp(noise(d), 1000, 7000), 1200, 6000) * adsr(d, 0.45, 0.05, 0.9, 0.08) * 0.5
    return mix(creak * 0.8, hiss)

def en_lunge(v):
    d = 0.35; x = np.zeros(n_of(d))
    wh = sweep_lp(bp(noise(0.16), 300, 5000), 600, 4000) * adsr(0.16, 0.12, 0.02, 0.5, 0.02)
    place(x, wh * 0.6, 0)
    place(x, sat(thud(110 - 15 * v, 45, 0.2, 0.06, 0.6), 2), 0.14)
    return x

def en_shoot(v):
    d = 0.2
    spit = bp(noise(d), 500, 3000) * expenv(d, 0.04, 0.002)
    spit = sweep_lp(spit, 4000, 600)
    pop = osc(glide([300, 220][v], 120, 0.06), shape='tri') * expenv(0.06, 0.02) * 0.8
    return mix(spit, pop)

def en_telegraph(v):
    d = 0.6; x = np.zeros(n_of(d))
    for at, f in [(0, note('D5')), (0.22, note('Ab4'))]:  # tritone: uneasy, not a melody
        tone = osc(f, 0.3, 'square') * expenv(0.3, 0.07, 0.002)
        place(x, lp(tone, 2500) * 0.5 + bell(f, 0.3, 0.08) * 0.4, at)
    return x

def en_death(v):
    d = 0.42; x = np.zeros(n_of(d))
    place(x, thud(120 + 15 * v, 55, 0.2, 0.05, 0.4), 0)
    for k in range(6 + v):
        at = RNG.uniform(0.01, 0.2)
        g = bp(noise(0.04), RNG.uniform(800, 2000), 6000) * expenv(0.04, 0.008) * RNG.uniform(0.3, 0.7)
        place(x, g, at)
    puff = lp(noise(d), 1200) * adsr(d, 0.02, 0.1, 0.3, 0.25) * 0.4
    return mix(x, puff)

def boss_phase(v):
    d = 2.6
    toll = bell(note('D2'), d, tau=1.1, bright=0.8, detune=0.012)
    toll2 = bell(note('D3'), d, tau=0.7, bright=0.6, detune=0.02) * 0.4
    sub = osc(note('D1'), d) * expenv(d, 0.9, 0.01) * 0.6
    rattle = crackle(d, 60, 400, 3000, decay=0.6) * 0.3
    return sat(mix(toll, toll2, sub, rattle), 1.5)

def pl_hurt(v):
    d = 0.3
    blip = osc(glide([440, 392][v], 140, 0.18), shape='square') * expenv(0.18, 0.06, 0.001)
    blip = lp(blip, 2500)
    hit = thud(160, 55, d, 0.07, 0.8)
    grit = bp(noise(d), 300, 3000) * expenv(d, 0.05) * 0.5
    return sat(mix(blip * 0.5, hit, grit), 2.0)

def pl_lowhp_loop(v):
    d = 1.0; x = np.zeros(n_of(d))
    for at, a in [(0.0, 1.0), (0.2, 0.7)]:
        place(x, thud(62, 40, 0.18, 0.05, 0.05) * a, at)
    return x  # finalize keeps loop: no fade-out needed, tail is silent

def pl_levelup(v):
    d = 1.0; x = np.zeros(n_of(d))
    for k, nm in enumerate(['D5', 'F5', 'A5', 'D6']):
        place(x, bell(note(nm), d - 0.07 * k, tau=0.35, bright=1.1) * (0.7 + 0.1 * k), 0.07 * k)
    sparkle = hp(noise(d), 6000) * adsr(d, 0.2, 0.2, 0.2, 0.5) * 0.15
    return mix(x, sparkle)

def ui_pick(v):
    d = 0.3; x = np.zeros(n_of(d))
    place(x, osc(note('A5'), 0.12, 'tri') * expenv(0.12, 0.04), 0)
    place(x, osc(note('D6'), 0.25, 'tri') * expenv(0.25, 0.08) + bell(note('D6'), 0.25, 0.1) * 0.3, 0.05)
    return x

def ui_reroll(v):
    d = 0.4; x = np.zeros(n_of(d))
    for k in range(7):
        at = 0.035 * k
        c = bp(noise(0.012), 2000 + 250 * k, 7000) * expenv(0.012, 0.002)
        place(x, c * (0.6 + 0.05 * k), at)
    wh = sweep_lp(bp(noise(d), 500, 6000), 800, 5000) * adsr(d, 0.15, 0.1, 0.4, 0.15) * 0.4
    return mix(x, wh)

def fuser_open(v):
    d = 1.2
    roar = sweep_lp(noise(d), 300, 2500) * adsr(d, 0.08, 0.3, 0.5, 0.7) * 1.2
    roar = lp(roar, 3000)
    b = bell(note('D3'), d, tau=0.5, bright=0.7) * 0.7
    cr = crackle(d, 120, 1500, 8000, decay=0.5) * 0.4
    return mix(roar, b, cr)

def fuser_fuse(v):
    d = 1.1; t = tt(0.6)
    a = osc(glide(note('D5'), note('A4'), 0.6)) * adsr(0.6, 0.05, 0.1, 0.7, 0.05)
    b = osc(glide(note('D4'), note('A4'), 0.6)) * adsr(0.6, 0.05, 0.1, 0.7, 0.05)
    x = np.zeros(n_of(d))
    place(x, (a + b) * 0.3, 0)
    place(x, mix(bell(note('A4'), 0.5, 0.25, 1.2) * 0.9, thud(150, 60, 0.3, 0.06, 0.6) * 0.7), 0.6)
    return x

def fuser_evolve(v):
    d = 1.8; x = np.zeros(n_of(d))
    rise = osc(glide(110, 880, 0.9), shape='saw')
    rise = sweep_lp(rise, 400, 5000) * adsr(0.9, 0.6, 0.1, 0.8, 0.1) * 0.35
    place(x, rise, 0)
    place(x, sweep_lp(noise(0.9), 500, 8000) * adsr(0.9, 0.8, 0.0, 1, 0.05) * 0.2, 0)
    for nm in ['D5', 'F#5', 'A5', 'D6']:
        place(x, bell(note(nm), 0.9, 0.35, 1.2) * 0.45, 0.9)
    place(x, thud(120, 50, 0.4, 0.1, 0.8) * 0.8, 0.9)
    return x

def pk_xp(v):
    f = [note('A6'), note('B6'), note('C#7')][v]
    return osc(f, 0.09, 'sine') * expenv(0.09, 0.02, 0.0005) + 0.3 * osc(f * 2.4, 0.09) * expenv(0.09, 0.008)

def pk_gold(v):
    d = 0.22; f = [2350, 2600][v]
    parts = [(1, 1, 1), (1.47, 0.6, 0.7), (2.09, 0.4, 0.5), (2.8, 0.25, 0.4)]
    x = bell(f / 2, d, tau=0.08, partials=[(r, a, k) for r, a, k in parts])
    y = np.zeros(n_of(d)); place(y, x[:n_of(d - 0.03)] * 0.5, 0.03)
    return mix(x, y)

def pk_heal(v):
    d = 0.5
    a = osc(glide(note('D5'), note('F#5'), d)) * adsr(d, 0.04, 0.1, 0.6, 0.3)
    b = osc(glide(note('A5'), note('D6'), d)) * adsr(d, 0.08, 0.1, 0.5, 0.3)
    shimmer = hp(noise(d), 5000) * adsr(d, 0.1, 0.1, 0.3, 0.3) * 0.08
    return mix(a * 0.5, b * 0.4, shimmer)

def board_expand(v):
    d = 3.0; t = tt(d)
    grind = lp(noise(d), 300, 4) * 4 * (0.7 + 0.3 * np.sin(2 * np.pi * 7 * t)) * adsr(d, 0.3, 0.5, 0.8, 0.9)
    groan = bp(osc(glide(55, 75, d) * (1 + 0.03 * np.sin(2 * np.pi * 3 * t)), shape='saw'), 60, 800) * adsr(d, 0.4, 0.5, 0.7, 0.9)
    x = mix(grind, groan * 0.7)
    for k in range(18):  # chain links
        at = 0.3 + k * 0.13 + RNG.uniform(0, 0.04)
        link = bell(RNG.uniform(1800, 2600), 0.15, 0.04, 1.0) * 0.18
        place(x, link, at)
    place(x, thud(90, 40, 0.5, 0.15, 0.5), 2.6)
    return x

def clear_bonus(v):
    d = 0.8; x = np.zeros(n_of(d))
    for k, nm in enumerate(['A5', 'D6', 'F6', 'A6', 'D7']):
        place(x, osc(note(nm), 0.3, 'tri') * expenv(0.3, 0.09, 0.001) * 0.5, 0.05 * k)
    place(x, bell(note('D6'), 0.55, 0.25, 1.3) * 0.5, 0.25)
    x += hp(noise(d), 7000) * adsr(d, 0.05, 0.2, 0.2, 0.5) * 0.12
    return x

def ui_click(v):
    return mix(bp(noise(0.02), 2000, 6000) * expenv(0.02, 0.003, 0.0002),
               osc(1400, 0.05, 'tri') * expenv(0.05, 0.012) * 0.6)

def sting_win(v):
    d = 3.0; x = np.zeros(n_of(d))
    for k, nm in enumerate(['D4', 'F4', 'A4', 'C5', 'D5']):  # minor climb...
        place(x, bell(note(nm), 0.8, 0.3) * 0.5, 0.12 * k)
    for nm in ['D4', 'F#4', 'A4', 'D5', 'F#5']:  # ...resolving to major
        place(x, bell(note(nm), 2.4, 0.9, 1.1) * 0.35, 0.65)
        place(x, lp(osc(note(nm), 2.3, 'saw'), 2500) * adsr(2.3, 0.08, 0.4, 0.5, 1.2) * 0.12, 0.65)
    place(x, thud(110, 45, 0.6, 0.2, 0.5) * 0.8, 0.65)
    return x

def sting_lose(v):
    d = 2.6; x = np.zeros(n_of(d))
    for k, nm in enumerate(['A4', 'F4', 'D4', 'C#4']):
        f = note(nm) * (1 - 0.01 * k)
        place(x, lp(osc(f, 0.6, 'square'), 1500) * expenv(0.6, 0.25, 0.01) * 0.3 + bell(f, 0.6, 0.3) * 0.3, 0.28 * k)
    place(x, bell(note('D2'), 1.7, 0.8, 0.6, detune=0.03) * 0.9, 0.9)
    place(x, lp(noise(1.7), 200) * adsr(1.7, 0.1, 0.5, 0.4, 1.0) * 0.8, 0.9)
    return x

SFX = [  # (key, fn, variations, peak dB, category)
    ('ball_launch', ball_launch, 3, -10, 'Balls'), ('ball_wall', ball_wall, 3, -13, 'Balls'),
    ('ball_hit', ball_hit, 4, -6, 'Balls'), ('ball_crit', ball_crit, 2, -3, 'Balls'),
    ('ball_catch', ball_catch, 2, -8, 'Balls'), ('ball_return', ball_return, 1, -12, 'Balls'),
    ('el_fire', el_fire, 2, -1, 'Elements'), ('el_freeze', el_freeze, 2, -5, 'Elements'),
    ('el_lightning', el_lightning, 2, -5, 'Elements'), ('el_poison', el_poison, 2, -5, 'Elements'),
    ('el_bomb', el_bomb, 2, -2, 'Elements'), ('el_laser', el_laser, 2, -5, 'Elements'),
    ('el_quake', el_quake, 1, -2, 'Elements'),
    ('en_step', en_step, 3, -12, 'Enemies'), ('en_windup', en_windup, 1, -9, 'Enemies'),
    ('en_lunge', en_lunge, 2, -4, 'Enemies'), ('en_shoot', en_shoot, 2, -8, 'Enemies'),
    ('en_telegraph', en_telegraph, 1, -7, 'Enemies'), ('en_death', en_death, 3, -6, 'Enemies'),
    ('boss_phase', boss_phase, 1, -1, 'Enemies'),
    ('pl_hurt', pl_hurt, 2, -3, 'Player'), ('pl_lowhp_loop', pl_lowhp_loop, 1, -6, 'Player'),
    ('pl_levelup', pl_levelup, 1, -4, 'Player'), ('ui_pick', ui_pick, 1, -8, 'Player'),
    ('ui_reroll', ui_reroll, 1, -8, 'Player'),
    ('fuser_open', fuser_open, 1, -3, 'Fuser'), ('fuser_fuse', fuser_fuse, 1, -3, 'Fuser'),
    ('fuser_evolve', fuser_evolve, 1, -2, 'Fuser'),
    ('pk_xp', pk_xp, 3, -13, 'Pickups'), ('pk_gold', pk_gold, 2, -9, 'Pickups'),
    ('pk_heal', pk_heal, 1, -7, 'Pickups'),
    ('board_expand', board_expand, 1, -2, 'Board & UI'), ('clear_bonus', clear_bonus, 1, -5, 'Board & UI'),
    ('ui_click', ui_click, 1, -10, 'Board & UI'),
    ('sting_win', sting_win, 1, -1, 'Stings'), ('sting_lose', sting_lose, 1, -1, 'Stings'),
]

# First batch for audition (2026-10-19). The other recipes above are drafts: render them with --all.
BATCH = {'ball_launch', 'ball_wall', 'ball_hit', 'ball_crit', 'ball_catch',
         'en_death', 'pl_hurt', 'pl_levelup', 'pk_xp', 'ui_click'}


# ---------------------------------------------------------------- music (electro, stereo, 44.1 kHz)

MUSIC_SR = 44100

def nsaw(freq, d=None, phase=0.0):
    """Naive (aliasing) saw: cheap; always used under a lowpass."""
    f = np.full(n_of(d), float(freq)) if np.isscalar(freq) else np.asarray(freq, float)
    return 2 * ((np.cumsum(f) / SR + phase) % 1.0) - 1

def svf_lp(x, fc, damp=0.3):
    """Chamberlin state-variable lowpass; fc may be an array (filter envelope). Lower damp = more resonance."""
    fc = np.broadcast_to(np.asarray(fc, float), x.shape)
    f = 2 * np.sin(np.pi * np.minimum(fc, SR / 6) / SR)
    lo = bd = 0.0; y = np.empty_like(x)
    for i in range(len(x)):
        hi = x[i] - lo - damp * bd
        bd += f[i] * hi; lo += f[i] * bd
        y[i] = lo
    return y

def place_st(L, R, x, at, pan=0.0):
    a = (pan + 1) * np.pi / 4
    place(L, x * np.cos(a), at); place(R, x * np.sin(a), at)

def e_kick(a=1.0):
    d = 0.42
    body = osc(glide(180, 56, 0.07).tolist() + [56] * (n_of(d) - n_of(0.07)), shape='sine') * expenv(d, 0.09, 0.0008)
    click = hp(noise(0.006), 3000) * expenv(0.006, 0.0015) * 0.9
    return sat(mix(body, click), 2.6) * a

def e_clap(a=1.0):
    x = np.zeros(n_of(0.3))
    for k, off in enumerate((0, 0.009, 0.019)):
        place(x, bp(noise(0.02), 900, 4800) * expenv(0.02, 0.004, 0.0003) * (0.7 + 0.15 * k), off)
    place(x, bp(noise(0.27), 1000, 6000) * expenv(0.27, 0.06) * 0.6, 0.02)
    return x * a

def e_hat(a=1.0, open_=False):
    d = 0.22 if open_ else 0.045
    return hp(noise(d), 7500, 2) * expenv(d, 0.07 if open_ else 0.01, 0.0004) * a

def e_bass(f, d, peak=2400):
    x = nsaw(f, d) * 0.8 + nsaw(f * 1.006, d) * 0.5 + osc(f / 2, d, 'sine') * 0.3
    cut = 260 + peak * np.exp(-tt(d) / 0.05)
    return sat(svf_lp(x, cut, 0.28), 1.6) * adsr(d, 0.002, 0.04, 0.8, 0.012)

def e_pluck(f, d, bright):
    x = nsaw(f, d) * 0.7 + osc(f * 1.003, d, 'square') * 0.3
    cut = 500 + bright * np.exp(-tt(d) / 0.07)
    return svf_lp(x, cut, 0.35) * expenv(d, 0.12, 0.002)

def supersaw(freqs, d, cutoff=2400):
    L = np.zeros(n_of(d)); R = np.zeros(n_of(d))
    for f in freqs:
        for k, c in enumerate((-14, -7, 0, 7, 14)):
            v = nsaw(f * 2 ** (c / 1200), d, RNG.random())
            if k % 2: L += v
            else: R += v
            if k == 2: L += v * 0.5
    env = adsr(d, 0.02, 0.3, 0.75, 0.15)
    norm_ = 1 / (3 * len(freqs))
    return lp(L, cutoff, 2) * env * norm_, lp(R, cutoff, 2) * env * norm_

def battle_loop():
    """Punchy electro: 128 BPM, 16 bars = 30 s, D minor (Dm Bb Gm A), kick-ducked bass and pad, ping-pong arp."""
    bpm = 128; beat = 60 / bpm; bar = 4 * beat; s16 = beat / 4; L_ = 16 * bar
    chords = [['D3', 'F3', 'A3'], ['Bb2', 'D3', 'F3'], ['G2', 'Bb2', 'D3'], ['A2', 'C#3', 'E3']]
    roots = ['D2', 'Bb1', 'G1', 'A1']
    arp_pat = [0, 1, 2, 3, 2, 1, 3, 4, 0, 2, 1, 3, 4, 3, 2, 1]
    tail = 2.5
    n = n_of(L_ + tail)
    dL, dR = np.zeros(n), np.zeros(n)       # drums (not ducked)
    sL, sR = np.zeros(n), np.zeros(n)       # synths (ducked by the kick)
    aL, aR = np.zeros(n), np.zeros(n)       # arp, gets ping-pong delay
    for b in range(16):
        t0 = b * bar
        ci = (b // 2) % 4
        for k in range(4):
            place_st(dL, dR, e_kick(0.8), t0 + k * beat)
            place_st(dL, dR, e_hat(0.5, open_=True), t0 + k * beat + beat / 2, 0.3)
            for q in (1, 3):
                place_st(dL, dR, e_hat(0.26 if q == 1 else 0.36), t0 + k * beat + q * s16, -0.45)
        place_st(dL, dR, e_clap(0.75), t0 + beat, 0.05); place_st(dL, dR, e_clap(0.75), t0 + 3 * beat, -0.05)
        if b == 15:  # clap roll into the loop point
            for k in range(8): place_st(dL, dR, e_clap(0.25 + 0.06 * k), t0 + 2 * beat + k * s16)
        # rolling bass: 16ths 2-4 of each beat (the 1 is the kick's)
        fr = note(roots[ci])
        for k in range(4):
            for q in (1, 2, 3):
                f = fr * (2 if (k == 3 and q == 3) else 1)
                place_st(sL, sR, e_bass(f, s16 * 0.85, 1800 + 500 * (q == 2)) * 0.5, t0 + k * beat + q * s16)
        if b % 2 == 0:
            pl, pr = supersaw([note(x) for x in chords[ci]], 2 * bar, 1600 if b < 8 else 2600)
            place(sL, pl * 0.9, t0); place(sR, pr * 0.9, t0)
        if b >= 4:  # arp enters at bar 5, filter opens through bars 5-8
            tones = [note(x) * 2 for x in chords[ci]] + [note(chords[ci][0]) * 4, note(chords[ci][1]) * 4]
            bright = 1200 + min(1.0, (b - 4) / 4) * 3200
            for q in range(16):
                f = tones[arp_pat[q]]
                place_st(aL, aR, e_pluck(f, s16 * 1.6, bright) * 0.42, t0 + q * s16, 0.25 if q % 2 else -0.25)
        if b in (0, 8):
            place_st(dL, dR, bell(note('D5'), 1.6, tau=0.5, bright=1.0) * 0.22, t0)  # foundry bell, ties to the SFX
    # riser in the last bar
    rd = bar
    riser = fade(sweep_lp(noise(rd), 400, 9000) * np.linspace(0, 1, n_of(rd)) ** 2 * 0.35, 0.001, 0.006)
    place_st(dL, dR, riser, 15 * bar, 0.0)
    # ping-pong delay on the arp (3/16), 4 repeats
    for k in range(1, 5):
        g = 0.42 ** k; sh = n_of(3 * s16 * k)
        src = aL + aR
        tgt = aR if k % 2 else aL
        tgt[sh:] += lp(src[:-sh], 3500) * g * 0.5
    # sidechain: duck synths on every kick
    t = np.arange(n) / SR
    phase = (t % beat)
    duck = 1 - 0.75 * np.exp(-phase / 0.11) * (1 - np.exp(-phase / 0.0015))  # ~3 ms dip, 110 ms release
    L = dL + (sL + aL * 0.85) * duck
    R = dR + (sR + aR * 0.85) * duck
    L, R = hp(L, 40), hp(R, 40)
    m = n_of(L_)
    outL, outR = L[:m].copy(), R[:m].copy()
    outL[:n - m] += L[m:]; outR[:n - m] += R[m:]
    st = np.stack([outL, outR], 1)
    return sat(st / np.max(np.abs(st)), 1.1)  # light glue

MUSIC = [('mus_battle_loop', battle_loop, -1.5)]

# ---------------------------------------------------------------- output

def write_wav(path, x):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    wavfile.write(path, SR, (np.clip(x, -1, 1) * 32767).astype(np.int16))

def encode(wav, mp3, quality, stereo=False):
    os.makedirs(os.path.dirname(mp3), exist_ok=True)
    subprocess.run(['lame', '--quiet', '-m', 'j' if stereo else 'm', '-V', str(quality), '--noreplaygain', wav, mp3], check=True)

def main():
    global SR
    render_all = '--all' in sys.argv
    manifest = {'sfx': [], 'music': []}
    for key, fn, nvar, peak, cat in SFX:
        if not render_all and key not in BATCH: continue
        for v in range(nvar):
            x = fn(v)
            loop = key.endswith('_loop')
            x = norm(x, peak) if loop else fade(norm(x, peak), 0.0005, 0.012)
            name = f'{key}_{v + 1}'
            wav = os.path.join(ROOT, 'masters', name + '.wav'); mp3 = os.path.join(ROOT, 'sfx', name + '.mp3')
            write_wav(wav, x); encode(wav, mp3, 6)
            manifest['sfx'].append({'key': key, 'file': f'sfx/{name}.mp3', 'category': cat, 'dur': round(len(x) / SR, 3),
                                    'bytes': os.path.getsize(mp3), 'loop': loop})
            print(f'{name:22s} {len(x)/SR:5.2f}s {os.path.getsize(mp3):7d} B')
    SR = MUSIC_SR
    for key, fn, peak in MUSIC:
        x = fn(); x = x - x.mean(0); x = x / np.max(np.abs(x)) * 10 ** (peak / 20)
        wav = os.path.join(ROOT, 'masters', key + '.wav'); mp3 = os.path.join(ROOT, 'music', key + '.mp3')
        write_wav(wav, x); encode(wav, mp3, 2, stereo=True)
        manifest['music'].append({'key': key, 'file': f'music/{key}.mp3', 'category': 'Music', 'dur': round(len(x) / SR, 3),
                                  'bytes': os.path.getsize(mp3), 'loop': True})
        print(f'{key:22s} {len(x)/SR:5.2f}s {os.path.getsize(mp3):7d} B')
    total = sum(e['bytes'] for e in manifest['sfx'] + manifest['music'])
    manifest['total_bytes'] = total
    with open(os.path.join(ROOT, 'manifest.json'), 'w') as f: json.dump(manifest, f, indent=1)
    with open(os.path.join(ROOT, 'manifest.js'), 'w') as f:  # lets preview.html work from file:// too
        f.write('window.AUDIO_MANIFEST = ' + json.dumps(manifest) + ';\n')
    print(f'TOTAL {total} B = {total/1048576:.2f} MiB')

if __name__ == '__main__':
    main()
