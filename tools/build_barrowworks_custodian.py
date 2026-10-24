"""Refit and rig an authored CC-BY character. No AI-generated mesh or asset ingestion into generation services.

Source: Robot, piacenti (2015), CC BY 3.0. Guns, missile pods and spikes are removed.
Only ordinary Blender mesh selection, material conversion, rigid weighting and hand-keyed articulation are used.
"""
import argparse,hashlib,json,math,sys
from pathlib import Path
import bpy,bmesh
from mathutils import Matrix,Vector
parser=argparse.ArgumentParser();parser.add_argument('--depot',default=r'D:\Otherreach_Depot\barrowworks_codex')
args=parser.parse_args(sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else [])
depot=Path(args.depot);source=depot/'sources/piacenti_robot'
bpy.ops.wm.open_mainfile(filepath=str(source/'robot game.blend'),load_ui=False,use_scripts=False)
original=bpy.data.objects['1'];data=original.data.copy();data.transform(original.matrix_world)
low=Vector(tuple(min(v.co[i] for v in data.vertices) for i in range(3)))
high=Vector(tuple(max(v.co[i] for v in data.vertices) for i in range(3)))
data.transform(Matrix.Scale(2.5/(high.z-low.z),4)@Matrix.Translation(Vector((-(low.x+high.x)/2,-(low.y+high.y)/2,-low.z))))
# Select whole connected source parts, preserving authored UVs and normals on all retained surfaces.
bm=bmesh.new();bm.from_mesh(data);seen=set();removed=[]
for v in bm.verts:
    if v in seen:continue
    todo=[v];part=[];seen.add(v)
    while todo:
        current=todo.pop();part.append(current)
        for edge in current.link_edges:
            other=edge.other_vert(current)
            if other not in seen:seen.add(other);todo.append(other)
    lo=[min(v.co[i] for v in part) for i in range(3)];hi=[max(v.co[i] for v in part) for i in range(3)]
    # Front gun tubes/rings, shoulder missile rack and projectile/spike tips.
    if lo[1]<-.70 or lo[2]>1.89 or (abs((lo[0]+hi[0])/2)>1.01 and lo[2]>1.50):removed+=part
bmesh.ops.delete(bm,geom=removed,context='VERTS');bm.to_mesh(data);bm.free()
for obj in list(bpy.data.objects):bpy.data.objects.remove(obj,do_unlink=True)
material=bpy.data.materials.new('piacenti_ancient_iron');material.use_nodes=True
nodes=material.node_tree.nodes;bsdf=nodes.get('Principled BSDF');bsdf.inputs['Metallic'].default_value=.12;bsdf.inputs['Roughness'].default_value=.78
for filename,socket in [('ancient color.png','Base Color'),('normal.png','Normal')]:
    image=bpy.data.images.load(str(source/filename))
    image.pack()
    tex=nodes.new('ShaderNodeTexImage');tex.image=image
    if socket=='Normal':
        image.colorspace_settings.name='Non-Color';normal=nodes.new('ShaderNodeNormalMap');material.node_tree.links.new(tex.outputs['Color'],normal.inputs['Color']);material.node_tree.links.new(normal.outputs['Normal'],bsdf.inputs[socket])
    else:material.node_tree.links.new(tex.outputs['Color'],bsdf.inputs[socket])
data.materials.clear();data.materials.append(material)
body=bpy.data.objects.new('authored_custodian',data);bpy.context.collection.objects.link(body)
for polygon in data.polygons:polygon.material_index=0
bones={
 'root':((0,0,0),(0,0,1.15),None),'housing':((0,0,1.15),(0,0,2.4),'root'),
 'arm.L':((-.74,0,1.97),(-.98,0,1.45),'housing'),'arm.R':((.74,0,1.97),(.98,0,1.45),'housing'),
 'forearm.L':((-.98,0,1.45),(-.98,-.1,1.0),'arm.L'),'forearm.R':((.98,0,1.45),(.98,-.1,1.0),'arm.R'),
 'leg.L':((-.45,.2,1.27),(-.45,.2,.67),'root'),'leg.R':((.45,.2,1.27),(.45,.2,.67),'root'),
 'shin.L':((-.45,.2,.67),(-.45,.2,.14),'leg.L'),'shin.R':((.45,.2,.67),(.45,.2,.14),'leg.R'),
 'index':((0,.60,1.75),(0,.60,2.10),'housing')}
