"""Build the P2 spell SFX from the recovered libraries: a cast, a flight or activation and an end for each P2 working.

Every file is cut from the owned library at a fixed window, filtered, and levelled to the loudness the existing magic family sits at
(-21.5 LUFS for a cast, -23 for flight, -19 for an impact, -22 for a resolution, -24 for an end), mono 48 kHz PCM_24, the same shape
SoundBank loads. The rows carry the source file, pack, licence and window, so credits.py credits them and the game's own audio-coverage
test resolves them.

usage: PYTHONPATH=tools python -P tools/audio_pipeline/recovery_20261016/build_spells.py
"""
import hashlib, json, os, sys

import numpy as np
import soundfile as sf

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import dsp
from alib import SR, load, lufs_momentary_max, true_peak_db

SRC = r'C:\Otherreach_Recovery\asset_work\audio\_src'
ROOT = r'C:\Otherreach_Recovery\asset_work\audio_recovery'
OUT = os.path.join(ROOT, 'out')
SONNISS = 'sonniss'
LICENCE = ('Sonniss GDC 2026 licence: royalty-free, commercial use, no attribution; no redistribution as a library; '
           'owner rule: no AI/ML use')
LIBRARY = 'Sonniss #GameAudioGDC 2026 bundle'

ELEMENTAL = '1of5/344 Audio - Elemental Palette Designed Vol. 1'
MUTATION = '2of5/Epic Stock Media - Elemental Mutation Whooshes and Impacts'
ICE = '2of5/Alexander Kopeikin - 100 kHz Designed Ice'
EMOTION = '2of5/Alexander Kopeikin - Emotion and Magic'
FANTASY = '2of5/Epic Stock Media - Fantasy Game 2 - Sound Kit for Enchanted Realms'
ORGANIC = '2of5/CB_Sounddesign - Applicable Sounds - Organic UI and Building Games SFX'

# take -> (pack dir, file, window start, window length, filter, filter args, pitch semitones, target LUFS, ceiling dBTP)
TAKES = {
    'fire': (MUTATION, 'FIREWhsh_Whoosh Fire Deep Growl Monster Saturated Crisp 03_ESM_EMWI.wav', 0.10, 1.20, 'hp', 90, 0.0),
    'fire_bright': (MUTATION, 'FIREWhsh_Whoosh Fire Deep Growl Monster Saturated Crisp 03_ESM_EMWI.wav', 0.14, 0.90, 'hp', 200, 2.0),
    'fire_flight': (MUTATION, 'FIREWhsh_Whoosh Fire Deep Growl Monster Saturated Crisp 03_ESM_EMWI.wav', 0.30, 1.00, 'lp', 4200, 0.0),
    'fire_burst': (MUTATION, 'FIREWhsh_Whoosh Fire Deep Growl Monster Saturated Crisp 03_ESM_EMWI.wav', 0.14, 0.70, 'hp', 150, -3.0),
    'ice_field': (ICE, 'ice, movement, ice drift, ice field cracking up, initial, wide-001.wav', 0.90, 1.40, 'lp', 7000, 0.0),
    'ice_snap': (ICE, 'ice, crack, ice block snapping-001.wav', 0.53, 1.40, 'lp', 8000, -2.0),
    'ice_snap_low': (ICE, 'ice, crack, ice block snapping-001.wav', 0.53, 1.60, 'lp', 8000, -3.0),
    'ice_crush': (ICE, 'ice, block of ice crushed, heavy-015.wav', 0.25, 1.60, 'lp', 9000, 0.0),
    'shards_flight': (MUTATION, 'GLASMvmt_Whoosh Glass Crystal Fragments Sharp Shards Dry 05_ESM_EMWI.wav', 0.18, 1.20, 'bp', (500, 6000), 0.0),
    'ward_light': (FANTASY, 'MAGAngl_Magic Light Spell Enchantment Potion Effect Tonal Bright 03_ESM_FG2.wav', 1.70, 1.40, 'hp', 120, 0.0),
    'ward_set': (ELEMENTAL, 'METLMisc_Metal, Slow Whoosh, Rattle, Pass By x4 01_344 Audio_Elemental Palette Designed Vol 1.wav',
                 0.06, 1.20, 'lp', 3200, 0.0),
    'ward_end': (EMOTION, 'magic, drone, tension, spellbound, evanescence-002.wav', 2.00, 1.60, 'lp', 5000, 0.0),
    'mend_warm': (ORGANIC, 'GAMEMisc_Magic Creation 23_CB Sounddesign_APPlicable Sounds.wav', 0.22, 1.50, 'hp', 100, 0.0),
    'mend_slow': (EMOTION, 'magic, energy flow, astonishment-001.wav', 0.71, 2.00, 'hp', 90, 0.0),
    'venom_out': (ELEMENTAL, 'WATRMisc_Water, Liquid Impact, Bubble, Sci Fi, Hit 04_344 Audio_Elemental Palette Designed Vol 1.wav',
                  0.12, 1.20, 'hp', 150, 0.0),
    'venom_hiss': (MUTATION, 'ELECMisc_Impact Electric Tonal Deep Movement Motion Hiss Glitch 01_ESM_EMWI.wav', 0.03, 1.20, 'lp', 6000, 0.0),
    'weight_lift': (ELEMENTAL, 'WINDDsgn_Wind, Rush, Whoosh, Long x5 01_344 Audio_Elemental Palette Designed Vol 1.wav',
                    0.02, 1.00, 'bp', (200, 7000), 0.0),
    'weight_gone': (EMOTION, 'magic, energy flow, astral travel-015.wav', 0.48, 1.60, 'lp', 8000, 0.0),
}

