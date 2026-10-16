"""Rebuild the combat sound families from recorded takes (Sonniss GDC 2026, Cosmos, Kenney CC0) plus synthesised air and string,
S2-style: every impact is a transient + a body + a low thud with the hit at the file start; every variant uses different takes or a
different layering (no pitch-copies of one take); families carry a pitch jitter the game applies per play.
usage: PYTHONPATH=tools python -P tools/build_combat.py <recovery root>   (deterministic)"""
import json, os, sys, hashlib, zlib
import numpy as np, soundfile as sf
from alib import SR, lufs_momentary_max, true_peak_db
from dsp import take, hp, lp, bp, pitch, window, whoosh, twang, ring, mix, level

ROOT = sys.argv[1]
OUT = os.path.join(ROOT, 'out', 'audio', 'recovered_combat')
INDEX = json.load(open(os.path.join(ROOT, 'src', 'pool', 'index.json'), encoding='utf-8'))
os.makedirs(OUT, exist_ok=True)
SYNTH = {'library': 'synthesised (numpy, this build)', 'licence': 'original, owned by the project'}


class V:
    """One variant being built: its layers and their provenance."""
    def __init__(self, family, i):
        self.rng = np.random.default_rng(zlib.crc32(f'{family}.{i}'.encode()))
        self.layers, self.prov = [], []

    def t(self, ref, at=0.0, gain=0.0, st=0.0, hpf=None, lpf=None, **cut):
        x = take(ref)
        if cut:
            x = window(x, **cut)
        x = pitch(x, st)
        if hpf:
            x = hp(x, hpf)
        if lpf:
            x = lp(x, lpf)
        src = INDEX[ref]
        self.layers.append((x / (np.abs(x).max() + 1e-9), at, gain))
        self.prov.append({'library': src['library'], 'file': os.path.basename(src['file']), 'window_s': [src['start_s'], src['end_s']],
                          'cut': {k: v for k, v in cut.items()}, 'offset_s': at, 'gain_db': gain, 'semitones': st, 'hp': hpf, 'lp': lpf,
                          'licence': src['licence']})
        return self

    def s(self, what, sig, at=0.0, gain=0.0):
        self.layers.append((sig, at, gain))
        self.prov.append({**SYNTH, 'what': what, 'offset_s': at, 'gain_db': gain})
        return self

    def u(self, lo, hi):
        return float(self.rng.uniform(lo, hi))


def pick(seq, i, k=1):
    return seq[(i * k) % len(seq)]


def n(tag, count):
    return [f'{tag}/{j:02d}.wav' for j in range(1, count + 1)]


THUD = [f'swing_thud/{j:02d}.wav' for j in (1, 2, 3, 6, 8, 9, 11, 14, 16, 19)]
WET = [f'gore_rip/{j:02d}.wav' for j in (1, 4, 7, 8, 10, 11, 13, 19, 22)] + [f'gore_guts/{j:02d}.wav' for j in (2, 3, 4, 5, 6, 7)]
STICK = n('spear_stick', 16)
WOODH = [f'wood_heavy/{j:02d}.wav' for j in (1, 4, 5, 17, 29)]

FAMILIES = {}


def family(fid, count, target, jitter, role, group):
    def deco(fn):
        FAMILIES[fid] = (count, target, jitter, role, group, fn)
        return fn
    return deco


# ---- melee impacts: flesh -------------------------------------------------------------------------------------------------
@family('sfx.weapon.sword.impact.flesh', 6, -15, 0.8, 'sword cutting into flesh: blade bite, wet tear, body thud', 'weapon.impact')
def sword_flesh(v, i):
    v.t(pick(n('blade_cut', 4), i), 0, -3, st=v.u(-1.5, 1), hpf=1500, length=0.25)
    v.t(pick(WET, i, 2), 0.004, -6, st=v.u(-3, 0), lpf=7000, start=0, length=0.22)
    v.t(pick(THUD, i), 0.0, 0, st=v.u(-2, 0), from_peak=-0.006, length=0.35, lpf=4000)
    v.t(pick(n('k_punch_h', 5), i + 1), 0.002, -4, st=v.u(-3, -1), lpf=900)


