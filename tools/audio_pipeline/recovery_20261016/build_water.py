"""Build the water set for the swimming movement: wading by depth, swim strokes, treading, entering and leaving water, and the muffled
bed heard under it.

Every file is one window of one owned library recording, filtered, levelled to the mix the rest of the player's movement sits in
(a wade is a footstep at -30, a knee-deep step is heavier at -28, a stroke is an arm's work at -26, treading is small at -29, going in
is an event at -22, coming out at -24, and the underwater bed is a loop at -30), mono 48 kHz PCM_24 except the bed, which is a stereo
loop cut with a crossfaded seam.

usage: PYTHONPATH=tools python -P tools/audio_pipeline/recovery_20261016/build_water.py [credits file ...]
"""
import glob, hashlib, json, os, sys

import numpy as np
import soundfile as sf

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import dsp
from alib import SR, load, lufs_momentary_max, true_peak_db

SRC = r'C:\Otherreach_Recovery\asset_work\audio\_src'
ROOT = r'C:\Otherreach_Recovery\asset_work\audio_recovery'
OUT = os.path.join(ROOT, 'out')

COSMOS = 'Leartes Cosmos Premium (Sounds)'
COSMOS_LICENCE = ('Cosmos Premium subscription terms (post-subscription rights unconfirmed, see ASSET_DECISIONS); '
                  'owner rule: no AI/ML use')
SONNISS = 'Sonniss #GameAudioGDC 2026 bundle'
SONNISS_LICENCE = ('Sonniss GDC 2026 licence: royalty-free, commercial use, no attribution; no redistribution as a library; '
                   'owner rule: no AI/ML use')

# take -> (library, pack, file, window start, window length, filter, filter args, pitch semitones, fade in, fade out)
TAKES = {
    'wade1': (COSMOS, 'Walking on Puddle (WW2_Walking_Puddle)', 'A_WW2_Walk_Puddle_001.wav', 0.00, 0.49, 'hp', 300, 0.0, 0.004, 0.05),
    'wade2': (COSMOS, 'Walking on Puddle (WW2_Walking_Puddle)', 'A_WW2_Walk_Puddle_002.wav', 0.00, 0.50, 'hp', 300, 0.0, 0.004, 0.05),
    'wade3': (COSMOS, 'Walking on Puddle (WW2_Walking_Puddle)', 'A_WW2_Walk_Puddle_003.wav', 0.00, 0.50, 'hp', 280, 0.0, 0.004, 0.05),
    'wade4': (COSMOS, 'Walking on Puddle (WW2_Walking_Puddle)', 'A_WW2_Walk_Puddle_004.wav', 0.00, 0.50, 'hp', 320, 0.0, 0.004, 0.05),
    'deep1': (COSMOS, 'Running on Puddle (WW2_Run_Puddle)', 'A_WW2_Run_Puddle_001.wav', 0.00, 0.29, 'lp', 5000, -2.0, 0.004, 0.05),
    'deep2': (COSMOS, 'Running on Puddle (WW2_Run_Puddle)', 'A_WW2_Run_Puddle_003.wav', 0.00, 0.26, 'lp', 5000, -2.0, 0.004, 0.05),
    'deep3': (COSMOS, 'Running on Puddle (WW2_Run_Puddle)', 'A_WW2_Run_Puddle_005.wav', 0.00, 0.30, 'lp', 5000, -2.0, 0.004, 0.05),
    'deep4': (COSMOS, 'Running on Puddle (WW2_Run_Puddle)', 'A_WW2_Run_Puddle_007.wav', 0.00, 0.30, 'lp', 5000, -2.0, 0.004, 0.05),
    'slosh1': (SONNISS, 'Just Sound Effects - Highlands of Norway',
               'WATRLap_Small Waves sloshing onto Rock Slabs at huge Glacial Lake _JSE_HoN_Quad.wav', 4.20, 0.90, 'bp', (250, 5200), 0.0, 0.01, 0.08),
    'slosh2': (SONNISS, 'Just Sound Effects - Highlands of Norway',
               'WATRLap_Small Waves sloshing onto Rock Slabs at huge Glacial Lake _JSE_HoN_Quad.wav', 12.60, 0.90, 'bp', (250, 5200), -1.0, 0.01, 0.08),
    'slosh3': (SONNISS, 'Just Sound Effects - Highlands of Norway',
               'WATRLap_Small Waves sloshing onto Rock Slabs at huge Glacial Lake _JSE_HoN_Quad.wav', 21.40, 0.90, 'bp', (250, 5200), 1.0, 0.01, 0.08),
    'ripple1': (SONNISS, 'Epic Stock Media - Public Spaces - Storms Lakes Parks and Rural Nature Exteriors',
                'WATRLap_Summer Tennessee Lake Dock Water Ripples Wake Wave Gentle 05 Distant_ESM_CPS.wav', 3.10, 0.60, 'bp', (300, 4500), 0.0, 0.01, 0.08),
    'ripple2': (SONNISS, 'Epic Stock Media - Public Spaces - Storms Lakes Parks and Rural Nature Exteriors',
                'WATRLap_Summer Tennessee Lake Dock Water Ripples Wake Wave Gentle 05 Distant_ESM_CPS.wav', 9.70, 0.60, 'bp', (300, 4500), 0.0, 0.01, 0.08),
    'ripple3': (SONNISS, 'Epic Stock Media - Public Spaces - Storms Lakes Parks and Rural Nature Exteriors',
                'WATRLap_Summer Tennessee Lake Dock Water Ripples Wake Wave Gentle 05 Distant_ESM_CPS.wav', 16.30, 0.60, 'bp', (300, 4500), 0.0, 0.01, 0.08),
    'plunge': (SONNISS, 'Epic Stock Media - Elemental Mutation Whooshes and Impacts',
               'WATRImpt_Impact Water Deep Submerge Bubble Drown Ship Hit 05_ESM_EMWI.wav', 0.00, 1.10, 'lp', 7000, 0.0, 0.004, 0.12),
    'surface': (COSMOS, 'Spear Fishing (Spear fishing)', 'Survival SFX - spearfishing2.wav', 0.00, 0.50, 'hp', 150, -2.0, 0.004, 0.06),
    'rising': (COSMOS, 'Landing on Puddle (WW2_Landing_Puddle)', 'A_WW2_Landing_Puddle_001.wav', 0.00, 0.36, 'lp', 6000, -5.0, 0.004, 0.08),
    'runoff': (COSMOS, 'Pouring Loop (PAD_Pouring_Loop)', 'A_Pouring_Loop.wav', 0.50, 0.90, 'hp', 250, 0.0, 0.01, 0.12),
    'bed': (SONNISS, 'Just Sound Effects - Highlands of Norway',
            'WATRLap_Small Waves sloshing onto Rock Slabs at huge Glacial Lake _JSE_HoN_Quad.wav', 30.00, 8.25, 'lp', 700, 0.0, 0.0, 0.0),
}

