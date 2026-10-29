"""Repair the isolated DramaticDeath demo preview's missing post-process references.

The acquired source project, animation keys and HumGen targets are untouched.
Epic retargeting reads the original animation tracks, not this demo's skin correctives.
"""
import json
import os
from pathlib import Path
import traceback
import unreal

job = Path(os.environ['OTHERREACH_HUMGEN_ANIMATION_JOB'])
report = dict(status='REPAIRING', meshes=[])
try:
    for source in json.loads((job / 'catalog_sources_death.json').read_text())['sources']:
        if source['path'] != 'DramaticDeath':
            continue
        for item in source['assets']:
            if item['kind'] != 'mesh':
                continue
            mesh = unreal.load_asset(item['asset'])
            old = mesh.get_editor_property('post_process_anim_blueprint')
            if old:
                mesh.set_editor_property('post_process_anim_blueprint', None)
                assert unreal.EditorAssetLibrary.save_loaded_asset(mesh, False)
            report['meshes'].append(dict(asset=item['asset'], previous=str(old)))
    report['status'] = 'DONE'
except Exception:
    report.update(status='FAILED', error=traceback.format_exc())
(job / 'source_preview_repair.json').write_text(json.dumps(report, indent=2))
unreal.SystemLibrary.quit_editor()
