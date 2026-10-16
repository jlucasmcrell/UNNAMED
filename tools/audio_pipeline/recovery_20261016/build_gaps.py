"""The audit's silent gaps that recorded sources on BEAST can fill: gathering wood, herbs and fibre; cooking and workbench crafting;
trade; eating, drinking and salves; an interior room tone. Mono one-shots placed in the world; the room tone is a stereo loop
cross-faded into its own head. usage: PYTHONPATH=tools python -P tools/build_gaps.py <recovery root>"""
import json, os, sys, hashlib, zlib
import numpy as np, soundfile as sf
from alib import SR, load, lufs_momentary_max, lufs_integrated, true_peak_db
from dsp import take, hp, lp, pitch, window, mix, level

ROOT = sys.argv[1]
OUT = os.path.join(ROOT, 'out', 'audio', 'recovered_world')
os.makedirs(OUT, exist_ok=True)
INDEX = json.load(open(os.path.join(ROOT, 'src', 'pool', 'index.json'), encoding='utf-8'))
S = r'C:\Otherreach_Recovery\asset_work\audio\_src'
IND = S + r'\cosmos\Indoor Ambience (cp-indoor-ambience)'
LIC_SON = 'Sonniss GDC 2026 Game Audio Bundle licence: royalty-free, commercial use, no attribution; no redistribution as a library; never into AI'
LIC_COS = 'Leartes Cosmos Premium subscription licence (private builds; post-subscription rights unconfirmed)'


def src(path, lib, lic, start=0.0, length=None, st=0.0, hpf=None, lpf=None, gain=0.0, at=0.0, stereo=False):
    x = load(path)
    x = x if stereo else x.mean(axis=1)
    a = int(start * SR)
    x = x[a:] if length is None else x[a:a + int(length * SR)]
    if not stereo:
        x = pitch(x, st)
        if hpf:
            x = hp(x, hpf)
        if lpf:
            x = lp(x, lpf)
        f = min(len(x), int(0.02 * SR))
        x[-f:] *= np.linspace(1, 0, f)
        x = x / (np.abs(x).max() + 1e-9)
    return (x, at, gain), {'library': lib, 'file': os.path.basename(path), 'start_s': start, 'length_s': length, 'semitones': st,
                           'hp': hpf, 'lp': lpf, 'gain_db': gain, 'offset_s': at, 'licence': lic}


def pooled(ref, **kw):
    gain, at = kw.pop('gain', 0.0), kw.pop('at', 0.0)
    st, hpf, lpf = kw.pop('st', 0.0), kw.pop('hpf', None), kw.pop('lpf', None)
    x = take(ref)
    if kw:
        x = window(x, **kw)
    x = pitch(x, st)
    if hpf:
        x = hp(x, hpf)
    if lpf:
        x = lp(x, lpf)
    s = INDEX[ref]
    return (x / (np.abs(x).max() + 1e-9), at, gain), {'library': s['library'], 'file': os.path.basename(s['file']), 'window_s': [s['start_s'], s['end_s']],
                                                       'semitones': st, 'gain_db': gain, 'offset_s': at, 'licence': s['licence']}


C = lambda name: (IND + '\\' + name, 'Cosmos - ' + os.path.basename(os.path.dirname(os.path.normpath(IND + '\\' + name))), LIC_COS)
COINS = [(S + r'\sonniss\2of5\Cinematic Sound Design - UI Interaction Elements\Ting Coins.wav', 'Sonniss GDC 2026 / Cinematic Sound Design - UI Interaction Elements', LIC_SON),
         (S + r'\sonniss\2of5\Cinematic Sound Design - Hybrid Game & UI Elements\Foley Coin Flip Single Fast.wav', 'Sonniss GDC 2026 / Cinematic Sound Design - Hybrid Game & UI Elements', LIC_SON),
         (S + r'\sonniss\3of5\InMotionAudio - Scratch Card\OBJMisc_ScratchCard_CoinTapScratch_01_InMotionAudio_ScratchCard.wav', 'Sonniss GDC 2026 / InMotionAudio - Scratch Card', LIC_SON),
         (IND + r'\PAD_Jukebox_CoinDrop\A_Jukebox_CoinDrop.wav', 'Cosmos - Jukebox Coin Drop', LIC_COS)]
