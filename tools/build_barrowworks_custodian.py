"""Manually articulated machine assembled from real CC0/CC-BY iron scans; CPU only.

Not a generated character mesh: the boiler, pipes and valve retain their licensed source geometry/materials.
The joint layout and keyed mechanical motions are authored here. No Fab or copyleft files enter this assembly.
"""
import argparse
import hashlib
import json
import math
from pathlib import Path
import sys

import bpy
from mathutils import Matrix, Vector

parser = argparse.ArgumentParser()
parser.add_argument('--depot', default=r'D:\Otherreach_Depot\barrowworks_codex')
args = parser.parse_args(sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else [])
depot = Path(args.depot)
bpy.ops.wm.read_factory_settings(use_empty=True)
sources = {}
for kind in ('boiler', 'pipe', 'valve', 'elbow'):
    asset_id = 'bw_codex_' + kind
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=str(depot/'ready'/asset_id/(asset_id+'.glb')))
    imported = set(bpy.data.objects)-before
    meshes = [o for o in imported if o.type == 'MESH']
    assert len(meshes) == 1, (kind, len(meshes))
    data = meshes[0].data.copy()
    data.transform(meshes[0].matrix_world)
    if kind == 'pipe':
        data.transform(Matrix.Rotation(-math.pi/2, 4, 'Y')) # the real straight pipe's long axis is X
    low = Vector(tuple(min(v.co[i] for v in data.vertices) for i in range(3)))
    high = Vector(tuple(max(v.co[i] for v in data.vertices) for i in range(3)))
    data.transform(Matrix.Translation(-(low+high)/2))
    sources[kind] = (data, high-low)
    for obj in imported:
        bpy.data.objects.remove(obj, do_unlink=True)

bones = {
    'root': ((0,0,0), (0,0,.9), None),
    'housing': ((0,0,.9), (0,0,2.2), 'root'),
    'arm.L': ((-.56,0,2.02), (-.78,0,1.45), 'housing'),
    'forearm.L': ((-.78,0,1.45), (-.78,-.2,.98), 'arm.L'),
    'arm.R': ((.56,0,2.02), (.78,0,1.45), 'housing'),
    'forearm.R': ((.78,0,1.45), (.78,-.2,.98), 'arm.R'),
    'leg.L': ((-.32,0,.9), (-.32,0,.15), 'root'),
    'leg.R': ((.32,0,.9), (.32,0,.15), 'root'),
    'index': ((0,.58,1.55), (0,.58,1.95), 'housing'),
}
rig_data = bpy.data.armatures.new('CustodianJoints')
rig = bpy.data.objects.new('Custodian', rig_data)
bpy.context.collection.objects.link(rig)
bpy.context.view_layer.objects.active = rig
rig.select_set(True)
bpy.ops.object.mode_set(mode='EDIT')
for name, (head, tail, parent) in bones.items():
    bone = rig_data.edit_bones.new(name)
    bone.head, bone.tail = head, tail
    if parent: bone.parent = rig_data.edit_bones[parent]
bpy.ops.object.mode_set(mode='OBJECT')
pieces = []

def piece(kind, name, at, dimensions, bone, rotation=(0,0,0)):
    data, size = sources[kind]
    obj = bpy.data.objects.new(name, data.copy())
    bpy.context.collection.objects.link(obj)
    obj.location = at
    obj.rotation_euler = rotation
    obj.scale = tuple(dimensions[i]/size[i] for i in range(3))
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    group = obj.vertex_groups.new(name=bone)
    group.add(list(range(len(obj.data.vertices))), 1, 'REPLACE')
    pieces.append(obj)

piece('boiler', 'riveted_housing', (0,0,1.57), (1.06,1.0,1.5), 'housing')
for side, sign in (('L', -1), ('R', 1)):
    piece('pipe', 'support_'+side, (sign*.32,0,.49), (.23,.23,.8), 'leg.'+side)
    piece('elbow', 'foot_'+side, (sign*.32,-.16,.12), (.34,.5,.21), 'leg.'+side)
    piece('pipe', 'upper_arm_'+side, (sign*.69,0,1.74), (.25,.25,.62), 'arm.'+side,
          (0, -sign*.31, 0))
    piece('pipe', 'index_arm_'+side, (sign*.78,-.1,1.2), (.18,.18,.55), 'forearm.'+side,
          (-.33,0,0))
    piece('valve', 'clamp_'+side, (sign*.78,-.22,.98), (.32,.26,.25), 'forearm.'+side)
# The rear mechanism remains visibly exposed. Its offset index contrasts with the closed, riveted face.
piece('valve', 'exposed_rear_index', (0,.58,1.7), (.5,.2,.5), 'index', (math.pi/2,0,0))
piece('pipe', 'rear_spindle', (0,.51,1.68), (.12,.12,.6), 'index')
bpy.ops.object.select_all(action='DESELECT')
for obj in pieces: obj.select_set(True)
bpy.context.view_layer.objects.active = pieces[0]
bpy.ops.object.join()
body = bpy.context.object
body.name = 'bw_codex_custodian'
body.data = body.data.copy()
modifier = body.modifiers.new('RigidJointWeights', 'ARMATURE')
modifier.object = rig
body.parent = rig
rig.animation_data_create()
scene = bpy.context.scene
scene.render.fps = 30
rigged = depot/'rigged'/'bw_codex_custodian'
rigged.mkdir(parents=True, exist_ok=True)

