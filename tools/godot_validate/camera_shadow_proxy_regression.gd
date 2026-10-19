# Build Presentation (Debug), then run the mono editor headless with:
# --path src/Presentation --script <absolute path to this file>
extends SceneTree

func _initialize():
	call_deferred("run")

func run():
	var body = load("res://Art/SkinnedModel.cs").new()
	root.add_child(body)
	var visible = MeshInstance3D.new()
	visible.name = "garment"
	visible.mesh = BoxMesh.new()
	body.add_child(visible)
	var proxy = MeshInstance3D.new()
	proxy.name = "body_shadow"
	proxy.mesh = BoxMesh.new()
	proxy.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_SHADOWS_ONLY
	body.add_child(proxy)
	for turn in range(3):
		body.SetShadowOnly(true)
		if visible.cast_shadow != GeometryInstance3D.SHADOW_CASTING_SETTING_SHADOWS_ONLY:
			fail("the first-person body is visible")
			return
		body.SetShadowOnly(false)
		if visible.cast_shadow != GeometryInstance3D.SHADOW_CASTING_SETTING_ON:
			fail("the third-person garment stays hidden")
			return
		if proxy.cast_shadow != GeometryInstance3D.SHADOW_CASTING_SETTING_SHADOWS_ONLY:
			fail("the shadow proxy has become a visible outfit")
			return
	print("CAMERA_SHADOW_PROXY_REGRESSION PASS: repeated first/third-person toggles preserve the shadow-only body")
	body.free()
	quit(0)

func fail(reason: String):
	push_error("CAMERA_SHADOW_PROXY_REGRESSION FAIL: " + reason)
	quit(1)