# (audio id, take, role, target LUFS, ceiling dBTP, channels)
FILES = [
    ('sfx.player.wade.shallow.01', 'wade1', 'Wading ankle-deep: one foot through shallow water', -30.0, -4.0, 1),
    ('sfx.player.wade.shallow.02', 'wade2', 'Wading ankle-deep, second step', -30.0, -4.0, 1),
    ('sfx.player.wade.shallow.03', 'wade3', 'Wading ankle-deep, third step', -30.0, -4.0, 1),
    ('sfx.player.wade.shallow.04', 'wade4', 'Wading ankle-deep, fourth step', -30.0, -4.0, 1),
    ('sfx.player.wade.knee_deep.01', 'deep1', 'Wading knee-deep: the water pushed aside and coming back', -28.0, -4.0, 1),
    ('sfx.player.wade.knee_deep.02', 'deep2', 'Wading knee-deep, second step', -28.0, -4.0, 1),
    ('sfx.player.wade.knee_deep.03', 'deep3', 'Wading knee-deep, third step', -28.0, -4.0, 1),
    ('sfx.player.wade.knee_deep.04', 'deep4', 'Wading knee-deep, fourth step', -28.0, -4.0, 1),
    ('sfx.player.swim.stroke.01', 'slosh1', 'A swimming stroke: an arm drawn through still water', -26.0, -3.0, 1),
    ('sfx.player.swim.stroke.02', 'slosh2', 'A swimming stroke, second', -26.0, -3.0, 1),
    ('sfx.player.swim.stroke.03', 'slosh3', 'A swimming stroke, third', -26.0, -3.0, 1),
    ('sfx.player.swim.tread.01', 'ripple1', 'Treading water: small water moved to stay up', -29.0, -5.0, 1),
    ('sfx.player.swim.tread.02', 'ripple2', 'Treading water, second', -29.0, -5.0, 1),
    ('sfx.player.swim.tread.03', 'ripple3', 'Treading water, third', -29.0, -5.0, 1),
    ('sfx.player.water.enter.01', 'plunge', 'Entering water: the body going under', -22.0, -2.0, 1),
    ('sfx.player.water.enter.02', 'surface', 'Entering water a second way: the surface breaking', -22.0, -2.0, 1),
    ('sfx.player.water.exit.01', 'rising', 'Leaving water: the body coming up out of it', -24.0, -3.0, 1),
    ('sfx.player.water.exit.02', 'runoff', 'Leaving water: what runs off afterwards', -24.0, -3.0, 1),
    ('sfx.water.underwater.bed.01', 'bed', 'Under water: the muffled wash heard with the head below the surface', -30.0, -8.0, 2),
]


def source(take):
    library, pack, name, start, length, kind, arg, pitch, fade_in, fade_out = TAKES[take]
    hits = glob.glob(os.path.join(SRC, '**', name), recursive=True)
    if len(hits) == 0:
        raise SystemExit(f'{name} is not in the library')
    x = load(sorted(hits)[0])
    y = dsp.window(x, start=start, length=length, fade_in=fade_in, fade_out=fade_out)
    if kind == 'hp':
        y = dsp.hp(y, arg)
    elif kind == 'lp':
        y = dsp.lp(y, arg)
    else:
        y = dsp.bp(y, arg[0], arg[1])
    return dsp.pitch(y, pitch)