@family('sfx.weapon.polearm.impact.flesh', 6, -15, 0.8, 'spear point driving into flesh: pierce, wet, heavy body thud, shaft judder', 'weapon.impact')
def spear_flesh(v, i):
    v.t(pick(n('spear_fish', 4), i), 0, -4, st=v.u(-2, 0), hpf=1200, start=0, length=0.18)
    v.t(pick(WET, i + 5, 2), 0.006, -5, st=v.u(-4, -1), lpf=6000, start=0, length=0.25)
    v.t(pick(THUD, i + 4), 0, 0, st=v.u(-3, -1), from_peak=-0.006, length=0.4, lpf=3500)
    v.t(pick(n('k_soft_h', 5), i), 0.003, -3, lpf=400)
    v.t(pick([s for s in STICK if s not in ('spear_stick/06.wav', 'spear_stick/11.wav')], i, 3), 0.02, -14, hpf=300, length=0.2)


@family('sfx.weapon.bow.arrow.impact.flesh', 5, -17, 0.9, 'arrow striking flesh: a sharp thwack, wet, a short body thump', 'weapon.impact')
def arrow_flesh(v, i):
    v.t(pick(n('spear_fish', 4), i + 2), 0, -3, st=v.u(1, 3), hpf=1800, start=0, length=0.12)
    v.t(pick(WET, i + 9), 0.003, -8, st=v.u(-1, 2), lpf=7000, start=0, length=0.15)
    v.t(pick(n('k_punch_m', 5), i), 0, -2, st=v.u(-2, 0), lpf=2500)


# ---- plate --------------------------------------------------------------------------------------------------------------
@family('sfx.weapon.sword.impact.plate', 5, -15, 0.6, 'sword on armour plate: heavy clang with mass under it', 'weapon.impact')
def sword_plate(v, i):
    v.t(pick(n('k_plate_h', 5), i), 0, -1, st=v.u(-3, -1))
    v.t(pick(n('dmg_metal', 4), i), 0.002, -4, st=v.u(-2, 1), hpf=600)
    v.t(pick(n('k_metal_h', 5), i + 2), 0, -6, st=v.u(-4, -2))
    v.t('metal_thud/01.wav', 0, -4, st=v.u(-2, 1), length=0.45, lpf=300)
    v.t(pick(n('sword_slide', 6), i), 0.03, -16, hpf=3000, length=0.3)


@family('sfx.weapon.polearm.impact.plate', 5, -15, 0.6, 'spear on armour plate: point skid and clang, a dull mass', 'weapon.impact')
def spear_plate(v, i):
    v.t(pick(n('k_metal_h', 5), i), 0, -1, st=v.u(-5, -2))
    v.t(pick(n('dmg_metal', 4), i + 1), 0, -5, st=v.u(-3, 0), hpf=500)
    v.t(pick(n('k_plate_m', 5), i + 3), 0.004, -5, st=v.u(-4, -2))
    v.t('metal_thud/01.wav', 0, -3, st=v.u(-3, 0), length=0.4, lpf=260)
    v.t(pick(STICK, i, 5), 0.01, -12, hpf=400, length=0.18)


# ---- wood and stone ----------------------------------------------------------------------------------------------------------
@family('sfx.weapon.sword.impact.wood', 4, -18, 0.7, 'sword biting into wood', 'weapon.impact')
def sword_wood(v, i):
    v.t(pick(n('chop', 4), i), 0, 0, st=v.u(-2, 1))
    v.t(pick(n('k_wood_h', 5), i), 0, -3, st=v.u(-3, 0))
    v.t(pick(WOODH, i), 0, -8, st=v.u(-1, 2), length=0.35)
    v.t(pick(n('blade_cut', 4), i + 1), 0, -12, hpf=3000, length=0.15)


@family('sfx.weapon.polearm.impact.wood', 4, -18, 0.7, 'spear point into wood: hard knock and shaft rattle', 'weapon.impact')
def spear_wood(v, i):
    v.t(pick([s for s in STICK if s not in ('spear_stick/06.wav', 'spear_stick/11.wav')], i, 2), 0, 0, st=v.u(-2, 1))
    v.t(pick(n('k_wood_h', 5), i + 2), 0, -3, st=v.u(-2, 0))
    v.t(pick(WOODH, i + 2), 0, -9, st=v.u(-2, 1), length=0.3)


