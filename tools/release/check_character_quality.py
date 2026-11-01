"""Fail closed on the assembled package and observations from its ordinary populated world.

Preflight is part of build_playtest. The played gate additionally supplies its JSONL
telemetry and F12 golden index; source/editor/fixture evidence cannot pass it.
The source-approved contract is copied with the art, never inferred from package pixels.
"""
import argparse
import hashlib
import json
import re
from pathlib import Path
from PIL import Image
from character_quality_video import check_dialogue

REQUIRED_STATES = {'idle', 'idle_shift', 'idle_foldarms', 'talk', 'sit_talk'}
REQUIRED_FACE = {'eyeBlinkLeft', 'eyeBlinkRight', 'jawOpen', 'mouthClose', 'mouthPucker', 'mouthFunnel'}
GOLDENS = {'player', 'npc.cairnwold.mara_ling', 'npc.otherhome.mkt_seller_00', 'npc.otherhome.mkt_buyer_00'}
REQUIRED_SHARED_NATIVE = tuple('primary/rigged/humgen_mouth/' + name for name in
    ('TeethTongueSet_C_2K.png', 'TeethTongueSet_N_2K.png', 'TeethTongueSet_ROUGH_2K.png'))


def sha256(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def package_files(package):
    files = [package / 'Otherreach.exe', package / 'VERSION.txt',
             package / 'primary/manifests/character_quality_contract.json']
    files.extend(package.glob('*.pck'))
    files.extend(p for p in (package / 'primary/manifests').rglob('*') if p.is_file())
    for data in package.glob('data_*'):
        files.extend(p for p in data.rglob('*') if p.is_file())
    files.extend(p for p in (package / 'content').rglob('*') if p.is_file())
    return {p.relative_to(package).as_posix(): sha256(p) for p in sorted(set(files))}


def local(package, relative):
    path = (package / relative).resolve()
    if not path.is_relative_to(package.resolve()):
        raise ValueError('Contract points outside package: ' + relative)
    if not path.is_file():
        raise ValueError('Missing art root file: ' + relative)
    return path


def preflight(package, contract):
    errors = []
    if contract.get('schema') != 1 or not contract.get('models'):
        return ['Missing/empty native character quality contract']
    for relative in REQUIRED_SHARED_NATIVE:
        try:
            if contract.get('common_files', {}).get(relative) != sha256(local(package, relative)):
                errors.append('Unpinned/changed shared native input: ' + relative)
        except (OSError, ValueError) as error:
            errors.append(str(error))
    if set(contract.get('golden_actors', [])) != GOLDENS:
        errors.append('The four required release goldens are not declared')
    if set(contract.get('golden_sources', {})) != GOLDENS:
        errors.append('The four native source renders are not pinned')
    for actor, sources in contract.get('golden_sources', {}).items():
        if isinstance(sources, dict):
            if not sources or any(model not in contract['models'] or not re.fullmatch('[0-9a-f]{64}', digest)
                                  for model, digest in sources.items()):
                errors.append(actor + ': missing/invalid model-specific native source render')
        elif not isinstance(sources, str) or not re.fullmatch('[0-9a-f]{64}', sources):
            errors.append(actor + ': invalid native source render hash')
    try:
        cast = json.loads(local(package, 'content/_quality_humgen_cast.json').read_text(encoding='utf-8-sig'))
        creator = json.loads(local(package, 'content/_quality_humgen_creator.json').read_text(encoding='utf-8-sig'))
        bindings = json.loads(local(package, 'content/_art_bindings.json').read_text(encoding='utf-8-sig'))
        bound = json.loads(local(package, 'content/_quality_bound_dependencies.json').read_text(encoding='utf-8-sig'))
        if bound.get('status') != 'PASS' or bound.get('packaged') is not True or not bound.get('boundModels'):
            errors.append('Missing/failed actual-package bound dependency check')
        if (bound.get('checked_scope') != 'native_player_npc_inputs' or type(bound.get('npc_definitions')) is not int
                or bound['npc_definitions'] < 1):
            errors.append('Bound dependency check omitted actual packaged NPC definitions')
        if (Path(bound.get('executable', '')).resolve() != (package / 'Otherreach.exe').resolve()
                or bound.get('executable_sha256') != sha256(local(package, 'Otherreach.exe'))):
            errors.append('Bound dependency check used a different executable')
        if bound.get('binding_sha256') != sha256(package / 'content/_art_bindings.json'):
            errors.append('Bound dependency check used different game bindings')
        binding_files = {p.relative_to(package).as_posix():sha256(p) for p in (package / 'content').glob('_art_bindings*.json')}
        if bound.get('binding_files') != binding_files:
            errors.append('Bound dependency check omitted/changed a packaged regional binding')
        required = {m['id'] for m in cast['models']}
        required.update(m['id'] for m in bound.get('boundModels', []) if m['id'].startswith('humgen_'))
        required.update(m['model'] for m in creator['baked'])
        required.update(creator['runtime']['bases'].values())
        required.update(model for body in bindings.get('player_bodies', {}).get('types', []) for model in body['models'] if model.startswith('humgen_'))
        if not required:
            errors.append('The exported character catalogue is empty')
        for model in sorted(required - contract['models'].keys()):
            errors.append(model + ': exported player/NPC choice has no native quality contract')
        for collection, kind in (('hair', 'hair'), ('facial_hair', 'beard'), ('outfits', 'outfit'), ('footwear', 'footwear')):
            for option in creator.get(collection, []):
                for sex in option['paths']:
                    part = 'humgen_creator_' + sex + '_' + kind + '_' + re.sub('[^a-z0-9]+', '_', option['id'].lower()).strip('_')
                    if part not in contract.get('parts', {}):
                        errors.append(part + ': offered native part has no source contract')
        for face in creator.get('faces', []):
            if 'native' in face and face['id'] not in contract.get('presets', {}):
                errors.append(face['id'] + ': offered native face has no material source contract')
    except (OSError, ValueError, KeyError, TypeError) as error:
        errors.append('Incomplete exported character catalogue: ' + str(error))
    for model, spec in contract['models'].items():
        if not spec.get('native_lod0_original') or not spec.get('native_hair_ultra'):
            errors.append(model + ': source has reduced native geometry/hair')
        if spec.get('native_skinning_influences') != 8:
            errors.append(model + ': native Ultra skinning was capped below eight influences')
        if not spec.get('textures') or not spec.get('files'):
            errors.append(model + ': incomplete native source contract')
        for role in ('body', 'motion', 'native_materials'):
            if spec.get(role) not in spec.get('files', {}):
                errors.append(model + ': unpinned ' + role)
        for relative, expected in spec.get('files', {}).items():
            try:
                if sha256(local(package, relative)) != expected:
                    errors.append(model + ': wrong art file ' + relative)
            except (OSError, ValueError) as error:
                errors.append(str(error))
    for part, spec in contract.get('parts', {}).items():
        if spec.get('body') not in spec.get('files', {}) or (not spec.get('textures') and not spec.get('native_untextured')):
            errors.append(part + ': incomplete native part source contract')
        if spec.get('kind') in ('hair', 'beard') and spec.get('native_hair_ultra') is not True:
            errors.append(part + ': reduced native groom')
        if spec.get('kind') in ('outfit', 'footwear') and not spec.get('native_lod0_original'):
            errors.append(part + ': reduced native wardrobe geometry')
        for relative, expected in spec.get('files', {}).items():
            try:
                if sha256(local(package, relative)) != expected:
                    errors.append(part + ': wrong native part file ' + relative)
            except (OSError, ValueError) as error:
                errors.append(str(error))
    for root in ('primary', 'fallback'):
        for manifest in (package / root / 'rigged').glob('*/creator_part.json'):
            if manifest.parent.name not in contract.get('parts', {}):
                errors.append(manifest.parent.name + ': installed character part has no native source contract')
    for preset, spec in contract.get('presets', {}).items():
        if spec.get('manifest') not in spec.get('files', {}) or not spec.get('textures'):
            errors.append(preset + ': incomplete native preset contract')
        for relative, expected in spec.get('files', {}).items():
            try:
                if sha256(local(package, relative)) != expected:
                    errors.append(preset + ': changed native preset file ' + relative)
            except (OSError, ValueError) as error:
                errors.append(str(error))
    return errors


def sample_problems(sample, contract, package):
    errors = []
    if sample.get('schema') != 1 or not sample.get('packaged'):
        errors.append('Evidence is not from the packaged game')
    if sample.get('ordinaryLaunch') is not True:
        errors.append('Staged launch, private review hook, or capture fixture is not acceptance evidence')
    if sample.get('packageFiles') != contract.get('_package_files'):
        errors.append('Played executable/data/content/art contract differs from the assembled package')
    try:
        executable = Path(sample['executable']).resolve()
        if executable != (package / 'Otherreach.exe').resolve():
            errors.append('Evidence executable is outside this package')
    except (KeyError, TypeError):
        errors.append('Missing package executable identity')
    if sample.get('tier') != 'ultra' or not sample.get('normalCamera') or sample.get('renderScale', 0) < 1:
        errors.append('Normal camera at full-resolution Ultra is required')
    if sample.get('mipBias', 1) > 0:
        errors.append('Positive texture mip bias')
    if sample.get('npcs', 0) <= 0:
        errors.append('The world is not populated')
    if sample.get('fallbacks') or sample.get('artProblems'):
        errors.append('Fallback/placeholder/unavailable art was used')
    expected_actors = sample.get('expectedActors', [])
    observed_actors = [f.get('actor') for f in sample.get('figures', [])]
    if (not isinstance(expected_actors, list) or 'player' not in expected_actors
            or len(set(expected_actors)) != sample.get('npcs', 0) + 1):
        errors.append('Missing/incomplete populated-world character inventory')
    elif set(expected_actors) - set(observed_actors):
        errors.append('Player/NPC missing native observations: ' + ', '.join(sorted(set(expected_actors) - set(observed_actors))))
    for figure in sample.get('figures', []):
        model, actor = figure.get('model'), figure.get('actor')
        spec = contract.get('models', {}).get(model)
        if spec is None:
            errors.append(str(actor) + ': character has no native source contract')
            continue
        animation = figure.get('animation', {})
        if model in ('humgen_creator_female', 'humgen_creator_male'):
            preset = contract.get('presets', {}).get(animation.get('preset'))
            try:
                if not preset or Path(animation.get('presetSource', '')).resolve() != local(package, preset['manifest']):
                    errors.append(str(actor) + ': missing/wrong-root native preset material set')
            except (OSError, ValueError, KeyError) as error:
                errors.append(str(actor) + ': ' + str(error))
        try:
            source = Path(animation['source']).resolve()
            if source != local(package, spec['body']):
                errors.append(str(actor) + ': body was loaded from a different art root')
            if Path(animation['nativeMaterials']).resolve() != local(package, spec['native_materials']):
                errors.append(str(actor) + ': native materials were not loaded from this package')
            if Path(animation['motion']).resolve() != local(package, spec['motion']):
                errors.append(str(actor) + ': native motion was not loaded from this package')
        except (KeyError, OSError, ValueError) as error:
            errors.append(str(actor) + ': ' + str(error))
        if not REQUIRED_STATES.issubset(animation.get('states', [])):
            errors.append(str(actor) + ': missing native idle/talking set')
        if animation.get('throttled'):
            errors.append(str(actor) + ': native animation throttled on Ultra')
        near = figure.get('distance', 999) <= 18 or figure.get('talking')
        if near and animation.get('lod') != 0:
            errors.append(str(actor) + ': LOD above 0 in near/dialogue range')
        face = figure.get('face', {})
        if not face.get('attached') or face.get('bindings', 0) <= 0 or not face.get('jawBound') or face.get('eyeBindings') != 2:
            errors.append(str(actor) + ': missing native facial animation bindings')
        if not REQUIRED_FACE.issubset(face.get('channels', [])):
            errors.append(str(actor) + ': missing native blink/jaw/lip channels')
        visible_names = {m['name'] for m in animation.get('meshes', [])}
        if near and not any('_Body_LOD0' in name for name in visible_names):
            errors.append(str(actor) + ': missing native LOD0 body')
        for name in spec.get('required_near_meshes', []):
            if near and name not in visible_names:
                errors.append(str(actor) + ': missing native near mesh ' + name)
        for mesh in animation.get('meshes', []):
            maps = spec
            if mesh.get('part'):
                maps = contract.get('parts', {}).get(mesh['part'], {})
                try:
                    if not maps or Path(mesh.get('source', '')).resolve() != local(package, maps['body']):
                        errors.append(str(actor) + ': uncontracted/wrong-root native part ' + mesh['part'])
                except (OSError, ValueError, KeyError) as error:
                    errors.append(str(actor) + ': ' + str(error))
            if mesh['name'] in maps.get('native_ultra_skinned_meshes', []) and mesh.get('skinningInfluences') != 8:
                errors.append(str(actor) + ': game loader reduced native Ultra skinning')
            expected = {(t['mesh'], t['surface'], t['role']): (t['width'], t['height']) for t in maps.get('textures', [])}
            for index, surface in enumerate(mesh.get('surfaces', [])):
                actual = {t['role']: (t['width'], t['height']) for t in surface.get('textures', [])}
                for (name, number, role), dimensions in expected.items():
                    if name == mesh['name'] and number == index and actual.get(role) != dimensions:
                        errors.append(f'{actor}: capped/missing native map {name}/{index}/{role}: {actual.get(role)} != {dimensions}')
                shader_groom = (surface.get('shader') or '').endswith(('siann_groom.gdshader', 'siann_white_hair.gdshader'))
                filtered_taa = sample.get('taa') and sample.get('renderer') == 'forward_plus'
                if 'HairCards' in surface.get('name', '') or 'Haircap' in surface.get('name', '') or shader_groom:
                    transparency = surface.get('transparency')
                    if transparency == 'AlphaHash' and not filtered_taa:
                        errors.append(str(actor) + ': unfiltered alpha-hashed hair')
                    if transparency == 'AlphaScissor' and not (filtered_taa or
                            surface.get('alphaAa') in ('AlphaToCoverage', 'AlphaToCoverageAndToOne') and sample.get('msaa') != 'Disabled'):
                        errors.append(str(actor) + ': unfiltered alpha-scissored hair')
                    if shader_groom and not filtered_taa:
                        errors.append(str(actor) + ': unfiltered shader hair')
                if '_Skin' in surface.get('name', '') and not (surface.get('shader') or '').endswith('/humgen_skin.gdshader'):
                    errors.append(str(actor) + ': generic/fallback skin shader')
                if '_Skin' in surface.get('name', '') and (not surface.get('nativeSkin') or not surface.get('sssEnabled')
                        or surface.get('sssStrength', 0) <= 0 or not surface.get('nativeSpecular')):
                    errors.append(str(actor) + ': native skin/SSS/specular inputs disabled')
                for part, shader in (('_Eyes_LOD0','humgen_inner_eye.gdshader'),('_Cornea_LOD0','humgen_cornea.gdshader')):
                    if mesh['name'].endswith(part) and not (surface.get('shader') or '').endswith('/'+shader):
                        errors.append(str(actor) + ': native layered eye shader missing: ' + mesh['name'])
    if not sample.get('figures'):
        errors.append('No real character observations')
    return errors


def played_gate(package, contract, paths, golden_index, ffmpeg=None, ffprobe=None, video_proof=None):
    errors, observations = [], []
    contract = dict(contract, _package_files=package_files(package))
    for path in paths:
        observations.extend(json.loads(line) for line in path.read_text(encoding='utf-8-sig').splitlines() if line.strip())
    if not observations:
        return ['No packaged-game telemetry supplied']
    for sample in observations:
        errors.extend(sample_problems(sample, contract, package))
    seen = {f['actor'] for s in observations for f in s.get('figures', []) if f.get('visible') and f.get('distance', 999) <= 18}
    if not GOLDENS.issubset(seen):
        errors.append('Missing normal-world near-range observations: ' + ', '.join(sorted(GOLDENS - seen)))
    for actor in GOLDENS:
        frames = [(s, f) for s in observations for f in s.get('figures', [])
                  if f.get('actor') == actor and f.get('visible') and f.get('distance', 999) <= 18]
        if len(frames) < 2 or frames[-1][0]['wallSeconds'] - frames[0][0]['wallSeconds'] < 10:
            errors.append(actor + ': missing 10 seconds of normal-world character motion')
            continue
        if len({f.get('animation', {}).get('bodyPose') for _, f in frames}) < 3:
            errors.append(actor + ': native body pose is frozen or missing (eye/jaw motion cannot stand in for idle)')
        idle_progress = {(f.get('animation', {}).get('clip'), f.get('animation', {}).get('seconds')) for _, f in frames
                         if f.get('animation', {}).get('clip') in REQUIRED_STATES}
        if len(idle_progress) < 3:
            errors.append(actor + ': native idle/talking clip clock did not advance')
        blinks = [f.get('face', {}).get('blink', 0) for _, f in frames]
        peaks = [f.get('face', {}).get('blinkPeak', 0) for _, f in frames]
        if max(peaks) - min(blinks) < .3:
            errors.append(actor + ': native blink animation did not play')
    speech = [s for s in observations if s.get('conversation') == 'npc.cairnwold.mara_ling']
    if len(speech) < 2 or speech[-1]['wallSeconds'] - speech[0]['wallSeconds'] < 10:
        errors.append('Missing 10 seconds of real-time Mara dialogue')
    jaws = [f.get('face', {}).get('jaw', 0) for s in speech for f in s.get('figures', []) if f.get('actor') == 'npc.cairnwold.mara_ling']
    jaw_peaks = [f.get('face', {}).get('jawPeak', 0) for s in speech for f in s.get('figures', []) if f.get('actor') == 'npc.cairnwold.mara_ling']
    if not jaws or max(jaw_peaks) - min(jaws) < .03:
        errors.append('Native speech animation did not play during dialogue')
    if golden_index is None:
        return errors + ['No regenerated F12 golden comparisons supplied']
    goldens = json.loads(golden_index.read_text(encoding='utf-8-sig'))
    video_errors, proof = check_dialogue(golden_index, goldens, observations, ffmpeg, ffprobe)
    errors.extend(video_errors)
    if video_proof is not None:
        video_proof.update(proof)
    if goldens.get('package_files') != contract['_package_files']:
        errors.append('Golden comparisons belong to a different package')
    for actor in GOLDENS:
        pair = goldens.get('actors', {}).get(actor, {})
        expected_source = contract.get('golden_sources', {}).get(actor)
        if isinstance(expected_source, dict):
            expected_source = expected_source.get(pair.get('model'))
        if not expected_source or pair.get('source', {}).get('sha256') != expected_source:
            errors.append(actor + ': golden source differs from the native source render')
        roles = ('source', 'packaged_game') if actor == 'player' else ('source', 'packaged_game', 'dialogue')
        for role in roles:
            image = pair.get(role, {})
            file = golden_index.parent / image.get('file', '__MISSING__')
            if not file.is_file() or sha256(file) != image.get('sha256'):
                errors.append(actor + ': missing/changed golden ' + role)
                continue
            try:
                with Image.open(file) as picture:
                    if picture.format != 'PNG' or min(picture.size) < 480:
                        errors.append(actor + ': golden is not a full-sized PNG: ' + role)
                    picture.verify()
            except (OSError, ValueError) as error:
                errors.append(actor + ': invalid golden image: ' + str(error))
            if role == 'source':
                continue
            matches = [s for s in observations if s.get('normalCamera') and s.get('ordinaryLaunch')
                       and s.get('cameraMode') == ('dialogue' if role == 'dialogue' else 'gameplay')
                       and s.get('frame') == image.get('frame')
                       and (s.get('screenshot') or {}).get('sha256') == image.get('sha256')
                       and (role != 'dialogue' or s.get('conversation') == actor)
                       and any(f.get('actor') == actor and f.get('visible')
                               and (not isinstance(contract.get('golden_sources', {}).get(actor), dict) or f.get('model') == pair.get('model'))
                               and f.get('distance', 999) <= (4 if role == 'dialogue' else 6)
                               and (role != 'dialogue' or f.get('talking')) for f in s.get('figures', []))]
            if not matches:
                errors.append(actor + ': golden has no matching F12 normal-game observation: ' + role)
    return errors


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('package', type=Path)
    parser.add_argument('--preflight', action='store_true')
    parser.add_argument('--evidence', type=Path, nargs='+')
    parser.add_argument('--goldens', type=Path)
    parser.add_argument('--verify-verdict', type=Path)
    parser.add_argument('--ffmpeg', type=Path)
    parser.add_argument('--ffprobe', type=Path)
    parser.add_argument('--out', type=Path, required=True)
    args = parser.parse_args()
    video_proof = {}
    try:
        contract = json.loads(local(args.package, 'primary/manifests/character_quality_contract.json').read_text(encoding='utf-8-sig'))
        errors = preflight(args.package, contract)
        if args.verify_verdict:
            verdict = json.loads(args.verify_verdict.read_text(encoding='utf-8-sig'))
            if verdict.get('status') != 'PASS' or verdict.get('preflight_only') is not False or verdict.get('package_files') != package_files(args.package):
                errors.append('Missing/failed/stale played package quality verdict')
        elif not args.preflight:
            errors.extend(played_gate(args.package, contract, args.evidence or [], args.goldens, args.ffmpeg, args.ffprobe, video_proof))
    except (OSError, ValueError, KeyError) as error:
        errors = [str(error)]
    exe = args.package / 'Otherreach.exe'
    contract_file = args.package / 'primary/manifests/character_quality_contract.json'
    result = {'status': 'FAIL' if errors else 'PASS', 'preflight_only': args.preflight,
              'package_sha256': sha256(exe) if exe.is_file() else None,
              'contract_sha256': sha256(contract_file) if contract_file.is_file() else None,
              'package_files': package_files(args.package) if not errors else None,
              'dialogue_video': video_proof, 'failures': sorted(set(errors))}
    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_text(json.dumps(result, indent=2), encoding='utf-8')
    print(json.dumps(result, indent=2))
    return 1 if errors else 0


if __name__ == '__main__':
    raise SystemExit(main())
