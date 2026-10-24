"""Export original FAL sequences on their native preview, solely to audit travel direction."""
import json
import os
import traceback
from pathlib import Path
import unreal

root = Path(os.environ['OTHERREACH_HUMGEN_DIRECTION_PROBE'])
state = {'clips': []}
try:
    mesh = unreal.load_asset('/Game/FreeAnimationLibrary/Demo/Characters/Mannequins/Meshes/SKM_Manny_Simple')
    for folder, name in [('Walk', 'anim_Walk_Bwd_Loop_L'), ('Jog', 'anim_Jog_Loop_Bwd'),
                         ('Walk', 'anim_Walk_Left_Loop'), ('Jog', 'anim_Jog_Loop_Left_L')]:
        animation = unreal.load_asset('/Game/FreeAnimationLibrary/Animations/' + folder + '/' + name)
        animation.set_preview_skeletal_mesh(mesh)
        options = unreal.GLTFExportOptions()
        options.bake_material_inputs = unreal.GLTFMaterialBakeMode.DISABLED
        options.export_vertex_skin_weights = True
        options.export_animation_sequences = True
        options.export_preview_mesh = True
        options.export_uniform_scale = .01
        options.export_morph_targets = False
        options.use_mesh_quantization = False
        path = root / (name + '.glb')
        task = unreal.AssetExportTask()
        task.object = animation
        task.filename = str(path)
        task.automated = True
        task.prompt = False
        task.replace_identical = True
        task.options = options
        if not path.exists():
            assert unreal.Exporter.run_asset_export_task(task)
        state['clips'].append({'path': str(path), 'asset': animation.get_path_name(), 'seconds': animation.sequence_length,
                               'rate_scale': animation.get_editor_property('rate_scale')})
    if os.environ.get('OTHERREACH_HUMGEN_SOURCE_REPORT'):
        manifest = json.loads(Path(os.environ['OTHERREACH_HUMGEN_SOURCE_REPORT']).read_text())
        state['source_rates'] = {clip['source_asset']: unreal.load_asset(clip['source_asset']).get_editor_property('rate_scale') for clip in manifest['clips']}
    state['status'] = 'DONE'
except Exception:
    state.update(status='FAILED', error=traceback.format_exc())
(root / 'source_direction.json').write_text(json.dumps(state, indent=2))
unreal.SystemLibrary.quit_editor()