@family('sfx.weapon.sword.impact.stone', 4, -18, 0.7, 'sword on stone: hard grit and ring', 'weapon.impact')
def sword_stone(v, i):
    v.t(pick(n('k_mining', 5), i), 0, -1, st=v.u(-1, 2), length=0.4)
    v.t(pick(n('hammer', 4), i), 0, -6, st=v.u(-1, 2))
    v.t(pick(n('rock_throw', 4), i), 0.01, -8, st=v.u(-1, 1))
    v.t(pick(n('sword_slide', 6), i + 2), 0.0, -14, hpf=3500, length=0.25)


@family('sfx.weapon.polearm.impact.stone', 4, -18, 0.7, 'spear on stone: point crack and grit', 'weapon.impact')
def spear_stone(v, i):
    v.t(pick(n('k_mining', 5), i + 2), 0, -1, st=v.u(0, 2), length=0.35)
    v.t(pick(n('rock_throw', 4), i + 1), 0.005, -6, st=v.u(-1, 1))
    v.t(pick(STICK, i, 3), 0.01, -12, hpf=400, length=0.18)


@family('sfx.weapon.bow.arrow.impact.wood', 4, -19, 0.8, 'arrow thunking into wood, the shaft quivering', 'weapon.impact')
def arrow_wood(v, i):
    v.t(pick(n('k_wood_m', 5), i), 0, 0, st=v.u(-1, 2))
    v.t(pick(n('chop', 4), i + 2), 0, -6, st=v.u(1, 3), length=0.15)
    v.s('shaft quiver', ring(v.u(110, 170), 0.45, decay=v.u(7, 10), wobble=v.u(16, 24), depth=0.6), 0.01, -14)


@family('sfx.weapon.bow.arrow.impact.stone', 3, -19, 0.8, 'arrow glancing off stone: crack and clatter', 'weapon.impact')
def arrow_stone(v, i):
    v.t(pick(n('k_generic', 5), i), 0, 0, st=v.u(0, 3))
    v.t(pick(n('rock_throw', 4), i + 2), 0.0, -4, st=v.u(1, 3))
    v.t(pick(n('k_mining', 5), i + 3), 0, -9, st=v.u(2, 4), length=0.2)


# ---- blocks -----------------------------------------------------------------------------------------------------------------
@family('sfx.weapon.sword.block', 6, -16, 0.7, 'a blow caught on the blade: steel on steel, ringing scrape', 'weapon.block')
def sword_block(v, i):
    v.t(pick(n('k_metal_h', 5), i), 0, 0, st=v.u(-1, 2))
    v.t(pick(n('dmg_metal', 4), i + 2), 0, -3, st=v.u(0, 2), hpf=800)
    v.t(pick(n('sword_slide', 6), i), 0.01, -9, st=v.u(-1, 1), hpf=2500, length=0.35)
    v.t(pick(n('k_punch_m', 5), i), 0, -10, lpf=500)


@family('sfx.weapon.polearm.block', 4, -16, 0.7, 'a blow caught on the spear shaft: hard wooden knock, hands jarred', 'weapon.block')
def spear_block(v, i):
    v.t(pick([s for s in STICK if s not in ('spear_stick/06.wav', 'spear_stick/11.wav')], i, 4), 0, 0, st=v.u(-2, 0))
    v.t(pick(n('k_wood_h', 5), i + 1), 0, -4)
    v.t(pick(n('dmg_metal', 4), i), 0, -12, hpf=1500)


# ---- swings ------------------------------------------------------------------------------------------------------------
@family('sfx.weapon.sword.swing.light', 6, -21, 0.9, 'a sword cut through the air: swift swish with a faint blade ring', 'weapon.swing')
def sword_swing(v, i):
    d = v.u(0.28, 0.4)
    v.s('blade whoosh', whoosh(d, v.u(700, 1100), v.u(2500, 4200), v.rng, peak_at=v.u(0.45, 0.6), width=v.u(0.5, 0.7)), 0, 0)
    v.s('low air push', lp(whoosh(d, 120, 380, v.rng, peak_at=0.55, width=0.7, air=0), 700), 0, -7)
    v.t(pick(THUD, i + 3), v.u(0.0, 0.04), -6, st=v.u(-1, 2), hpf=600, start=0, length=0.12)
    v.t(pick(n('sticks_wing', 4), i), v.u(0.05, 0.1), -7, st=v.u(-2, 1))
    v.t(pick(n('sword_slide', 6), i + 1), 0.06, -22, hpf=5000, length=0.2)


