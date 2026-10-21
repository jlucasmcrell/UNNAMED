import bpy, json, hashlib
from pathlib import Path
from mathutils import Matrix
source = Path(r'C:\Otherreach_External_Assets\Sourced_20261018\Vegetation_Weapons_Followup\Godot_Ready\CC0\ornate_medieval_dagger.glb')
out = Path(r'D:\Otherreach_Scratch\rcd5_20261021\shared_fallback\ready\weapon_cc0_dagger')
out.mkdir(parents=True, exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=str(source))
for obj in list(bpy.data.objects):
    if 'scabbard' in obj.name.lower():
        bpy.data.objects.remove(obj, do_unlink=True)
socket = bpy.data.objects.new('SOCK_grip_primary', None)
bpy.context.collection.objects.link(socket)
# Source blade points +Y in glTF (+Z in Blender). Grip sits midway along handle.
socket.matrix_world = Matrix(((0,0,1,0),(0,-1,0,0),(1,0,0,-.035),(0,0,0,1)))
bpy.ops.export_scene.gltf(filepath=str(out/'weapon_cc0_dagger.glb'), export_format='GLB', export_yup=True)
(out/'weapon_cc0_dagger_meta.json').write_text(json.dumps({
    'id':'weapon_cc0_dagger','author':'Sofia Pahaoja','license':'CC0',
    'source':'https://polyhaven.com/a/ornate_medieval_dagger',
    'input_sha256':hashlib.sha256(source.read_bytes()).hexdigest(),
    'changes':'Removed separate scabbard; added standard grip socket. Authored dimensions and PBR preserved.'
},indent=2),encoding='utf-8')
