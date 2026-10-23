extends SceneTree
var JOB:String
const ALLOWED=["strafe_left","strafe_right","walk_strafe_left","walk_strafe_right"]
func _initialize():
 JOB=OS.get_environment("OTHERREACH_PIPELINE_ROOT").replace("\\","/").trim_suffix("/")+"/"
 assert(JOB.is_absolute_path() and JOB!="/","Fresh absolute OTHERREACH_PIPELINE_ROOT required")
 assert(not FileAccess.file_exists(JOB+"runtime/catalog.json"),"Fresh runtime required; never overwrite a live character catalog")
 assert(not JOB.begins_with("D:/Otherreach_Scratch/mh_playtest_20261019/"),"Frozen f87 output forbidden")
 call_deferred("run")
func digest(a:Animation)->String:
 var h=HashingContext.new();h.start(HashingContext.HASH_SHA256)
 h.update(var_to_bytes([a.length,a.loop_mode,a.get_meta_list().map(func(k):return [k,a.get_meta(k)])]))
 for t in a.get_track_count():
  h.update(var_to_bytes([a.track_get_type(t),a.track_get_path(t),a.track_get_interpolation_type(t),a.track_get_key_count(t)]))
  for k in a.track_get_key_count(t):h.update(var_to_bytes([a.track_get_key_time(t,k),a.track_get_key_value(t,k),a.track_get_key_transition(t,k)]))
 return h.finish().hex_encode()
func rig_in(n:Node)->Skeleton3D:
 if n is Skeleton3D:return n
 for c in n.get_children():
  var found=rig_in(c)
  if found:return found
 return null
func contact_samples(a:Animation,body:String)->Dictionary:
 var doc=GLTFDocument.new();var state=GLTFState.new();assert(doc.append_from_file(body,state)==OK)
 var model=doc.generate_scene(state);root.add_child(model);var rig=rig_in(model);assert(rig)
 var player=AnimationPlayer.new();rig.add_child(player);player.root_node=NodePath("..")
 player.callback_mode_process=AnimationMixer.ANIMATION_CALLBACK_MODE_PROCESS_MANUAL
 var lib=AnimationLibrary.new();var copy=a.duplicate(true)
 for t in copy.get_track_count():
  var p=copy.track_get_path(t);assert(p.get_subname_count()==1);assert(rig.find_bone(p.get_subname(0))>=0)
  copy.track_set_path(t,NodePath(".:"+p.get_subname(0)))
 lib.add_animation("probe",copy);player.add_animation_library("",lib)
 var n=ceili(a.length*120);var dt=a.length/n;var feet=[];var floor_=INF
 for i in n:
  player.play("probe",0);player.seek(i*dt,true);player.advance(0)
  var row=[]
  for name_ in ["foot_l","foot_r"]:
   var p:Vector3=rig.get_bone_global_pose(rig.find_bone(name_)).origin
   row.append(p);floor_=minf(floor_,p.y)
  feet.append(row)
 var speeds=[];var contacts=[]
 for side in 2:
  var heights=feet.map(func(f):return f[side].y);var low:float=heights.min();var high:float=heights.max()
  var armed=true;var peak=heights.find(high)
  if high-low>.005:
   for offset in n:
    var i=(peak+offset)%n
    if heights[i]>low+minf(.015,(high-low)*.5):armed=true
    if armed and heights[i]<=low+.001:
     contacts.append({"side":"L" if side==0 else "R","time":i*dt});armed=false
 for i in range(1,n-1):
  for side in 2:
   if feet[i][side].y<=floor_+.02:
    var v:Vector3=(feet[i+1][side]-feet[i-1][side])/(2*dt);var speed=Vector2(v.x,v.z).length()
    if speed>.05:speeds.append(speed)
 speeds.sort();assert(not speeds.is_empty());assert(contacts.size()>=2)
 contacts.sort_custom(func(a,b):return a.time<b.time)
 model.free()
 return {"pace":speeds[speeds.size()/2],"contacts":{"length":a.length,"events":contacts},"phase":float(contacts.filter(func(e):return e.side=="L")[0].time)/a.length}
