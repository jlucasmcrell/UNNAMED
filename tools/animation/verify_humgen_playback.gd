extends SceneTree
## Compare actual packaged resources: time may change; native pose values must not.

func _initialize() -> void:
 var args := OS.get_cmdline_user_args()
 assert(args.size()==3,"Old library, corrected manifest, report path required")
 var original: AnimationLibrary = load(args[0])
 var manifest: Dictionary = JSON.parse_string(FileAccess.get_file_as_string(args[1]))
 var corrected: AnimationLibrary = load(manifest.output)
 assert(corrected.get_meta("source_playback_schema",0)==1)
 var keys_checked := 0
 var rows := []
 for entry in manifest.clips:
  var before := original.get_animation(entry.state)
  var after := corrected.get_animation(entry.state)
  var rate := float(entry.source_rate_scale)
  assert(is_equal_approx(after.length,before.length/absf(rate)))
  assert(before.get_track_count()==after.get_track_count())
  for track in before.get_track_count():
   assert(before.track_get_path(track)==after.track_get_path(track))
   assert(before.track_get_type(track)==after.track_get_type(track))
   var count := before.track_get_key_count(track)
   assert(count==after.track_get_key_count(track))
   for key in count:
    var other := count-1-key if rate<0 else key
    var at := before.track_get_key_time(track,key)
    var wanted := ((before.length-at) if rate<0 else at)/absf(rate)
    assert(absf(after.track_get_key_time(track,other)-wanted)<.00001)
    assert(var_to_bytes(before.track_get_key_value(track,key))==var_to_bytes(after.track_get_key_value(track,other)),"Native pose value changed: "+entry.state)
    keys_checked+=1
  rows.append({"state":entry.state,"rate":rate,"seconds":after.length})
 var report := {"status":"PASS","original":args[0],"corrected":manifest.output,"source_clips":rows,"pose_values_byte_identical":keys_checked}
 var file := FileAccess.open(args[2],FileAccess.WRITE)
 file.store_string(JSON.stringify(report,"  "))
 print("HUMGEN_SOURCE_PLAYBACK_INVARIANTS_PASS ",keys_checked)
 quit()
