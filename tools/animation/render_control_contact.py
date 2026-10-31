"""CPU Workbench contact frames of exact compiled Godot poses on the unchanged original full native body.

The Godot-global pose delta is applied to the importer's own native rest frame.
This is a coordinate bridge for authoring review, never a runtime retarget or shipped key edit.
"""
import json
from pathlib import Path
import sys
import bpy
from mathutils import Matrix, Vector

args = sys.argv[sys.argv.index('--')+1:]
samples = json.loads(Path(args[0]).read_text())
out = Path(args[1]);out.mkdir(parents=True, exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=samples['body'])
rig = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
rig.animation_data_clear()
for obj in bpy.data.objects:
    if obj.type == 'MESH':obj.hide_render = '_LOD0' not in obj.name or (len(args)>2 and args[2] not in obj.name)
conversion = Matrix(((1,0,0,0),(0,0,-1,0),(0,1,0,0),(0,0,0,1)))
rest = {bone.name: rig.matrix_world @ bone.matrix_local for bone in rig.data.bones}
camera = bpy.data.objects.new('Compiled pose contact camera', bpy.data.cameras.new('Compiled pose contact camera'))
bpy.context.collection.objects.link(camera)
camera.data.type = 'ORTHO';camera.data.ortho_scale = 2.05
scene = bpy.context.scene;scene.camera = camera
scene.render.engine = 'BLENDER_WORKBENCH'
scene.render.resolution_x = 640;scene.render.resolution_y = 720
scene.render.resolution_percentage = 100
scene.display.shading.light = 'STUDIO';scene.display.shading.studio_light = 'paint.sl'
scene.display.shading.color_type = 'MATERIAL'
scene.display.shading.show_cavity = True;scene.display.shading.cavity_type = 'BOTH'
scene.display.shading.background_type = 'WORLD'
scene.world = bpy.data.worlds.new('Authoring world');scene.world.color = (.075,.075,.075)
scene.view_settings.view_transform = 'Standard'
maximum_error = 0
def depth(bone):
    return 0 if bone.parent is None else depth(bone.parent)+1
ordered = sorted(rig.pose.bones, key=depth)
for sample in samples['samples']:
    if len(args)>2 and (sample['clip']!='shove' or abs(sample['fraction']-.35)>.001): continue
    for bone in rig.pose.bones:bone.matrix_basis = Matrix.Identity(4)
    bpy.context.view_layer.update()
    for bone in ordered:
        if bone.name not in sample['bones']:continue
        delta = Matrix(sample['bones'][bone.name]) @ Matrix(samples['rest'][bone.name]).inverted()
        posed = conversion @ delta @ conversion.inverted() @ rest[bone.name]
        bone.matrix = rig.matrix_world.inverted() @ posed
        bpy.context.view_layer.update()
    bpy.context.view_layer.update()
    points = []
    for name in ('Head','Hips','LeftHand','RightHand','LeftFoot','RightFoot'):
        actual = (rig.matrix_world @ rig.pose.bones[name].matrix).translation
        expected = (conversion @ Matrix(sample['bones'][name])).translation
        maximum_error = max(maximum_error,(actual-expected).length)
        points.append(actual)
    assert maximum_error < .001, ('Pose bridge differs from compiled Godot joints',maximum_error)
    centre = Vector(tuple((min(p[i] for p in points)+max(p[i] for p in points))*.5 for i in range(3)))
    for view, offset in [('front',(0,-4,1)),('side',(4,-1,1))]:
        camera.location = centre+Vector(offset)
        camera.rotation_euler = (centre-camera.location).to_track_quat('-Z','Y').to_euler()
        scene.render.filepath = str(out/f"{sample['clip']}_{round(sample['fraction']*100):02d}_{view}.png")
        bpy.ops.render.render(write_still=True)
(out/'COMPILED_POSE_PREVIEW.json').write_text(json.dumps({'body_sha256':samples['body_sha256'],
    'library_sha256':samples['library_sha256'],'max_joint_error_m':maximum_error,
    'renderer':'CPU Workbench authoring preview','runtime_verdict':False,'samples':len(samples['samples'])},indent=2))
