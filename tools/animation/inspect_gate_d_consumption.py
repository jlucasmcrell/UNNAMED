"""Read source pelvis/feet poses to distinguish seated and standing consumption."""
import json
import os
from pathlib import Path
import unreal

job = Path(os.environ['OTHERREACH_HUMGEN_ANIMATION_JOB'])
sources = json.loads((job / 'catalog_sources.json').read_text())['sources']
report = []
for source in sources:
    for item in source['assets']:
        if item.get('name') not in ('AS_Eats', 'AS_EatingOnParty', 'AS_DrinkingWineFromGlass'):
            continue
        animation = unreal.load_asset(item['asset'])
        options = unreal.AnimPoseEvaluationOptions()
        samples = []
        for fraction in (0, .25, .5, .75, 1):
            pose = unreal.AnimPoseExtensions.get_anim_pose_at_time(animation, animation.sequence_length * fraction, options)
            points = {}
            for bone in ('pelvis', 'head', 'foot_l', 'foot_r'):
                point = unreal.AnimPoseExtensions.get_bone_pose(pose, bone, unreal.AnimPoseSpaces.WORLD).translation
                points[bone] = [point.x, point.y, point.z]
            samples.append(points)
        report.append(dict(asset=item['asset'], seconds=animation.sequence_length, samples=samples))
(job / 'consumption_source_poses.json').write_text(json.dumps(report, indent=2))
unreal.SystemLibrary.quit_editor()
