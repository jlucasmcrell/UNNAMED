extends SceneTree
## Headless/Dummy-audio development check on the game's actual C# sound bank.
func _initialize():
 call_deferred("check")

func check():
 var args:=OS.get_cmdline_user_args()
 assert(args.size()==2,"Pass art root and output receipt")
 var bank=load("res://Audio/SoundBank.cs").new()
 root.add_child(bank)
 bank.Load(args[0])
 var binding: Dictionary=JSON.parse_string(FileAccess.get_file_as_string("res://Art/art_bindings.json"))
 var family: String=binding.audio.water.underwater_bed
 var bed=bank.Loop(family)
 assert(bed!=null and bed.stream!=null,"Bound underwater family did not resolve to a real stream")
 assert(bed.stream is AudioStreamWAV and bed.stream.loop_mode!=AudioStreamWAV.LOOP_DISABLED)
 assert(bed.stream.get_length()>2.0 and bed.playing)
 var selected: String=bank.Last(family)
 assert(not selected.is_empty())
 var exact=bank.Loop(selected)
 assert(exact!=null and exact.stream==bed.stream,"Exact-ID looping changed")
 var report={"status":"PASS","family":family,"selected_id":selected,"seconds":bed.stream.get_length(),"loop_mode":bed.stream.loop_mode,"rendered":false,"audio_driver":AudioServer.get_driver_name()}
 var out:=FileAccess.open(args[1],FileAccess.WRITE)
 out.store_string(JSON.stringify(report,"  "));out.close()
 print("WATER_AUDIO_FAMILY_PASS ",report)
 bank.queue_free()
 await process_frame
 quit()
