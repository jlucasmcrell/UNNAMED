extends SceneTree
## Run with Godot --headless --script <absolute path to this file>.

func _initialize() -> void:
 var packager = load(get_script().resource_path.get_base_dir().path_join("build_humgen_motion.gd"))
 for stature in [.6, .7, 1.0]:
  assert(packager.torso_is_upright(stature*.7,stature,false))
  assert(not packager.torso_is_upright(stature*.4,stature,false))
  assert(packager.torso_is_upright(stature*.3,stature,true))
  assert(not packager.torso_is_upright(stature*.2,stature,true))
 # Sel's native sprint: an upright short torso incorrectly failed the old 45 cm cutoff.
 assert(packager.torso_is_upright(.4355388879776,.63087677955627,false))
 assert(not packager.torso_is_upright(.4355388879776,0,false))
 print("NATIVE_TORSO_GUARD_SCALE_INVARIANCE_PASS")
 quit()
