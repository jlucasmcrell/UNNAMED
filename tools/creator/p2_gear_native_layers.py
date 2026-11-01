"""Read the accepted current native Creator underlayers for garment authoring."""
import bpy
from HumGen3D.backend.preferences.preference_func import get_prefs
from pathlib import Path


def fitted_native_surface(root,identifier):
    """Read the actual worn A-pose from its saved native authoring scene."""
    import json,hashlib
    folder=root/'primary/rigged'/identifier
    contract=json.loads((folder/'creator_part.json').read_text())
    assert contract['native_rest_error']==0
    assert not any(contract['fit_audit']['reference_static_triangle_intersections'])
    scene=root/'sources'/identifier/'native_outfit.blend'
    before=set(bpy.data.objects)
    with bpy.data.libraries.load(str(scene),link=False) as (source,loaded):
        names=tuple(source.objects);loaded.objects=list(names)
    for obj in loaded.objects:
        if obj is not None:bpy.context.scene.collection.objects.link(obj)
    garment=next(obj for name,obj in zip(names,loaded.objects) if name==identifier.split('__')[0])
    bpy.context.view_layer.update();deps=bpy.context.evaluated_depsgraph_get()
    mesh=bpy.data.meshes.new_from_object(garment.evaluated_get(deps),depsgraph=deps)
    mesh.transform(garment.matrix_world)
    for obj in set(bpy.data.objects)-before:bpy.data.objects.remove(obj,do_unlink=True)
    clean=bpy.data.objects.new('P2_actual_reference_'+identifier,mesh);bpy.context.collection.objects.link(clean)
    return clean,dict(identifier=identifier,authoring=str(scene),contract_sha256=hashlib.sha256((folder/'creator_part.json').read_bytes()).hexdigest())

def default_office(human,work,sex):
    short='f' if sex=='female' else 'm'
    identifier='humgen_creator_'+short+'_outfit_outfit_office_new_intern'
    source=work/'native_wardrobe/rigged'/identifier/(identifier+'_rigged.glb')
    # Exported pieces are in the Godot T-pose bind. Their PBR maps are reusable;
    # their posed coordinates are not an authoring reference for an A-pose human.
    before=set(bpy.data.objects);bpy.ops.import_scene.gltf(filepath=str(source))
    imported=set(bpy.data.objects)-before
    def kind(name):
        name=name.lower()
        return 'pants' if any(n in name for n in ('jeans','chinos','pants','trousers')) else 'sweater' if 'sweater' in name else 'shirt'
    materials={kind(o.name):list(o.data.materials) for o in imported if o.type=='MESH' and o.name.endswith('_LOD0')}
    assert set(materials)=={'pants','shirt','sweater'}
    for obj in imported:bpy.data.objects.remove(obj,do_unlink=True)
    # Use HumGen's normal library fit on an isolated copy of the CURRENT body.
    # The source body, pose, controls and previously authored gear stay unchanged.
    copy=human.duplicate(context=bpy.context)
    copy.clothing.outfit.remove();copy.clothing.footwear.remove()
    path='outfits/'+sex+'/Office/New_Intern.blend'
    copy.clothing.outfit.set(path,context=bpy.context)
    bpy.context.view_layer.update();deps=bpy.context.evaluated_depsgraph_get();pieces=[]
    for obj in list(copy.clothing.outfit.objects):
        mesh=bpy.data.meshes.new_from_object(obj.evaluated_get(deps),depsgraph=deps)
        mesh.transform(obj.matrix_world)
        maps=materials[kind(obj.name)]
        assert len(maps)==1,'Expected one native PBR material per wardrobe piece'
        mesh.materials.clear();mesh.materials.append(maps[0])
        for face in mesh.polygons:face.material_index=0
        clean=bpy.data.objects.new('P2_reference_'+obj.name,mesh)
        bpy.context.scene.collection.objects.link(clean);pieces.append(clean)
    copy.delete()
    pants=[o for o in pieces if any(n in o.name.lower() for n in ('jeans','chinos','pants','trousers'))]
    assert len(pants)==1,[(o.name) for o in pieces]
    upper=[o for o in pieces if o not in pants]
    assert len(upper)==2,'Expected the accepted shirt and sweater'
    hem=min(v.co.z for o in upper for v in o.data.vertices if abs(v.co.x)<.25)
    print('P2_CURRENT_OFFICE_LAYERS',sex,'shirt_hem',hem,'trouser_top',max(v.co.z for v in pants[0].data.vertices),flush=True)
    return pants[0],upper,hem,Path(get_prefs().filepath)/path