def loopable(x, fade_s):
    """A seamless loop: run the tail over the head and keep the crossfaded length."""
    fade = int(fade_s * SR)
    if fade <= 0 or len(x) <= 2 * fade:
        return x
    head, tail, body = x[:fade].copy(), x[-fade:], x[:-fade]
    ramp = np.linspace(0, 1, fade)[:, None] if head.ndim > 1 else np.linspace(0, 1, fade)
    body[:fade] = body[:fade] * ramp + tail * (1 - ramp)
    return body


def main():
    os.makedirs(os.path.join(OUT, 'audio', 'recorded_world'), exist_ok=True)
    rows = []
    for audio_id, take, role, target, ceiling, channels in FILES:
        x = source(take)
        if channels == 1:
            x = x.mean(axis=1)
        x = dsp.level(x, target, ceiling=ceiling)
        if audio_id.endswith('bed.01'):
            x = loopable(x, 0.35)
            x = dsp.level(x, target, ceiling=ceiling)
        x = np.clip(x, -1.0, 1.0)
        delivered = f'audio/recorded_world/{audio_id}.wav'
        path = os.path.join(OUT, *delivered.split('/'))
        sf.write(path, x.astype(np.float32), SR, subtype='PCM_24')
        library, pack, name, start, length, *_ = TAKES[take]
        rows.append({
            'audio_id': audio_id,
            'category': 'player',
            'group': 'player.water',
            'gameplay_role': role,
            'human_auditioned': False,
            'seconds': round(len(x) / SR, 3),
            'channels': int(x.shape[1]) if x.ndim > 1 else 1,
            'loop': audio_id.endswith('bed.01'),
            'sample_rate': SR,
            'subtype': 'PCM_24',
            'peak_dbfs': round(float(20 * np.log10(np.abs(x).max() + 1e-12)), 2),
            'sha256': hashlib.sha256(open(path, 'rb').read()).hexdigest(),
            'delivered': delivered,
            'loudness_lufs': round(lufs_momentary_max(x if x.ndim > 1 else x[:, None]), 1),
            'loudness_measure': 'momentary_max',
            'true_peak_dbtp': round(true_peak_db(x if x.ndim > 1 else x[:, None]), 1),
            'source': {'kind': 'recording / sound library', 'layers': [{
                'kind': 'recording / sound library',
                'library': library,
                'pack': pack,
                'file': name,
                'licence': COSMOS_LICENCE if library == COSMOS else SONNISS_LICENCE,
                'window_s': [round(start, 3), round(start + length, 3)],
            }]},
        })
        print(f'{audio_id:34s} {len(x)/SR:5.2f}s ch={rows[-1]["channels"]} {rows[-1]["loudness_lufs"]:6.1f} LUFS {rows[-1]["true_peak_dbtp"]:6.1f} dBTP')
    with open(os.path.join(OUT, 'water_rows.json'), 'w', encoding='utf-8') as f:
        json.dump({'sounds': rows}, f, indent=2, ensure_ascii=False)
        f.write('\n')
    print(f'{len(rows)} files, rows in out/water_rows.json')
    with open(os.path.join(OUT, 'CREDITS_water_sfx.txt'), 'w', encoding='utf-8', newline='\n') as f:
        f.write(section(rows))
    for target in sys.argv[1:]:
        credite(target, rows)


def section(rows):
    used = {}
    for r in rows:
        for layer in r['source']['layers']:
            used.setdefault((layer['library'], layer['pack'], layer['file']), set()).add(r['audio_id'].rsplit('.', 1)[0])
    block = ['== Water SFX (swimming)', f'   licence: {COSMOS_LICENCE}',
             f'   licence: {SONNISS_LICENCE}',
             '   Cut from the library at one window, filtered and levelled to the movement mix: wading ankle-deep -30 LUFS momentary max,',
             '   knee-deep -28, a stroke -26, treading -29, entering -22, leaving -24, the underwater bed -30 as a stereo loop; PCM_24.']
    for (library, pack, file), ids in sorted(used.items()):
        block.append(f'   {library} / {pack} / {file}  ->  {", ".join(sorted(ids))}')
    return '\n'.join(block) + '\n'


def credite(path, rows):
    text = open(path, encoding='utf-8-sig').read() if os.path.exists(path) else ''
    kept, dropping = [], False
    for line in text.split('\n'):
        if line.startswith('== Water SFX'):
            dropping = True
            continue
        if dropping and line.startswith('== '):
            dropping = False
        if not dropping:
            kept.append(line)
    while kept and kept[-1] == '':
        kept.pop()
    with open(path, 'w', encoding='utf-8', newline='\n') as f:
        f.write('\n'.join(kept).rstrip('\n') + '\n\n' + section(rows))
    print(f'credits: {path}')


if __name__ == '__main__':
    main()
