"""Native MakeHuman rest fitting followed by HumGen custom-content skinning.

Only garment objects are changed. Source body and shipping HumGen skeleton are
never exported or modified by this helper.
"""
import json
import sys
from pathlib import Path
import bpy
from mathutils import Vector,Matrix
services=next(module for name,module in sys.modules.items() if name.endswith('.mpfb.services') and hasattr(module,'HumanService'))
HumanService,TargetService=services.HumanService,services.TargetService

def unity_armour(obj,path):
    """Rebuild the source Unity Standard material's authored texture channels."""
    folder=path.parents[2]/'Texture/Armor'
    assert folder.is_dir(),str(folder)
    mat=bpy.data.materials.new('GateC_Knight_Authored_Armor');mat.use_nodes=True
    nodes,links=mat.node_tree.nodes,mat.node_tree.links
    shader=nodes.get('Principled BSDF')
    def texture(name,linear=False):
        node=nodes.new('ShaderNodeTexImage');node.image=bpy.data.images.load(str(folder/name),check_existing=True)
        if linear:node.image.colorspace_settings.name='Non-Color'
        assert node.image.size[0]>=1024,'Native armour texture not loaded: '+name
        return node
    color=texture('T_Armor_Base_color_1.tga')
    links.new(color.outputs['Color'],shader.inputs['Base Color'])
    packed=texture('T_Armor_MetallicSmoothness.tga',True)
    separate=nodes.new('ShaderNodeSeparateColor');links.new(packed.outputs['Color'],separate.inputs[0])
    links.new(separate.outputs['Red'],shader.inputs['Metallic'])
    rough=nodes.new('ShaderNodeMath');rough.operation='SUBTRACT';rough.inputs[0].default_value=1
    links.new(packed.outputs['Alpha'],rough.inputs[1]);links.new(rough.outputs[0],shader.inputs['Roughness'])
    normal=texture('T_Armor_Normal.tga',True);mapping=nodes.new('ShaderNodeNormalMap')
    links.new(normal.outputs['Color'],mapping.inputs['Color']);links.new(mapping.outputs['Normal'],shader.inputs['Normal'])
    obj.data.materials.clear();obj.data.materials.append(mat)
    for polygon in obj.data.polygons:polygon.material_index=0


def fit(human, entry, sex):
    receipts=[]
    if entry.get('native_base'):
        print('ROLE_BASE_START',entry['native_base'][sex],flush=True)
        human.clothing.outfit.set(entry['native_base'][sex])
    for source in entry['sources']:
        print('ROLE_SOURCE_START',source,flush=True)
        path=Path(source)
        before=set(bpy.data.objects)
        if path.suffix.lower()=='.mhclo':
            macros=TargetService.get_default_macro_info_dict()
            macros['gender']=0.0 if sex=='f' else 1.0
            base=HumanService.create_human(macro_detail_dict=macros)
            garment=HumanService.add_mhclo_asset(str(path),base,subdiv_levels=0,set_up_rigging=False)
            bpy.context.view_layer.update()
            deps=bpy.context.evaluated_depsgraph_get()
            evaluated=garment.evaluated_get(deps)
            mesh=bpy.data.meshes.new_from_object(evaluated,depsgraph=deps)
            obj=bpy.data.objects.new(path.stem,mesh)
            bpy.context.scene.collection.objects.link(obj)
            obj.matrix_world=garment.matrix_world.copy()
            points=[base.matrix_world@Vector(c) for c in base.bound_box]
            source_height=max(p.z for p in points)-min(p.z for p in points)
            obj.scale*=float(human.height.meters)/source_height
        elif path.suffix.lower() in ('.fbx','.glb'):
            if path.suffix.lower()=='.fbx':bpy.ops.import_scene.fbx(filepath=str(path))
            else:bpy.ops.import_scene.gltf(filepath=str(path))
            imported=[o for o in set(bpy.data.objects)-before if o.type=='MESH']
            if not imported:raise ValueError('Source contains no garment: '+str(path))
            for piece in imported:
                piece.matrix_world=piece.matrix_world.copy()
                world=piece.matrix_world.copy();piece.parent=None;piece.matrix_world=world
                piece.modifiers.clear();piece.vertex_groups.clear()
            bpy.ops.object.select_all(action='DESELECT')
            for piece in imported:piece.select_set(True)
            bpy.context.view_layer.objects.active=imported[0]
            bpy.ops.object.join();obj=imported[0]
            if 'Modular_Fantasy_knight_character' in str(path):unity_armour(obj,path)
            # Source garments are authored in metres on a 1.8 m mannequin.
            # Transform only the incoming garment; native human/rest stay intact.
            scale=float(human.height.meters)/entry.get('source_height',1.8)
            obj.matrix_world=Matrix.Scale(scale,4)@obj.matrix_world
        else:raise ValueError('Unsupported source format: '+str(path))
        for old in set(bpy.data.objects)-before:
            if old is not obj:bpy.data.objects.remove(old,do_unlink=True)
        if (entry.get('leather_apron') and 'apron' in path.stem.lower()) or (entry.get('leather_tunic') and 'tunic' in path.stem.lower()):
            folder=Path('C:/Otherreach_External_Assets/MPFB_Slice_20261019/textures')
            material=bpy.data.materials.new('Smith_Leather_Apron');material.use_nodes=True
            shader=material.node_tree.nodes.get('Principled BSDF')
            for filename,socket in [('Leather014_4K-JPG_Color.jpg','Base Color'),('Leather014_4K-JPG_Roughness.jpg','Roughness')]:
                image=bpy.data.images.load(str(folder/filename),check_existing=True)
                node=material.node_tree.nodes.new('ShaderNodeTexImage');node.image=image
                if socket!='Base Color':image.colorspace_settings.name='Non-Color'
                material.node_tree.links.new(node.outputs['Color'],shader.inputs[socket])
            image=bpy.data.images.load(str(folder/'Leather014_4K-JPG_NormalGL.jpg'),check_existing=True);image.colorspace_settings.name='Non-Color'
            node=material.node_tree.nodes.new('ShaderNodeTexImage');node.image=image
            normal=material.node_tree.nodes.new('ShaderNodeNormalMap')
            material.node_tree.links.new(node.outputs['Color'],normal.inputs['Color'])
            material.node_tree.links.new(normal.outputs['Normal'],shader.inputs['Normal'])
            obj.data.materials.clear();obj.data.materials.append(material)
            for face in obj.data.polygons:face.material_index=0
        bpy.ops.object.select_all(action='DESELECT')
        obj.select_set(True);bpy.context.view_layer.objects.active=obj
        bpy.ops.object.transform_apply(location=True,rotation=True,scale=True)
        method=human.clothing.outfit.add_obj(obj,entry.get('region','full'),recalculate_weights=True)
        if method=='closest_point':raise RuntimeError('Native skin solver failed: '+str(path))
        receipts.append(dict(source=str(path),solver=method,vertices=len(obj.data.vertices)))
        print('ROLE_NATIVE_FIT',json.dumps(receipts[-1]),flush=True)
    return receipts
