extends SceneTree
# CPU/editor-only BC7 build. Source PNGs and their dimensions/packed channels are unchanged.
func _initialize():
    var primary = "C:/Otherreach_Recovery/Otherreach_Playtest_RESTORE_20261015b/primary"
    var output = "D:/Otherreach_Depot/barrowworks_codex/barrowworks_terrain/texture_cache"
    DirAccess.make_dir_recursive_absolute(output)
    var layers = JSON.parse_string(FileAccess.get_file_as_string(primary + "/terrain_layers.json"))
    var entries = {}
    for layer in layers:
        for suffix in ["_albedo_height", "_normal_rough"]:
            var relative = "materials/" + layer.id + "/" + layer.id + suffix + ".png"
            var source = primary + "/" + relative
            var image = Image.load_from_file(source)
            if image == null: quit(1); return
            var dimensions = image.get_size()
            image.convert(Image.FORMAT_RGBA8)
            image.generate_mipmaps()
            var result = image.compress(Image.COMPRESS_BPTC, Image.COMPRESS_SOURCE_GENERIC)
            if result != OK: push_error("BC7 failed: " + source); quit(2); return
            var name = layer.id + suffix + ".dds"
            if image.save_dds(output + "/" + name) != OK: quit(3); return
            var file = FileAccess.open(source, FileAccess.READ)
            var length = file.get_length()
            var bytes = file.get_buffer(mini(65536, length))
            if length > 65536:
                var tail = mini(65536, length - bytes.size())
                file.seek(length - tail)
                bytes.append_array(file.get_buffer(tail))
            var hash = HashingContext.new()
            hash.start(HashingContext.HASH_SHA256); hash.update(bytes)
            entries[relative] = {"dds": name, "size": length, "sig": hash.finish().hex_encode().left(24).to_upper(), "role": "data"}
            print("PACKED_TERRAIN_BC7 ", relative, " ", dimensions, " ", image.get_data().size(), " bytes")
    var manifest = FileAccess.open(output + "/manifest.json", FileAccess.WRITE)
    manifest.store_string(JSON.stringify({"comment": "Signed full-resolution packed terrain BC7 copies, editor-built only.", "entries": entries}, "  "))
    quit(0)
