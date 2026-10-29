"""Migrate owned humanoid animation packs through Epic, without changing source assets.

Run inside the acquired Fab project. The destination is this lane's isolated
motion project; its native source skeletons remain distinct from HumGen.
"""
import json
import os
from pathlib import Path
import re
import traceback
import unreal

job = Path(os.environ['OTHERREACH_HUMGEN_ANIMATION_JOB'])
packs = ['ActorCore_Sample_Motions', 'Aura', 'DashFX', 'DramaticDeath', 'Dwarven_Citadel', 'DynamicFalling', 'FLifeAnimPack',
         'FreeAnimationLibrary', 'FreeAnimationSet', 'FreeAnimationsPack',
         'FreeAnimationsPack2', 'FreeAnimsMixPack', 'FreeSampleAnimationSet',
         'FreeSword_Animation', 'Free_Crawl_Animation', 'Free_Interaction_Animation', 'Free_Magic', 'Free_Spells',
         'LevitationFX', 'LookWindowPack01', 'MCO_Mocap_Basics', 'MC_Sample',
         'MagicalFootstepFX', 'MudraSamples', 'Qi_Gong_animations_Pack', 'GBH_ActorHealthSystem', 'Underwater_Sunken_Ship',
         'QuantumCharacter', 'RIGIPOFreeAnims', 'SampleAnimationPack',
         'StruggleToAscension', 'Time_Warper', 'UnrealBucket']
report = dict(status='MIGRATING', packs=packs, sources=[], excluded=[], failures=[])
if os.environ.get('OTHERREACH_GATE_D_NATIVE_PACKS'):
    packs = json.loads(os.environ['OTHERREACH_GATE_D_NATIVE_PACKS'])
    report['packs'] = packs
try:
    registry = unreal.AssetRegistryHelpers.get_asset_registry()
    registry.search_all_assets(True)
    meshes = {}
    for data in registry.get_assets_by_class(unreal.TopLevelAssetPath('/Script/Engine', 'SkeletalMesh'), True):
        path = str(data.package_name)
        if not any(path.startswith('/Game/' + pack + '/') for pack in packs):
            continue
        mesh = data.get_asset()
        component = unreal.new_object(unreal.SkeletalMeshComponent)
        component.set_skeletal_mesh_asset(mesh)
        bones = [re.sub('[^a-z0-9]', '', str(component.get_bone_name(i)).lower())
                 for i in range(component.get_num_bones())]
        if not any(b.endswith(('pelvis', 'hips')) for b in bones) or not any('foot' in b for b in bones):
            continue  # First-person arms and props are not complete people.
        skeleton = mesh.get_editor_property('skeleton')
        if skeleton:
            meshes.setdefault(skeleton.get_path_name(), mesh)
    packages = set()
    groups = {}
    for pack in packs:
        if not unreal.EditorAssetLibrary.does_directory_exist('/Game/' + pack):
            report['excluded'].append(dict(pack=pack, reason='Not present in this acquired source project; reconcile other catalog source'))
            continue
        for path in unreal.EditorAssetLibrary.list_assets('/Game/' + pack, True, False):
            if '/Rigs/Poses/' in path:
                report['excluded'].append(dict(asset=path, reason='Source mannequin corrective calibration, not a performance take'))
                continue
            data = unreal.EditorAssetLibrary.find_asset_data(path)
            if str(data.asset_class_path.asset_name) != 'AnimSequence':
                continue
            animation = data.get_asset()
            skeleton = animation.get_editor_property('skeleton')
            mesh = meshes.get(skeleton.get_path_name()) if skeleton else None
            if mesh is None:
                report['excluded'].append(dict(asset=path, reason='No acquired complete humanoid source mesh for its skeleton'))
                continue
            key = skeleton.get_path_name()
            group = groups.setdefault(key, dict(id='catalog_' + re.sub('[^a-z0-9]+', '_', key.lower()).strip('_'),
                path=pack, assets=[dict(kind='mesh', asset=mesh.get_path_name())]))
            group['assets'].append(dict(kind='animation', asset=animation.get_path_name(), name=animation.get_name()))
            packages.update(p.split('.')[0] for p in (animation.get_path_name(), mesh.get_path_name(), key))
    assert groups, 'No native humanoid catalog sources found'
    options = unreal.MigrationOptions()
    options.prompt = False
    options.ignore_dependencies = False
    options.asset_conflict = unreal.AssetMigrationConflict.SKIP
    if os.environ.get('OTHERREACH_GATE_D_NATIVE_IN_PLACE') != '1':
        unreal.AssetToolsHelpers.get_asset_tools().migrate_packages(sorted(packages), str(job / 'project/Content'), options)
    report['sources'] = list(groups.values())
    for package in packages:
        assert (job / 'project/Content' / (package.removeprefix('/Game/') + '.uasset')).exists(), package
    report['status'] = 'DONE'
except Exception:
    report.update(status='FAILED', error=traceback.format_exc())
(job / os.environ.get('OTHERREACH_GATE_D_NATIVE_REPORT', 'catalog_sources.json')).write_text(json.dumps(report, indent=2), encoding='utf-8')
unreal.SystemLibrary.quit_editor()
