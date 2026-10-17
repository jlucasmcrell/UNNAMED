extends SceneTree
# CPU/headless: verify published cache bytes and external texture sharing, never build/write the primary package.
var _passed = false
func _initialize():
    _finish.call_deferred()
    var manifest_path = ProjectSettings.globalize_path("res://").path_join("../../docs/BARROWWORKS_SCENE_CACHE.json").simplify_path()
    var manifest = JSON.parse_string(FileAccess.get_file_as_string(manifest_path))
    for group in [manifest.outputs, manifest.shared_textures]:
        for record in group:
            assert(FileAccess.get_sha256(record.file) == record.sha256, record.file)
    var first = ""
    var custodian = ""
    for record in manifest.outputs:
        if record.file.ends_with("bld_fab_cw_house_01.glb.scn"): first = record.file
        if record.file.ends_with("bw_codex_custodian_rigged.glb.scn"): custodian = record.file
    assert(not first.is_empty() and not custodian.is_empty())
    var a = ResourceLoader.load(first, "PackedScene", ResourceLoader.CACHE_MODE_IGNORE).instantiate()
    var b = ResourceLoader.load(first, "PackedScene", ResourceLoader.CACHE_MODE_IGNORE).instantiate()
    assert(a.get_instance_id() != b.get_instance_id())
    var left = textures(a)
    var right = textures(b)
    var shared = 0
    for path in left:
        if right.has(path):
            assert(left[path].get_instance_id() == right[path].get_instance_id(), path)
            assert(left[path].get_image().is_compressed(), path)
            shared += 1
    assert(shared > 0)
    var rig = ResourceLoader.load(custodian, "PackedScene", ResourceLoader.CACHE_MODE_IGNORE).instantiate()
    var skeletons = rig.find_children("*", "Skeleton3D", true, false)
    assert(skeletons.size() == 1 and skeletons[0].get_bone_count() == 11)
    a.free(); b.free(); rig.free()
    print("BARROWWORKS_NATIVE_CACHE_PASS scenes=", manifest.outputs.size(), " textures=", manifest.shared_textures.size(), " shared_across_independent_scenes=", shared, " custodian_joints=11")
    _passed = true

func _finish():
    quit(0 if _passed else 1)

func textures(root):
    var found = {}
    for mesh in root.find_children("*", "MeshInstance3D", true, false):
        if mesh.mesh == null: continue
        for index in mesh.mesh.get_surface_count():
            var material = mesh.mesh.surface_get_material(index)
            if material is BaseMaterial3D and material.albedo_texture != null:
                var texture = material.albedo_texture
                if not texture.resource_path.is_empty(): found[texture.resource_path] = texture
    return found
