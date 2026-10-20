extends Node
class_name MHSliceLeaderPose
## Body-to-face pose sync from ibrews/MetaHumanGodot release.gd.
## Native rigs, bone names, rest transforms and skin binds remain unchanged.
## Source and MIT license: Tests/MetaHumanGodot_LICENSE.txt and Tests/SOURCE.txt.

@export var body: Skeleton3D
@export var face: Skeleton3D
var _pairs: Array = []
var animation_driven := false
var update_pending := true

func _ready() -> void:
	process_priority = 100
	assert(body != null and face != null and body != face)
	assert(body.find_bone("calf_l") >= 0)
	assert(face.find_bone("FACIAL_L_Eye") >= 0)
	for bi in range(body.get_bone_count()):
		var fi: int = face.find_bone(body.get_bone_name(bi))
		if fi < 0:
			continue
		var rest_xfer: Transform3D = body.get_bone_global_rest(bi).affine_inverse() \
			* face.get_bone_global_rest(fi)
		_pairs.append([bi, fi, face.get_bone_parent(fi), rest_xfer])
	_pairs.sort_custom(func(a, b): return a[1] < b[1])
	assert(not _pairs.is_empty(), "No shared native body/face bones")
	print("[mh-slice] leader-pose shared bones: %d" % _pairs.size())

func _process(_delta: float) -> void:
	if not animation_driven or update_pending:
		apply_leader_pose()
		update_pending = false

func apply_leader_pose() -> void:
	var pfg_cache := {}
	for pair in _pairs:
		var bi: int = pair[0]
		var fi: int = pair[1]
		var fpar: int = pair[2]
		var pfg: Transform3D = body.get_bone_global_pose(bi) * (pair[3] as Transform3D)
		pfg_cache[fi] = pfg
		var parent_g: Transform3D = Transform3D.IDENTITY
		if fpar >= 0:
			parent_g = pfg_cache[fpar] if pfg_cache.has(fpar) else face.get_bone_global_pose(fpar)
		var local: Transform3D = parent_g.affine_inverse() * pfg
		face.set_bone_pose_position(fi, local.origin)
		face.set_bone_pose_rotation(fi, local.basis.get_rotation_quaternion())
		face.set_bone_pose_scale(fi, local.basis.get_scale())
