"""Interim original score: generate the music cues locally with Stable Audio Open 1.0 through the running ComfyUI (localhost only),
two seeds a cue, text prompts only (no library audio goes in). Run under the GPU lock. Naming cues generates only those, and their rows
replace theirs in generated.json; the others are kept.
usage: python -P tools/gen_music.py <recovery root> [cue ...]"""
import json, os, sys, time, urllib.request

ROOT = sys.argv[1]
ONLY = set(sys.argv[2:])
API = 'http://127.0.0.1:8188'
NEG = 'vocals, singing, voice, lyrics, bass, sub bass, electronic, synth, edm, drum kit, snare, hi-hat, guitar, piano, orchestra, brass, distortion, low quality, noise, clipping'
# AUDIO_DESIGN.md: Veth - warm, acoustic; fiddle, frame drum, breath, room tone; a battered wooden flute in every cue; no low end.
CUES = {
    'mus.title': (47, 'slow solemn folk theme, solo battered wooden flute melody over a soft fiddle drone, sparse frame drum, wide room reverb, warm acoustic, melancholic and hopeful, 70 bpm'),
    'mus.settlement.day': (47, 'gentle village folk tune, wooden flute and fiddle playing together slightly out of tune, light frame drum, warm acoustic, calm daytime, 92 bpm'),
    'mus.settlement.night': (47, 'quiet night folk lullaby, solo wooden flute, very sparse, soft fiddle harmonics, hushed, lots of silence, warm room tone, 60 bpm'),
    'mus.combat': (47, 'tense driving folk battle music, pounding frame drums, urgent fiddle ostinato, sharp wooden flute stabs, acoustic, dark and fast, 140 bpm'),
    # owner ruling 2026-10-24: a known place, now and then, on a later visit - quieter and slower than the settlement, still Veth
    'mus.explore': (47, 'quiet wandering folk air, solo battered wooden flute, long pauses, soft fiddle drone, faint frame drum like a slow heartbeat, open air, warm acoustic, contemplative, lots of silence, 66 bpm'),
    'mus.stinger.discovery': (9, 'short discovery fanfare phrase, wooden flute rising melody over a swelling fiddle drone, acoustic, wonder, ends on a held note'),
}
SEEDS = (11, 23)


def post(path, body):
    req = urllib.request.Request(API + path, data=json.dumps(body).encode(), headers={'Content-Type': 'application/json'})
    return json.loads(urllib.request.urlopen(req, timeout=60).read())


def graph(prompt, seconds, seed, prefix):
    return {
        '1': {'class_type': 'CheckpointLoaderSimple', 'inputs': {'ckpt_name': 'stable_audio_open_1.0.safetensors'}},
        '2': {'class_type': 'CLIPLoader', 'inputs': {'clip_name': 't5_base.safetensors', 'type': 'stable_audio'}},
        '3': {'class_type': 'CLIPTextEncode', 'inputs': {'text': prompt, 'clip': ['2', 0]}},
        '4': {'class_type': 'CLIPTextEncode', 'inputs': {'text': NEG, 'clip': ['2', 0]}},
        '5': {'class_type': 'ConditioningStableAudio', 'inputs': {'positive': ['3', 0], 'negative': ['4', 0], 'seconds_start': 0.0, 'seconds_total': float(seconds)}},
        '6': {'class_type': 'EmptyLatentAudio', 'inputs': {'seconds': float(seconds), 'batch_size': 1}},
        '7': {'class_type': 'KSampler', 'inputs': {'model': ['1', 0], 'positive': ['5', 0], 'negative': ['5', 1], 'latent_image': ['6', 0], 'seed': seed,
                                                   'steps': 100, 'cfg': 7.0, 'sampler_name': 'dpmpp_3m_sde_gpu', 'scheduler': 'exponential', 'denoise': 1.0}},
        '8': {'class_type': 'VAEDecodeAudio', 'inputs': {'samples': ['7', 0], 'vae': ['1', 2]}},
        '9': {'class_type': 'SaveAudio', 'inputs': {'audio': ['8', 0], 'filename_prefix': prefix}},
    }


def main():
    jobs = {}
    for cue, (seconds, prompt) in CUES.items():
        if ONLY and cue not in ONLY:
            continue
        for seed in SEEDS:
            prefix = f'otherreach_music/{cue}_s{seed}'
            r = post('/prompt', {'prompt': graph(prompt, seconds, seed, prefix)})
            jobs[r['prompt_id']] = (cue, seed, prompt, seconds)
    results = []
    while jobs:
        time.sleep(5)
        for pid in list(jobs):
            h = json.loads(urllib.request.urlopen(f'{API}/history/{pid}', timeout=30).read())
            if pid not in h:
                continue
            st = h[pid].get('status', {})
            cue, seed, prompt, seconds = jobs.pop(pid)
            files = [a for o in h[pid].get('outputs', {}).values() for a in o.get('audio', [])]
            results.append({'cue': cue, 'seed': seed, 'prompt': prompt, 'negative': NEG, 'seconds': seconds, 'status': st.get('status_str'),
                            'files': [os.path.join(f.get('subfolder', ''), f['filename']) for f in files],
                            'model': 'stable_audio_open_1.0.safetensors', 'text_encoder': 't5_base.safetensors',
                            'settings': {'steps': 100, 'cfg': 7.0, 'sampler': 'dpmpp_3m_sde_gpu', 'scheduler': 'exponential'}})
            print(cue, seed, st.get('status_str'), results[-1]['files'], flush=True)
    os.makedirs(os.path.join(ROOT, 'src', 'music'), exist_ok=True)
    path = os.path.join(ROOT, 'src', 'music', 'generated.json')
    kept = [r for r in json.load(open(path, encoding='utf-8')) if r['cue'] not in ONLY] if ONLY and os.path.exists(path) else []
    json.dump(kept + results, open(path, 'w', encoding='utf-8'), indent=1)


main()
