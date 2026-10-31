extends SceneTree

var output := OS.get_environment("B043_OUT")
var main: Node

func _initialize() -> void:
	_run.call_deferred()

func button(creator: Node, id: String) -> BaseButton:
	if id.begins_with("creator.hair."):
		var choice := id.trim_prefix("creator.hair.")
		var catalogue: Dictionary = JSON.parse_string(FileAccess.get_file_as_string("res://Art/humgen_creator.json"))
		var option: Dictionary
		for item in catalogue.hair:
			if item.id == choice:
				option = item
		var label: String = creator.call("OptionName", 1, choice, option.label)
		for node in creator.find_children("*", "BaseButton", true, false):
			var parent: Node = node.get_parent()
			if parent.get_child_count() > 1 and parent.get_child(1) is Label and parent.get_child(1).text == label:
				return node
	for node in creator.find_children("*", "BaseButton", true, false):
		if node.get_meta("ui_id", "") == id:
			return node
	return null

func click(creator: Node, id: String) -> bool:
	var target := button(creator, id)
	if target == null:
		push_error("B043 FAILED: Missing actual creator card: " + id)
		quit(1)
		return false
	target.emit_signal("pressed")
	await process_frame
	await process_frame
	return true

func picture(name: String, creator: Node) -> void:
	await create_timer(2.0).timeout
	if DisplayServer.get_name() != "headless":
		await RenderingServer.frame_post_draw
		get_root().get_texture().get_image().save_png(output.path_join(name + ".png"))
	var audit := []
	for mesh in creator.find_children("*", "MeshInstance3D", true, false):
		if mesh.has_meta("creator_part_kind") or "Skin_LOD0" in str(mesh.name):
			var materials := []
			for s in range(mesh.mesh.get_surface_count()):
				var mat: Material = mesh.get_active_material(s)
				var record := {"class": mat.get_class(), "name": mat.resource_name}
				if mat is BaseMaterial3D:
					record.merge({"alpha": mat.albedo_color.a, "transparency": mat.transparency, "texture": mat.albedo_texture != null})
				materials.append(record)
			audit.append({"name": str(mesh.name), "visible": mesh.visible, "in_tree": mesh.is_visible_in_tree(), "kind": mesh.get_meta("creator_part_kind", "skin"), "materials": materials})
	var file := FileAccess.open(output.path_join(name + ".json"), FileAccess.WRITE)
	file.store_string(JSON.stringify(audit, "  "))
	var expected := name.substr(2)
	if not expected.begins_with("face"):
		var attached := false
		for mesh in audit:
			if mesh.kind == "Hair" and mesh.in_tree and mesh.visible and expected in mesh.name:
				attached = true
		if not attached:
			push_error("B043 FAILED: Selected hair not drawn: " + name)
			quit(1)
	print("B043 CARD FRAME ", name)

func _run() -> void:
	DirAccess.make_dir_recursive_absolute(output)
	main = load("res://Main.tscn").instantiate()
	root.add_child(main)
	var creator: Node
	while creator == null:
		await create_timer(1).timeout
		creator = main.get_node_or_null("Creator")
	# The same handler as the title's New game button, before title-world prewarming.
	main.call("OpenCreator")
	await create_timer(4).timeout
	creator.call("UsePeople", "people.veth", "")
	for sex in ["m", "f"]:
		creator.call("UseFace", "body.veth_" + sex + ".average", "face." + sex + ".caucasian_presets.caucasian_2")
		creator.call("Go", 3)
		creator.call("Frame", 1.0)
		await create_timer(3).timeout
		if not await click(creator, "creator.options.Hair.long"):
			return
		for hair in ["bob_long", "bun", "medium_center_part", "medium_side_part", "ponytail_short", "ponytail"]:
			if not await click(creator, "creator.hair.hair.long." + hair):
				return
			await picture(sex + "_" + hair, creator)
		creator.call("Go", 2)
		await picture(sex + "_face", creator)
		creator.call("Turn", -0.96)
		await picture(sex + "_face_three", creator)
		creator.call("Turn", 0.96)
	print("B043 complete")
	quit(0)
