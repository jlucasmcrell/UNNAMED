extends SceneTree
const LeaderPose = preload("../LeaderPose.gd")

func _init() -> void:
	call_deferred("_run")

func _make_rig(names: Array, parents: Array, offsets: Array) -> Skeleton3D:
	var rig := Skeleton3D.new()
	for i in range(names.size()):
		rig.add_bone(names[i])
		rig.set_bone_parent(i, parents[i])
		rig.set_bone_rest(i, Transform3D(Basis.IDENTITY, offsets[i]))
	rig.reset_bone_poses()
	return rig

func _rest_snapshot(rig: Skeleton3D) -> Array:
	var data := []
	for i in range(rig.get_bone_count()):
		data.append([rig.get_bone_name(i), rig.get_bone_parent(i), rig.get_bone_rest(i)])
	return data

func _run() -> void:
	var host := Node3D.new()
	root.add_child(host)
	var body := _make_rig(["root", "neck_01", "head", "calf_l"], [-1, 0, 1, 0],
		[Vector3.ZERO, Vector3(0, 1.4, 0), Vector3(0, 0.2, 0), Vector3(0.1, 0.5, 0)])
	var face := _make_rig(["root", "neck_01", "head", "FACIAL_L_Eye"], [-1, 0, 1, 2],
		[Vector3.ZERO, Vector3(0, 1.42, 0), Vector3(0, 0.21, 0), Vector3(0.03, 0.05, 0.08)])
	host.add_child(body)
	host.add_child(face)
	var before_body := _rest_snapshot(body)
	var before_face := _rest_snapshot(face)
	var sync = LeaderPose.new()
	sync.body = body
	sync.face = face
	host.add_child(sync)
	var max_error := 0.0
	for degrees in [-75.0, -45.0, 0.0, 45.0, 75.0]:
		body.set_bone_pose_rotation(1, Quaternion(Vector3.UP, deg_to_rad(degrees)))
		body.set_bone_pose_rotation(2, Quaternion(Vector3.RIGHT, deg_to_rad(degrees * 0.6)))
		sync.apply_leader_pose()
		for name in ["root", "neck_01", "head"]:
			var bi := body.find_bone(name)
			var fi := face.find_bone(name)
			var expected := body.get_bone_global_pose(bi) * body.get_bone_global_rest(bi).affine_inverse() * face.get_bone_global_rest(fi)
			var actual := face.get_bone_global_pose(fi)
			max_error = max(max_error, expected.origin.distance_to(actual.origin))
			assert(expected.is_equal_approx(actual), "Leader pose mismatch: " + name)
	assert(before_body == _rest_snapshot(body), "Body rest skeleton changed")
	assert(before_face == _rest_snapshot(face), "Face rest skeleton changed")
	print("PASS leader-pose native math: 5 extreme neck/head poses; rest bones/names/parents unchanged; max_origin_error_m=", max_error)
	print("This math check is NOT G1: it contains no character boundary vertices or game render.")
	host.queue_free()
	quit(0)
