"""Package Epic transports on a frozen native target, retaining rest/hierarchy audits."""
import argparse
import json
from pathlib import Path
import subprocess
import sys
import shutil
import time

def package(job, actor, runtime):
    job, runtime = job.resolve(), runtime.resolve()
    receipt = json.loads((job / ('retarget_' + actor + '.json')).read_text())
    assert receipt['status'] == 'DONE', receipt.get('error')
    baseline = job / ('retarget_base_' + actor + '.json')
    if baseline.exists():
        old = json.loads(baseline.read_text())
        if old['body_sha256'] == receipt['body_sha256']:
            clips = {clip['state']: clip for clip in old['clips']}
            clips.update({clip['state']: clip for clip in receipt['clips']})
            receipt['clips'] = list(clips.values())
        else:
            assert {clip['state'] for clip in old['clips']} <= {clip['state'] for clip in receipt['clips']}, 'Incomplete supplement targets another frozen body'
    body = job / 'targets' / (actor + '.glb')
    output = runtime / 'humgen_characters/mocap_motion' / (actor + '_motion.res')
    staged_output = job / (actor + '_motion.res')
    manifest = job / ('library_final_' + actor + '.json')
    for clip in receipt['clips']:
        clip.setdefault('display_name', clip['source'].rsplit('/', 1)[-1])
    manifest.write_text(json.dumps(dict(body=str(body), output=str(staged_output), clips=receipt['clips']), indent=2), encoding='utf-8')
    subprocess.run([sys.executable, str(Path(__file__).with_name('audit_humgen_transport.py')), str(body)]
        + ['--manifest', str(manifest), '--report', str(job / ('audit_final_' + actor + '.json'))], check=True)
    audit = json.loads((job / ('audit_final_' + actor + '.json')).read_text())
    for clip in audit['clips']:
        if Path(clip['clip']).stem == 'work_read':
            assert max(clip['motion'][bone]['max_displacement_m'] for bone in ('Head', 'LeftHand', 'RightHand')) > .002, ('Static reading performance', clip['clip'])
        elif Path(clip['clip']).stem in ('work_smith', 'work_cook', 'work_write', 'work_carry', 'talk_1'):
            assert max(clip['motion'][hand]['max_displacement_m'] for hand in ('LeftHand', 'RightHand')) > .02, ('Static retargeted performance', clip['clip'])
    subprocess.run(['pwsh', '-NoProfile', '-File', str(Path(__file__).with_name('package_gate_d_library.ps1').resolve()),
        '-Manifest', str(manifest), '-JobRoot', str(job)], check=True)
    assert staged_output.with_suffix('.res.audit.json').exists(), 'Exact-target package audit missing'
    # Publish complete resources only. A farm mirror or a running creator may be
    # reading the previous complete library while this CPU batch finishes.
    pending = output.with_suffix('.pending.res')
    shutil.copy2(staged_output, pending)
    for attempt in range(20):
        try:
            pending.replace(output)
            break
        except PermissionError:
            if attempt == 19:
                raise
            time.sleep(.25)
    shutil.copy2(staged_output.with_suffix('.res.audit.json'), output.with_suffix('.res.audit.json'))

if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('job', type=Path)
    parser.add_argument('actor')
    parser.add_argument('runtime', type=Path)
    args = parser.parse_args()
    package(args.job, args.actor, args.runtime)
