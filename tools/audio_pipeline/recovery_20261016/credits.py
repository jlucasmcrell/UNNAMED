"""CREDITS for the recovery build: every library and source file used, its licence, and which IDs use it.
usage: python -P tools/credits.py <recovery root>"""
import json, os, sys, collections

root = sys.argv[1]
use = collections.defaultdict(lambda: collections.defaultdict(set))
lic = {}
music = []
for name in ('combat_rows.json', 'gap_rows.json', 'music_rows.json'):
    p = os.path.join(root, 'out', name)
    if not os.path.exists(p):
        continue
    for r in json.load(open(p, encoding='utf-8'))['sounds']:
        if r['category'] == 'music':
            music.append(r)
            continue
        for layer in r['source']['layers']:
            lib = layer.get('library', '?')
            lic[lib] = layer.get('licence', '?')
            use[lib][layer.get('file') or layer.get('what', 'synthesis')].add(r['audio_id'].rsplit('.', 1)[0])
lines = ['OTHERREACH - audio recovery build, 2026-10-16: sources and licences', '',
         'No attribution is legally required for any file below (Sonniss GDC: none required; Kenney: CC0; Cosmos: subscription; synthesis and',
         'generated music: our own). Credit is still given here. Raw library files are not redistributed; only the processed game sounds ship.',
         'Nothing from Sonniss, Cosmos or Kenney was fed to any AI model.', '']
for lib in sorted(use):
    lines.append(f'== {lib}')
    lines.append(f'   licence: {lic[lib]}')
    for f in sorted(use[lib]):
        lines.append(f'   {f}  ->  {", ".join(sorted(use[lib][f]))}')
    lines.append('')
if music:
    lines.append('== Music (interim, generated locally from text prompts)')
    lines.append('   model: Stable Audio Open 1.0, Stability AI Community License (commercial use free under USD 1M annual revenue; owner revenue USD 0)')
    for r in music:
        lines.append(f'   {r["audio_id"]}: seed {r["seed"]}; prompt "{r["prompt"]}"')
open(os.path.join(root, 'out', 'CREDITS_audio_recovery.txt'), 'w', encoding='utf-8').write('\n'.join(lines) + '\n')
print('\n'.join(lines[:12]))
print(sum(len(v) for v in use.values()), 'source files')
