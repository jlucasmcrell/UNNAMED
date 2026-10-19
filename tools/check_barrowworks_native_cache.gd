extends SceneTree
# CPU/headless: verify published cache bytes and external texture sharing, never build/write the primary package.
var _passed = false
func _initialize():
    _finish.call_deferred()
    var manifest_path = ProjectSettings.globalize_path("res://").path_join("../../docs/BARROWWORKS_SCENE_CACHE.json").simplify_path()
    var args = OS.get_cmdline_user_args()
    var option = args.find("--manifest")
    if option >= 0:
        assert(option + 1 < args.size(), "--manifest requires a path")
        manifest_path = args[option + 1]
    var manifest = JSON.parse_string(FileAccess.get_file_as_string(manifest_path))
    for group in [manifest.outputs, manifest.shared_textures]:
        for record in group:
            assert(FileAccess.get_sha256(record.file) == record.sha256, record.file)
    var first = ""
    var custodian = ""
    var interior_samples = []
    for record in manifest.outputs:
        # The new accepted exterior need not request that retired house model.
        # Two independent instances of an actual interior prove texture sharing.
        if record.file.ends_with("bw_codex_room_12.glb.scn"): first = record.file
        if record.file.ends_with("bw_codex_custodian_rigged.glb.scn"): custodian = record.file
        if record.file.ends_with("bw_codex_room_12.glb.scn") or record.file.ends_with("bw_codex_entrance_drain.glb.scn"):
            interior_samples.append(record.file)
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
    assert(interior_samples.size() == 2)
    var compressed_maps = compressed_material_maps(rig)
    for path in interior_samples:
        var specimen = ResourceLoader.load(path, "PackedScene", ResourceLoader.CACHE_MODE_IGNORE).instantiate()
        compressed_maps += compressed_material_maps(specimen)
        specimen.free()
    a.free(); b.free(); rig.free()
    print("BARROWWORKS_NATIVE_CACHE_PASS scenes=", manifest.outputs.size(), " textures=", manifest.shared_textures.size(), " shared_across_independent_scenes=", shared, " custodian_joints=11 compressed_interior_maps=", compressed_maps)
    _passed = true

func _finish():
    quit(0 if _passed else 1)

func compressed_material_maps(root):
    var count = 0
    for mesh in root.find_children("*", "MeshInstance3D", true, false):
        if mesh.mesh == null: continue
        for surface in mesh.mesh.get_surface_count():
            var material = mesh.mesh.surface_get_material(surface)
            if not material is BaseMaterial3D: continue
            for slot in BaseMaterial3D.TEXTURE_MAX:
                var texture = material.get_texture(slot)
                if texture == null: continue
                assert(not texture.resource_path.is_empty(), mesh.name)
                assert(texture.get_image().is_compressed(), texture.resource_path)
                count += 1
    assert(count > 0, root.name)
    return count

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
