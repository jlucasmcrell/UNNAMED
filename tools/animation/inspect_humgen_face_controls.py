"""Read the paid add-on's native facial controls; no production export or asset mutation."""
import json
import os
import traceback
from pathlib import Path
import bpy
from HumGen3D.human.human import Human

root = Path(os.environ["OTHERREACH_HUMGEN_ANIMATION_JOB"])
report = {}
try:
    bpy.ops.wm.read_homefile(use_empty=True)
    human = Human.from_preset(os.path.join("models", "female", "Caucasian presets", "Caucasian 1.json"))
    human.expression.load_facial_rig()
    rig = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
    report['bones'] = [b.name for b in rig.pose.bones if any(k in b.name.lower() for k in ['jaw', 'eye', 'look'])]
    jaw = rig.pose.bones['jaw']
    parent = jaw.bone.parent
    rest = parent.matrix_local.inverted() @ jaw.bone.matrix_local if parent else jaw.bone.matrix_local
    report['jaw_rest_local'] = [list(row) for row in rest]
    report['jaw_constraints'] = [{'type': c.type, 'target': str(getattr(c, 'target', None)), 'subtarget': str(getattr(c, 'subtarget', ''))} for c in jaw.constraints]
    report['jaw_samples'] = []
    control = rig.pose.bones['jaw_open_lt_rt_frwd']
    for weight in [0, .25, .5, 1]:
        control.location.z = -weight / 50
        rig.update_tag()
        bpy.context.view_layer.update()
        evaluated = rig.evaluated_get(bpy.context.evaluated_depsgraph_get())
        bone = evaluated.pose.bones['jaw']
        local = bone.parent.matrix.inverted() @ bone.matrix if bone.parent else bone.matrix
        delta = rest.inverted() @ local
        q = delta.to_quaternion()
        report['jaw_samples'].append({'weight': weight, 'body_jaw_open': human.objects.body.data.shape_keys.key_blocks['jawOpen'].value,
                                      'delta_quat_xyzw': [q.x, q.y, q.z, q.w], 'delta_translation': list(delta.translation)})
    report['status'] = 'DONE'
except Exception:
    report.update(status='FAILED', error=traceback.format_exc())
(root/'native_face_controls.json').write_text(json.dumps(report, indent=2))
