"""Package Epic transports on a frozen native target, retaining rest/hierarchy audits."""
import argparse
import json
from pathlib import Path
import subprocess
import sys

def package(job, actor, runtime):
    receipt = json.loads((job / ('retarget_' + actor + '.json')).read_text())
    assert receipt['status'] == 'DONE', receipt.get('error')
    baseline = job / ('retarget_base_' + actor + '.json')
    if baseline.exists():
        old = json.loads(baseline.read_text())
        assert old['body_sha256'] == receipt['body_sha256'], 'Performance supplement targets another frozen body'
        clips = {clip['state']: clip for clip in old['clips']}
        clips.update({clip['state']: clip for clip in receipt['clips']})
        receipt['clips'] = list(clips.values())
    body = job / 'targets' / (actor + '.glb')
    subprocess.run([sys.executable, str(Path(__file__).with_name('audit_humgen_transport.py')), str(body)]
        + [clip['path'] for clip in receipt['clips']] + ['--report', str(job / ('audit_final_' + actor + '.json'))], check=True)
    audit = json.loads((job / ('audit_final_' + actor + '.json')).read_text())
    for clip in audit['clips']:
        if Path(clip['clip']).stem in ('work_smith', 'work_cook', 'work_read', 'work_carry', 'talk_1'):
            assert max(clip['motion'][hand]['max_displacement_m'] for hand in ('LeftHand', 'RightHand')) > .02, ('Static retargeted performance', clip['clip'])
    output = runtime / 'humgen_characters/mocap_motion' / (actor + '_motion.res')
    manifest = job / ('library_final_' + actor + '.json')
    manifest.write_text(json.dumps(dict(body=str(body), output=str(output), clips=receipt['clips']), indent=2), encoding='utf-8')
    subprocess.run(['C:/Otherreach_Worker/tools/godot_4_7_2_net8_0_31/godot/Godot_v4.7.2-stable_mono_win64_console.exe',
        '--headless', '--path', str(job / 'rig_gate'), '--script',
        str(Path(__file__).with_name('build_humgen_motion.gd').resolve()), '--', str(manifest)], check=True, timeout=3600)
    assert output.with_suffix('.res.audit.json').exists(), 'Exact-target package audit missing'

if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('job', type=Path)
    parser.add_argument('actor')
    parser.add_argument('runtime', type=Path)
    args = parser.parse_args()
    package(args.job, args.actor, args.runtime)
