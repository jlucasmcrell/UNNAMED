"""Compare rebuilt families with what the current playtest plays: loudness, length, low-end share (<250 Hz), onset time, and
whether any two variants of a family are near-identical (correlation of their envelopes).
usage: PYTHONPATH=tools python -P tools/qa.py <recovery root> <playtest primary>"""
import json, os, sys, collections
import numpy as np, soundfile as sf
from alib import SR, lufs_momentary_max

root, primary = sys.argv[1], sys.argv[2]
new = json.load(open(os.path.join(root, 'out', 'combat_rows.json'), encoding='utf-8'))['sounds']
old = {s['audio_id']: s for s in json.load(open(os.path.join(primary, 'manifests', 'playable_prototype_audio_v3.json'), encoding='utf-8'))['sounds']}


def stats(path):
    x, sr = sf.read(path, dtype='float64', always_2d=True)
    m = x.mean(1)
    spec = np.abs(np.fft.rfft(m)) ** 2
    fr = np.fft.rfftfreq(len(m), 1 / sr)
    on = int(np.argmax(np.abs(m) > np.abs(m).max() * 0.1)) / sr
    env = np.array([np.abs(m[i:i + 480]).max() for i in range(0, min(len(m), sr), 480)])
    return lufs_momentary_max(x), len(m) / sr, float(spec[fr < 250].sum() / spec.sum()), on, env


fams = collections.defaultdict(list)
for r in new:
    fams[r['audio_id'].rsplit('.', 1)[0]].append(r)
report = []
for fam, rows in fams.items():
    ns = [stats(os.path.join(root, 'out', r['delivered'])) for r in rows]
    olds = [stats(os.path.join(primary, s['delivered'])) for k, s in old.items() if k.rsplit('.', 1)[0] == fam and os.path.exists(os.path.join(primary, s['delivered']))]
    srcs_old = {json.dumps(s.get('source'), sort_keys=True)[:200] for k, s in old.items() if k.rsplit('.', 1)[0] == fam}
    sim = 0.0
    for i in range(len(ns)):
        for j in range(i + 1, len(ns)):
            a, b = ns[i][4], ns[j][4]
            k = min(len(a), len(b))
            sim = max(sim, float(np.corrcoef(a[:k], b[:k])[0, 1]))
    line = (f"{fam:40s} new {len(ns)} x {np.mean([s[0] for s in ns]):6.1f} LUFS-M low {np.mean([s[2] for s in ns]):.2f} onset {max(s[3] for s in ns) * 1000:4.0f} ms "
            f"max-env-corr {sim:.2f} | old {len(olds)} x " + (f"{np.mean([s[0] for s in olds]):6.1f} LUFS-M low {np.mean([s[2] for s in olds]):.2f} distinct sources {len(srcs_old)}" if olds else '-'))
    print(line)
    report.append(line)
open(os.path.join(root, 'out', 'combat_qa.txt'), 'w', encoding='utf-8').write('\n'.join(report) + '\n')
