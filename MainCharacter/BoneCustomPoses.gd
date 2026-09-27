@tool
class_name BoneCustomPoses
extends SkeletonModifier3D

# Godot 3's Skeleton custom poses, which Godot 4 removed: an extra transform per bone, applied between
# the rest and the animated pose (rest * custom * pose in Godot 3, where pose was relative to the rest).

var _custom := {}  # bone index -> Transform3D

## The modifier on this skeleton, added on first use.
static func of(skeleton: Skeleton3D) -> BoneCustomPoses:
	for child in skeleton.get_children():
		if child is BoneCustomPoses:
			return child
	var poses := BoneCustomPoses.new()
	poses.name = "BoneCustomPoses"
	skeleton.add_child(poses)
	return poses

func set_bone_custom_pose(bone: int, pose: Transform3D) -> void:
	if bone >= 0:  # Godot 3 refused an unknown bone the same way, after printing an error
		_custom[bone] = pose

func get_bone_custom_pose(bone: int) -> Transform3D:
	return _custom.get(bone, Transform3D.IDENTITY)

## The bone's global pose with the custom poses applied, as Godot 3's get_bone_global_pose returned it.
## Skeleton3D.get_bone_global_pose() leaves them out: the skeleton restores the unmodified poses after each update.
func get_bone_global_pose_with_custom(bone: int) -> Transform3D:
	var skeleton := get_skeleton()
	var global := Transform3D.IDENTITY
	var chain := []
	while bone >= 0:
		chain.push_front(bone)
		bone = skeleton.get_bone_parent(bone)
	for b in chain:
		global = global * _posed(skeleton, b)
	return global

func _process_modification_with_delta(_delta: float) -> void:
	var skeleton := get_skeleton()
	for bone in _custom:
		skeleton.set_bone_pose(bone, _posed(skeleton, bone))

# Godot 4's pose is Godot 3's rest * pose, so rest * custom * pose becomes rest * custom * rest⁻¹ * pose.
func _posed(skeleton: Skeleton3D, bone: int) -> Transform3D:
	var pose := skeleton.get_bone_pose(bone)
	if not _custom.has(bone):
		return pose
	var rest := skeleton.get_bone_rest(bone)
	return rest * _custom[bone] * rest.affine_inverse() * pose