# (audio id, take, role, target LUFS, ceiling dBTP)
FILES = [
    ('sfx.magic.cinder_dart.cast.01', 'fire', 'Cinder Dart gathering: a quick breath of hot air', -21.5, -6.0),
    ('sfx.magic.cinder_dart.cast.02', 'fire_bright', 'Cinder Dart gathering, brighter: a spark catching', -21.5, -6.0),
    ('sfx.magic.cinder_dart.travel.01', 'fire_flight', 'Cinder Dart in flight: fire dragged behind a small shape', -23.0, -8.0),
    ('sfx.magic.cinder_dart.impact.01', 'fire_burst', 'Cinder Dart landing: a burst of flame on a body', -19.0, -1.0),
    ('sfx.magic.shiver_spike.cast.01', 'ice_field', 'Shiver Spike gathering: a field of ice cracking open', -21.5, -6.0),
    ('sfx.magic.shiver_spike.cast.02', 'ice_snap', 'Shiver Spike gathering, sharper: a block of ice splitting', -21.5, -6.0),
    ('sfx.magic.shiver_spike.travel.01', 'shards_flight', 'Shiver Spike in flight: shards of ice dragged through air', -23.0, -8.0),
    ('sfx.magic.shiver_spike.impact.01', 'ice_crush', 'Shiver Spike landing: a block of ice crushed against a body', -19.0, -1.0),
    ('sfx.magic.shiver_spike.impact.02', 'ice_snap_low', 'Shiver Spike landing, smaller: ice snapping off a surface', -19.0, -1.0),
    ('sfx.magic.holdfast.cast.01', 'ward_light', 'Holdfast gathering: a bright spell-sound closing around a body', -21.5, -6.0),
    ('sfx.magic.holdfast.activate.01', 'ward_set', 'Holdfast settling: a slow metal pass, the boundary taking hold', -21.0, -1.5),
    ('sfx.magic.holdfast.end.01', 'ward_end', 'Holdfast fading: the drone of a ward letting go', -24.0, -10.0),
    ('sfx.magic.ember_mend.cast.01', 'mend_warm', 'Ember Mend gathering: a warm swell made by hand', -21.5, -6.0),
    ('sfx.magic.ember_mend.resolve.01', 'mend_slow', 'Ember Mend resolving: the slow warmth taking hold over six seconds', -22.0, -6.0),
    ('sfx.magic.hush_the_sting.cast.01', 'venom_out', 'Hush the Sting gathering: a liquid drawing out of a wound', -21.5, -6.0),
    ('sfx.magic.hush_the_sting.activate.01', 'venom_hiss', 'Hush the Sting lifting the venom: a hiss and a glitch leaving the blood', -21.0, -6.0),
    ('sfx.magic.clear_the_dead_weight.cast.01', 'weight_lift', 'Clear the Dead Weight gathering: a rush of air under a fallen body', -21.5, -6.0),
    ('sfx.magic.clear_the_dead_weight.resolve.01', 'weight_gone', 'Clear the Dead Weight lifting: the weakened state travelling off', -22.0, -6.0),
]


