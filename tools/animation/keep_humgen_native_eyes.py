"""Keep HumGen's real recessed iris and cornea, which its glTF wrapper otherwise removes."""
import bpy
STAGE='before_export'
def main(context,human):
    eyes=human.objects.eyes
    if len(eyes.data.materials)<2: return
    inner=eyes.data.materials[1]
    # Blender's native separation preserves weights, UVs, tangents and blendshapes.
    before=set(bpy.data.objects)
    bpy.ops.object.select_all(action='DESELECT')
    eyes.select_set(True);context.view_layer.objects.active=eyes
    bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.mesh.separate(type='MATERIAL');bpy.ops.object.mode_set(mode='OBJECT')
    pieces=[eyes]+list(set(bpy.data.objects)-before)
    for obj in pieces:
        used={obj.data.materials[p.material_index] for p in obj.data.polygons}
        if inner in used:
            obj.data.materials.clear();obj.data.materials.append(inner)
            for p in obj.data.polygons:p.material_index=0
            obj['hg_eyes']=True
        else:
            if 'hg_eyes' in obj:del obj['hg_eyes']
            obj.name=eyes.name.replace('_Eyes','_Cornea')
            material=bpy.data.materials.new('humgen_native_Cornea');material.use_nodes=True
            bsdf=material.node_tree.nodes.get('Principled BSDF')
            bsdf.inputs['Base Color'].default_value=(1,1,1,1)
            bsdf.inputs['Roughness'].default_value=.02
            bsdf.inputs['Alpha'].default_value=.04
            material.surface_render_method='DITHERED'
            obj.data.materials.clear();obj.data.materials.append(material)
            for p in obj.data.polygons:p.material_index=0
    print('NATIVE_EYE_LAYERS_PRESERVED',[(o.name,len(o.data.polygons)) for o in pieces],flush=True)
