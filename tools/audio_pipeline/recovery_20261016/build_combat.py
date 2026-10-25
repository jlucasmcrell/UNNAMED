"""Rebuild the combat sound families from recorded takes (Sonniss GDC 2024/2026, Cosmos, Kenney CC0, OpenGameArt CC0) plus synthesised
air and string, S2-style: every impact is a transient + a body + a low thud with the hit at the file start; every variant uses different
takes or a different layering (no pitch-copies of one take); families carry a pitch jitter the game applies per play. The creature voice
families that gain takes keep the ones the sound set at <asset root> already has.
usage: PYTHONPATH=tools python -P tools/build_combat.py <recovery root> <asset root>   (deterministic)"""
import json, os, sys, hashlib, shutil, zlib
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


# ---- creature materials: what a blade, a spear point and an arrow meet on each kind of body ---------------------------------
# One body layer per material, under the weapon's own bite; fur and hide are dull and dry, chitin cracks, bone knocks.
def fur(v, i, light):
    v.t(pick(THUD, i + 5), 0, -2 if not light else -6, st=v.u(-2, 0), from_peak=-0.006, length=0.25, lpf=2000)
    v.t(pick(n('k_soft_h', 5), i + 2), 0.002, -4, st=v.u(-2, 0), lpf=600)
    v.t(pick(n('cloth', 4), i + 1), 0, -2, st=v.u(-2, 0), length=0.22, lpf=7000)
    v.t(pick(n('cloth', 4), i + 3), 0.03, -8, st=v.u(0, 2), length=0.2, lpf=6000)


def hide(v, i, light):
    v.t(pick(n('whip', 1), i), 0, -6, st=v.u(-9, -7), lpf=3500, length=0.15)
    v.t(pick(n('k_punch_h', 5), i + 1), 0, -1, st=v.u(-6, -4), lpf=1800)
    v.t(pick(n('punch_body', 4), i + 2), 0, -3 if not light else -7, st=v.u(-5, -3), length=0.3, lpf=3000)
    v.t(pick(THUD, i + 7), 0, -3, st=v.u(-6, -4), from_peak=-0.006, length=0.45, lpf=900)


def chitin(v, i, light):
    v.t(pick(['shell_crack/01.wav', 'shell_crack/02.wav', 'ice_crack/01.wav', 'ice_crack/03.wav'], i), 0, 0, st=v.u(0, 3), hpf=900, length=0.18)
    v.t(pick(n('bone_break', 7), i + 1), 0.004, -5 if not light else -9, st=v.u(2, 4), hpf=600, length=0.2)
    v.t(pick(n('k_generic', 5), i + 2), 0, -6, st=v.u(-1, 2))
    v.t(pick(['squelch/01.wav'] + n('flesh_drop', 3), i), 0.012, -13, st=v.u(1, 3), length=0.18, lpf=5000)


def bone(v, i, light):
    v.t(pick(n('bone_break', 7), i), 0, 0, st=v.u(-2, 1), length=0.25)
    v.t(pick(n('k_wood_m', 5), i + 1), 0, -3, st=v.u(1, 4), hpf=300)
    v.t(pick(n('wood_snap', 4), i), 0.006, -9 if not light else -13, st=v.u(-1, 2), length=0.2)
    v.t(pick(n('k_plank', 5), i + 3), 0, -10, st=v.u(-2, 0), lpf=1800)


def blade(v, i, light, lpf=None):
    v.t(pick(n('blade_cut', 4), i + 2), 0, -4, st=v.u(-1.5, 1), hpf=1500, lpf=lpf, length=0.22)


def point(v, i, light, lpf=None):
    v.t(pick(n('spear_fish', 4), i + 1), 0, -4, st=v.u(-2, 0), hpf=1200, lpf=lpf, start=0, length=0.16)
    v.t(pick([s for s in STICK if s not in ('spear_stick/06.wav', 'spear_stick/11.wav')], i, 5), 0.02, -15, hpf=300, length=0.18)


def arrow(v, i, light, lpf=None):
    v.t(pick(n('spear_fish', 4), i + 3), 0, -3, st=v.u(1, 3), hpf=1800, lpf=lpf, start=0, length=0.11)


