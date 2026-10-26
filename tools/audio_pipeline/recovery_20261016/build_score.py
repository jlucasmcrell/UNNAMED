"""The score (owner ruling 2026-10-25: the ElevenLabs Music tracks replace the interim Stable Audio score; mus.opening stays, timed to the
cinematic). Each bed is cut to a seamless loop: the loop's start and end are the pair of places where the music around them matches best
(a few seconds of spectrum and rhythm either side), the end is nudged to the start's transient phase, and the music running on past the
end is equal-power cross-faded into the start, so the file's last sample runs straight into its first. The stinger is a one-shot with a
faded tail. Levels are the interim score's integrated targets, under the -18 LUFS dialogue. 48 kHz stereo Vorbis.
usage: PYTHONPATH=tools python -P tools/build_score.py <ElevenLabs music dir> <recovery root>"""
import json, os, sys, hashlib
import numpy as np, soundfile as sf
from scipy.signal import stft
from alib import SR, load, lufs_integrated, true_peak_db

SRC, ROOT = sys.argv[1], sys.argv[2]
OUT = os.path.join(ROOT, 'out', 'audio', 'music')
os.makedirs(OUT, exist_ok=True)
# cue: source file, integrated target, role
CUES = {'mus.title': ('title_v1.mp3', -21, 'the start screen theme'),
        'mus.settlement.day': ('town_waystation_v1.mp3', -23, 'a settlement by day'),
        'mus.settlement.night': ('town_night_v1.mp3', -25, 'a settlement by night'),
        'mus.combat': ('combat_v1.mp3', -20, 'a fight (any engaged creature near the player)'),
        'mus.tension': ('tension_v1.mp3', -26, 'something has noticed the character, before the fight'),
        'mus.explore': ('explore_frontier_v1.mp3', -26, 'a known place on a later visit, now and then'),
        'mus.stinger.discovery': ('discovery_sting_v1.mp3', -21, 'a place discovered: a short phrase over the ducked bed')}
# The night theme builds through its first half: its loop starts after the build (up to half way in, not a third) and holds its
# level across the seam, or the full texture drops back to the sparse opening at every wrap.
LOOP = {'mus.settlement.night': (0.5, 0.2)}
HOP = 1024
XF = int(0.5 * SR)
CONTEXT = 3.0


def features(x):
    """Per frame: log energy in 48 log-spaced bands (z-scored per frame) and spectral flux (the rhythm)."""
    f, _, z = stft(x.mean(axis=1), SR, nperseg=4096, noverlap=4096 - HOP, boundary=None, padded=False)
    mag = np.abs(z)
    edges = np.geomspace(40, 16000, 49)
    bands = np.stack([mag[(f >= lo) & (f < hi)].sum(axis=0) for lo, hi in zip(edges[:-1], edges[1:])], axis=1)
    logb = np.log(bands + 1e-6)
    spec = (logb - logb.mean(axis=1, keepdims=True)) / (logb.std(axis=1, keepdims=True) + 1e-6)
    flux = np.maximum(np.diff(logb, axis=0, prepend=logb[:1]), 0).sum(axis=1)
    level = 10 * np.log10((bands ** 2).sum(axis=1) + 1e-12)
    return spec / np.sqrt(spec.shape[1]), flux, level


def loop_points(x, latest_start=0.35, level_weight=0.05):
    """(start, end) samples: the end's surroundings sound most like the start's; the loop keeps most of the track."""
    spec, flux, level = features(x)
    n, w = len(spec), int(CONTEXT * SR / HOP)
    a_rng = np.arange(w, int(latest_start * n))
    b_rng = np.arange(int(0.6 * n), n - w - int(1.0 * SR / HOP))
    sim = np.zeros((len(a_rng), len(b_rng)), np.float32)
    fz = (flux - flux.mean()) / (flux.std() + 1e-9)
    rhythm = np.zeros_like(sim)
    lev_a = np.zeros(len(a_rng)); lev_b = np.zeros(len(b_rng))
    for k in range(-w, w):
        sim += spec[a_rng + k] @ spec[b_rng + k].T
        rhythm += np.outer(fz[a_rng + k], fz[b_rng + k])
        lev_a += level[a_rng + k]; lev_b += level[b_rng + k]
    sim /= 2 * w; rhythm /= 2 * w
    lev = np.abs(lev_a[:, None] - lev_b[None, :]) / (2 * w)
    length = (b_rng[None, :] - a_rng[:, None]) / n
    score = sim + 0.5 * rhythm - level_weight * lev + 0.1 * length
    i, j = np.unravel_index(np.argmax(score), score.shape)
    a, b = a_rng[i] * HOP, b_rng[j] * HOP
    # Sample-accurate: slide the end within a frame either way to best match the start's waveform (its transients and phase).
    m = x.mean(axis=1)
    ref = m[a:a + XF]
    best = max(range(-HOP, HOP + 1, 4), key=lambda d: float(np.dot(ref, m[b + d:b + d + XF])) / (np.linalg.norm(m[b + d:b + d + XF]) + 1e-9))
    return a, b + best, float(sim[i, j]), float(rhythm[i, j])


