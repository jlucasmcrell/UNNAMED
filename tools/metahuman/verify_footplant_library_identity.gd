extends SceneTree
## Contact metadata must not change even one authored animation key.
func _initialize():call_deferred("run")
func run():
 var args=OS.get_cmdline_user_args();assert(args.size()==1)
 var manifest:Dictionary=JSON.parse_string(FileAccess.get_file_as_string(args[0]))
 var rows=[]
 for entry in manifest.entries:
  var source=load(entry.library) as AnimationLibrary
  var candidate=load(entry.output) as AnimationLibrary
  assert(candidate.get_meta("foot_plants_schema")==1)
  assert(source.get_animation_list()==candidate.get_animation_list())
  var keys=0
  for clip in source.get_animation_list():
   var a=source.get_animation(clip);var b=candidate.get_animation(clip)
   assert(a.length==b.length and a.loop_mode==b.loop_mode)
   assert(a.get_track_count()==b.get_track_count())
   for t in a.get_track_count():
    assert(a.track_get_type(t)==b.track_get_type(t) and a.track_get_path(t)==b.track_get_path(t))
    assert(a.track_get_key_count(t)==b.track_get_key_count(t))
    for k in a.track_get_key_count(t):
     assert(a.track_get_key_time(t,k)==b.track_get_key_time(t,k))
     assert(a.track_get_key_value(t,k)==b.track_get_key_value(t,k),entry.id+" "+str(clip))
     keys+=1
  rows.append({"id":entry.id,"unchanged_keys":keys,"clips":source.get_animation_list().size()})
 FileAccess.open(args[0].get_base_dir()+"/identity.json",FileAccess.WRITE).store_string(JSON.stringify({"status":"PASS","libraries":rows},"  "))
 print("FOOT_PLANT_LIBRARY_IDENTITY_PASS ",rows.size());quit()
