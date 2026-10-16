"""Turn the generated cues into the score: 48 kHz stereo Vorbis, beds cut to a seamless loop (the tail equal-power cross-faded into the
head), levelled by integrated loudness under the effects; the stinger a one-shot with a faded tail.
usage: PYTHONPATH=tools python -P tools/build_music.py <recovery root> <ComfyUI output dir>"""
import json, os, sys, hashlib
import numpy as np, soundfile as sf
from alib import SR, load, lufs_integrated, true_peak_db
from dsp import hp

ROOT, COMFY = sys.argv[1], sys.argv[2]
OUT = os.path.join(ROOT, 'out', 'audio', 'music')
os.makedirs(OUT, exist_ok=True)
gen = json.load(open(os.path.join(ROOT, 'src', 'music', 'generated.json'), encoding='utf-8'))
TARGET = {'mus.title': -21, 'mus.settlement.day': -23, 'mus.settlement.night': -25, 'mus.combat': -20, 'mus.stinger.discovery': -21}
ROLE = {'mus.title': 'the start screen theme', 'mus.settlement.day': 'a settlement by day', 'mus.settlement.night': 'a settlement by night',
        'mus.combat': 'a fight (any engaged creature near the player)', 'mus.stinger.discovery': 'a place discovered: a short phrase over the ducked bed'}
rows = []
count = {}
for g in sorted(gen, key=lambda g: (g['cue'], g['seed'])):
    cue = g['cue']
    x = load(os.path.join(COMFY, g['files'][0]))
    if x.shape[1] == 1:
        x = np.repeat(x, 2, axis=1)
    x = hp(x.T, 30).T
    loop = not cue.startswith('mus.stinger')
    if loop:
        xf = int(3.0 * SR)
        body = x[:len(x) - int(0.5 * SR)]          # the model's last half second often thins out
        n = len(body) - xf
        w = np.linspace(0, np.pi / 2, xf)[:, None]
        y = body[:n].copy()
        y[:xf] = body[:xf] * np.sin(w) + body[n:n + xf] * np.cos(w)
    else:
        a = np.abs(x).max(axis=1)
        keep = np.nonzero(a > a.max() * 10 ** (-50 / 20))[0]
        y = x[:keep[-1] + 1].copy()
        f = min(len(y), int(1.5 * SR))
        y[-f:] *= np.linspace(1, 0, f)[:, None] ** 2
    y *= 10 ** ((TARGET[cue] - lufs_integrated(y)) / 20)
    tp = true_peak_db(y)
    if tp > -1:
        y *= 10 ** ((-1 - tp) / 20)
    count[cue] = count.get(cue, 0) + 1
    aid = f'{cue}.{count[cue]:02d}'
    path = os.path.join(OUT, aid + '.ogg')
    # libsndfile's Vorbis encoder crashes on one large write; feed it in blocks
    with sf.SoundFile(path, 'w', SR, 2, format='OGG', subtype='VORBIS') as f:
        for i in range(0, len(y), 16384):
            f.write(y[i:i + 16384].astype(np.float32))
    back = load(path)
    rows.append({'audio_id': aid, 'category': 'music', 'group': 'music', 'gameplay_role': ROLE[cue], 'human_auditioned': False,
                 'model': 'Stable Audio Open 1.0 (local ComfyUI; Stability AI Community License - free commercial use under USD 1M annual revenue)',
                 'text_encoder': g['text_encoder'], 'prompt': g['prompt'], 'negative_prompt': g['negative'], 'seed': g['seed'], 'settings': g['settings'],
                 'channels': 2, 'loop': loop, 'sample_rate': SR, 'subtype': 'VORBIS', 'seconds': round(len(back) / SR, 3), 'delivered_seconds': round(len(back) / SR, 3),
                 'loudness_lufs': round(lufs_integrated(back), 1), 'loudness_measure': 'integrated', 'true_peak_dbtp': round(true_peak_db(back), 2),
                 'peak_dbfs': round(float(20 * np.log10(np.abs(back).max() + 1e-9)), 2), 'clipped_samples': int((np.abs(back) >= 0.9999).sum()),
                 'sha256': hashlib.sha256(open(path, 'rb').read()).hexdigest(), 'delivered': f'audio/music/{aid}.ogg',
                 'source': {'kind': 'generated original (text prompt only; no library audio in)', 'build': 'audio_recovery/tools/gen_music.py + build_music.py',
                            'raw': g['files'][0], 'interim': 'stands in until an ACE-Step score or licensed tracks are chosen (AUDIO_DESIGN.md)'}})
    print(f"{aid:28s} {rows[-1]['seconds']:6.1f}s {rows[-1]['loudness_lufs']:6.1f} LUFS-I tp {rows[-1]['true_peak_dbtp']}")
json.dump({'sounds': rows}, open(os.path.join(ROOT, 'out', 'music_rows.json'), 'w', encoding='utf-8'), indent=1)
