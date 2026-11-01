extends SceneTree

func _initialize():
 var plan = JSON.parse_string(FileAccess.get_file_as_string(OS.get_cmdline_user_args()[0]))
 var proof = JSON.parse_string(FileAccess.get_file_as_string(plan.proof))
 assert(proof.same_parents and proof.joints == 59 and proof.maximum_rest_error < .00001)
 assert(FileAccess.get_sha256(proof.old_body) == proof.old_sha256)
 assert(FileAccess.get_sha256(proof.new_body) == proof.new_sha256)
 var original: AnimationLibrary = load(plan.old_motion)
 assert(original != null and original.get_meta("target_body_sha256") == proof.old_sha256)
 var result: AnimationLibrary = original.duplicate(true)
 result.set_meta("target_body_sha256", proof.new_sha256)
 assert(not FileAccess.file_exists(plan.new_motion), "Never overwrite accepted motion")
 assert(ResourceSaver.save(result, plan.new_motion) == OK)
 var readback: AnimationLibrary = ResourceLoader.load(plan.new_motion, "", ResourceLoader.CACHE_MODE_IGNORE)
 assert(readback.get_animation_list() == original.get_animation_list())
 var keys = 0
 for name_ in original.get_animation_list():
  var a = original.get_animation(name_)
  var b = readback.get_animation(name_)
  assert(a.length == b.length and a.loop_mode == b.loop_mode and a.get_track_count() == b.get_track_count())
  for track in a.get_track_count():
   assert(a.track_get_path(track) == b.track_get_path(track) and a.track_get_type(track) == b.track_get_type(track))
   assert(a.track_get_interpolation_type(track) == b.track_get_interpolation_type(track))
   assert(a.track_get_key_count(track) == b.track_get_key_count(track))
   for key in a.track_get_key_count(track):
    assert(a.track_get_key_time(track,key) == b.track_get_key_time(track,key))
    assert(a.track_get_key_value(track,key) == b.track_get_key_value(track,key))
    assert(a.track_get_key_transition(track,key) == b.track_get_key_transition(track,key))
    keys += 1
 for meta in original.get_meta_list():
  if meta != "target_body_sha256": assert(original.get_meta(meta) == readback.get_meta(meta))
 var receipt = {"status":"PASS", "original_motion_sha256":FileAccess.get_sha256(plan.old_motion),
  "new_motion_sha256":FileAccess.get_sha256(plan.new_motion), "unchanged_keys":keys, "states":original.get_animation_list().size()}
 var output = FileAccess.open(plan.new_motion + ".quality.json", FileAccess.WRITE)
 output.store_string(JSON.stringify(receipt,"  "))
 output.close()
 print("NATIVE_MOTION_REPACKAGE ",receipt)
 quit()
