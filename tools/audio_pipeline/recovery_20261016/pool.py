"""Cut every source into its separate takes (onset detection), mono 48 kHz float, into src/pool/<tag>/NN.wav, with an index of
where each take came from. usage: PYTHONPATH=tools python -P tools/pool.py <recovery root>"""
import json, os, sys, glob
import numpy as np, soundfile as sf
from alib import load, onsets, SR

ROOT = sys.argv[1]
S = r'C:\Otherreach_Recovery\asset_work\audio\_src'
SON, COS, KEN = S + r'\sonniss', S + r'\cosmos', S + r'\kenney\Audio'
LIC_SON = 'Sonniss GDC 2026 Game Audio Bundle licence: royalty-free, commercial use, no attribution; no redistribution as a library; never into AI'
LIC_COS = 'Leartes Cosmos Premium subscription licence (private builds; post-subscription rights unconfirmed)'
LIC_KEN = 'CC0 1.0 (Kenney Impact Sounds)'
IND = COS + r'\Indoor Ambience (cp-indoor-ambience)'

# tag: (glob, licence, library, threshold dB, min gap s, max take s)
SOURCES = {
    'swing_thud':   (SON + r'\2of5\David Dumais Audio - Melee Weapons Sound Effects Pack 2\SWSH_*.wav', LIC_SON, 'Sonniss GDC 2026 / David Dumais Audio - Melee Weapons 2', -30, 0.35, 3.0),
    'swing_scrape': (SON + r'\2of5\David Dumais Audio - Melee Weapons Sound Effects Pack 2\METLFric_*.wav', LIC_SON, 'Sonniss GDC 2026 / David Dumais Audio - Melee Weapons 2', -40, 0.5, 3.0),
    'metal_swing_hit': (SON + r'\2of5\David Dumais Audio - Melee Weapons Sound Effects Pack 2\METLImpt_*.wav', LIC_SON, 'Sonniss GDC 2026 / David Dumais Audio - Melee Weapons 2', -45, 0.5, 3.0),
    'spear_stick':  (SON + r'\1of5\344 Audio - Historical Weapons Vol. 2\WEAPBlnt_*.wav', LIC_SON, 'Sonniss GDC 2026 / 344 Audio - Historical Weapons Vol. 2', -30, 0.2, 1.5),
    'shield':       (SON + r'\1of5\344 Audio - Historical Weapons Vol. 2\WEAPArmr_*.wav', LIC_SON, 'Sonniss GDC 2026 / 344 Audio - Historical Weapons Vol. 2', -28, 0.25, 2.0),
    'sword_slide':  (SON + r'\1of5\344 Audio - Historical Weapons Vol. 2\WEAPSwrd_*.wav', LIC_SON, 'Sonniss GDC 2026 / 344 Audio - Historical Weapons Vol. 2', -30, 0.3, 2.5),
    'punch_body':   (SON + r'\1of5\344 Audio - Cinematic Fight Vol. 1\FGHTImpt_*.wav', LIC_SON, 'Sonniss GDC 2026 / 344 Audio - Cinematic Fight Vol. 1', -30, 0.15, 1.2),
    'gore_smash':   (SON + r'\2of5\Epic Stock Media - Halloween Game - Haunted House and Horror Audio Scare Kit\GORESplt_*.wav', LIC_SON, 'Sonniss GDC 2026 / Epic Stock Media - Halloween Game', -40, 0.3, 2.0),
    'metal_whoosh': (SON + r'\1of5\344 Audio - Elemental Palette Designed Vol. 1\METLMisc_*.wav', LIC_SON, 'Sonniss GDC 2026 / 344 Audio - Elemental Palette Designed Vol. 1', -35, 0.08, 1.0),
    'wind_whoosh':  (SON + r'\1of5\344 Audio - Elemental Palette Designed Vol. 1\WINDDsgn_*.wav', LIC_SON, 'Sonniss GDC 2026 / 344 Audio - Elemental Palette Designed Vol. 1', -35, 0.08, 1.0),
    'metal_thud':   (SON + r'\5of5\The Noisery - Moaning Metal\DSGNImpt_*.wav', LIC_SON, 'Sonniss GDC 2026 / The Noisery - Moaning Metal', -35, 0.5, 2.5),
    'wood_heavy':   (SON + r'\1of5\344 Audio - Haunting Ambiences Vol. 3\WOODImpt_*.wav', LIC_SON, 'Sonniss GDC 2026 / 344 Audio - Haunting Ambiences Vol. 3', -30, 0.5, 2.0),
    'metal_bang':   (SON + r'\1of5\344 Audio - Haunting Ambiences Vol. 3\METLImpt_*.wav', LIC_SON, 'Sonniss GDC 2026 / 344 Audio - Haunting Ambiences Vol. 3', -30, 0.5, 2.0),
    'panting':      (SON + r'\5of5\SoundBits - Vox Hominis - Human Effort Voices\HMNBrth_Panting*.wav', LIC_SON, 'Sonniss GDC 2026 / SoundBits - Vox Hominis', -35, 0.12, 1.2),
    'cloth':        (SON + r'\3of5\InMotionAudio - Foley T-Shirt\*.wav', LIC_SON, 'Sonniss GDC 2026 / InMotionAudio - Foley T-Shirt', -35, 0.15, 1.0),
    'blade_cut':    (IND + r'\Bladecutting\*.wav', LIC_COS, 'Cosmos - Blade Cutting', -40, 1, 2),
    'dmg_metal':    (IND + r'\Damage_metal\*.wav', LIC_COS, 'Cosmos - Damage Metal', -40, 1, 2),
    'dmg_wound':    (IND + r'\Damage_metal_wound\*.wav', LIC_COS, 'Cosmos - Damage Metal Wound', -40, 1, 2),
    'sticks_wing':  (COS + r'\Sticks Wing (Sticks wing)\*\*.wav', LIC_COS, 'Cosmos - Sticks Wing', -40, 1, 2),
    'spear_fish':   (COS + r'\Spear Fishing (Spear fishing)\*\*.wav', LIC_COS, 'Cosmos - Spear Fishing', -40, 1, 2),
    'hammer':       (IND + r'\Hammer\*.wav', LIC_COS, 'Cosmos - Hammer', -40, 1, 2),
    'rock_throw':   (COS + r'\Throwing Rock (Throwing rock)\*\*.wav', LIC_COS, 'Cosmos - Throwing Rock', -40, 1, 2),
    'dying':        (IND + r'\Dying\*.wav', LIC_COS, 'Cosmos - Dying', -45, 1, 3),
    'breathe':      (COS + r'\Male Breathe (WW2_Male_Breathe)\*\*.wav', LIC_COS, 'Cosmos - WW2 Male Breathe', -35, 0.15, 1.5),
    'cough':        (IND + r'\Cough\*.wav', LIC_COS, 'Cosmos - Cough', -45, 1, 2),
    'gore_rip':     (COS + r'\Zombie Voices Pack - Pull Rip off and Tear (*)\*\*.wav', LIC_COS, 'Cosmos - Zombie Voices Pack, Pull Rip off and Tear', -45, 1, 2),
    'gore_guts':    (COS + r'\Zombie Voices Pack - Guts and Organs Falling on the Ground (*)\*\*.wav', LIC_COS, 'Cosmos - Zombie Voices Pack, Guts and Organs Falling', -45, 1, 2),
    'wood_creak':   (COS + r'\Wood Creaking (Wood creaking)\*\*.wav', LIC_COS, 'Cosmos - Wood Creaking', -45, 1, 2),
    'chop':         (IND + r'\Chopingwood\*.wav', LIC_COS, 'Cosmos - Chopping Wood', -45, 1, 2),
    'k_punch_h':    (KEN + r'\impactPunch_heavy_*.ogg', LIC_KEN, 'Kenney Impact Sounds', -50, 1, 2),
    'k_punch_m':    (KEN + r'\impactPunch_medium_*.ogg', LIC_KEN, 'Kenney Impact Sounds', -50, 1, 2),
    'k_soft_h':     (KEN + r'\impactSoft_heavy_*.ogg', LIC_KEN, 'Kenney Impact Sounds', -50, 1, 2),
    'k_plate_h':    (KEN + r'\impactPlate_heavy_*.ogg', LIC_KEN, 'Kenney Impact Sounds', -50, 1, 2),
    'k_plate_m':    (KEN + r'\impactPlate_medium_*.ogg', LIC_KEN, 'Kenney Impact Sounds', -50, 1, 2),
    'k_metal_h':    (KEN + r'\impactMetal_heavy_*.ogg', LIC_KEN, 'Kenney Impact Sounds', -50, 1, 2),
    'k_metal_m':    (KEN + r'\impactMetal_medium_*.ogg', LIC_KEN, 'Kenney Impact Sounds', -50, 1, 2),
    'k_wood_h':     (KEN + r'\impactWood_heavy_*.ogg', LIC_KEN, 'Kenney Impact Sounds', -50, 1, 2),
    'k_wood_m':     (KEN + r'\impactWood_medium_*.ogg', LIC_KEN, 'Kenney Impact Sounds', -50, 1, 2),
    'k_plank':      (KEN + r'\impactPlank_medium_*.ogg', LIC_KEN, 'Kenney Impact Sounds', -50, 1, 2),
    'k_mining':     (KEN + r'\impactMining_*.ogg', LIC_KEN, 'Kenney Impact Sounds', -50, 1, 2),
    'k_generic':    (KEN + r'\impactGeneric_light_*.ogg', LIC_KEN, 'Kenney Impact Sounds', -50, 1, 2),
}

