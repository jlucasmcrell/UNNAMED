"""Inventory existing inspected animation sources and prepare an isolated full retarget job.

Copies receipts, never edits the Gate C source worktree or its assets.
"""
import argparse
import json
import re
from pathlib import Path


def prepare(original, output):
    output.mkdir(parents=True, exist_ok=True)
    read = lambda path: json.loads(path.read_text(encoding='utf-8-sig'))
    sources = read(original / 'sources.json')
    selection = read(Path(__file__).with_name('humgen_sources.json'))
    for source in sources['sources']:
        prefix = Path(source['path']).stem
        for item in source['assets']:
            if item['kind'] != 'animation':
                continue
            name = item['name'].removeprefix(prefix).lstrip('_')
            if 'tpose' in name.lower():
                continue
            state = 'owned_' + source['id'] + '_' + re.sub('[^a-z0-9]+', '_', name.lower()).strip('_')
            selection[state] = [source['id'], name]
    fal = read(original / 'fal_migration.json')
    for package in fal['packages']:
        if '/Animations/' not in package:
            continue
        name = package.rsplit('/', 1)[-1]
        selection['owned_fal_' + re.sub('[^a-z0-9]+', '_', name.lower())] = ['fal', name]
    # These semantic names refer to genuine, inspected native takes.
    selection.update(work_chop=['ual2', 'TreeChopping_Loop'], carry_walk=['ual2', 'Walk_Carry_Loop'],
                     work_harvest=['ual2', 'Farm_Harvest'], eat=['ual2', 'Consume'],
                     emote_yes=['ual2', 'Yes'], emote_dance=['ual1', 'Dance_Loop'])
    for name in ('sources.json', 'fal_migration.json', 'target_humgen_creator_female.json', 'target_humgen_creator_male.json'):
        (output / name).write_text(json.dumps(read(original / name), indent=2), encoding='utf-8')
    (output / 'selection.json').write_text(json.dumps(selection, indent=2), encoding='utf-8')
    print(f'{len(selection)} selected states from inspected UAL/FAL receipts; Mixamo/Rokoko/CMU coverage still pending')


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('original', type=Path)
    parser.add_argument('output', type=Path)
    args = parser.parse_args()
    prepare(args.original, args.output)