def export(path, animation):
    bpy.ops.object.select_all(action='DESELECT')
    rig.select_set(True)
    body.select_set(True)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.export_scene.gltf(filepath=str(path), export_format='GLB', use_selection=True, export_yup=True,
        export_apply=False, export_animations=animation, export_animation_mode='ACTIVE_ACTIONS',
        export_force_sampling=True, export_lights=False)

export(rigged/'bw_codex_custodian_rigged.glb', False)
output = []
for state, frames, loop in (('idle',120,True), ('walk',18,True), ('run',16,True),
                            ('sweep',90,False), ('fall',100,False), ('grasp',75,False),
                            ('hit',24,False), ('death',90,False)):
    action = bpy.data.actions.new('custodian_'+state)
    rig.animation_data.action = action
    scene.frame_start, scene.frame_end = 1, frames+1
    for frame in range(1, frames+2):
        t = (frame-1)/frames
        for bone in rig.pose.bones:
            bone.rotation_mode = 'XYZ'
            bone.rotation_euler = (0,0,0)
            bone.location = (0,0,0)
        pose = rig.pose.bones
        if state in ('idle','walk','run'):
            pose['index'].rotation_euler.y = .14*math.sin(t*2*math.pi)
            pose['housing'].rotation_euler.z = .025*math.sin(t*2*math.pi)
            if state != 'idle':
                a = math.sin(t*2*math.pi)
                swing = .67 if state == 'walk' else .9
                pose['leg.L'].rotation_euler.x = swing*a
                pose['leg.R'].rotation_euler.x = -swing*a
                pose['arm.L'].rotation_euler.x = -.12*a
                pose['arm.R'].rotation_euler.x = .12*a
                pose['root'].location.z = .015*(1-math.cos(t*4*math.pi))
        elif state in ('sweep','fall','grasp'):
            # Windup 0..45%, contact 45..70%, recovery 70..100%, matching the game's sampled clip phases.
            tell = min(1,t/.45)
            strike = min(1,max(0,(t-.45)/.25))
            recovery = min(1,max(0,(t-.7)/.3))
            if state == 'sweep':
                pose['arm.R'].rotation_euler.y = -.85*tell*(1-recovery)
                pose['housing'].rotation_euler.z = (-.6*tell+1.2*strike)*(1-recovery)
                pose['forearm.R'].rotation_euler.x = -.4*tell*(1-recovery)
            elif state == 'fall':
                for side in ('L','R'):
                    pose['arm.'+side].rotation_euler.x = (-1.5*tell+2.2*strike)*(1-recovery)
                pose['housing'].rotation_euler.x = (-.18*tell+.48*strike)*(1-recovery)
                pose['leg.L'].rotation_euler.x = .2*strike*(1-recovery)
            else:
                pose['arm.L'].rotation_euler.x = (-.5*tell+1.2*strike)*(1-recovery)
                pose['forearm.L'].rotation_euler.x = -.75*tell*(1-recovery)
                pose['housing'].rotation_euler.z = .15*strike*(1-recovery)
        elif state == 'hit':
            pose['housing'].rotation_euler.x = -.16*math.sin(t*math.pi)
        elif state == 'death':
            fall = min(1,t/.65)
            pose['housing'].rotation_euler.x = 1.25*fall
            pose['root'].location.z = -.6*fall
            pose['leg.L'].rotation_euler.y = -.5*fall
            pose['leg.R'].rotation_euler.y = .5*fall
            pose['arm.L'].rotation_euler.x = -.8*fall
            pose['arm.R'].rotation_euler.x = -.6*fall
        for bone in pose:
            bone.keyframe_insert('rotation_euler', frame=frame)
            bone.keyframe_insert('location', frame=frame)
    clip_id = 'creature.bw_codex_custodian.'+state
    folder = depot/'animation'/'ready'/'creatures'
    folder.mkdir(parents=True, exist_ok=True)
    path = folder/('anim.'+clip_id+'.glb')
    export(path, True)
    clips = depot/'animation'/'clips'
    clips.mkdir(parents=True, exist_ok=True)
    (clips/('anim.'+clip_id+'.json')).write_text(json.dumps({'id':clip_id, 'loop':loop, 'duration_s':frames/30,
        **({'authored_speed_m_s':1.55 if state=='walk' else 2.2} if state in ('walk','run') else {}),
        'source':'Manually keyed rigid mechanical articulation; no generated mesh or animation service.'},indent=2)+'\n',encoding='utf-8')
    output.append({'clip':clip_id, 'sha256':hashlib.sha256(path.read_bytes()).hexdigest(), 'frames':frames})
    rig.animation_data.action = None
credits = {
    'asset_id':'bw_codex_custodian', 'built_by':str(Path(__file__).name), 'mesh':'Real source parts, manually assembled and rigidly weighted to nine joints.',
    'sources':[
        {'title':'Boiler from the puffer VIC 32','author':'Scottish Maritime Museum','licence':'CC0 1.0',
         'url':'https://sketchfab.com/3d-models/884ed11e38054aa48a3db1d9025c881d'},
        {'title':'Old Industrial Pipe Pack PBR','author':'SLASH-RENDAR','licence':'CC-BY 4.0',
         'url':'https://sketchfab.com/3d-models/a10a6011334e4d11a7023d67705ac1d4',
         'modifications':'Reoriented, resized, decimated, assembled and animated; attribution required.'}],
    'clips':output, 'shippable':True,
}
(rigged/'bw_codex_custodian_meta.json').write_text(json.dumps(credits,indent=2)+'\n',encoding='utf-8')
print('CUSTODIAN_EXPORT '+json.dumps(credits), flush=True)