@family('sfx.weapon.sword.swing.heavy', 4, -20, 0.7, 'a heavy two-handed sword swing: deep rushing air', 'weapon.swing')
def sword_heavy(v, i):
    v.s('heavy whoosh', whoosh(v.u(0.45, 0.6), v.u(300, 450), v.u(1400, 2000), v.rng, peak_at=v.u(0.5, 0.65), width=0.9, air=0.4), 0, 0)
    v.s('low air push', lp(whoosh(0.55, 90, 300, v.rng, peak_at=0.55, width=0.9, air=0), 600), 0, -5)
    v.t('metal_whoosh/01.wav', 0.05, -10, st=v.u(-4, -2), lpf=5000, from_peak=-0.18, length=0.4)
    v.t(pick(n('cloth', 4), i), 0, -16, length=0.3)


@family('sfx.weapon.polearm.thrust', 5, -21, 0.8, 'a spear thrust: a short hard push of air, shaft and grip', 'weapon.swing')
def spear_thrust(v, i):
    v.s('thrust air', whoosh(v.u(0.22, 0.3), v.u(500, 700), v.u(1800, 2800), v.rng, peak_at=v.u(0.55, 0.7), width=0.45), 0, 0)
    v.s('low air push', lp(whoosh(0.28, 110, 330, v.rng, peak_at=0.6, width=0.5, air=0), 650), 0, -6)
    v.t(pick(n('sticks_wing', 4), i + 2), v.u(0.02, 0.06), -5, st=v.u(-4, -2))
    v.t(pick(n('cloth', 4), i + 1), 0, -14, length=0.2)
    v.t(pick(STICK, i, 7), 0.0, -20, hpf=800, length=0.1)


# ---- bow --------------------------------------------------------------------------------------------------------------------
@family('sfx.weapon.bow.draw', 3, -26, 0.4, 'drawing a yew bow: limb creak, string tension, leather', 'weapon.bow')
def bow_draw(v, i):
    v.t(pick(n('wood_creak', 4), i), 0, 0, st=v.u(1, 4), hpf=250, length=0.8)
    v.t(pick(n('cloth', 4), i + 2), 0.05, -10, length=0.5)


@family('sfx.weapon.bow.release', 4, -20, 0.6, 'loosing an arrow: string slap and twang, limb thump', 'weapon.bow')
def bow_release(v, i):
    v.s('bowstring twang', twang(v.u(95, 140), 0.35, v.rng, bright=v.u(0.993, 0.997)), 0, 0)
    v.t(pick(n('k_wood_m', 5), i + 1), 0, -6, st=v.u(-3, -1), lpf=2000)
    v.t(pick(n('k_soft_h', 5), i + 3), 0, -6, lpf=300)
    v.s('arrow leaving', whoosh(0.18, 1200, 3500, v.rng, peak_at=0.3, width=0.4), 0.01, -12)


@family('sfx.player.weapon.bow.nock', 2, -28, 0.5, 'nocking an arrow: a small wooden click', 'weapon.bow')
def bow_nock(v, i):
    v.t(pick(n('k_generic', 5), i + 1), 0, 0, st=v.u(6, 9), hpf=800, length=0.08)
    v.t(pick(n('cloth', 4), i + 3), 0.02, -10, length=0.15)


@family('sfx.weapon.bow.arrow.flight', 3, -25, 0.8, 'an arrow in flight: a thin fast hiss', 'weapon.bow')
def arrow_flight(v, i):
    v.s('arrow hiss', whoosh(v.u(0.35, 0.5), v.u(2500, 3500), v.u(1500, 2200), v.rng, peak_at=0.25, width=0.8, air=0.6), 0, 0)