BAG = (S + r'\sonniss\2of5\Epic Stock Media - Fantasy Game 2 - Sound Kit for Enchanted Realms\CLOTHFlp_Action Inventory Open Flip Cloth Canvas Bag Slide Light 02_ESM_FG2.wav',
       'Sonniss GDC 2026 / Epic Stock Media - Fantasy Game 2', LIC_SON)
FIRE = (S + r'\sonniss\3of5\Ivo Vicic - Campfire - Bonfire FX\24 Campfire, Dropping Fresh Pine Branches in Fire, Crackling, Sizzling Strong, Close 02.wav',
        'Sonniss GDC 2026 / Ivo Vicic - Campfire - Bonfire FX', LIC_SON)


def recipes():
    r = {}
    r['sfx.crafting.gather.wood'] = (-19, 0.7, 'gathering wood: a hatchet bite and the split', [
        [src(*C(r'Chopingwood\Survival SFX - chopingwood%d.wav' % (i + 1))), pooled('k_wood_h/%02d.wav' % (i + 1), gain=-5, st=-2),
         pooled('wood_heavy/%02d.wav' % (4, 5, 17)[i], gain=-12, length=0.35)] for i in range(3)])
    r['sfx.crafting.gather.herb'] = (-27, 0.9, 'picking herbs: leaves rustling and a stem snapping', [
        [pooled('cloth/%02d.wav' % (1, 2, 3)[i], hpf=1800, start=(0.4, 0.15, 0.3)[i], length=0.35), pooled('k_generic/%02d.wav' % (i + 1), gain=-10, st=9, hpf=2000, at=0.18, length=0.06)]
        for i in range(3)])
    r['sfx.crafting.gather.fibre'] = (-27, 0.9, 'pulling fibre (bog cotton): a soft tearing pull', [
        [pooled('cloth/%02d.wav' % (2, 1, 3)[i], hpf=900, lpf=9000, st=-2, start=(0.1, 0.5, 0.5)[i], length=0.45), src(*BAG, gain=-8, hpf=600, start=0.05, length=0.4)]
        for i in range(3)])
    r['sfx.crafting.cook'] = (-24, 0.4, 'meat laid on the cookfire: a hiss and sizzle', [
        [src(*C(r'..\Sizzling Meat Loop (Sizzling meat loop)\Sizzling meat loop\Survival SFX - sizzlingmeatloop%d.wav' % (i + 1)), start=1.0 + i, length=1.8),
         src(*FIRE, gain=-8, start=10 + 17 * i, length=1.8)] for i in range(3)])
    r['sfx.crafting.workbench'] = (-22, 0.6, 'work at the bench: fitting, tapping and binding', [
        [src(*C(r'Crafting_assembling\Survival SFX - crafting(assembling)%d.wav' % (i + 1))),
         src(*C(r'Crafting_hammering\Survival SFX - crafting(hammering)1 (%d).wav' % (i + 1)), gain=-4, at=0.5)] for i in range(3)])
    r['sfx.interaction.trade.coins'] = (-25, 0.5, 'a trade: coins counted over', [
        [src(*COINS[i], hpf=400), src(*COINS[(i + 2) % 4], hpf=400, gain=-6, at=0.12)] for i in range(3)])
    r['sfx.player.eat'] = (-24, 0.5, 'eating: bites and chewing', [[src(*C(r'Eating\Survival SFX - eating%d.wav' % i))] for i in (1, 3, 4)])
    r['sfx.player.drink'] = (-24, 0.5, 'drinking: swallows', [[src(*C(r'Drinking\Survival SFX - drinking%d.wav' % i))] for i in (1, 2, 3)])
    r['sfx.player.salve'] = (-25, 0.5, 'applying a salve: a pot opened, cloth and a breath', [[src(*C(r'First Aid\Survival SFX - firstaid%d.wav' % i))] for i in (1, 2, 3)])
    return r


