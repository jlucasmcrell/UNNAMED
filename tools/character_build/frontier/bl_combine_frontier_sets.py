"""Bake layered source outfits with native waist seams, retaining source PBR materials."""
import bpy,bmesh,hashlib,json,os,sys
from pathlib import Path
from mathutils import Vector

sys.path.insert(0,str(Path(__file__).parent))
from native_clothing import approved_surface,arm_weights
from frontier_region_masks import REGIONS
root=Path(r'D:\Otherreach_Scratch\frontier_native_20261018')
fit=os.environ.get('OTHERREACH_CLOTHING_BODY','veth_f');body_id='mh_'+fit
bases={'linen':'frontier_linen_shirt','trousers':'frontier_wool_trousers','vest':'frontier_sealed_vest',
       'jack':'frontier_lamework_jack','cap':'frontier_standing_cap','coif':'frontier_lame_coif',
       'boots':'frontier_sealed_boots','sabatons':'frontier_lame_sabatons','gloves':'frontier_smith_gloves','cloak':'frontier_wool_cloak'}
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=str(root/'approved_clothing_sources'/(fit+'.glb')))
arm=next(o for o in bpy.data.objects if o.type=='ARMATURE')
body_target=approved_surface(fit)
# Reference shapes are read only and never exported as clothing.
for obj in list(bpy.data.objects):
    if obj not in (arm,body_target):bpy.data.objects.remove(obj,do_unlink=True)
body_target.hide_set(True)
sources={};source_hashes={}
for key,base in bases.items():
    aid=base+'__'+body_id;path=root/'game_candidate/primary/rigged'/aid/(aid+'_rigged.glb')
    source_hashes[key]=hashlib.sha256(path.read_bytes()).hexdigest()
    old=set(bpy.data.objects);bpy.ops.import_scene.gltf(filepath=str(path))
    imported=set(bpy.data.objects)-old
    selected=next(o for o in imported if o.type=='MESH' and o.name==aid)
    matrix=selected.matrix_world.copy();selected.parent=arm;selected.matrix_world=matrix
    # Cape keeps its own ordinary garment skeleton and native spring chains.
    if key!='cloak':
        for mod in selected.modifiers:
            if mod.type=='ARMATURE':mod.object=arm
    sources[key]=selected
    for obj in imported:
        if obj!=selected and not (key=='cloak' and obj.type=='ARMATURE'):
            bpy.data.objects.remove(obj,do_unlink=True)
    selected.hide_set(True)

def active(obj):
    bpy.ops.object.select_all(action='DESELECT');obj.hide_set(False);obj.select_set(True)
    bpy.context.view_layer.objects.active=obj

def copy_part(key):
    original=sources[key];obj=original.copy();obj.data=original.data.copy()
    bpy.context.collection.objects.link(obj);obj.hide_set(False);return obj

def tuck_waist(inner,trousers):
    # A normal author-time waist seam; never cut sleeves with a torso volume.
    edit=bmesh.new();edit.from_mesh(inner.data)
    arms=arm_weights(inner)
    selected=[]
    for face in edit.faces:
        if sum(arms[v.index] for v in face.verts)/len(face.verts)<.35:selected.append(face)
    geom=set(selected)
    for face in selected:geom.update(face.edges);geom.update(face.verts)
    pelvis=(arm.matrix_world@arm.data.bones['pelvis'].head_local).z
    point=inner.matrix_world.inverted()@Vector((0,0,pelvis+.18))
    normal=inner.matrix_world.to_3x3().transposed()@Vector((0,0,1))
    bmesh.ops.bisect_plane(edit,geom=list(geom),plane_co=point,plane_no=normal,dist=1e-6,clear_inner=True)
    edit.to_mesh(inner.data);edit.free()
    # Tuck the seam inside the trouser waist. A vertical overlap alone leaves
    # the shirt outside the trousers; this native modifier makes a real tuck.
    torso=[i for i,weight in enumerate(arm_weights(inner)) if weight<.35]
    group=inner.vertex_groups.new(name='tucked_waist')
    for i in torso:
        z=(inner.matrix_world@inner.data.vertices[i].co).z
        weight=1-min(1,max(0,(z-pelvis-.20)/.03))
        if weight:group.add([i],weight,'REPLACE')
    active(inner)
    wrap=inner.modifiers.new('Native trouser waist tuck','SHRINKWRAP')
    wrap.target=trousers;wrap.wrap_method='NEAREST_SURFACEPOINT';wrap.wrap_mode='ON_SURFACE';wrap.offset=-.004
    wrap.vertex_group=group.name
    bpy.ops.object.modifier_move_up(modifier=wrap.name);bpy.ops.object.modifier_apply(modifier=wrap.name)
    inner.vertex_groups.remove(inner.vertex_groups['tucked_waist'])

