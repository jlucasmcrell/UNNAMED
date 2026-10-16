"""Describe pool takes: length, peak time, crest, spectral centroid, low share (<250 Hz), a coarse 40 ms envelope.
usage: PYTHONPATH=tools python -P tools/probe.py <pool dir> [tag ...]"""
import os, sys, glob
import numpy as np, soundfile as sf

pool = sys.argv[1]
for tag in sys.argv[2:] or sorted(os.listdir(pool)):
    for f in sorted(glob.glob(os.path.join(pool, tag, '*.wav'))):
        x, sr = sf.read(f, dtype='float32')
        a = np.abs(x)
        pk = int(a.argmax())
        spec = np.abs(np.fft.rfft(x * np.hanning(len(x))))
        fr = np.fft.rfftfreq(len(x), 1 / sr)
        cen = float((spec * fr).sum() / (spec.sum() + 1e-9))
        low = float((spec[fr < 250] ** 2).sum() / ((spec ** 2).sum() + 1e-12))
        hop = int(0.04 * sr)
        env = ''.join(' .:-=+*#%@'[min(9, max(0, int((20 * np.log10(a[i:i + hop].max() + 1e-9) + 50) / 5)))] for i in range(0, len(x), hop))
        print(f'{tag}/{os.path.basename(f)} {len(x) / sr:5.2f}s pk@{pk / sr:4.2f} {20 * np.log10(a.max() + 1e-9):5.1f}dB cen{cen:5.0f} low{low:4.2f} |{env[:60]}|')
