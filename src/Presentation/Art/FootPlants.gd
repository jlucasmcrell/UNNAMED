extends Node
## Per-instance method tracks on already-retargeted clips. Original libraries remain unchanged.
signal planted(clip: String, side: String, contact_seconds: float)
var dominant: Callable

func configure(player: AnimationPlayer, select_clip: Callable) -> bool:
 if OS.get_environment("OTHERREACH_FOOTSTEP_REVIEW_BASELINE")=="1":return false
 var source:=player.get_animation_library("")
 if int(source.get_meta("foot_plants_schema",0))!=1:return false
 var contacts:Dictionary=source.get_meta("foot_plants",{})
 if contacts.is_empty():return false
 dominant=select_clip
 name="FootPlants"
 var animation_root:=player.get_node(player.root_node)
 animation_root.add_child(self)
 var library:=source.duplicate() as AnimationLibrary
 for clip in source.get_animation_list():
  var key:String="crouch_walk" if clip=="crouch_move" else str(clip)
  if not contacts.has(key):continue
  var data:Dictionary=contacts[key]
  var animation:=source.get_animation(clip).duplicate(true) as Animation
  assert(is_equal_approx(animation.length,float(data.length)))
  var track:=animation.add_track(Animation.TYPE_METHOD)
  animation.track_set_path(track,animation_root.get_path_to(self))
  for contact in data.events:
   assert(contact.side in ["L","R"] and float(contact.time)>=0 and float(contact.time)<animation.length)
   animation.track_insert_key(track,float(contact.time),{"method":"plant","args":[str(clip),contact.side,float(contact.time)]})
  library.remove_animation(clip);library.add_animation(clip,animation)
 player.remove_animation_library("");player.add_animation_library("",library)
 player.callback_mode_method=AnimationMixer.ANIMATION_CALLBACK_MODE_METHOD_IMMEDIATE
 return true

func plant(clip: String, side: String, seconds: float) -> void:
 # Every nonzero blend may deliver a method key. Only the dominant gait speaks.
 if str(dominant.call())==clip:planted.emit(clip,side,seconds)
