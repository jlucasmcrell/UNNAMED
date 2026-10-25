"""Check that an asset root carries the whole score (MusicChoice's families, the settlement theme by day and night), each with its file:
the package step of export_playtest_clean.ps1 -Primary. Exits 1 and names what is missing.
usage: python -P tools/check_music.py <asset root>"""
import json, os, sys

root = sys.argv[1]
NEED = {'mus.title', 'mus.combat', 'mus.tension', 'mus.explore', 'mus.settlement.day', 'mus.settlement.night', 'mus.stinger.discovery'}
manifest = json.load(open(os.path.join(root, 'manifests', 'playable_prototype_audio_v3.json'), encoding='utf-8'))
fam = lambda aid: aid.rsplit('.', 1)[0] if aid.rsplit('.', 1)[-1].isdigit() else aid
have = {fam(s['audio_id']) for s in manifest['sounds'] if s['audio_id'].startswith('mus.') and os.path.exists(os.path.join(root, *s['delivered'].split('/')))}
missing = sorted(NEED - have)
print(f'music missing: {missing}' if missing else f'music: all {len(NEED)} families present')
sys.exit(1 if missing else 0)