def cut(ref):
    pack, name, start, length, kind, arg, semitones = TAKES[ref]
    x = load(os.path.join(SRC, SONNISS, pack, name)).mean(axis=1)
    y = dsp.window(x, start=start, length=length, fade_in=0.004, fade_out=0.06)
    if kind == 'hp':
        y = dsp.hp(y, arg)
    elif kind == 'lp':
        y = dsp.lp(y, arg)
    else:
        y = dsp.bp(y, arg[0], arg[1])
    return dsp.pitch(y, semitones)


def main():
    rows = []
    os.makedirs(os.path.join(OUT, 'audio', 'recorded_world'), exist_ok=True)
    for audio_id, ref, role, target, ceiling in FILES:
        x = dsp.level(cut(ref), target, ceiling=ceiling)
        x = x / max(1.0, np.abs(x).max()) if np.abs(x).max() > 1.0 else x
        delivered = f'audio/recorded_world/{audio_id}.wav'
        path = os.path.join(OUT, *delivered.split('/'))
        sf.write(path, x.astype(np.float32), SR, subtype='PCM_24')
        pack, name, start, length, _kind, _arg, _semi = TAKES[ref]
        rows.append({
            'audio_id': audio_id,
            'category': 'magic',
            'group': 'magic',
            'gameplay_role': role,
            'human_auditioned': False,
            'locomotion_convention': None,
            'seconds': round(len(x) / SR, 3),
            'delivered_seconds': round(len(x) / SR, 3),
            'duration_error_s': 0.0,
            'channels': 1,
            'loop': False,
            'sample_rate': SR,
            'subtype': 'PCM_24',
            'peak_dbfs': round(float(20 * np.log10(np.abs(x).max() + 1e-12)), 2),
            'gain_reduction_db': 0.0,
            'clipped_samples': 0,
            'sha256': hashlib.sha256(open(path, 'rb').read()).hexdigest(),
            'delivered': delivered,
            'loudness_lufs': round(lufs_momentary_max(x[:, None]), 1),
            'loudness_measure': 'momentary_max',
            'true_peak_dbtp': round(true_peak_db(x[:, None]), 1),
            'source': {'kind': 'recording / sound library', 'layers': [{
                'kind': 'recording / sound library',
                'library': LIBRARY,
                'pack': os.path.basename(pack),
                'file': name,
                'licence': LICENCE,
                'window_s': [round(start, 3), round(start + length, 3)],
            }]},
        })
        print(f'{audio_id:44s} {len(x)/SR:5.2f}s  {rows[-1]["loudness_lufs"]:6.1f} LUFS  {rows[-1]["true_peak_dbtp"]:6.1f} dBTP')
    with open(os.path.join(OUT, 'spell_rows.json'), 'w', encoding='utf-8') as f:
        json.dump({'sounds': rows}, f, indent=2, ensure_ascii=False)
        f.write('\n')
    print(f'{len(rows)} files, rows in out/spell_rows.json')
    with open(os.path.join(OUT, 'CREDITS_spell_sfx.txt'), 'w', encoding='utf-8', newline='\n') as f:
        f.write(section(rows))
    for target in sys.argv[1:]:
        credite(target, rows)


def section(rows):
    """The credits lines for these rows: one line per library file, naming the spell sounds it made."""
    used = {}
    for r in rows:
        for layer in r['source']['layers']:
            used.setdefault((layer['pack'], layer['file']), set()).add(r['audio_id'].rsplit('.', 2)[0])
    block = ['== Spell SFX (P2)', f'   licence: {LICENCE}',
             '   Cut from the library at one window, filtered and levelled to the magic family: cast -21.5 LUFS momentary max, flight -23,',
             '   impact -19, a resolution -22, an end -24; mono 48 kHz PCM_24. Window and loudness are in the audio manifest per file.']
    for (pack, file), ids in sorted(used.items()):
        block.append(f'   {pack} / {file}  ->  {", ".join(sorted(ids))}')
    return '\n'.join(block) + '\n'


def credite(path, rows):
    """Put the spell section in a credits file, replacing the section a previous run wrote."""
    text = open(path, encoding='utf-8-sig').read() if os.path.exists(path) else ''
    kept, dropping = [], False
    for line in text.split('\n'):
        if line.startswith('== Spell SFX'):
            dropping = True
            continue
        if dropping and line.startswith('== '):
            dropping = False
        if not dropping:
            kept.append(line)
    while kept and kept[-1] == '':
        kept.pop()
    kept.append('')
    with open(path, 'w', encoding='utf-8', newline='\n') as f:
        f.write('\n'.join(kept).rstrip('\n') + '\n\n' + section(rows))
    print(f'credits: {path}')


if __name__ == '__main__':
    main()