def write(aid, y, rows, role, group, jitter, layers, loop=False):
    path = os.path.join(OUT, aid + '.wav')
    sf.write(path, y.astype(np.float32), SR, subtype='PCM_24')
    back, _ = sf.read(path, dtype='float32', always_2d=True)
    stereo = back.shape[1] == 2
    rows.append({'audio_id': aid, 'category': aid.split('.')[1], 'group': group, 'gameplay_role': role, 'human_auditioned': False, 'model': None,
                 'channels': back.shape[1], 'loop': loop, 'sample_rate': SR, 'subtype': 'PCM_24', 'seconds': round(len(back) / SR, 3),
                 'delivered_seconds': round(len(back) / SR, 3), 'peak_dbfs': round(float(20 * np.log10(np.abs(back).max() + 1e-9)), 2),
                 'clipped_samples': int((np.abs(back) >= 0.9999).sum()),
                 'loudness_lufs': round((lufs_integrated if loop else lufs_momentary_max)(back.astype(np.float64)), 1),
                 'loudness_measure': 'integrated' if loop else 'momentary_max', 'true_peak_dbtp': round(true_peak_db(back), 2),
                 **({'pitch_jitter_semitones': jitter} if jitter else {}),
                 'sha256': hashlib.sha256(open(path, 'rb').read()).hexdigest(), 'delivered': f'audio/recovered_world/{aid}.wav',
                 'source': {'kind': 'layered recording / sound library', 'build': 'audio_recovery/tools/build_gaps.py', 'layers': layers}})
    print(f"{aid:40s} {rows[-1]['seconds']:6.2f}s {rows[-1]['loudness_lufs']:6.1f} {rows[-1]['loudness_measure']} ch{rows[-1]['channels']}")


def room_tone(rows):
    """amb.interior.room_tone.01: the Cosmos room tone and winter interior bed, high-passed, 40 s with a 4 s equal-power seam."""
    (a, _, _), pa = src(IND + r'\CP_RoomTone\A_IndoorRoomTone_001.wav', 'Cosmos - Indoor Ambience, Room Tone', LIC_COS, stereo=True)
    (b, _, _), pb = src(S + r'\cosmos\Winter Indoor Ambience (WP_InteriorAmbience)\WP_InteriorAmbience\A_WinterPath_Interior_AmbienceLoop_3.wav',
                         'Cosmos - Winter Indoor Ambience', LIC_COS, stereo=True)
    length, xf = 40.0, 4.0
    n, f = int(length * SR), int(xf * SR)
    tone = np.concatenate([a] * (int((n + f) / len(a)) + 2))[:n + f]
    bed = b[int(5 * SR):int(5 * SR) + n + f]
    y = hp(tone, 60) * 0.6 + hp(bed, 80) * 0.8
    head, tail = y[:f], y[n:n + f]
    w = np.linspace(0, np.pi / 2, f)[:, None]
    y = y[:n].copy()
    y[:f] = head * np.sin(w) + tail * np.cos(w)
    y *= 10 ** ((-34 - lufs_integrated(y)) / 20)
    pa.update({'length_s': None, 'gain_db': -4.4}); pb.update({'start_s': 5.0, 'length_s': length + xf, 'gain_db': -1.9})
    write('amb.interior.room_tone.01', y, rows, 'inside a building: a quiet room tone under the outdoor bed', 'ambience.bed', 0, [pa, pb], loop=True)


def build():
    rows = []
    for fid, (target, jitter, role, layers_by_variant) in recipes().items():
        for i, layers in enumerate(layers_by_variant):
            y = level(mix([l for l, _ in layers]), target)
            write(f'{fid}.{i + 1:02d}', y, rows, role, fid.split('.')[1] + '.' + fid.split('.')[2], jitter, [p for _, p in layers])
    room_tone(rows)
    json.dump({'sounds': rows}, open(os.path.join(ROOT, 'out', 'gap_rows.json'), 'w', encoding='utf-8'), indent=1)


if __name__ == '__main__':
    build()