func run():
 var manifest:Dictionary=JSON.parse_string(FileAccess.get_file_as_string(JOB+"mocap_overlay.json"))
 var catalog:Dictionary=JSON.parse_string(FileAccess.get_file_as_string(manifest.source_catalog))
 var pairs:Array=manifest.pairs
 var rows=[]
 for pair in pairs:
  for i in pair.size():pair[i]=str(pair[i]).replace("\\","/")
  var base=load(pair[1]) as AnimationLibrary;var donor=load(pair[2]) as AnimationLibrary;assert(base and donor)
  var result=base.duplicate(true) as AnimationLibrary
  var contacts:Dictionary=base.get_meta("foot_plants",{}).duplicate(true)
  var paces:Dictionary=base.get_meta("motion_paces",{}).duplicate(true)
  var phases:Dictionary=base.get_meta("motion_phases",{}).duplicate(true)
  var changed=[];var unchanged=[]
  for clip in ALLOWED:
   assert(donor.has_animation(clip),pair[0]+" missing "+clip)
   if result.has_animation(clip):result.remove_animation(clip)
   result.add_animation(clip,donor.get_animation(clip).duplicate(true))
   assert(digest(result.get_animation(clip))==digest(donor.get_animation(clip)))
   var sample=contact_samples(result.get_animation(clip),pair[3])
   paces[clip]=sample.pace;phases[clip]=sample.phase
   var donor_contacts:Dictionary=donor.get_meta("foot_plants",{})
   contacts[clip]=donor_contacts.get(clip,sample.contacts)
   changed.append({"clip":clip,"donor_digest":digest(donor.get_animation(clip)),"pace":paces[clip],"contact_metadata":contacts[clip]})
  for clip in base.get_animation_list():
   if str(clip) in ALLOWED:continue
   assert(result.has_animation(clip));assert(digest(base.get_animation(clip))==digest(result.get_animation(clip)),"Protected clip changed: "+pair[0]+" "+clip)
   unchanged.append(str(clip))
  result.set_meta("foot_plants",contacts);result.set_meta("motion_paces",paces);result.set_meta("motion_phases",phases)
  assert(pair[4].begins_with(JOB) and not FileAccess.file_exists(pair[4]),"Fresh output under job root required")
  DirAccess.make_dir_recursive_absolute(pair[4].get_base_dir());assert(ResourceSaver.save(result,pair[4])==OK)
  if pair[0] in ["ada","mateo"]:
   var config:Dictionary=catalog.characters[pair[0]]
   config.animation_library="animations_mocap_approved/"+pair[0]+".res"
   for field in ["animation_paces","animation_phases"]:
    var values:Dictionary=config.get(field,{}).duplicate(true)
    for clip in ALLOWED:values[clip]=paces[clip] if field=="animation_paces" else phases[clip]
    config[field]=values
  rows.append({"id":pair[0],"base_sha256":FileAccess.get_sha256(pair[1]),"donor_sha256":FileAccess.get_sha256(pair[2]),"output":pair[4],"body_sha256":FileAccess.get_sha256(pair[3]),"changed":changed,"protected_clip_digests_identical":unchanged})
 FileAccess.open(JOB+"runtime/catalog.json",FileAccess.WRITE).store_string(JSON.stringify(catalog,"  "))
 FileAccess.open(JOB+"evidence/mocap_overlay_identity.json",FileAccess.WRITE).store_string(JSON.stringify({"status":"PASS","libraries":rows,"route":"Reused existing Epic IK Retargeter native exports; clip selection and contact metadata only, no retarget or native mesh/rest edits"},"  "))
 print("STRAFE_ONLY_IDENTITY_PASS libraries=",rows.size());quit()
