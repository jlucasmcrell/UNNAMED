"""Bind exact approved bodies and native identities. Run only on a NEW staged asset root."""
import argparse, copy, hashlib, json
from pathlib import Path
HERE = Path(__file__).resolve().parent
REPO = HERE.parents[1]
ap = argparse.ArgumentParser(description=__doc__)
ap.add_argument('--asset-root', type=Path, required=True)
ap.add_argument('--integrity-report', type=Path, required=True)
ap.add_argument('--baseline-bindings', type=Path, help='Existing world/depot bindings to retain when creating the new root')
a = ap.parse_args()
run = a.asset_root / 'animation/ready/player/anim.player.mh.run.glb'
assert hashlib.sha256(run.read_bytes()).hexdigest() == 'ac1878ab2c600ca399c69f91cdae5908ec2e19eea3b1cbf8f0f7099e368a9b77', 'Accepted run changed'
base_path = REPO / 'src/Presentation/Art/art_bindings.json'
paths = [base_path, *sorted((REPO / 'content').glob('_art_bindings*.json')), a.asset_root / 'bindings.json']
documents = [json.loads(p.read_text(encoding='utf-8-sig')) if p.exists() else {} for p in paths]
if a.baseline_bindings is not None:
    assert a.baseline_bindings.resolve() != paths[-1].resolve(), 'Baseline must be a separate read-only source'
    documents[-1] = json.loads(a.baseline_bindings.read_text(encoding='utf-8-sig'))
base = documents[0]
recipes = [json.loads(p.read_text()) for p in sorted((HERE / 'npc_bodies').glob('*.json'))]
recipes = [r for r in recipes if r.get('active', True) and r['id'].startswith(('npc_', 'resident_'))]
canonical = {p.stem: p.read_text().splitlines()[0].removeprefix('id: ') for p in (REPO / 'content/npcs').glob('*/*.yaml')}
proof = {r['id']: r for r in json.loads(a.integrity_report.read_text())}
looks, pools = {}, {}
for r in recipes:
    rid, sex, people = r['id'], r['sex'], r['people']
    # Surface-only peoples reuse six native identity masters; no body reshaping.
    physical = rid if rid.startswith('npc_') or people in ('people.veth', 'people.siann', 'people.kal') else 'resident_veth_' + rid.rsplit('_', 1)[1]
    record = proof[physical]
    assert record['original_body_bin_skin_four_lods_material_joint_transforms_exact'] and record['player_correctives_identical'], physical
    assert record['active_clothing_meshes'] == [], physical
    asset = 'mh_' + physical
    folder = a.asset_root / 'rigged' / asset
    assert (folder / (asset + '_rigged.glb')).is_file(), physical
    assert (folder / (asset + '_correctives.json')).is_file(), physical
    template = base['people_by_visual_option']['player=metahuman_' + sex]['player']
    motion = base['people_clips_by_visual_option']['player=metahuman_' + sex]['player']
    look = {k: copy.deepcopy(template[k]) for k in ('hand', 'off_hand', 'grip', 'off_grip') if k in template}
    look.update(model=asset, people_ref=people, uniform_scale=r['uniform_scale'], outfit=[],
                clips=copy.deepcopy(motion['clips']), paces=copy.deepcopy(motion['paces']),
                note='Exact approved player body skin, geometry, binds and corrective tables; native Creator identity. Uniform stature only. Original player clips. Base layer pending published wardrobe pass.')
    # Paces stay unscaled: SkinnedFigure applies UniformScale exactly once.
    assert look['paces'] == motion['paces']
    if rid.startswith('resident_'):
        key = 'resident.' + rid.removeprefix('resident_').replace('_', '.')
        pools.setdefault(people, []).append(key)
    else:
        key = r.get('npc_ref') or canonical[rid.removeprefix('npc_')]
    looks[key] = look
assert len([k for k in looks if k.startswith('npc.')]) == len(canonical) == 8
assert len(pools) == 8 and all(len(v) == 6 for v in pools.values())
for document in documents:
    for section in ('people_by_visual_option', 'people_clips_by_visual_option'):
        for values in document.get(section, {}).values():
            for key in looks:
                values.pop(key, None)
    for key in looks:
        document.get('people', {}).pop(key, None)
base.setdefault('people', {}).update(looks)
base['resident_people'] = pools
# Retire stale NPC depot overrides as well as repository visual options.
documents[-1].setdefault('people', {}).update(copy.deepcopy(looks))
documents[-1]['resident_people'] = copy.deepcopy(pools)
for path, document in zip(paths, documents):
    if document or path.exists():
        path.write_text(json.dumps(document, indent=1 if path == base_path else 2) + '\n', encoding='utf-8')
print('NPC_BINDINGS: 8 named; 48 resident appearances; 26 physical models; player clips and unscaled paces')
