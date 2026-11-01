"""Positive control plus the owner's six required package-quality failure cases."""
import copy
import json
import os
import shutil
import subprocess
import tempfile
from pathlib import Path
from PIL import Image
from check_character_quality import GOLDENS, REQUIRED_FACE, REQUIRED_STATES, REQUIRED_SHARED_NATIVE, package_files, played_gate, preflight, sample_problems, sha256
from character_quality_video import check_dialogue

with tempfile.TemporaryDirectory() as directory:
    package = Path(directory)
    body = 'primary/rigged/humgen_test/humgen_test_rigged.glb'
    motion = 'humgen_characters/mocap_motion/humgen_test_motion.res'
    native = 'primary/rigged/humgen_test/native_materials.json'
    for relative in (body, motion, native, 'Otherreach.exe', 'VERSION.txt', 'data_test/game.dll', 'content/test.yaml') + REQUIRED_SHARED_NATIVE:
        path = package / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(relative)
    spec = {'body': body, 'motion': motion, 'native_materials': native, 'native_lod0_original': True, 'native_hair_ultra': True,
            'native_skinning_influences': 8, 'native_ultra_skinned_meshes': ['Native_Body_LOD0'],
            'files': {p: sha256(package / p) for p in (body, motion, native)},
            'textures': [{'mesh': 'Native_Body_LOD0', 'surface': 0, 'role': 'color', 'width': 4096, 'height': 4096}]}
    contract = {'schema': 1, 'models': {'humgen_test': spec}, 'golden_actors': sorted(GOLDENS), 'golden_sources': {a: '0' * 64 for a in GOLDENS}}
    contract['common_files'] = {p:sha256(package/p) for p in REQUIRED_SHARED_NATIVE}
    contract_file = package / 'primary/manifests/character_quality_contract.json'
    contract_file.parent.mkdir(parents=True)
    contract_file.write_text(json.dumps(contract))
    catalogues = {'_quality_humgen_cast.json': {'models': [{'id': 'humgen_test'}]},
                  '_quality_humgen_creator.json': {'baked': [], 'runtime': {'bases': {'f': 'humgen_test'}}},
                  '_art_bindings.json': {'player_bodies': {'types': []}}}
    for name, catalogue in catalogues.items():
        (package / 'content' / name).write_text(json.dumps(catalogue))
    (package / 'content/_quality_bound_dependencies.json').write_text(json.dumps({'status':'PASS','packaged':True,
        'checked_scope':'native_player_npc_inputs','npc_definitions':1,'executable':str(package/'Otherreach.exe'),
        'executable_sha256':sha256(package/'Otherreach.exe'),
        'binding_sha256':sha256(package / 'content/_art_bindings.json'),'binding_files':{'content/_art_bindings.json':sha256(package / 'content/_art_bindings.json')},
        'boundModels':[{'id':'humgen_test','relative':body}]}))
    assert not preflight(package, contract)
    unpinned = copy.deepcopy(contract)
    unpinned['common_files'].pop(REQUIRED_SHARED_NATIVE[0])
    assert any('shared native input' in error for error in preflight(package, unpinned))
    dependencies = package / 'content/_quality_bound_dependencies.json'
    original_dependencies = dependencies.read_text()
    for field, value in (('checked_scope', 'gallery'), ('npc_definitions', 0), ('executable', str(package/'Editor.exe')),
                         ('executable_sha256', '0'*64)):
        bad_bound = json.loads(original_dependencies)
        bad_bound[field] = value
        dependencies.write_text(json.dumps(bad_bound))
        assert preflight(package, contract), field
    dependencies.write_text(original_dependencies)
    bound = json.loads(original_dependencies)
    bound['boundModels'].append({'id':'humgen_unmanifested_npc','relative':'primary/rigged/missing.glb'})
    dependencies.write_text(json.dumps(bound))
    assert any('humgen_unmanifested_npc' in error for error in preflight(package, contract))
    dependencies.write_text(original_dependencies)
    regional = package / 'content/_art_bindings_frontier.json'
    regional.write_text('{}')
    assert any('regional binding' in error for error in preflight(package, contract))
    regional.unlink()
    missing = copy.deepcopy(contract)
    missing['models'] = {'humgen_unrelated': spec}
    assert any('exported player/NPC choice' in error for error in preflight(package, missing))
    contract['_package_files'] = package_files(package)
    sample = {'schema': 1, 'packaged': True, 'ordinaryLaunch': True, 'executable': str(package / 'Otherreach.exe'), 'packageFiles': contract['_package_files'],
              'tier': 'ultra', 'normalCamera': True, 'renderScale': 1, 'mipBias': 0, 'npcs': 3, 'taa': True, 'msaa': 'Disabled', 'renderer':'forward_plus',
              'viewportWidth': 960, 'viewportHeight': 540,
              'figures': [{'actor': 'player', 'model': 'humgen_test', 'visible': True, 'distance': 3.5,
                           'face': {'attached': True, 'bindings': 70, 'channels': sorted(REQUIRED_FACE), 'jawBound': True, 'eyeBindings': 2},
                           'animation': {'source': str(package / body), 'nativeMaterials': str(package / native), 'motion': str(package / motion), 'lod': 0, 'throttled': False,
                                         'states': sorted(REQUIRED_STATES), 'meshes': [{'name': 'Native_Body_LOD0', 'skinningInfluences': 8, 'surfaces': [
                                             {'name': 'Native_Skin', 'shader': 'res://Art/HumanShaders/humgen_skin.gdshader',
                                              'nativeSkin':True,'sssEnabled':True,'sssStrength':.5,'nativeSpecular':True,
                                              'textures': [{'role': 'color', 'width': 4096, 'height': 4096}]}]},
                                             {'name': 'Native_Hair_LOD0', 'surfaces': [{'name': 'Native_HairCards', 'shader': None, 'transparency': 'AlphaHash', 'textures': []}]}]}}]}
    sample['expectedActors'] = sorted(GOLDENS)
    for actor in sorted(GOLDENS - {'player'}):
        sample['figures'].append(dict(copy.deepcopy(sample['figures'][0]), actor=actor))
    assert not sample_problems(sample, contract, package), sample_problems(sample, contract, package)
    for defect in ('lod', 'skinning_cap', 'texture_cap', 'hair_aa', 'hair_renderer', 'sss', 'native_specular', 'eye_bindings', 'idle', 'face', 'fallback', 'placeholder', 'missing_mesh', 'source_scene', 'private_hook', 'wrong_camera', 'changed_build', 'missing_inventory', 'omitted_npc'):
        bad = copy.deepcopy(sample)
        figure = bad['figures'][0]
        if defect == 'lod': figure['animation']['lod'] = 1
        if defect == 'skinning_cap': figure['animation']['meshes'][0]['skinningInfluences'] = 4
        if defect == 'texture_cap': figure['animation']['meshes'][0]['surfaces'][0]['textures'][0]['width'] = 2048
        if defect == 'hair_aa': bad['taa'] = False
        if defect == 'hair_renderer': bad['renderer'] = 'gl_compatibility'
        if defect == 'sss': figure['animation']['meshes'][0]['surfaces'][0]['sssEnabled'] = False
        if defect == 'native_specular': figure['animation']['meshes'][0]['surfaces'][0]['nativeSpecular'] = False
        if defect == 'eye_bindings': figure['face']['eyeBindings'] = 1
        if defect == 'idle': figure['animation']['states'].remove('idle_shift')
        if defect == 'face': figure['face']['channels'].remove('jawOpen')
        if defect == 'fallback': bad['fallbacks'] = [{'Outcome': 'fallback', 'Allowed': 'legacy'}]
        if defect == 'placeholder': figure['model'] = 'greybox'
        if defect == 'missing_mesh': figure['animation']['meshes'] = []
        if defect == 'source_scene': bad['packaged'] = False
        if defect == 'private_hook': bad['ordinaryLaunch'] = False
        if defect == 'wrong_camera': bad['normalCamera'] = False
        if defect == 'changed_build': bad['packageFiles'] = dict(bad['packageFiles'], **{'data_test/game.dll': 'changed'})
        if defect == 'missing_inventory': bad.pop('expectedActors')
        if defect == 'omitted_npc': bad['figures'].pop()
        assert sample_problems(bad, contract, package), defect

    # Exercise the complete played-gate parser and F12/image binding with synthetic
    # control data. These files test rejection logic; they are never game evidence.
    source = package / 'source.png'
    Image.new('RGB', (960, 540), (30, 40, 50)).save(source)
    contract['golden_sources'] = {a: sha256(source) for a in GOLDENS}
    (package / 'primary/manifests/character_quality_contract.json').write_text(json.dumps({k: v for k, v in contract.items() if not k.startswith('_')}))
    contract['_package_files'] = package_files(package)
    observations = []
    goldens = {'package_files': contract['_package_files'], 'actors': {a: {'source': {'file': source.name, 'sha256': sha256(source)}} for a in GOLDENS}}
    actors = sorted(GOLDENS)
    for section, focus in enumerate([None] + [a for a in actors if a != 'player']):
        for index in range(41):
            current = copy.deepcopy(sample)
            current.update(packageFiles=contract['_package_files'], cameraMode='dialogue' if focus else 'gameplay',
                           wallSeconds=section * 11 + index * .25, frame=(section * 660 + index * 15), fps=60, conversation=focus)
            current['figures'] = []
            for actor in actors:
                figure = copy.deepcopy(sample['figures'][0])
                figure.update(actor=actor, talking=actor == focus)
                figure['animation']['pose'] = str(index % 3)
                figure['animation']['bodyPose'] = str(index % 3)
                figure['animation']['clip'] = 'idle'
                figure['animation']['seconds'] = index * .25
                figure['face'].update(blink=.1, blinkPeak=.8, jaw=.01, jawPeak=.2)
                current['figures'].append(figure)
            if index == 20:
                shot = package / f'control_{section}.png'
                Image.new('RGB', (960, 540), (60 + section, 80, 100)).save(shot)
                current['screenshot'] = {'file': shot.name, 'sha256': sha256(shot)}
                for actor in ([focus] if focus else actors):
                    goldens['actors'][actor]['dialogue' if focus else 'packaged_game'] = {
                        'file': shot.name, 'sha256': sha256(shot), 'frame': current['frame']}
            observations.append(current)
    ffmpeg = Path(os.environ.get('OTHERREACH_TEST_FFMPEG') or shutil.which('ffmpeg') or 'C:/Otherreach_Worker/tools/ffmpeg/bin/ffmpeg.exe')
    ffprobe = ffmpeg.with_name('ffprobe' + ffmpeg.suffix)
    video = package / 'synthetic_control.mp4'
    subprocess.run([str(ffmpeg), '-hide_banner', '-v', 'error', '-f', 'lavfi', '-i', 'testsrc2=size=960x540:rate=30:duration=10',
                    '-f', 'lavfi', '-i', 'anullsrc=r=48000:cl=stereo', '-t', '10', '-threads', '2',
                    '-c:v', 'libx264', '-preset', 'ultrafast', '-c:a', 'aac', str(video)], check=True, timeout=60)
    goldens['dialogue_video'] = {'file': video.name, 'sha256': sha256(video), 'start_frame': 660, 'end_frame': 1260}
    telemetry = package / 'control.jsonl'
    telemetry.write_text('\n'.join(json.dumps(s) for s in observations))
    index = package / 'goldens.json'
    index.write_text(json.dumps(goldens))
    def played():
        return played_gate(package, contract, [telemetry], index, ffmpeg, ffprobe)
    assert not played(), played()
    frozen_body = copy.deepcopy(observations)
    for row in frozen_body:
        for figure in row['figures']: figure['animation']['bodyPose'] = 'frozen-body-with-moving-eyes'
    telemetry.write_text('\n'.join(json.dumps(row) for row in frozen_body))
    assert any('native body pose is frozen' in error for error in played())
    frozen_clock = copy.deepcopy(observations)
    for row in frozen_clock:
        for figure in row['figures']: figure['animation']['seconds'] = 0
    telemetry.write_text('\n'.join(json.dumps(row) for row in frozen_clock))
    assert any('clip clock did not advance' in error for error in played())
    telemetry.write_text('\n'.join(json.dumps(row) for row in observations))
    contract['golden_sources'] = {a: {'humgen_test':sha256(source)} for a in GOLDENS}
    for actor in actors: goldens['actors'][actor]['model'] = 'humgen_test'
    index.write_text(json.dumps(goldens))
    assert not played(), played()
    wrong_model = copy.deepcopy(goldens)
    wrong_model['actors']['player']['model'] = 'humgen_other_model'
    index.write_text(json.dumps(wrong_model))
    assert any('golden source differs' in error for error in played())
    index.write_text(json.dumps(goldens))
    frozen = package / 'synthetic_frozen.mp4'
    subprocess.run([str(ffmpeg), '-hide_banner', '-v', 'error', '-f', 'lavfi', '-i', 'color=gray:size=960x540:rate=30:duration=10',
                    '-f', 'lavfi', '-i', 'anullsrc=r=48000:cl=stereo', '-t', '10', '-threads', '2',
                    '-c:v', 'libx264', '-preset', 'ultrafast', '-c:a', 'aac', str(frozen)], check=True, timeout=60)
    frozen_index = dict(goldens, dialogue_video=dict(goldens['dialogue_video'], file=frozen.name, sha256=sha256(frozen)))
    problems, proof = check_dialogue(index, frozen_index, observations, ffmpeg, ffprobe)
    assert any('repeated-frame freeze' in error for error in problems), proof
    wrong = copy.deepcopy(goldens)
    wrong['actors']['player']['packaged_game']['sha256'] = sha256(source)
    wrong['actors']['player']['packaged_game']['file'] = source.name
    index.write_text(json.dumps(wrong))
    assert any('matching F12' in e for e in played()), 'unrelated image must fail even with a valid image hash'
    index.write_text(json.dumps(goldens))
    goldens['package_files']['data_test/game.dll'] = 'changed'
    index.write_text(json.dumps(goldens))
    assert any('different package' in e for e in played())
    wrong = copy.deepcopy(goldens)
    wrong.pop('dialogue_video')
    index.write_text(json.dumps(wrong))
    assert any('Missing real-time' in e for e in played())
    wrong['dialogue_video'] = dict(goldens['dialogue_video'], sha256='changed')
    index.write_text(json.dumps(wrong))
    assert any('video hash differs' in e for e in played())
    part_manifest = package / 'primary/rigged/native_missing_part/creator_part.json'
    part_manifest.parent.mkdir(parents=True)
    part_manifest.write_text('{}')
    assert any('installed character part' in e for e in preflight(package, contract))
    bad = copy.deepcopy(sample)
    bad['packageFiles'] = contract['_package_files']
    bad['figures'][0]['animation']['meshes'][1]['part'] = 'native_missing_part'
    assert any('uncontracted' in e for e in sample_problems(bad, contract, package))
    (package / body).unlink()
    assert any('Missing art root file' in error for error in preflight(package, contract))
print('CHARACTER_QUALITY_GATE_NEGATIVE_CONTROLS_PASS: static/sample/played positives with software-decoded video; 38 failures rejected')
