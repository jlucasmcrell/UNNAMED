extends SceneTree
## Add exact-target idle transports while retaining every approved animation and metadata value.
func _initialize():
 var m: Dictionary=JSON.parse_string(FileAccess.get_file_as_string(OS.get_cmdline_user_args()[0]))
 var original: AnimationLibrary=load(m.original)
 var extra: AnimationLibrary=load(m.supplement)
 assert(original!=null and extra!=null,"Missing baseline or exact-target idle supplement")
 assert(m.output.simplify_path()!=m.original.simplify_path(),"Never replace the accepted baseline path")
 var sha=FileAccess.get_sha256(m.body)
 assert(original.get_meta("target_body_sha256")==sha and extra.get_meta("target_body_sha256")==sha)
 var merged: AnimationLibrary=original.duplicate(true)
 var sources: Dictionary=original.get_meta("animation_sources").duplicate(true)
 var additions: Dictionary=extra.get_meta("animation_sources")
 assert(extra.has_animation("idle_observe"))
 for name_ in ["idle_observe"]:
  assert(extra.has_animation(name_) and not original.has_animation(name_))
  assert("cmu_idle_" in str(additions[name_]) and not "kaykit" in str(additions[name_]).to_lower())
  merged.add_animation(name_,extra.get_animation(name_))
  sources[name_]=additions[name_]
 merged.set_meta("animation_sources",sources)
 DirAccess.make_dir_recursive_absolute(m.output.get_base_dir())
 # Replace the directory entry, never the contents of a potentially shared hardlink.
 var pending=m.output+".pending.res"
 assert(ResourceSaver.save(merged,pending)==OK)
 assert(DirAccess.rename_absolute(pending,m.output)==OK)
 var readback: AnimationLibrary=ResourceLoader.load(m.output,"",ResourceLoader.CACHE_MODE_IGNORE)
 var keys=0
 for name_ in original.get_animation_list():
  var a=original.get_animation(name_)
  var b=readback.get_animation(name_)
  assert(a.length==b.length and a.loop_mode==b.loop_mode and a.get_track_count()==b.get_track_count())
  for track in a.get_track_count():
   assert(a.track_get_path(track)==b.track_get_path(track) and a.track_get_type(track)==b.track_get_type(track))
   assert(a.track_get_interpolation_type(track)==b.track_get_interpolation_type(track))
   assert(a.track_get_key_count(track)==b.track_get_key_count(track))
   for key in a.track_get_key_count(track):
    assert(a.track_get_key_time(track,key)==b.track_get_key_time(track,key))
    assert(a.track_get_key_value(track,key)==b.track_get_key_value(track,key))
    assert(a.track_get_key_transition(track,key)==b.track_get_key_transition(track,key))
    keys+=1
 for meta in original.get_meta_list():
  if meta!="animation_sources":assert(original.get_meta(meta)==readback.get_meta(meta))
 var receipt={"status":"PASS","body_sha256":sha,"original_sha256":FileAccess.get_sha256(m.original),"output_sha256":FileAccess.get_sha256(m.output),"original_states":original.get_animation_list().size(),"unchanged_keys":keys,"added_states":["idle_observe"],"states":readback.get_animation_list().size()}
 var f=FileAccess.open(m.output+".merge.json",FileAccess.WRITE)
 f.store_string(JSON.stringify(receipt,"  "));f.close()
 print("HUMGEN_IDLE_MERGE_PASS ",receipt)
 quit()
