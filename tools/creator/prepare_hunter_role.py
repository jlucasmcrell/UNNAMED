"""Compose already native-fitted garments on the identical HumGen preset rig."""
import bpy,json,numpy as np
from pathlib import Path
from HumGen3D.human.human import Human
roots={'f':Path('D:/gc_roles02'),'m':Path('D:/gc_roles_m')}
for sex,root in roots.items():
    identifier='humgen_creator_'+sex+'_outfit_outfit_period_leathers'
    source=root/'fit_sources'/identifier/'native.blend'
    bpy.ops.wm.open_mainfile(filepath=str(source))
    human=Human.from_existing(bpy.data.objects.get(identifier),strict_check=False)
    assert human is not None
    rig=human.objects.rig
    for obj in list(human.clothing.outfit.objects):
        if 'Cloak' not in obj.name:bpy.data.objects.remove(obj,do_unlink=True)
    tunic=root/'fit_sources'/('humgen_creator_'+sex+'_outfit_outfit_period_tunic')/'native.blend'
    with bpy.data.libraries.load(str(tunic),link=False) as (data,loaded):
        loaded.objects=[name for name in data.objects if name in ('tunicviking','pants_wool')]
    assert len(loaded.objects)==2
    for obj in loaded.objects:
        bpy.context.scene.collection.objects.link(obj)
        source_rig=obj.parent
        assert source_rig and all(np.max(np.abs(np.array(source_rig.data.bones[bone.name].matrix_local)-np.array(bone.matrix_local)))<1e-6 for bone in rig.data.bones)
        obj.parent=rig
        for modifier in obj.modifiers:
            if modifier.type=='ARMATURE':modifier.object=rig
        if obj.name.startswith('tunicviking'):
            material=bpy.data.materials.new('Hunter_Leather_Tunic');material.use_nodes=True
            shader=material.node_tree.nodes.get('Principled BSDF')
            folder=Path('C:/Otherreach_External_Assets/MPFB_Slice_20261019/textures')
            for suffix,socket in [('Color','Base Color'),('Roughness','Roughness')]:
                node=material.node_tree.nodes.new('ShaderNodeTexImage');node.image=bpy.data.images.load(str(folder/('Leather014_4K-JPG_'+suffix+'.jpg')),check_existing=True)
                if suffix!='Color':node.image.colorspace_settings.name='Non-Color'
                material.node_tree.links.new(node.outputs['Color'],shader.inputs[socket])
            node=material.node_tree.nodes.new('ShaderNodeTexImage');node.image=bpy.data.images.load(str(folder/'Leather014_4K-JPG_NormalGL.jpg'),check_existing=True);node.image.colorspace_settings.name='Non-Color'
            normal=material.node_tree.nodes.new('ShaderNodeNormalMap');material.node_tree.links.new(node.outputs['Color'],normal.inputs['Color']);material.node_tree.links.new(normal.outputs['Normal'],shader.inputs['Normal'])
            obj.data.materials.clear();obj.data.materials.append(material)
            for face in obj.data.polygons:face.material_index=0
    destination=Path('D:/gc_hunter02/prepared')/sex/'native.blend';destination.parent.mkdir(parents=True,exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=str(destination),compress=True)
    print('HUNTER_NATIVE_COMPOSITION',sex,destination,flush=True)
