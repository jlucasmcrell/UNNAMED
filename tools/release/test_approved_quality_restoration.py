"""Synthetic source-lineage controls; never grants visual approval to art."""
import copy
import hashlib
import json
from pathlib import Path
import tempfile
from approved_player_bodies import check_quality_restoration

with tempfile.TemporaryDirectory() as directory:
    root = Path(directory)
    def write(name, value):
        path = root / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(value if isinstance(value, str) else json.dumps(value))
        return path
    def record(path): return {'bytes': path.stat().st_size, 'sha256': hashlib.sha256(path.read_bytes()).hexdigest()}
    old_body = 'fallback/rigged/native/native_rigged.glb'
    new_body = 'primary/rigged/native/native_rigged.glb'
    motion = 'humgen_characters/mocap_motion/native_motion.res'
    baseline = {'kind': 'accepted_native_character_delivery', 'files': {
        old_body: {'bytes': 10, 'sha256': 'accepted-body'}, motion: {'bytes': 20, 'sha256': 'accepted-motion'},
        'primary/rigged/unchanged.glb': record(write('primary/rigged/unchanged.glb', 'unchanged'))}}
    source = write('primary/manifests/source_native_approval.json', baseline)
    body_record = record(write(new_body, 'native-detail-restored'))
    motion_record = record(write(motion, 'same-original-motion-keys-and-times'))
    rig = {'old_sha256': 'accepted-body', 'new_sha256': body_record['sha256'], 'same_parents': True,
           'joints': 59, 'maximum_rest_error': 0}
    motion_proof = {'status': 'PASS', 'unchanged_keys': 100855, 'states': 49,
                    'original_motion_sha256': 'accepted-motion', 'new_motion_sha256': motion_record['sha256']}
    rig_path = 'primary/rigged/native/rig_equivalence.json'
    motion_path = motion + '.quality.json'
    write(rig_path, rig); write(motion_path, motion_proof)
    delivery = {'baseline_manifest': source.relative_to(root).as_posix(), 'baseline_sha256': record(source)['sha256'],
                'files': {new_body: body_record, motion: motion_record, 'primary/rigged/unchanged.glb': baseline['files']['primary/rigged/unchanged.glb']},
                'restorations': {'native': {'source_body': old_body, 'source_motion': motion, 'body': new_body, 'motion': motion,
                                            'rig_proof': rig_path, 'motion_proof': motion_path}}}
    check_quality_restoration(root, delivery)
    for defect in ('wrong_source', 'changed_rest', 'changed_parents', 'changed_motion_source', 'unrelated_change', 'changed_bytes'):
        bad = copy.deepcopy(delivery)
        bad_rig = copy.deepcopy(rig)
        bad_motion = copy.deepcopy(motion_proof)
        if defect == 'wrong_source': bad_rig['old_sha256'] = 'unapproved'
        if defect == 'changed_rest': bad_rig['maximum_rest_error'] = .01
        if defect == 'changed_parents': bad_rig['same_parents'] = False
        if defect == 'changed_motion_source': bad_motion['original_motion_sha256'] = 'unapproved'
        if defect == 'unrelated_change': bad['files']['primary/rigged/unchanged.glb'] = body_record
        if defect == 'changed_bytes': write(new_body, 'changed-without-proof')
        write(rig_path, bad_rig); write(motion_path, bad_motion)
        try:
            check_quality_restoration(root, bad)
            raise RuntimeError('Defect accepted: ' + defect)
        except AssertionError:
            pass
        write(new_body, 'native-detail-restored')
    write(rig_path, rig); write(motion_path, motion_proof)
    appended = copy.deepcopy(delivery)
    appended['files'][motion] = record(write(motion, 'native49-plus-two-owned-water-takes'))
    additions = {'status':'PASS','body_sha256':rig['new_sha256'],'original_sha256':motion_record['sha256'],
                 'output_sha256':appended['files'][motion]['sha256'],'original_states':49,'states':51,
                 'unchanged_keys':100855,'added_states':['swim','tread']}
    additions_path = motion + '.merge.json'
    appended['restorations']['native']['motion_additions_proof'] = additions_path
    write(additions_path, additions)
    check_quality_restoration(root, appended)
    for field, wrong in [('body_sha256','wrong'),('original_sha256','wrong'),('unchanged_keys',1),('added_states',['invented'])]:
        write(additions_path,dict(additions,**{field:wrong}))
        try:
            check_quality_restoration(root, appended)
            raise RuntimeError('Invalid supplement accepted: '+field)
        except AssertionError:
            pass
print('NATIVE_RESTORATION_LINEAGE_CONTROLS_PASS: original approval and native water append preserved; ten unproved changes rejected')