# The weapon's bite is muffled by fur and dulled by hide.
MATERIALS = {
    'fur': (fur, 2500, 'fur: a dull padded thud, a rustle, no wet'),
    'hide': (hide, 5000, 'thick hide: a low leathery smack with mass behind it'),
    'chitin': (chitin, None, 'chitin: a shell cracking, a little ichor'),
    'bone': (bone, None, 'bare bone: a dry hollow knock and splinter'),
}
WEAPONS = {
    'sfx.weapon.sword.impact': (blade, False, -15, 'a sword into'),
    'sfx.weapon.polearm.impact': (point, False, -15, 'a spear point into'),
    'sfx.weapon.bow.arrow.impact': (arrow, True, -17, 'an arrow into'),
}
for _stem, (_weapon, _light, _target, _verb) in WEAPONS.items():
    for _material, (_body, _muffle, _what) in MATERIALS.items():
        def _impact(v, i, weapon=_weapon, body=_body, muffle=_muffle, light=_light):
            weapon(v, i, light, muffle)
            body(v, i, light)
        family(f'{_stem}.{_material}', 4, _target, 0.7, f'{_verb} {_what}', 'weapon.impact')(_impact)


@family('sfx.weapon.bow.arrow.impact.plate', 4, -17, 0.7, 'an arrow on armour plate: a hard tick and a glancing ring', 'weapon.impact')
def arrow_plate(v, i):
    v.t(pick(n('k_metal_m', 5), i), 0, 0, st=v.u(1, 4))
    v.t(pick(n('dmg_metal', 4), i + 3), 0, -6, st=v.u(1, 3), hpf=1200, length=0.3)
    v.t(pick(n('k_generic', 5), i + 4), 0, -8, st=v.u(4, 7), hpf=1500)


# ---- creature blows: what lands on the character (or a companion) for each kind of attack ----------------------------------
@family('sfx.creature.blow.bite', 4, -17, 0.7, 'jaws closing on a body: a wet clamp and a tug', 'weapon.impact')
def blow_bite(v, i):
    v.t(pick(['chomp/01.wav', 'chomp/06.wav', 'chomp/08.wav', 'chomp/16.wav', 'chomp/17.wav'], i), 0, 0, st=v.u(-2, 1), length=0.3)
    v.t(pick(n('k_punch_m', 5), i + 1), 0, -6, st=v.u(-3, -1), lpf=1500)
    v.t(pick(WET, i + 2, 2), 0.01, -9, st=v.u(-2, 0), lpf=5000, start=0, length=0.2)
    v.t(pick(n('oga_eat', 4), i), 0.03, -14, st=v.u(-3, -1), length=0.2)


@family('sfx.creature.blow.claw', 4, -17, 0.7, 'claws raking a body: a scrape that tears, a blow under it', 'weapon.impact')
def blow_claw(v, i):
    v.t(pick(n('nail_scratch', 2), i), 0, 0, st=v.u(-2, 2), hpf=700, length=0.3)
    v.t(pick(n('gore_rip', 22), i, 5), 0.01, -5, st=v.u(-1, 1), lpf=7000, start=0, length=0.25)
    v.t(pick(n('k_punch_m', 5), i + 3), 0, -5, st=v.u(-2, 0), lpf=1800)


@family('sfx.creature.blow.gore', 4, -17, 0.6, 'a tusk or horn driven into a body: a heavy butt and a stab', 'weapon.impact')
def blow_gore(v, i):
    v.t(pick(n('k_punch_h', 5), i + 2), 0, 0, st=v.u(-5, -3), lpf=1500)
    v.t(pick(n('punch_body', 4), i + 3), 0, -2, st=v.u(-3, -1), length=0.35)
    v.t(pick(THUD, i + 1), 0, -3, st=v.u(-5, -3), from_peak=-0.006, length=0.45, lpf=1200)
    v.t(pick(n('axe_flesh', 5), i), 0.006, -10, st=v.u(-3, -1), length=0.25, lpf=6000)


@family('sfx.creature.blow.sting', 4, -18, 0.8, 'a fang or stinger stabbing a body: a sharp pierce and a click', 'weapon.impact')
def blow_sting(v, i):
    v.t(pick(n('spear_fish', 4), i + 2), 0, 0, st=v.u(2, 4), hpf=1500, start=0, length=0.12)
    v.t(pick(n('bug', 13), i, 3), 0.005, -8, st=v.u(-1, 2), length=0.2)
    v.t(pick(WET, i + 4, 2), 0.008, -11, st=v.u(0, 2), lpf=6000, start=0, length=0.15)
    v.t(pick(n('k_punch_m', 5), i + 4), 0, -10, lpf=1200)


@family('sfx.creature.blow.slam', 4, -18, 0.5, 'a construct\'s limb slammed into a body: metal mass and a hard thud', 'weapon.impact')
def blow_slam(v, i):
    v.t('metal_thud/01.wav', 0, 0, st=v.u(-4, -1), length=0.5, lpf=500)
    v.t(pick(n('k_plate_h', 5), i + 2), 0, -4, st=v.u(-5, -3))
    v.t(pick(n('k_punch_h', 5), i), 0, -2, st=v.u(-4, -2), lpf=1200)
    v.t(pick(n('metal_bang', 2), i), 0.01, -12, st=v.u(-3, 0), length=0.35)