sets=[]
for chest in ('linen','vest','jack'):
    for pants in (False,True):
        keys=[('linen' if chest=='linen' else 'vest')]+(['trousers'] if pants else [])+(['jack'] if chest=='jack' else [])
        sets.append(('frontier_'+chest+('_trousers' if pants else '')+'_set',keys,
                     REGIONS['base_shirt']+(REGIONS['trousers'] if pants else [])))
sets.extend([('frontier_cap_coif_set',['cap','coif'],['hair']),
             ('frontier_boots_sabatons_set',['boots','sabatons'],['feet']),
             ('frontier_jack_item',['vest','jack'],[])])
out=root/'combined_sources'/fit;out.mkdir(parents=True,exist_ok=True)
reports=[]
for base,keys,hidden in sets:
    objects={key:copy_part(key) for key in keys}
    if 'trousers' in objects and 'linen' in objects:
        tuck_waist(objects['linen'],objects['trousers'])
    if 'linen' in objects and 'trousers' not in objects:
        # Free-hanging hem rather than the tucked variant; native displacement
        # acts on this garment only. The underlying approved hips stay drawn.
        obj=objects['linen'];active(obj)
        pelvis=(arm.matrix_world@arm.data.bones['pelvis'].head_local).z
        group=obj.vertex_groups.new(name='free_linen_hem')
        for v in obj.data.vertices:group.add([v.index],1-min(1,max(0,(v.co.z-pelvis-.18)/.04)),'REPLACE')
        mod=obj.modifiers.new('Free linen hem ease','DISPLACE');mod.strength=.007;mod.mid_level=0;mod.vertex_group=group.name
        bpy.ops.object.modifier_move_up(modifier=mod.name);bpy.ops.object.modifier_apply(modifier=mod.name)
        obj.vertex_groups.remove(obj.vertex_groups['free_linen_hem'])
    bpy.ops.object.select_all(action='DESELECT')
    for obj in objects.values():obj.hide_set(False);obj.select_set(True)
    bpy.context.view_layer.objects.active=next(iter(objects.values()));bpy.ops.object.join()
    combined=bpy.context.object;combined.name=base
    # The completed cloth is weighted ONCE, after its cuts. Keep existing rigid
    # plate bindings; the native transfer fills new Boolean boundary vertices.
    if keys==['cap','coif']:
        combined.vertex_groups.clear();combined.vertex_groups.new(name='head').add(list(range(len(combined.data.vertices))),1,'REPLACE')
    else:
        for source_group in body_target.vertex_groups:
            if source_group.name not in combined.vertex_groups:combined.vertex_groups.new(name=source_group.name)
        metal={i for p in combined.data.polygons if combined.data.materials[p.material_index].name.startswith('frontier_lamework_metal') for i in p.vertices}
        selected=combined.vertex_groups.new(name='finished_cloth_binding')
        selected.add([v.index for v in combined.data.vertices if v.index not in metal],1,'REPLACE')
        transfer=combined.modifiers.new('Standard finished outfit body weights','DATA_TRANSFER')
        transfer.object=body_target;transfer.use_vert_data=True;transfer.data_types_verts={'VGROUP_WEIGHTS'}
        transfer.vert_mapping='POLYINTERP_NEAREST';transfer.layers_vgroup_select_src='ALL';transfer.layers_vgroup_select_dst='NAME'
        transfer.vertex_group=selected.name
        bpy.ops.object.modifier_move_up(modifier=transfer.name);bpy.ops.object.modifier_apply(modifier=transfer.name)
        combined.vertex_groups.remove(combined.vertex_groups['finished_cloth_binding'])

    lods=[]
    for level,ratio in ((1,.65),(2,.4),(3,.25)):
        lod=combined.copy();lod.data=combined.data.copy();lod.name=base+f'_LOD{level}'
        bpy.context.collection.objects.link(lod);active(lod)
        mod=lod.modifiers.new('Native outfit LOD','DECIMATE');mod.ratio=ratio;mod.delimit={'MATERIAL'}
        bpy.ops.object.modifier_apply(modifier=mod.name);lods.append(lod)
    bpy.ops.object.select_all(action='DESELECT')
    for obj in [combined,*lods,arm]:obj.select_set(True)
    bpy.ops.export_scene.gltf(filepath=str(out/(base+'_native.glb')),export_format='GLB',use_selection=True,
        export_skins=True,export_def_bones=False,export_influence_nb=8,export_animations=False)
    (out/(base+'.json')).write_text(json.dumps({'hide_regions':hidden,'source_parts':keys,'construction':'One stitched surface and native waist seam; one combined skinned outfit mesh with4LODs'},indent=2))
    reports.append({'set':base,'parts':keys,'hide_regions':hidden,'vertices':len(combined.data.vertices)})
    for obj in [combined,*lods]:obj.hide_set(True)
bpy.ops.wm.save_as_mainfile(filepath=str(out/'frontier_combined_sets.blend'))
(out/'sources.json').write_text(json.dumps({'body':body_id,'source_hashes':source_hashes,'sets':reports},indent=2))
print('COMBINED_FRONTIER_SETS',fit,len(sets))
