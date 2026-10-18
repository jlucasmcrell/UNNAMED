"""Standard Surface Deform shape transfer and native Data Transfer skin weights."""
import bpy,bmesh,json,sys,hashlib
from pathlib import Path
from mathutils import Matrix
sys.path.insert(0,str(Path(__file__).parent))
from native_clothing import approved_surface
root=Path(r'D:\Otherreach_Scratch\frontier_native_20261018')
args=sys.argv[sys.argv.index('--')+1:];fit=args[0];parts=args[1:]
assert fit in ('veth_m','veth_f_lean','veth_m_lean')
bases={'base_shirt':'frontier_linen_shirt','trousers':'frontier_wool_trousers','vest':'frontier_sealed_vest',
 'gloves':'frontier_smith_gloves','boots':'frontier_sealed_boots','standing_cap':'frontier_standing_cap',
 'lame_coif':'frontier_lame_coif','lamework_jack':'frontier_lamework_jack','lame_sabatons':'frontier_lame_sabatons'}
for part in parts:
    base=bases[part];source=root/'sources/fits/veth_f'/part/(base+'_native.glb')
    digest=hashlib.sha256(source.read_bytes()).hexdigest()
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=str(root/'approved_clothing_sources/veth_f.glb'))
    cage=approved_surface('veth_f');cage.name='SurfaceDeform base cage'
    cage.shape_key_clear()
    for mod in list(cage.modifiers):cage.modifiers.remove(mod)
    cage.data.transform(cage.matrix_world);cage.matrix_world=Matrix.Identity(4)
    old=set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=str(root/'approved_clothing_sources'/(fit+'.glb')))
    target_arm=next(o for o in set(bpy.data.objects)-old if o.type=='ARMATURE')
    target=approved_surface(fit);target.shape_key_clear()
    for mod in list(target.modifiers):target.modifiers.remove(mod)
    target.data.transform(target.matrix_world);target.matrix_world=Matrix.Identity(4)
    assert len(cage.data.vertices)==len(target.data.vertices),f'{fit}: native cage topology differs'
    same_indices={tuple(sorted(p.vertices)) for p in cage.data.polygons}=={tuple(sorted(p.vertices)) for p in target.data.polygons}
    target_positions=[v.co.copy() for v in target.data.vertices]
    if not same_indices:
        # glTF can reorder split UV vertices. Restore native UV correspondence
        # before giving the same cage its target shape; no garment fitting here.
        def uv_keys(obj,body_count):
            uv=[set() for _ in obj.data.vertices]
            for loop in obj.data.loops:
                point=obj.data.uv_layers[0].data[loop.index].uv
                uv[loop.vertex_index].add((round(point.x,5),round(point.y,5)))
            return [(i>=body_count,tuple(sorted(value))) for i,value in enumerate(uv)]
        source_keys=uv_keys(cage,len(bpy.data.objects['native_veth_f_body'].data.vertices))
        target_keys=uv_keys(target,len(bpy.data.objects['native_'+fit+'_body'].data.vertices))
        lookup={}
        for key,vertex in zip(target_keys,target.data.vertices):lookup.setdefault(key,[]).append(vertex.co.copy())
        aligned=[]
        for vertex,key in zip(cage.data.vertices,source_keys):
            candidates=lookup.get(key)
            if not candidates:raise RuntimeError(f'{fit}: missing native UV correspondence {key}')
            # Split vertices at a UV seam can share a key; use the corresponding
            # nearby surface instead of averaging distinct native skin shells.
            aligned.append(min(candidates,key=lambda p:(p-vertex.co).length_squared))
        target_positions=aligned
        print('RESTORED_NATIVE_UV_VERTEX_ORDER',fit,flush=True)
    old=set(bpy.data.objects);bpy.ops.import_scene.gltf(filepath=str(source))
    garment=next(o for o in set(bpy.data.objects)-old if o.type=='MESH' and o.name==base)
    matrix=garment.matrix_world.copy();garment.parent=None;garment.matrix_world=matrix
    garment.shape_key_clear()
    for mod in list(garment.modifiers):garment.modifiers.remove(mod)
    bpy.ops.object.select_all(action='DESELECT');garment.select_set(True);bpy.context.view_layer.objects.active=garment
    wrap=garment.modifiers.new('Native Surface Deform body fit','SURFACE_DEFORM');wrap.target=cage
    bpy.ops.object.surfacedeform_bind(modifier=wrap.name)
    if not wrap.is_bound:raise RuntimeError('Native Surface Deform failed to bind')
    for vertex,point in zip(cage.data.vertices,target_positions):vertex.co=point
    cage.data.update();bpy.context.view_layer.update()
    bpy.ops.object.modifier_apply(modifier=wrap.name)
    garment.vertex_groups.clear()
    for group in target.vertex_groups:garment.vertex_groups.new(name=group.name)
    weights=garment.modifiers.new('Native body Data Transfer weights','DATA_TRANSFER')
    weights.object=target;weights.use_vert_data=True;weights.data_types_verts={'VGROUP_WEIGHTS'}
    weights.vert_mapping='POLYINTERP_NEAREST';weights.layers_vgroup_select_src='ALL';weights.layers_vgroup_select_dst='NAME'
    bpy.ops.object.modifier_apply(modifier=weights.name)
    if part in ('standing_cap','lame_coif'):
        garment.vertex_groups.clear();group=garment.vertex_groups.new(name='head')
        group.add(list(range(len(garment.data.vertices))),1,'REPLACE')
    elif part in ('lamework_jack','lame_sabatons'):
        # Each authored disconnected plate retains one rigid attachment.
        edit=bmesh.new();edit.from_mesh(garment.data);edit.verts.ensure_lookup_table()
        unseen=set(edit.verts);patches=[]
        while unseen:
            todo=[unseen.pop()];patch=[]
            while todo:
                vertex=todo.pop();patch.append(vertex.index)
                for edge in vertex.link_edges:
                    neighbour=edge.other_vert(vertex)
                    if neighbour in unseen:unseen.remove(neighbour);todo.append(neighbour)
            patches.append(patch)
        edit.free()
        for patch in patches:
            totals={}
            for index in patch:
                for influence in garment.data.vertices[index].groups:
                    totals[influence.group]=totals.get(influence.group,0)+influence.weight
            group=max(totals,key=totals.get)
            for old_group in garment.vertex_groups:old_group.remove(patch)
            garment.vertex_groups[group].add(patch,1,'REPLACE')
    skin=garment.modifiers.new('Approved target skeleton','ARMATURE');skin.object=target_arm
    matrix=garment.matrix_world.copy();garment.parent=target_arm;garment.matrix_world=matrix
    lods=[]
    for level,ratio in ((1,.65),(2,.4),(3,.25)):
        lod=garment.copy();lod.data=garment.data.copy();lod.name=base+f'_LOD{level}';bpy.context.collection.objects.link(lod)
        bpy.context.view_layer.objects.active=lod
        dec=lod.modifiers.new('Native clothing LOD','DECIMATE');dec.ratio=ratio;dec.delimit={'MATERIAL'}
        bpy.ops.object.modifier_apply(modifier=dec.name);lods.append(lod)
    bpy.ops.object.select_all(action='DESELECT')
    for obj in [garment,*lods,target_arm]:obj.select_set(True)
    out=root/'sources/fits'/fit/part;out.mkdir(parents=True,exist_ok=True)
    bpy.ops.export_scene.gltf(filepath=str(out/(base+'_native.glb')),export_format='GLB',use_selection=True,
        export_skins=True,export_def_bones=False,export_influence_nb=8,export_animations=False)
    bpy.ops.wm.save_as_mainfile(filepath=str(out/(base+'.blend')))
    assert hashlib.sha256(source.read_bytes()).hexdigest()==digest
    (out/'source.json').write_text(json.dumps({'source':str(source),'sha256':digest,'authored_on':'veth_f',
        'target':fit,'shape_transfer':'Blender Surface Deform','skin_transfer':'Blender Data Transfer',
        'body_edits':False,'acceptance':'PENDING visible turnaround/run review'},indent=2))
    print('AUTO_TRANSFERRED',part,fit,flush=True)
