"""Convert the CC-BY KrazyKaijus eel on its native rig, retaining native 4K detail.
Original authored poses only; deterministic olive pigment treatment, no AI input.
Blender CPU: --factory-startup --python prepare_eel_monster.py -- SOURCE OUTPUT
"""
import bpy,math,json,sys
from pathlib import Path
from mathutils import Vector
import numpy as np
args=sys.argv[sys.argv.index('--')+1:];SOURCE=Path(args[0]);OUT=Path(args[1]);OUT.mkdir(parents=True,exist_ok=True);ID='sedge_eel_serpent';FPS=30
bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False);bpy.ops.import_scene.gltf(filepath=str(SOURCE));scene=bpy.context.scene;scene.render.fps=FPS
for o in list(scene.objects):
 if o.name=='Icosphere':bpy.data.objects.remove(o,do_unlink=True) # Unskinned source display prop, not the creature.
arm=next(o for o in scene.objects if o.type=='ARMATURE');objects=[o for o in scene.objects if o.type=='MESH'];native_bones=[b.name for b in arm.data.bones]
for o in scene.objects:
 if o.animation_data:o.animation_data.action=None
for pb in arm.pose.bones:pb.matrix_basis.identity()
# Colour treatment preserves every 4K texel's painted detail and warm teeth.
for image in list(bpy.data.images):
 if image.size[0]!=4096:continue
 rgba=np.empty(image.size[0]*image.size[1]*4,np.float32);image.pixels.foreach_get(rgba);rgba=rgba.reshape(-1,4);rgb=rgba[:,:3];blue=rgb[:,2]>rgb[:,0]*1.05;l=rgb@np.array([.2126,.7152,.0722]);rgb[blue]=l[blue,None]*np.array([.70,.76,.48])
 treated=bpy.data.images.new('SedgeEel_olive_4k',width=4096,height=4096,alpha=True);treated.pixels.foreach_set(rgba.ravel());treated.update();treated.filepath_raw=str(OUT/'eel_native_4k_olive.png');treated.file_format='PNG';treated.save();treated.pack()
 for m in bpy.data.materials:
  if m.use_nodes:
   for node in m.node_tree.nodes:
    if node.type=='TEX_IMAGE' and node.image==image:node.image=treated

for m in bpy.data.materials:
 if m.use_nodes and (p:=m.node_tree.nodes.get('Principled BSDF')):
  p.inputs['Roughness'].default_value=.43;p.inputs['Metallic'].default_value=0;p.inputs['Coat Weight'].default_value=.22
# One global transform: native anatomy, skin weights and rest skeleton unchanged; fins may enter soft mud.
pts=[o.matrix_world@Vector(c) for o in objects for c in o.bound_box];low=min(p.z for p in pts);root=bpy.data.objects.new('EelMetricRoot',None);scene.collection.objects.link(root)
for o in list(scene.objects):
 if o!=root and o.parent is None:o.parent=root
root.scale=(5.5,)*3;root.location=(0,2.15,.30)  # Belly, rather than the lowest fin tip, meets the mud.
body_chain=['Bone_01']+['Bone.%03d_0%d'%(i,i+1) for i in range(1,9)]
tail=['Bone.%03d_0%d'%(i,i) for i in range(17,23)]
assert all(b in arm.pose.bones for b in body_chain+tail), (body_chain,tail)
head='Bone.009_010';jaw='Bone.013_014';states=['idle','walk','run','light_1','light_2','heavy_1','heavy_2','rush_1','rush_2','hit','death'];takes={}
for state in states:
 loop=state in ['idle','walk','run'];duration={'idle':2.4,'walk':1.6,'run':.9,'hit':.6,'death':1.8}.get(state,1.5);frames=round(duration*FPS);act=bpy.data.actions.new(state);arm.animation_data_create();arm.animation_data.action=act
 for frame in range(frames+1):
  t=frame/frames;wave=t*math.tau;side=-1 if state.endswith('_2') else 1;wind=math.sin(min(t/.45,1)*math.pi/2)*(1-t);strike=math.sin((t-.4)/.32*math.pi) if .4<t<.72 else 0
  for pb in arm.pose.bones:pb.rotation_mode='XYZ';pb.rotation_euler=(0,0,0);pb.location=(0,0,0)
  for j,name in enumerate(body_chain):
   pb=arm.pose.bones[name];pb.rotation_euler.z=math.sin(wave-j*.55)*(.045 if state=='idle' else .12 if loop else .02)
   if state.startswith('heavy'):pb.rotation_euler.z+=side*(.11*wind-.16*strike)
   if state.startswith('rush'):pb.rotation_euler.z+=side*.05*wind
  for j,name in enumerate(tail):arm.pose.bones[name].rotation_euler.z=math.sin(wave+j*.65)*(.08 if state=='idle' else .18 if loop else .04)
  h=arm.pose.bones[head];j=arm.pose.bones[jaw]
  if state.startswith('light'):h.rotation_euler.z=side*(.18*wind-.3*strike);j.rotation_euler.x=.42*strike
  elif state.startswith('heavy'):h.rotation_euler.z=side*(.3*wind-.5*strike);h.rotation_euler.x=.14*wind;j.rotation_euler.x=.5*strike
  elif state.startswith('rush'):h.rotation_euler.z=side*.12*strike;j.rotation_euler.x=.45*strike
  elif state=='hit':h.rotation_euler.z=math.sin(t*math.pi*3)*.24*(1-t)
  elif state=='death':arm.pose.bones['_rootJoint'].rotation_euler.y=.75*min(t*2,1);j.rotation_euler.x=.2*t
  for pb in arm.pose.bones:pb.keyframe_insert('rotation_euler',frame=frame);pb.keyframe_insert('location',frame=frame)
 scene.frame_start=0;scene.frame_end=frames;takes[state]=(act,duration,loop)
