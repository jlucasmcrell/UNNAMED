"""The opening's cue, mus.opening.01 (owner 2026-10-24): one 76 s piece timed to the pictures (src/Presentation/Ui/OpeningWords.cs) - the
night bed under the sky, the rise laid so that it ends on the hit, the hit's attack exactly at HIT, and its ringing tail under the title
theme, which takes over there. Takes are chosen by shape, not by ear (nobody has listened yet): the rise whose loudness climbs most
steadily to its end, the hit with the hardest attack over its tail, the night with the least going on.
usage: PYTHONPATH=tools python -P tools/build_opening.py <recovery root> <ComfyUI output dir>"""
import json, os, sys, hashlib
import numpy as np, soundfile as sf
from alib import SR, load, lufs_integrated, true_peak_db
from dsp import hp

ROOT, COMFY = sys.argv[1], sys.argv[2]
HIT, TAIL = 62.0, 14.0          # OpeningWords.Hit; the hit's tail rings on under the title theme's fade-in (MusicPlayer: 10 s)
OUT = os.path.join(ROOT, 'out', 'audio', 'music')
AID = 'mus.opening.01'
gen = [g for g in json.load(open(os.path.join(ROOT, 'src', 'music', 'generated.json'), encoding='utf-8')) if g['cue'].startswith('mus.opening.')]


def stereo(g):
    x = load(os.path.join(COMFY, g['files'][0]))
    x = np.repeat(x, 2, axis=1) if x.shape[1] == 1 else x
    return hp(x.T, 30).T


def envelope(x, hop=0.5):
    n = int(hop * SR)
    return np.array([10 * np.log10((x[i:i + n] ** 2).mean() + 1e-12) for i in range(0, len(x) - n, n)])


def rising(x):
    e = envelope(x[:len(x) - int(0.5 * SR)])
    t = np.arange(len(e))
    return np.corrcoef(t, e)[0, 1] + (e[-12:].mean() - e[:12].mean()) / 20


def attack(x):
    e = envelope(x, 0.1)
    return e[:8].max() - np.median(e[10:])


def busy(x):
    return envelope(x).std()


def pick(cue, score):
    takes = [(score(x := stereo(g)), g, x) for g in gen if g['cue'] == cue]
    best = max(takes, key=lambda t: t[0])
    print(f'{cue}: ' + ', '.join(f"seed {g['seed']} {s:.2f}" for s, g, _ in takes) + f" -> seed {best[1]['seed']}")
    return best[1], best[2]


def fade(x, a, b):
    """Fade in over the first a seconds and out over the last b, equal power."""
    y = x.copy()
    na, nb = int(a * SR), int(b * SR)
    if na:
        y[:na] *= np.sin(np.linspace(0, np.pi / 2, na))[:, None]
    if nb:
        y[-nb:] *= np.cos(np.linspace(0, np.pi / 2, nb))[:, None]
    return y


def to(x, lufs):
    return x * 10 ** ((lufs - lufs_integrated(x)) / 20)


night_g, night = pick('mus.opening.night', lambda x: -busy(x))
rise_g, rise = pick('mus.opening.rise', rising)
hit_g, hit = pick('mus.opening.hit', attack)

out = np.zeros((int((HIT + TAIL) * SR), 2))
# The night, under the sky shot and into the Foldscar's; the rise comes up under it.
night = to(fade(night[:int(22 * SR)], 2.5, 7.0), -27)
out[:len(night)] += night
# The rise ends on the hit at the end of its climax - where it last stands within 4 dB of its loudest in its final ten seconds (a take
# thins out after it) - with a breath of near-silence before the attack.
e = envelope(rise, 0.25)
held = np.nonzero(e[-40:] >= e[-40:].max() - 4)[0]
rise = rise[:(len(e) - 40 + int(held[-1]) + 1) * int(0.25 * SR)]
rise = to(fade(rise, 8.0, 0.0), -22)
breath = int(0.12 * SR)
rise[-breath:] *= np.linspace(1, 0.15, breath)[:, None] ** 2
start = int(HIT * SR) - len(rise)
out[start:start + len(rise)] += rise
# The hit's peak exactly at HIT, six decibels over the rise's last two seconds; what comes before its peak is its own swell, kept.
a = np.abs(hit).max(axis=1)
peak = int(np.argmax(a[:int(1.5 * SR)]))
lead = min(peak, int(0.25 * SR))
hit = fade(hit[peak - lead:peak - lead + int(TAIL * SR)], 0.01, 4.0)
level = lambda x: 10 * np.log10((x ** 2).mean() + 1e-12)
hit *= 10 ** ((level(rise[-2 * SR:]) + 6 - level(hit[lead:lead + SR // 2])) / 20)
at = int(HIT * SR) - lead
out[at:at + len(hit)] += hit
out = to(out, -21)            # mus.title's level (build_music.py), which takes over under the tail
tp = true_peak_db(out)
if tp > -1:
    out *= 10 ** ((-1 - tp) / 20)

path = os.path.join(OUT, AID + '.ogg')
with sf.SoundFile(path, 'w', SR, 2, format='OGG', subtype='VORBIS') as f:
    for i in range(0, len(out), 16384):
        f.write(out[i:i + 16384].astype(np.float32))
back = load(path)
row = {'audio_id': AID, 'category': 'music', 'group': 'music', 'gameplay_role': 'the opening at launch: night, the rise, and the hit on the name at 62 s',
       'human_auditioned': False,
       'model': 'Stable Audio Open 1.0 (local ComfyUI; Stability AI Community License - free commercial use under USD 1M annual revenue)',
       'prompt': ' | '.join(g['prompt'] for g in (night_g, rise_g, hit_g)), 'seed': [night_g['seed'], rise_g['seed'], hit_g['seed']],
       'channels': 2, 'loop': False, 'sample_rate': SR, 'subtype': 'VORBIS', 'seconds': round(len(back) / SR, 3), 'delivered_seconds': round(len(back) / SR, 3),
       'hit_seconds': HIT, 'loudness_lufs': round(lufs_integrated(back), 1), 'loudness_measure': 'integrated', 'true_peak_dbtp': round(true_peak_db(back), 2),
       'peak_dbfs': round(float(20 * np.log10(np.abs(back).max() + 1e-9)), 2), 'clipped_samples': int((np.abs(back) >= 0.9999).sum()),
       'sha256': hashlib.sha256(open(path, 'rb').read()).hexdigest(), 'delivered': f'audio/music/{AID}.ogg',
       'source': {'kind': 'generated originals (text prompts only; no library audio in), laid out to the pictures', 'build': 'audio_recovery/tools/gen_music.py + build_opening.py',
                  'raw': [g['files'][0] for g in (night_g, rise_g, hit_g)]}}
json.dump({'sounds': [row]}, open(os.path.join(ROOT, 'out', 'opening_rows.json'), 'w', encoding='utf-8'), indent=1)
print(f"{AID:28s} {row['seconds']:6.1f}s {row['loudness_lufs']:6.1f} LUFS-I tp {row['true_peak_dbtp']} (hit at {HIT} s)")