# ---- creature swings: the attack moving through the air, at its commit ---------------------------------------------------
@family('sfx.creature.swing.bite', 4, -22, 0.8, 'a lunge to bite: a short rush of air and teeth snapping shut', 'weapon.swing')
def swing_bite(v, i):
    v.s('lunge air', whoosh(v.u(0.18, 0.26), v.u(350, 500), v.u(1200, 1800), v.rng, peak_at=0.6, width=0.5), 0, 0)
    v.t(pick(['chomp/10.wav', 'chomp/12.wav', 'chomp/17.wav', 'chomp/15.wav'], i), v.u(0.14, 0.2), -4, st=v.u(1, 3), hpf=900, length=0.12)


@family('sfx.creature.swing.claw', 4, -22, 0.9, 'a raking claw swipe: a quick hiss through the air', 'weapon.swing')
def swing_claw(v, i):
    v.t(pick(n('swish', 13), i, 3), 0, 0, st=v.u(-4, -1))
    v.s('swipe air', whoosh(v.u(0.2, 0.28), v.u(900, 1300), v.u(2600, 3800), v.rng, peak_at=0.5, width=0.45), 0, -6)


@family('sfx.creature.swing.gore', 4, -21, 0.7, 'a head swung low to gore: heavy air, a snort of effort', 'weapon.swing')
def swing_gore(v, i):
    v.s('heavy rush', whoosh(v.u(0.38, 0.5), v.u(140, 220), v.u(600, 900), v.rng, peak_at=v.u(0.5, 0.65), width=0.9, air=0.15), 0, 0)
    v.t(pick(n('rock_throw', 4), i), 0.0, -14, st=v.u(-6, -4), lpf=900)
    v.t(pick(n('cloth', 4), i + 2), 0.05, -14, st=v.u(-5, -3), length=0.3, lpf=2000)


@family('sfx.creature.swing.sting', 4, -23, 0.9, 'a fang strike from the dark: a thin fast hiss and a chitter', 'weapon.swing')
def swing_sting(v, i):
    v.s('strike air', whoosh(v.u(0.14, 0.2), v.u(1500, 2200), v.u(3500, 5000), v.rng, peak_at=0.55, width=0.4, air=0.4), 0, -2)
    v.t(pick(n('bug', 13), i + 5, 2), 0, -3, st=v.u(-2, 1), length=0.25)


@family('sfx.creature.swing.slam', 4, -21, 0.6, 'a construct\'s limb swung through the air: deep rushing air and armour rattle', 'weapon.swing')
def swing_slam(v, i):
    v.s('heavy whoosh', whoosh(v.u(0.45, 0.6), v.u(200, 320), v.u(900, 1400), v.rng, peak_at=v.u(0.5, 0.65), width=0.9, air=0.3), 0, 0)
    v.t('metal_whoosh/01.wav', 0.05, -12, st=v.u(-6, -4), lpf=4000, from_peak=-0.18, length=0.4)
    v.t(pick(n('dmg_metal', 4), i + 1), 0.0, -18, st=v.u(-5, -3), length=0.3, lpf=3000)


# ---- creature voices: more takes for the hurt and attack calls heard on every exchange -----------------------------------
# These families already have takes in the sound set; CARRY names them, and build() keeps them unchanged ahead of the new ones.
CARRY = {}


def voice(fid, carried, count, target, role, group):
    CARRY[fid] = carried
    return family(fid, count, target, 0.5, role, group)


@voice('sfx.creature.grey_wolf.hurt', 2, 2, -23, 'a wolf struck: a sharp yelp', 'creature.hurt')
def wolf_hurt(v, i):
    v.t(pick(['dog_bark/03.wav', 'dog_bark/01.wav'], i), 0, 0, st=v.u(3, 4.5), hpf=250)


@voice('sfx.creature.grey_wolf.death', 1, 2, -20, 'a wolf dying: a whine that sinks into a growl', 'creature.death')
def wolf_death(v, i):
    v.t(pick(['dog_bark/04.wav', 'dog_bark/02.wav'], i), 0, 0, st=v.u(1, 2), hpf=250)
    v.t(pick(['dog_grunt/01.wav', 'dog_grunt/03.wav'], i), 0.22, -2, st=-3, length=1.6)


@voice('sfx.creature.bristleback_boar.hurt', 2, 2, -18, 'a boar struck: a squeal cut short', 'creature.hurt')
def boar_hurt(v, i):
    v.t('pig/01.wav', 0, 0, st=v.u(-4, -2), **[dict(start=0.0, length=0.55), dict(start=0.55, length=0.6)][i])


