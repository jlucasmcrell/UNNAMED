extends SceneTree
const Native=preload("res://MHCharacters/NativeCharacter.gd")
func _initialize():call_deferred("run")
func run():
 var source: String=OS.get_environment("OTHERREACH_MH_CHARACTERS_ROOT").replace("\\","/").trim_suffix("/")+"/"
 var catalog: Dictionary=JSON.parse_string(FileAccess.get_file_as_string(source+"catalog.json"))
 var records:=[];var failures:=[]
 for id in catalog.characters:
  var actor:=Native.new();root.add_child(actor)
  if not actor.configure(source,catalog.characters[id],{}):push_error("Failed to load "+id);quit(2);return
  actor.sample_clip("idle",.5);actor._animator.pause();actor._sync.apply_leader_pose()
  for side in ["l","r"]:
   var ankle: Vector3=actor.bone_position("foot_"+side)
   var ball: Vector3=actor.bone_position("ball_"+side)
   var direction:=ball-ankle
   var horizontal:=Vector2(direction.x,direction.z).length()
   var passed: bool=horizontal>.07 and absf(direction.y)<horizontal
   records.append({"id":id,"side":side,"clip":"idle","seconds":.5,"ankle_to_ball_m":[direction.x,direction.y,direction.z],"horizontal_m":horizontal,"passed":passed})
   if not passed:failures.append(id+" "+side+" downward foot; check Epic retarget-pose offsets")
  actor.queue_free();await process_frame
 var file:=FileAccess.open(source+"evidence/native_extremities_regression.json",FileAccess.WRITE)
 file.store_string(JSON.stringify({"status":"PASS" if failures.is_empty() else "FAIL","method":"Known downward-foot regression: idle ankle-to-ball horizontal component exceeds vertical; native skeleton and geometry read-only. Visual acceptance separate.","failures":failures,"feet":records},"  "));file.close()
 print("NATIVE_EXTREMITIES_REGRESSION failures=",failures.size());quit(0 if failures.is_empty() else 1)
