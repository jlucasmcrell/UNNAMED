"""Assemble proved native-source entries into a private, complete release art root.

This records source lineage, never visual approval. The ordinary packaged-game gate
must still regenerate and judge the four golden comparisons for this exact package.
"""
import argparse
import json
from pathlib import Path
import shutil

from approved_player_bodies import check_quality_restoration
from check_character_quality import GOLDENS, REQUIRED_SHARED_NATIVE, local, sha256


def entries(root, folder):
    return {p.stem: json.loads(p.read_text(encoding='utf-8-sig')) for p in sorted((root / folder).glob('*.json'))}


def assemble(base, entry_root, approved_base, golden_sources):
    base, approved_base = base.resolve(strict=True), approved_base.resolve(strict=True)
    if base == approved_base or base.is_relative_to(approved_base) or approved_base.is_relative_to(base):
        raise ValueError('Use a separate private release art root')
    contract = dict(schema=1, models=entries(entry_root, 'quality_contract_entries'),
                    parts=entries(entry_root, 'quality_part_entries'), presets=entries(entry_root, 'quality_preset_entries'),
                    golden_actors=sorted(GOLDENS), golden_sources={})
    if (len(contract['models']), len(contract['parts']), len(contract['presets'])) != (32, 204, 83):
        raise ValueError('Native source preparation incomplete: expected 32 bodies, 204 parts and 83 presets')
    if set(golden_sources) != GOLDENS:
        raise ValueError('Provide native source renders for all four golden actors')
    for actor, sources in golden_sources.items():
        if not isinstance(sources, dict) or not sources:
            raise ValueError('Source renders must be keyed by the actual native model: ' + actor)
        contract['golden_sources'][actor] = {}
        for model, source in sources.items():
            if model not in contract['models']:
                raise ValueError('Unknown golden source model: ' + model)
            contract['golden_sources'][actor][model] = sha256(Path(source))
    # Refuse stale entries and partial copies before writing either release manifest.
    for category in ('models', 'parts', 'presets'):
        for identifier, spec in contract[category].items():
            if not spec.get('files'):
                raise ValueError('Empty native source entry: ' + identifier)
            for relative, expected in spec['files'].items():
                if sha256(local(base, relative)) != expected:
                    raise ValueError('Stale/incomplete native entry: ' + relative)
    contract['common_files'] = {p: sha256(local(base, p)) for p in REQUIRED_SHARED_NATIVE}
    baseline_path = approved_base / 'primary/manifests/approved_player_bodies.json'
    baseline = json.loads(baseline_path.read_text(encoding='utf-8-sig'))
    if baseline.get('kind') != 'accepted_native_character_delivery':
        raise ValueError('Expected the original accepted native source approval')
    delivery = dict(schema=1, kind='native_quality_restoration_delivery',
                    baseline_manifest='primary/manifests/source_native_approval.json',
                    baseline_sha256=sha256(baseline_path), files=dict(baseline['files']), restorations={})
    for model, spec in contract['models'].items():
        sources = [p for p, record in baseline['files'].items()
                   if p.endswith('/' + model + '_rigged.glb') and record['sha256'] == spec['rig']['old_sha256']]
        if len(sources) != 1:
            raise ValueError('Missing/ambiguous original native body approval: ' + model)
        old_body, motion = sources[0], spec['motion']
        if motion not in baseline['files']:
            raise ValueError('Missing original native motion approval: ' + model)
        body_folder = Path(spec['body']).parent
        replacement = dict(source_body=old_body, source_motion=motion, body=spec['body'], motion=motion,
                           rig_proof=(body_folder / 'rig_equivalence.json').as_posix(), motion_proof=motion + '.quality.json')
        additions = motion + '.merge.json'
        if additions in spec['files']:
            replacement['motion_additions_proof'] = additions
        delivery['restorations'][model] = replacement
        del delivery['files'][old_body]
        del delivery['files'][motion]
        for relative in (spec['body'], motion):
            path = local(base, relative)
            delivery['files'][relative] = dict(bytes=path.stat().st_size, sha256=sha256(path))
    manifests = base / 'primary/manifests'
    manifests.mkdir(parents=True, exist_ok=True)
    baseline_copy = base / delivery['baseline_manifest']
    # Replace the private copy atomically; it may have been hardlinked during staging.
    temporary = baseline_copy.with_suffix('.staging')
    shutil.copyfile(baseline_path, temporary)
    temporary.replace(baseline_copy)
    check_quality_restoration(base, delivery)
    for name, document in (('approved_player_bodies.json', delivery), ('character_quality_contract.json', contract)):
        destination = manifests / name
        temporary = destination.with_suffix('.staging')
        temporary.write_text(json.dumps(document, indent=2), encoding='utf-8')
        temporary.replace(destination)
    return contract


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('asset_base', type=Path)
    parser.add_argument('--entries', required=True, type=Path)
    parser.add_argument('--approved-base', required=True, type=Path)
    parser.add_argument('--golden-sources', required=True, type=Path, help='actor -> native model -> source PNG path')
    args = parser.parse_args()
    contract = assemble(args.asset_base, args.entries, args.approved_base,
                        json.loads(args.golden_sources.read_text(encoding='utf-8-sig')))
    print('NATIVE_SOURCE_CONTRACT_ASSEMBLED', {k: len(contract[k]) for k in ('models', 'parts', 'presets')},
          'PACKAGED_GAME_VISUAL_ACCEPTANCE_REQUIRED')


if __name__ == '__main__':
    main()
