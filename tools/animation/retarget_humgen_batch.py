"""Run isolated native retarget jobs in one CPU-only Unreal process, with a receipt per body."""
import json
import os
from pathlib import Path
import runpy
import traceback
import unreal

job = Path(os.environ['OTHERREACH_HUMGEN_ANIMATION_JOB'])
manifest = json.loads(Path(os.environ['OTHERREACH_HUMGEN_BATCH']).read_text(encoding='utf-8-sig'))
report = job/'native_batch.json'
state = dict(status='RUNNING', characters=[])
try:
    os.environ['OTHERREACH_HUMGEN_KEEP_EDITOR'] = '1'
    for row in manifest:
        state['current'] = row['id']
        report.write_text(json.dumps(state, indent=2))
        os.environ['OTHERREACH_HUMGEN_TARGET_ID'] = row['native_id']
        os.environ['OTHERREACH_HUMGEN_TARGET_REPORT'] = row['target_report']
        result = runpy.run_path(str(Path(__file__).with_name('retarget_humgen_probe.py')))['state']
        if result['status'] != 'DONE':
            raise RuntimeError(f"{row['id']}: {result.get('error', 'native retarget incomplete')}")
        state['characters'].append(dict(id=row['id'], native_id=row['native_id'], clips=result['clips']))
    state.update(status='DONE', current='')
except Exception:
    state.update(status='FAILED', error=traceback.format_exc())
finally:
    report.write_text(json.dumps(state, indent=2))
    unreal.SystemLibrary.quit_editor()