rig_data=bpy.data.armatures.new('CustodianJoints');rig=bpy.data.objects.new('Custodian',rig_data);bpy.context.collection.objects.link(rig)
bpy.context.view_layer.objects.active=rig;rig.select_set(True);bpy.ops.object.mode_set(mode='EDIT')
for name,(head,tail,parent) in bones.items():
 bone=rig_data.edit_bones.new(name);bone.head=head;bone.tail=tail
 if parent:bone.parent=rig_data.edit_bones[parent]
bpy.ops.object.mode_set(mode='OBJECT')
groups={name:body.vertex_groups.new(name=name) for name in bones}
# Keep the authored torso/arm shells rigid. Only the connected knee transition blends across two leg joints.
neighbours=[[] for _ in data.vertices]
for edge in data.edges:
    a,b=edge.vertices;neighbours[a].append(b);neighbours[b].append(a)
seen=set()
for vertex in data.vertices:
    if vertex.index in seen:continue
    todo=[vertex.index];part=[];seen.add(vertex.index)
    while todo:
        i=todo.pop();part.append(i)
        for j in neighbours[i]:
            if j not in seen:seen.add(j);todo.append(j)
    centre=sum((data.vertices[i].co for i in part),Vector())/len(part)
    extent=max(abs(data.vertices[i].co.x) for i in part)
    side='L' if centre.x<0 else 'R'
    if len(part)>1500:
        groups['housing'].add(part,1,'REPLACE')
    elif extent>.80 and centre.z>.73:
        groups[('arm.' if centre.z>1.5 else 'forearm.')+side].add(part,1,'REPLACE')
    else:
        for i in part:
            upper=max(0,min(1,(data.vertices[i].co.z-.55)/.25))
            if upper>0:groups['leg.'+side].add([i],upper,'REPLACE')
            if upper<1:groups['shin.'+side].add([i],1-upper,'REPLACE')
pieces=[body]
# Retained industrial source parts form a measuring clamp and exposed rear index, never firearms.
for kind in ('elbow','valve'):
 before=set(bpy.data.objects);asset='bw_codex_'+kind
 bpy.ops.import_scene.gltf(filepath=str(depot/'ready'/asset/(asset+'.glb')))
 imported=set(bpy.data.objects)-before;obj=next(o for o in imported if o.type=='MESH');mesh=obj.data.copy();mesh.transform(obj.matrix_world)
 lo=Vector(tuple(min(v.co[i] for v in mesh.vertices) for i in range(3)));hi=Vector(tuple(max(v.co[i] for v in mesh.vertices) for i in range(3)));mesh.transform(Matrix.Translation(-(lo+hi)/2))
 for o in imported:bpy.data.objects.remove(o,do_unlink=True)
 specs=[((0,.66,1.85),(.40,.22,.40),'index') ] if kind=='valve' else [((sign*.98+jaw*.10,-.36,.99),(.12,.30,.27),'forearm.'+side) for side,sign in [('L',-1),('R',1)] for jaw in (-1,1)]
 for at,dimensions,bone in specs:
  o=bpy.data.objects.new('index' if kind=='valve' else 'caliper_jaw',mesh.copy());bpy.context.collection.objects.link(o);o.location=at;o.scale=tuple(dimensions[i]/(hi-lo)[i] for i in range(3))
  bpy.ops.object.select_all(action='DESELECT');o.select_set(True);bpy.context.view_layer.objects.active=o;bpy.ops.object.transform_apply(location=True,rotation=True,scale=True)
  g=o.vertex_groups.new(name=bone);g.add(list(range(len(o.data.vertices))),1,'REPLACE');pieces.append(o)
# A cast register, brass scale and exposed measuring jaws pull the source toward the works' vocabulary.
brass=bpy.data.materials.new('worn_register_brass');brass.use_nodes=True
bsdf=brass.node_tree.nodes.get('Principled BSDF');bsdf.inputs['Base Color'].default_value=(.32,.20,.07,1);bsdf.inputs['Metallic'].default_value=.5;bsdf.inputs['Roughness'].default_value=.65
details=[]
for radius,depth,y in ((.18,.10,-.82),(.145,.018,-.88)):
    bpy.ops.mesh.primitive_cylinder_add(vertices=32,radius=radius,depth=depth,location=(0,y,1.7),rotation=(math.pi/2,0,0))
    obj=bpy.context.object;obj.data.materials.append(brass);details.append(obj)
