"""Shared audio helpers: load any source as float32 at 48 kHz, BS.1770 K-weighted loudness, onset detection."""
import numpy as np
import soundfile as sf
from scipy.io import wavfile
from scipy.signal import lfilter, resample_poly

SR = 48000

# ITU-R BS.1770-4 K-weighting at 48 kHz
_K1 = ([1.53512485958697, -2.69169618940638, 1.19839281085285], [1.0, -1.69065929318241, 0.73248077421585])
_K2 = ([1.0, -2.0, 1.0], [1.0, -1.99004745483398, 0.99007225036621])


def load(path):
    """(frames, channels) float32 at 48 kHz."""
    try:
        x, sr = sf.read(path, always_2d=True, dtype="float32")
    except sf.LibsndfileError:
        # a few SoundBits files carry a malformed PEAK chunk that libsndfile rejects; scipy skips unknown chunks
        sr, x = wavfile.read(path)
        bits = {np.dtype("int16"): 15, np.dtype("int32"): 31}.get(x.dtype)
        x = x.astype(np.float32) / (2.0 ** bits if bits else 1.0)
        if x.ndim == 1:
            x = x[:, None]
    if x.shape[1] > 2:
        # B-format / quad: keep the first two channels (W/X or front pair)
        x = x[:, :2]
    if sr != SR:
        g = np.gcd(sr, SR)
        x = resample_poly(x, SR // g, sr // g, axis=0).astype(np.float32)
    return x


def kweight(x):
    y = lfilter(*_K1, x, axis=0)
    return lfilter(*_K2, y, axis=0)


def _block_power(x, block=0.4, overlap=0.75):
    y = kweight(x)
    n, hop = int(block * SR), int(block * SR * (1 - overlap))
    if len(y) < n:
        y = np.concatenate([y, np.zeros((n - len(y), y.shape[1]))])
    p = np.array([(y[i:i + n] ** 2).mean(axis=0).sum() for i in range(0, len(y) - n + 1, hop)])
    return p


def lufs_integrated(x):
    p = _block_power(x)
    l = -0.691 + 10 * np.log10(p + 1e-12)
    p = p[l > -70]
    if len(p) == 0:
        return -70.0
    rel = -0.691 + 10 * np.log10(p.mean()) - 10
    p = p[-0.691 + 10 * np.log10(p) > rel]
    return float(-0.691 + 10 * np.log10(p.mean()))


def lufs_momentary_max(x):
    return float((-0.691 + 10 * np.log10(_block_power(x) + 1e-12)).max())


def true_peak_db(x):
    up = resample_poly(x, 4, 1, axis=0)
    return float(20 * np.log10(np.abs(up).max() + 1e-12))


def onsets(x, thresh_db=-30, min_gap=0.12):
    """Segments (start, end, peak) of sound events in a mono/stereo signal."""
    m = x.mean(axis=1) if x.ndim > 1 else x
    win = int(0.01 * SR)
    env = np.sqrt(np.convolve(m * m, np.ones(win) / win, "same") + 1e-12)
    ref = np.percentile(env, 99.5)
    on = env > ref * 10 ** (thresh_db / 20)
    segs, start, last = [], None, None
    for i in range(0, len(on), win):
        if on[i]:
            if start is None:
                start = i
            last = i
        elif start is not None and (i - last) / SR > min_gap:
            segs.append((start, last + win))
            start = None
    if start is not None:
        segs.append((start, last + win))
    return [(a, b, float(env[a:b].max())) for a, b in segs]