def export(path,obs,animation):
 bpy.ops.object.select_all(action='DESELECT')
 for o in obs:o.select_set(True)
 bpy.context.view_layer.objects.active=arm;bpy.ops.export_scene.gltf(filepath=str(path),export_format='GLB',use_selection=True,export_animations=animation,export_animation_mode='ACTIVE_ACTIONS',export_force_sampling=True,export_frame_range=True,export_skins=True,export_def_bones=False,export_rest_position_armature=True,export_morph=False,export_cameras=False,export_lights=False,export_yup=True,export_apply=False,export_tangents=True)
def rest():
 arm.animation_data.action=None
 for pb in arm.pose.bones:pb.rotation_euler=(0,0,0);pb.location=(0,0,0)
 scene.frame_set(0)
rest();export(OUT/(ID+'.glb'),[root,arm]+objects,False)
# Preserve the exact native skeleton path in the animation-only exports.
data=bpy.data.meshes.new('clip_skin');data.from_pydata([(0,0,0),(.001,0,0),(0,.001,0)],[],[(0,1,2)]);stub=bpy.data.objects.new('clip_skin',data);scene.collection.objects.link(stub);stub.parent=arm;g=stub.vertex_groups.new(name='_rootJoint');g.add([0,1,2],1,'REPLACE');stub.modifiers.new('Skin','ARMATURE').object=arm
for state,(action,duration,loop) in takes.items():
 arm.animation_data.action=action;scene.frame_end=round(duration*FPS);scene.frame_set(0);folder=OUT/'animation/ready/creatures';folder.mkdir(parents=True,exist_ok=True);name='anim.creature.'+ID+'.'+state;export(folder/(name+'.glb'),[root,arm,stub],True)
 rec=dict(schema_version='1',animation_id=name,skeleton_family=ID,category='locomotion' if loop else 'combat',animation_family='creature',clip_type=state,loop=loop,root_motion=False,duration_s=duration,events=[],hand_profile=dict(left='none',right='none'),tags=['creature','serpent'],source='KrazyKaijus native rig; OTHERREACH authored poses; CC BY 4.0 derivative');(OUT/'animation/clips').mkdir(exist_ok=True);(OUT/'animation/clips'/(name+'.json')).write_text(json.dumps(rec,indent=2))
bpy.data.objects.remove(stub,do_unlink=True);rest();bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'sedge_eel_source.blend'))
(OUT/(ID+'_meta.json')).write_text(json.dumps(dict(asset_id=ID,source=str(SOURCE),author='KrazyKaijus / OTHERREACH motion and pigment treatment',licence='CC BY 4.0',url='https://sketchfab.com/3d-models/eel-monster-rigged-7747294b15b348cabd6f6a7c3f741f74',rigged=True,skeleton_family=ID,status='PROPOSED / UNREVIEWED',native_bones=native_bones,texture_resolution=4096,clips=states),indent=2));print('SEDGE_VENDOR_EEL_EXPORT_DONE')