for step in range(13):
    angle=step*math.pi/8
    bpy.ops.mesh.primitive_cube_add(size=1,location=(.11*math.cos(angle),-.898,1.7+.11*math.sin(angle)))
    obj=bpy.context.object;obj.scale=(.018,.018,.055);obj.rotation_euler.y=-angle;obj.data.materials.append(material);details.append(obj)
bpy.ops.mesh.primitive_cube_add(size=1,location=(.025,-.915,1.725))
obj=bpy.context.object;obj.scale=(.10,.018,.018);obj.rotation_euler.y=-.6;obj.data.materials.append(material);details.append(obj)
for obj in details:
    bpy.ops.object.select_all(action='DESELECT');obj.select_set(True);bpy.context.view_layer.objects.active=obj
    bpy.ops.object.transform_apply(location=True,rotation=True,scale=True)
    group=obj.vertex_groups.new(name='housing');group.add(list(range(len(obj.data.vertices))),1,'REPLACE')
pieces+=details
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
    (carrier if animation else body).select_set(True)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.export_scene.gltf(filepath=str(path), export_format='GLB', use_selection=True, export_yup=True,
        export_apply=False, export_animations=animation, export_animation_mode='ACTIVE_ACTIONS',
        export_force_sampling=True, export_lights=False)

# Brass register plate/ticks and measuring jaws are connected rigidly to the existing housing bones.
export(rigged/'bw_codex_custodian_rigged.glb', False)
bpy.ops.mesh.primitive_plane_add(size=.001);carrier=bpy.context.object;carrier.name='clip_skeleton_carrier'
carrier.parent=rig;group=carrier.vertex_groups.new(name='root');group.add(list(range(4)),1,'REPLACE')
modifier=carrier.modifiers.new('clip_rig','ARMATURE');modifier.object=rig

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
                # Two-link mechanical knees: the stance ankle stays at its authored ground height.
                # Swing lifts only the advancing foot; neither leg floats below its hip socket.
                root_z=-.025+.008*math.sin(t*4*math.pi)**2
                pose['root'].location.z=root_z
                for side,offset in (('L',0),('R',math.pi)):
                    phase=t*2*math.pi+offset
                    dy=.21*math.cos(phase)
                    ankle_z=.14+.12*max(0,math.sin(phase))
                    dz=1.27+root_z-ankle_z
                    distance=min(1.129,math.hypot(dy,dz));upper,lower=.60,.53
                    shoulder=math.acos(max(-1,min(1,(upper*upper+distance*distance-lower*lower)/(2*upper*distance))))
                    knee=math.pi-math.acos(max(-1,min(1,(upper*upper+lower*lower-distance*distance)/(2*upper*lower))))
                    pose['leg.'+side].rotation_euler.x=math.atan2(dy,dz)-shoulder
                    pose['shin.'+side].rotation_euler.x=knee
                pose['arm.L'].rotation_euler.x=-.12*math.sin(t*2*math.pi)
                pose['arm.R'].rotation_euler.x=.12*math.sin(t*2*math.pi)
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
    'asset_id':'bw_codex_custodian', 'built_by':str(Path(__file__).name),
    'mesh':'Authored 2015 Robot by piacenti; firearms and missile/spike components removed, refit to 2.5 m, manually weighted to eleven joints and hand-keyed. Mechanical measuring jaws and exposed rear index added.',
    'sources':[
        {'title':'Robot','author':'piacenti','licence':'CC BY 3.0','url':'https://opengameart.org/content/robot-5',
         'download_url':'https://opengameart.org/sites/default/files/robot%20game.zip',
         'licence_url':'https://creativecommons.org/licenses/by/3.0/',
         'archive_sha256':hashlib.sha256((depot/'sources/piacenti_robot/robot_game.zip').read_bytes()).hexdigest()},
        {'title':'Old Industrial Pipe Pack PBR','author':'SLASH-RENDAR','licence':'CC BY 4.0',
         'url':'https://sketchfab.com/3d-models/a10a6011334e4d11a7023d67705ac1d4',
         'modifications':'Only opposed caliper jaws and rear index; source parts retained, attribution required.'}],
    'clips':output,'shippable':True,
}
(rigged/'bw_codex_custodian_meta.json').write_text(json.dumps(credits,indent=2)+'\n',encoding='utf-8')
print('CUSTODIAN_EXPORT '+json.dumps(credits),flush=True)
