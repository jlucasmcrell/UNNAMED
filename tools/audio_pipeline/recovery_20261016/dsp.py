"""Layering primitives for the recovery build: take loading and cutting, filters, pitch, synthesised whoosh/twang, levelling."""
import os
import numpy as np, soundfile as sf
from scipy.signal import butter, sosfilt, resample_poly
from alib import SR, lufs_momentary_max, lufs_integrated, true_peak_db

POOL = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), 'src', 'pool')


def take(ref):
    x, sr = sf.read(os.path.join(POOL, ref), dtype='float32')
    assert sr == SR
    return x.astype(np.float64)


def hp(x, f, order=2):
    return sosfilt(butter(order, f, 'hp', fs=SR, output='sos'), x)


def lp(x, f, order=2):
    return sosfilt(butter(order, f, 'lp', fs=SR, output='sos'), x)


def bp(x, lo, hi, order=2):
    return sosfilt(butter(order, [lo, hi], 'bp', fs=SR, output='sos'), x)


def pitch(x, semitones):
    """Varispeed (pitch and length together, like tape): the honest way to vary a recorded take."""
    if abs(semitones) < 0.01:
        return x
    r = 2 ** (semitones / 12)
    up, down = 1000, int(round(1000 * r))
    return resample_poly(x, up, down)


def onset(x, below_db=-30):
    a = np.abs(x)
    th = a.max() * 10 ** (below_db / 20)
    i = int(np.argmax(a > th))
    return max(0, i - int(0.002 * SR))


def window(x, start=None, length=None, from_peak=None, fade_in=0.002, fade_out=0.03):
    """Cut a take: from an absolute start, or from its peak plus an offset; length in seconds."""
    if from_peak is not None:
        start = max(0.0, np.abs(x).argmax() / SR + from_peak)
    a = int((start or 0) * SR)
    b = len(x) if length is None else min(len(x), a + int(length * SR))
    y = x[a:b].copy()
    fi, fo = min(len(y), int(fade_in * SR)), min(len(y), int(fade_out * SR))
    if fi:
        y[:fi] *= np.linspace(0, 1, fi)
    if fo:
        y[-fo:] *= np.linspace(1, 0, fo) ** 2
    return y


def whoosh(dur, f0, f1, rng, peak_at=0.55, width=0.6, air=0.25):
    """Air moved by a blade: noise through a band that sweeps f0 -> f1, a swelling envelope peaking at peak_at, a little broadband air."""
    n = int(dur * SR)
    t = np.linspace(0, 1, n)
    noise = rng.standard_normal(n)
    env = np.exp(-((t - peak_at) / (0.22 * width)) ** 2)
    out = np.zeros(n)
    # sweep in 16 short blocks with overlap-add
    blocks = 16
    hop = n // blocks
    win = np.hanning(2 * hop)
    for i in range(blocks):
        a = max(0, i * hop - hop // 2)
        seg = noise[a:a + 2 * hop]
        if len(seg) < 2 * hop:
            seg = np.pad(seg, (0, 2 * hop - len(seg)))
        f = f0 * (f1 / f0) ** (i / (blocks - 1))
        y = bp(seg, max(40, f * 0.6), min(SR / 2 - 100, f * 1.6)) * win
        b = min(n, a + 2 * hop)
        out[a:b] += y[:b - a]
    out = out * env + air * hp(noise, 2500) * env * 0.3
    return out / (np.abs(out).max() + 1e-9)


def twang(freq, dur, rng, bright=0.996, pluck=0.6):
    """A bowstring let go: Karplus-Strong string with a short noise pluck and a fast-damped body."""
    n = int(dur * SR)
    p = int(SR / freq)
    buf = rng.uniform(-1, 1, p) * pluck
    out = np.zeros(n)
    for i in range(n):
        v = buf[i % p]
        out[i] = v
        buf[i % p] = bright * 0.5 * (v + buf[(i + 1) % p])
    out *= np.exp(-np.linspace(0, 1, n) * 7)
    return out / (np.abs(out).max() + 1e-9)


def ring(freq, dur, decay=10.0, wobble=7.0, depth=0.3):
    """An arrow shaft quivering in wood: a decaying low tone with tremolo."""
    t = np.arange(int(dur * SR)) / SR
    y = np.sin(2 * np.pi * freq * t) * np.exp(-t * decay) * (1 - depth + depth * np.sin(2 * np.pi * wobble * t))
    return y / (np.abs(y).max() + 1e-9)


def mix(layers):
    """layers: list of (signal, offset_s, gain_db)."""
    n = max(int(o * SR) + len(s) for s, o, _ in layers)
    out = np.zeros(n)
    for s, o, g in layers:
        a = int(o * SR)
        out[a:a + len(s)] += s * 10 ** (g / 20)
    return out


def level(x, target, measure='momentary_max', ceiling=-1.0, tail_db=-60):
    """Trim silence, level to a loudness target, keep true peak under the ceiling with a soft knee, end with a short fade."""
    x = x[onset(x, -40):]
    a = np.abs(x)
    keep = np.nonzero(a > a.max() * 10 ** (tail_db / 20))[0]
    x = x[:keep[-1] + int(0.02 * SR)] if len(keep) else x
    fo = min(len(x), int(0.015 * SR))
    x[-fo:] *= np.linspace(1, 0, fo)
    meas = lufs_momentary_max if measure == 'momentary_max' else lufs_integrated
    x = x * 10 ** ((target - meas(x[:, None])) / 20)
    lim = 10 ** (ceiling / 20) * 0.92
    a = np.abs(x)
    over = a > lim
    if over.any():
        # soft saturation of the few samples above the knee (transients only; at most a few dB)
        x[over] = np.sign(x[over]) * (lim + (1 - lim) * np.tanh((a[over] - lim) / (1 - lim)))
    tp = true_peak_db(x[:, None])
    if tp > ceiling:
        x *= 10 ** ((ceiling - tp) / 20)
    return x
