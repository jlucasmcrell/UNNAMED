extends SceneTree
## Retargeting metadata only. Never apply Rest Fixer or remove unmapped bones.
func _init() -> void:
	var map := BoneMap.new()
	map.profile=SkeletonProfileHumanoid.new()
	var pairs := {"Root":"root","Hips":"pelvis","Spine":"spine_01","Chest":"spine_03","UpperChest":"spine_05","Neck":"neck_01","Head":"head"}
	for side in ["Left","Right"]:
		var suffix := "_l" if side=="Left" else "_r"
		for limb in {"Shoulder":"clavicle","UpperArm":"upperarm","LowerArm":"lowerarm","Hand":"hand","UpperLeg":"thigh","LowerLeg":"calf","Foot":"foot","Toes":"ball"}:
			pairs[side+limb]={"Shoulder":"clavicle","UpperArm":"upperarm","LowerArm":"lowerarm","Hand":"hand","UpperLeg":"thigh","LowerLeg":"calf","Foot":"foot","Toes":"ball"}[limb]+suffix
	for name_ in pairs:
		assert(map.profile.find_bone(name_)>=0,"Unknown humanoid profile name "+name_)
		map.set_skeleton_bone_name(name_,pairs[name_])
	assert(ResourceSaver.save(map,"res://MHSlice/NativeBodyRetargetMap.tres")==OK)
	print("RETARGET_METADATA_ONLY ",pairs.size()," native names; no skeleton/rest mutations")
	quit()
