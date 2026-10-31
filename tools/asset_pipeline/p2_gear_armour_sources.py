"""Prepare original armour surfaces for icons and later native HumGen fitting.

Blender: -- <manifest> <private-art-root> [review-id ...]
This stage exports static source surfaces only; it does not attach old skins.
"""
import bpy,bmesh,json,sys,hashlib
from pathlib import Path
from mathutils import Vector

args=sys.argv[sys.argv.index('--')+1:]
root=Path(args[1]);only=set(args[2:])
rows=[r for r in json.loads(Path(args[0]).read_text()) if r['item_id'].startswith('item.armor.') and (not only or r['review_id'] in only)]

def native_pbr(obj,source):
    key=next(k for k in ('leather','heavy','scale','plate') if k in source.parent.parent.parent.name)
    folder=source.parent.parent/'textures'
    for slot in obj.material_slots:
        old=slot.material
        if not old:continue
        prefix=None
        if key=='leather':prefix='1001'
        elif key=='scale':prefix='Boots_and_Gloves' if 'Boots' in obj.name or 'Gloves' in obj.name else 'Helm' if 'Helm' in obj.name else 'Armor'
        elif key=='plate' or obj.name=='Platemail_gloves':prefix='Knight'
        else:
            for node in old.node_tree.nodes if old.use_nodes else []:
                if node.type=='TEX_IMAGE' and node.image:
                    filename=Path(node.image.filepath.replace('\\','/')).name
                    if filename.endswith('_BaseColor.png'):prefix=filename[:-14];break
        assert prefix,'Unknown original material: '+obj.name
        maps=Path(r'C:\Otherreach_External_Assets\Sourced_Clothing_20261019\devonlux_5_piece_platemail\extracted\textures') if prefix=='Knight' else folder
        def find(*suffixes):return next((maps/(prefix+s) for s in suffixes if (maps/(prefix+s)).exists()),None)
        mat=bpy.data.materials.new('P2_Native_'+prefix);mat.use_nodes=True
        nodes,links=mat.node_tree.nodes,mat.node_tree.links;shader=nodes.get('Principled BSDF')
        def texture(path,data=False):
            assert path,'Missing native map '+str(maps)
            node=nodes.new('ShaderNodeTexImage');node.image=bpy.data.images.load(str(path),check_existing=True)
            if data:node.image.colorspace_settings.name='Non-Color'
            return node
        links.new(texture(find('_baseColor.jpeg','_BaseColor.jpeg','_BaseColor.png','_Base_Color.png')).outputs['Color'],shader.inputs['Base Color'])
        normal=find('_normal.jpeg','_Normal.png','_Normal_DirectX.png')
        if normal:
            image=texture(normal,True)
            if 'DirectX' in normal.name:
                # Convert the incoming normal map on a private copy, leaving the held source intact.
                import numpy as np
                image.image=image.image.copy();pixels=np.array(image.image.pixels[:],dtype=np.float32).reshape(-1,4);pixels[:,1]=1-pixels[:,1]
                image.image.pixels.foreach_set(pixels.reshape(-1));image.image.update();image.image.pack()
            bump=nodes.new('ShaderNodeNormalMap');links.new(image.outputs['Color'],bump.inputs['Color']);links.new(bump.outputs['Normal'],shader.inputs['Normal'])
        packed=find('_metallicRoughness.png','_MetallicRoughness.png')
        if packed:
            split=nodes.new('ShaderNodeSeparateColor');links.new(texture(packed,True).outputs['Color'],split.inputs['Color'])
            links.new(split.outputs['Green'],shader.inputs['Roughness']);links.new(split.outputs['Blue'],shader.inputs['Metallic'])
        else:
            for suffix,socket in [('_Roughness.png','Roughness'),('_Metallic.png','Metallic')]:
                file=find(suffix)
                if file:links.new(texture(file,True).outputs['Color'],shader.inputs[socket])
        slot.material=mat

for row in rows:
    id=row['art_id'];folder=root/'ready'/id
    if (folder/(id+'.json')).exists():continue
    source=Path(row['source'])
    bpy.ops.wm.read_factory_settings(use_empty=True)
    if source.suffix=='.fbx':
        bpy.ops.import_scene.fbx(filepath=str(source));obj=bpy.data.objects.get(row['source_object']);assert obj
        native_pbr(obj,source)
    elif source.suffix=='.blend':
        bpy.ops.wm.open_mainfile(filepath=str(source))
        meshes=[o for o in bpy.context.scene.objects if o.type=='MESH' and not o.hide_render]
        assert meshes
        bpy.ops.object.select_all(action='DESELECT')
        for o in meshes:o.select_set(True)
        bpy.context.view_layer.objects.active=meshes[0]
        if len(meshes)>1:bpy.ops.object.join()
        obj=meshes[0]
    else:raise ValueError(str(source))
    deps=bpy.context.evaluated_depsgraph_get();mesh=bpy.data.meshes.new_from_object(obj.evaluated_get(deps),depsgraph=deps)
    mesh.transform(obj.matrix_world);clean=bpy.data.objects.new(id,mesh);bpy.context.scene.collection.objects.link(clean)
    # A06 has an accidental isolated strap below the outfit. Remove only loose
    # components separated vertically from the main garment by over half a metre.
    removed=0
    if row['review_id']=='A06':
        edit=bmesh.new();edit.from_mesh(mesh);remaining=set(edit.verts);components=[]
        while remaining:
            todo=[remaining.pop()];component=set(todo)
            while todo:
                vertex=todo.pop()
                for edge in vertex.link_edges:
                    neighbour=edge.other_vert(vertex)
                    if neighbour in remaining:remaining.remove(neighbour);component.add(neighbour);todo.append(neighbour)
            components.append(component)
        main=max(components,key=len);bottom=min(v.co.z for v in main)
        discard=[v for c in components if max(v.co.z for v in c)<bottom-.5 for v in c]
        removed=len(discard)
        if discard:bmesh.ops.delete(edit,geom=discard,context='VERTS')
        edit.to_mesh(mesh);edit.free()
    for other in list(bpy.data.objects):
        if other!=clean:bpy.data.objects.remove(other,do_unlink=True)
    clean.select_set(True);bpy.context.view_layer.objects.active=clean
    folder.mkdir(parents=True,exist_ok=True)
    bpy.ops.export_scene.gltf(filepath=str(folder/(id+'.glb')),export_format='GLB',use_selection=True,export_animations=False,export_skins=False,export_yup=True)
    rec=dict(asset_id=id,source=str(source),source_object=row.get('source_object'),source_sha256=hashlib.sha256(source.read_bytes()).hexdigest(),review_id=row['review_id'],loose_vertices_removed=removed,status='PRIVATE_STATIC_SOURCE_NOT_A_HUMGEN_FIT')
    (folder/(id+'.json')).write_text(json.dumps(rec,indent=2))
    print('P2_ARMOUR_SOURCE_PREPARED',row['review_id'],id,flush=True)