@voice('sfx.creature.cave_hunting_spider.hurt', 2, 2, -14, 'a spider struck: a chittering hiss', 'creature.hurt')
def spider_hurt(v, i):
    v.t('insectoid/01.wav', 0, 0, st=v.u(3, 5), **[dict(start=0.0, length=0.45), dict(start=1.3, length=0.5)][i])
    v.t(pick(n('bug', 13), i + 7, 2), 0.02, -6, st=v.u(-1, 1), length=0.3)


@voice('sfx.creature.ash_ember_hound.hurt', 2, 1, -19, 'an ember hound struck: a rasping snarl', 'creature.hurt')
def hound_hurt(v, i):
    v.t('beast_growl/06.wav', 0, 0, st=1.5, length=0.7)


@voice('sfx.creature.animated_armour.hurt', 2, 2, -24, 'animated armour struck: plates jarring and a hollow ring', 'creature.hurt')
def armour_hurt(v, i):
    v.t(pick(n('k_metal_h', 5), i + 3), 0, 0, st=v.u(-6, -4))
    v.t(pick(n('dmg_metal', 4), i + 2), 0.01, -4, st=v.u(-4, -2), length=0.45, lpf=3500)


@voice('sfx.creature.bone_walker_husk.hurt', 2, 1, -13, 'a bone walker struck: a screeching rasp over rattling bone', 'creature.hurt')
def husk_hurt(v, i):
    v.t('husk_breath/01.wav', 0, 0, st=v.u(1, 2), length=0.6)
    v.t(pick(n('bone_break', 7), i + 4), 0.02, -8, st=v.u(2, 4), hpf=500, length=0.25)


@voice('sfx.creature.barrowback.hurt', 1, 2, -21, 'a barrowback struck: a deep bellow of pain', 'creature.hurt')
def barrowback_hurt(v, i):
    v.t(pick(n('bear', 2), i), 0, 0, st=v.u(-5, -3), lpf=4000)


@voice('sfx.creature.barrowback.attack', 1, 2, -19, 'a barrowback charging: a bellowing roar', 'creature.attack')
def barrowback_attack(v, i):
    v.t('herbivore/02.wav', 0, 0, st=v.u(-3.5, -2.5), **[dict(start=0.3, length=1.6), dict(start=1.9, length=1.8)][i])


@voice('sfx.creature.fellhorn.hurt', 1, 2, -20, 'a fellhorn struck: a short honking bleat', 'creature.hurt')
def fellhorn_hurt(v, i):
    v.t('herbivore/01.wav', 0, 0, st=v.u(4, 5.5), **[dict(start=0.2, length=0.8), dict(start=1.6, length=0.9)][i])


@voice('sfx.creature.fellhorn.attack', 1, 2, -20, 'a fellhorn lowering its horns: a bellow', 'creature.attack')
def fellhorn_attack(v, i):
    v.t(pick(['herbivore/01.wav', 'herbivore/02.wav'], i), 0, 0, st=v.u(2.5, 3.5), start=[1.0, 2.6][i], length=1.3)


def carried(asset):
    """The takes a voice family already has in the sound set at <asset>, copied unchanged into the output (with the family's jitter)."""
    m = json.load(open(os.path.join(asset, 'manifests', 'playable_prototype_audio_v3.json'), encoding='utf-8'))
    rows = []
    for fid, count in CARRY.items():
        old = sorted((s for s in m['sounds'] if s['audio_id'].rsplit('.', 1)[0] == fid), key=lambda s: s['audio_id'])
        assert len(old) == count, (fid, len(old))
        for s in old:
            dst = os.path.join(ROOT, 'out', *s['delivered'].split('/'))
            os.makedirs(os.path.dirname(dst), exist_ok=True)
            shutil.copyfile(os.path.join(asset, *s['delivered'].split('/')), dst)
            rows.append({**s, 'pitch_jitter_semitones': FAMILIES[fid][2]})
    return rows


def build():
    rows = carried(sys.argv[2])
    for fid, (count, target, jitter, role, group, fn) in FAMILIES.items():
        for i in range(count):
            v = V(fid, i)
            fn(v, i)
            y = level(mix(v.layers), target)
            aid = f'{fid}.{i + 1 + CARRY.get(fid, 0):02d}'
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
    json.dump({'families': {f: FAMILIES[f][0] + CARRY.get(f, 0) for f in FAMILIES}, 'sounds': rows},
              open(os.path.join(ROOT, 'out', 'combat_rows.json'), 'w', encoding='utf-8'), indent=1)


if __name__ == '__main__':
    build()
