extends SkeletonModifier3D
## Authored Epic-retargeted right-hand pose, blended after the body animation.
## No rest, bind, bone hierarchy, body mesh or empty-hand changes.
var equipped := false
var rotations: Dictionary = {}
func _ready() -> void:
	influence=0.0
func _process(delta: float) -> void:
	influence=move_toward(influence,1.0 if equipped else 0.0,delta/.15)
func _process_modification_with_delta(_delta: float) -> void:
	var skeleton := get_skeleton()
	for bone: int in rotations:
		skeleton.set_bone_pose_rotation(bone,rotations[bone])
