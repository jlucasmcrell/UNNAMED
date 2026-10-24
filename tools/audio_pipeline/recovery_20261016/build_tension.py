"""The tension layer (owner ruling 2026-10-24: a split of mus.combat): the low band of the delivered combat loop - its frame drums and the
bottom of the fiddle, muffled - so it shares the take's tempo, length and loop seam sample for sample, and the music player can go into the
fight at the same playback position. A Demucs drum stem was tried first and kept drums for only ten of the 43 seconds. Take .01 only: take
.02 falls silent for its last three seconds, which a layer under every alert would repeat.
usage: PYTHONPATH=tools python -P tools/build_tension.py <recovery root>"""
import json, os, sys, hashlib
import numpy as np, soundfile as sf
from alib import SR, load, lufs_integrated, true_peak_db
from dsp import lp

ROOT = sys.argv[1]
OUT = os.path.join(ROOT, 'out', 'audio', 'music')
SOURCE, AID, CUTOFF, TARGET = 'mus.combat.01', 'mus.tension.01', 300, -26

x = load(os.path.join(OUT, SOURCE + '.ogg'))
# filtered as a loop: the last second runs through the filter first, so the seam carries no filter start-up
wrap = SR
y = lp(np.concatenate([x[-wrap:], x]).T, CUTOFF, order=4).T[wrap:]
y *= 10 ** ((TARGET - lufs_integrated(y)) / 20)
tp = true_peak_db(y)
if tp > -1:
    y *= 10 ** ((-1 - tp) / 20)
path = os.path.join(OUT, AID + '.ogg')
# libsndfile's Vorbis encoder crashes on one large write; feed it in blocks
with sf.SoundFile(path, 'w', SR, 2, format='OGG', subtype='VORBIS') as f:
    for i in range(0, len(y), 16384):
        f.write(y[i:i + 16384].astype(np.float32))
back = load(path)
assert len(back) == len(x), (len(back), len(x))
src = next(r for r in json.load(open(os.path.join(ROOT, 'out', 'music_rows.json'), encoding='utf-8'))['sounds'] if r['audio_id'] == SOURCE)
row = {'audio_id': AID, 'category': 'music', 'group': 'music', 'gameplay_role': 'something has noticed the character: the fight\'s low band, before the fight',
       'human_auditioned': False, 'model': f'{CUTOFF} Hz low band of {SOURCE}', 'prompt': src['prompt'], 'seed': src['seed'],
       'channels': 2, 'loop': True, 'sample_rate': SR, 'subtype': 'VORBIS', 'seconds': round(len(back) / SR, 3), 'delivered_seconds': round(len(back) / SR, 3),
       'loudness_lufs': round(lufs_integrated(back), 1), 'loudness_measure': 'integrated', 'true_peak_dbtp': round(true_peak_db(back), 2),
       'peak_dbfs': round(float(20 * np.log10(np.abs(back).max() + 1e-9)), 2), 'clipped_samples': int((np.abs(back) >= 0.9999).sum()),
       'sha256': hashlib.sha256(open(path, 'rb').read()).hexdigest(), 'delivered': f'audio/music/{AID}.ogg', 'stem_of': SOURCE,
       'source': {'kind': 'filtered from a generated original (no library audio in)', 'build': 'audio_recovery/tools/build_tension.py', 'raw': src['delivered']}}
json.dump({'sounds': [row]}, open(os.path.join(ROOT, 'out', 'tension_rows.json'), 'w', encoding='utf-8'), indent=1)
print(f"{AID:28s} {row['seconds']:6.1f}s {row['loudness_lufs']:6.1f} LUFS-I tp {row['true_peak_dbtp']} (low band of {SOURCE}, {len(back)} samples)")