# ---- the player's body ----------------------------------------------------------------------------------------------------
@family('sfx.player.hurt.light', 6, -20, 0.5, 'player struck lightly: a grunt with the blow landing on the body', 'player.hurt')
def hurt_light(v, i):
    v.t(pick([f'panting/{j:02d}.wav' for j in (1, 3, 4, 5, 7, 9, 11)], i), 0.01, 0, st=v.u(-1.5, 0), length=0.35)
    v.t(pick(n('k_punch_m', 5), i + 2), 0, -6, lpf=1500)
    v.t(pick(n('cloth', 4), i), 0, -16, length=0.15)


@family('sfx.player.hurt.heavy', 4, -18, 0.4, 'player struck hard: a pained cry over a heavy body hit', 'player.hurt')
def hurt_heavy(v, i):
    v.t(pick(n('dying', 6), i, 2), 0.02, 0, st=v.u(-1, 0.5), length=0.55)
    v.t(pick(n('k_punch_h', 5), i), 0, -3, lpf=1200)
    v.t(pick(n('punch_body', 4), i), 0, -6, lpf=5000, length=0.3)


@family('sfx.player.death', 2, -18, 0.0, 'the player dies: a last cry and the body falling', 'player.hurt')
def death(v, i):
    v.t(pick(n('dying', 6), i * 2 + 1), 0, 0, st=-0.5)
    v.t(pick(n('k_soft_h', 5), i), 0.85, -3, lpf=300)
    v.t(pick(n('cloth', 4), i + 1), 0.8, -10, length=0.4)


@family('sfx.player.impact.creature_blow', 5, -17, 0.7, 'a creature\'s blow landing on the player: claw/bite impact on the body', 'player.hurt')
def creature_blow(v, i):
    v.t(pick(n('k_punch_h', 5), i + 3), 0, 0, st=v.u(-2, 0))
    v.t(pick(n('punch_body', 4), i + 1), 0, -4, length=0.3)
    v.t(pick(WET, i + 3, 3), 0.004, -10, lpf=6000, start=0, length=0.15)


def build():
    rows = []
    for fid, (count, target, jitter, role, group, fn) in FAMILIES.items():
        for i in range(count):
            v = V(fid, i)
            fn(v, i)
            y = level(mix(v.layers), target)
            aid = f'{fid}.{i + 1:02d}'
            path = os.path.join(OUT, aid + '.wav')
            sf.write(path, y.astype(np.float32), SR, subtype='PCM_24')
            data = open(path, 'rb').read()
            back, _ = sf.read(path, dtype='float32')
            rows.append({
                'audio_id': aid, 'category': fid.split('.')[1], 'group': group, 'gameplay_role': role, 'human_auditioned': False,
                'model': None, 'channels': 1, 'loop': False, 'sample_rate': SR, 'subtype': 'PCM_24',
                'seconds': round(len(y) / SR, 3), 'delivered_seconds': round(len(y) / SR, 3),
                'peak_dbfs': round(float(20 * np.log10(np.abs(back).max() + 1e-9)), 2), 'clipped_samples': int((np.abs(back) >= 0.9999).sum()),
                'loudness_lufs': round(lufs_momentary_max(back[:, None].astype(np.float64)), 1), 'loudness_measure': 'momentary_max',
                'true_peak_dbtp': round(true_peak_db(back[:, None]), 2), 'pitch_jitter_semitones': jitter,
                'sha256': hashlib.sha256(data).hexdigest(), 'delivered': f'audio/recovered_combat/{aid}.wav',
                'source': {'kind': 'layered recording / sound library (+ synthesis where noted)', 'build': 'audio_recovery/tools/build_combat.py',
                           'layers': v.prov},
            })
            print(f"{aid:42s} {rows[-1]['seconds']:5.2f}s {rows[-1]['loudness_lufs']:6.1f} LUFS-M tp {rows[-1]['true_peak_dbtp']:5.1f}")
    json.dump({'families': {f: FAMILIES[f][0] for f in FAMILIES}, 'sounds': rows},
              open(os.path.join(ROOT, 'out', 'combat_rows.json'), 'w', encoding='utf-8'), indent=1)


if __name__ == '__main__':
    build()