def seam_report(y):
    """How the seam compares with the music's own change: the largest spectral jump in the second around the wrap, as a percentile of
    the largest jump in every second of the loop (50 is an ordinary second, 100 the roughest in the loop); and the sample step at the wrap."""
    spec, _, _ = features(np.concatenate([y, y]))
    jump = np.linalg.norm(np.diff(spec, axis=0), axis=1)
    n, w = len(y) // HOP, int(SR / HOP)
    every = np.array([jump[i:i + w].max() for i in range(0, n - w)])
    seam = jump[n - w // 2:n + w // 2].max()
    return round(float((every < seam).mean() * 100), 1), round(float(20 * np.log10(abs(y[-1] - y[0]).max() + 1e-9)), 1)


rows = []
for cue, (name, target, role) in CUES.items():
    x = load(os.path.join(SRC, name))
    loop = not cue.startswith('mus.stinger')
    detail = {}
    if loop:
        a, b, sim, rhythm = loop_points(x, *LOOP.get(cue, ()))
        w = np.linspace(0, np.pi / 2, XF)[:, None]
        y = x[a:b].copy()
        y[:XF] = x[a:a + XF] * np.sin(w) + x[b:b + XF] * np.cos(w)
        detail = {'loop_start_s': round(a / SR, 3), 'loop_end_s': round(b / SR, 3), 'crossfade_s': XF / SR,
                  'match_spectrum': round(sim, 3), 'match_rhythm': round(rhythm, 3)}
    else:
        a = np.abs(x).max(axis=1)
        keep = np.nonzero(a > a.max() * 10 ** (-50 / 20))[0]
        y = x[:keep[-1] + 1].copy()
        f = min(len(y), int(1.5 * SR))
        y[-f:] *= np.linspace(1, 0, f)[:, None] ** 2
    y *= 10 ** ((target - lufs_integrated(y)) / 20)
    tp = true_peak_db(y)
    if tp > -1:
        y *= 10 ** ((-1 - tp) / 20)
    aid = f'{cue}.01'
    path = os.path.join(OUT, aid + '.ogg')
    # libsndfile's Vorbis encoder crashes on one large write; feed it in blocks
    with sf.SoundFile(path, 'w', SR, 2, format='OGG', subtype='VORBIS') as f:
        for i in range(0, len(y), 16384):
            f.write(y[i:i + 16384].astype(np.float32))
    back = load(path)
    if loop:
        detail['seam_jump_percentile'], detail['seam_step_dbfs'] = seam_report(back)
    rows.append({'audio_id': aid, 'category': 'music', 'group': 'music', 'gameplay_role': role, 'human_auditioned': False,
                 'model': 'ElevenLabs Music (music_v2_5, instrumental; online service, chosen by the developer; the game never connects to it)',
                 'channels': 2, 'loop': loop, 'sample_rate': SR, 'subtype': 'VORBIS', 'seconds': round(len(back) / SR, 3), 'delivered_seconds': round(len(back) / SR, 3),
                 'loudness_lufs': round(lufs_integrated(back), 1), 'loudness_measure': 'integrated', 'true_peak_dbtp': round(true_peak_db(back), 2),
                 'peak_dbfs': round(float(20 * np.log10(np.abs(back).max() + 1e-9)), 2), 'clipped_samples': int((np.abs(back) >= 0.9999).sum()),
                 'sha256': hashlib.sha256(open(path, 'rb').read()).hexdigest(), 'delivered': f'audio/music/{aid}.ogg',
                 'source': {'kind': 'composed with ElevenLabs Music from a text prompt (no library audio in)', 'build': 'audio_recovery/tools/build_score.py',
                            'raw': name, 'raw_sha256': hashlib.sha256(open(os.path.join(SRC, name), 'rb').read()).hexdigest(), **detail}})
    print(f"{aid:28s} {rows[-1]['seconds']:6.1f}s {rows[-1]['loudness_lufs']:6.1f} LUFS-I tp {rows[-1]['true_peak_dbtp']:6.2f} {detail}")
json.dump({'sounds': rows}, open(os.path.join(ROOT, 'out', 'music_rows.json'), 'w', encoding='utf-8'), indent=1)
