"""Blender --background: open a .blend and export it as one glTF binary, Y-up, metres, embedded textures. Source untouched.
blender --background --python blend_to_glb.py -- <in.blend> <out.glb>"""
import sys
import bpy

src, dst = sys.argv[sys.argv.index("--") + 1:][:2]
bpy.ops.wm.open_mainfile(filepath=src)
meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
for o in bpy.context.scene.objects:
    o.select_set(o.type == "MESH")
print("meshes:", [(o.name, [round(v, 3) for v in o.dimensions]) for o in meshes])
bpy.ops.export_scene.gltf(filepath=dst, export_format="GLB", use_selection=True, export_yup=True, export_apply=True)