index = {}
for tag, (pattern, lic, lib, th, gap, mx) in SOURCES.items():
    files = sorted(glob.glob(pattern))
    if not files:
        print('MISSING', tag, pattern)
        continue
    out = os.path.join(ROOT, 'src', 'pool', tag)
    os.makedirs(out, exist_ok=True)
    n = 0
    for f in files:
        x = load(f).mean(axis=1)
        for a, b, _ in onsets(x[:, None], th, gap):
            a = max(0, a - int(0.004 * SR))
            b = min(len(x), b + int(0.06 * SR), a + int(mx * SR))
            if (b - a) / SR < 0.06:
                continue
            seg = x[a:b].copy()
            fade = min(len(seg), int(0.02 * SR))
            seg[-fade:] *= np.linspace(1, 0, fade)
            n += 1
            name = f'{n:02d}.wav'
            sf.write(os.path.join(out, name), seg.astype(np.float32), SR, subtype='FLOAT')
            index[f'{tag}/{name}'] = {'file': f, 'start_s': round(a / SR, 3), 'end_s': round(b / SR, 3), 'licence': lic, 'library': lib,
                                      'seconds': round((b - a) / SR, 3), 'peak_db': round(float(20 * np.log10(np.abs(seg).max() + 1e-9)), 1)}
    print(tag, len(files), 'files ->', n, 'takes')
json.dump(index, open(os.path.join(ROOT, 'src', 'pool', 'index.json'), 'w', encoding='utf-8'), indent=1)
