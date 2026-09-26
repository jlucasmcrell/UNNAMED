"""Inspect the six animated-armour clip GLBs: armature, actions, channels, frame ranges.
Run: blender -b --factory-startup -P inspect_clips.py -- <clip.glb> ...
"""
import bpy, sys

argv = sys.argv[sys.argv.index("--") + 1:]
for path in argv:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=path)
    print("=== CLIP", path)
    for ob in bpy.data.objects:
        print("  OBJ", ob.name, ob.type, "loc", tuple(round(v, 3) for v in ob.location),
              "rot", tuple(round(v, 3) for v in ob.rotation_euler), "scale", tuple(round(v, 3) for v in ob.scale))
    arm = next((o for o in bpy.data.objects if o.type == "ARMATURE"), None)
    if arm:
        for b in arm.data.bones[:4]:
            print("   bone", b.name, tuple(round(v, 3) for v in b.head_local), tuple(round(v, 3) for v in b.tail_local))
        print("   nbones", len(arm.data.bones))
    for a in bpy.data.actions:
        paths = set()
        try:
            for layer in a.layers:
                for strip in layer.strips:
                    for cb in strip.channelbags:
                        for fc in cb.fcurves:
                            paths.add(fc.data_path.split('"')[1] if '"' in fc.data_path else fc.data_path)
        except Exception as e:
            for fc in getattr(a, "fcurves", []):
                paths.add(fc.data_path)
        print("  ACTION", a.name, "range", tuple(a.frame_range), "bones", len(paths), sorted(paths)[:25])
    print("  fps", bpy.context.scene.render.fps)
