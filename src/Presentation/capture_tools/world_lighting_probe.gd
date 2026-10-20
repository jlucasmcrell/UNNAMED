extends Node
# Opt-in source diagnosis. No simulation state, saved settings or shipping lighting is changed.
var main: Node
var camera: Camera3D
var sky: WorldEnvironment
var sun: DirectionalLight3D
var output: String
var arm := "baseline"
var light_states := {}
var geometry_states := {}
var environment_state := {}
var target := Transform3D.IDENTITY
var records := []
var player: Node3D
var terrain_states := {}
var sky_fog: MeshInstance3D
var sky_fog_visible := true
var helpers := []
var scatter_meshes := []
var water_mesh: GeometryInstance3D
var legacy_terrain: GeometryInstance3D
var original_pancake := 0.0
var original_shadow_mode := 0
var mass_frames := 3
var caster_groups := {}
var layer_states := {}
var touched := {}

func run(root: Node, source: Camera3D, folder: String, locations: Array, figure: Node3D):
 main=root;output=folder+"/lighting";DirAccess.make_dir_recursive_absolute(output)
 process_priority=10000
 camera=Camera3D.new();camera.name="WorldLightingAuditCamera"
 camera.fov=source.fov;camera.near=source.near;camera.far=source.far;camera.cull_mask=source.cull_mask;camera.attributes=source.attributes;camera.environment=source.environment
 add_child(camera);camera.make_current()
 var fill:=source.get_node_or_null("CharacterFill")
 if fill:fill.reparent(camera,false)
 player=figure
 target.origin=locations[0].position
 for frame in 30:await RenderingServer.frame_post_draw
 for terrain in main.find_children("*","Terrain3D",true,false):terrain.call("set_camera",camera)
 sky=main.get_node("Hollow/Lighting/Sky3D");sun=sky.get("sun")
 sky_fog=sky.find_child("_FogMeshI",true,false)
 if sky_fog:sky_fog_visible=sky_fog.visible
 for terrain in main.find_children("*","Terrain3D",true,false):terrain_states[terrain]=terrain.get("cast_shadows")
 for node in main.find_children("*","Light3D",true,false):light_states[node]={"shadow":node.shadow_enabled,"visible":node.visible}
 for node in main.find_children("*","GeometryInstance3D",true,false):
  if node.get_viewport()!=main.get_viewport():continue
  geometry_states[node]=node.cast_shadow;layer_states[node]=node.layers
  for group in ["all_mesh_shadows_off","hollow_shadows_off","player_shadows_off","npcs_shadows_off","particles_shadows_off"]:
   var include: bool=group=="all_mesh_shadows_off" or (group=="hollow_shadows_off" and main.get_node("Hollow").is_ancestor_of(node)) or (group=="player_shadows_off" and player.is_ancestor_of(node)) or (group=="npcs_shadows_off" and main.get_node("Npcs").is_ancestor_of(node)) or (group=="particles_shadows_off" and (node is GPUParticles3D or node is CPUParticles3D))
   if include and (node.cast_shadow!=0 or str(node.get_meta("shadow_lod_original","Off"))!="Off"):
    if not caster_groups.has(group):caster_groups[group]=[]
    caster_groups[group].append(node)
  var path:=str(node.get_path()).to_lower()
  if "sky" in path or "cloud" in path or "helper" in path:helpers.append(node)
  if "/scatter/" in path:scatter_meshes.append(node)
 water_mesh=main.get_node_or_null("Hollow/Water")
 legacy_terrain=main.get_node_or_null("Hollow/Terrain")
 original_pancake=sun.directional_shadow_pancake_size;original_shadow_mode=sun.directional_shadow_mode
 for property in ["sdfgi_enabled","ssao_enabled","ssil_enabled","fog_enabled","volumetric_fog_enabled"]:environment_state[property]=sky.environment.get(property)
 var expected_proxy: String=OS.get_environment("OTHERREACH_LIGHTING_EXPECT_PROXY")
 var forge_proxy: bool=false
 for mesh in caster_groups["hollow_shadows_off"]:
  if str(mesh.get_path()).ends_with("/forge/bld_fab_ah_forge_shed/bld_fab_ah_forge_shed2/bld_fab_ah_forge_shed"):forge_proxy=mesh.mesh.shadow_mesh!=null;break
 if not expected_proxy.is_empty() and forge_proxy!=(expected_proxy=="1"):
  _write("failed.json",{"error":"Wrong production import/shadow proxy arm","expected_proxy":expected_proxy,"actual_proxy":forge_proxy});push_error("Wrong production import/shadow proxy arm");get_tree().quit(3);return
 var inventory: Dictionary=_inventory(locations)
 inventory.forge_shadow_proxy=forge_proxy;inventory.camera_provider="Independent current Camera3D, Terrain3D tracking attached, viewport shadow-LOD eye; no SpringArm parent"
 _write("inventory.json",inventory)
 if OS.get_environment("OTHERREACH_LIGHTING_SHADOW_PROXY")=="1":
  await _shadow_proxy_probe(locations[0])
  get_tree().quit()
  return
 if OS.get_environment("OTHERREACH_LIGHTING_BISECT")=="1":
  await _bisect_hollow(locations[0])
  get_tree().quit()
  return
 var arms=["baseline","sky_helper_shadows_off","all_directional_shadows_off","terrain_shadows_off","sky_fog_off","local_lights_off","sdfgi_off","ssao_off","ssil_off","fog_off","clouds_off"]
 if not OS.get_environment("OTHERREACH_LIGHTING_ARMS").is_empty():arms=Array(OS.get_environment("OTHERREACH_LIGHTING_ARMS").split(","))
 if "all_directional_shadows_off" in arms:
  var i:=arms.find("all_directional_shadows_off")
  arms.insert(i,"world_sun_shadows_off");arms.insert(i+1,"creator_shadows_off")
 for candidate in arms:
  arm=candidate;_restore();mass_frames=3;_apply()
  for location in locations:
   if not OS.get_environment("OTHERREACH_LIGHTING_LOCATIONS").is_empty() and location.name not in OS.get_environment("OTHERREACH_LIGHTING_LOCATIONS").split(","):continue
   # Full owner protocol for baseline and requested confirmation arms. Ablations use identical reduced views.
   var full: bool=arm=="baseline" or OS.get_environment("OTHERREACH_LIGHTING_FULL")=="1"
   if not full and location.name not in ["spawn","blackvein","out_200m"]:continue
   target.origin=location.position;mass_frames=3
   target.basis=Basis.IDENTITY
   for frame in 90:await RenderingServer.frame_post_draw
   for pitch in ([-60,-30,0,30,60] if full else [-60,0]):
    for yaw in ([0,90,180,270] if full else [0,180]):
     mass_frames=3
     target.basis=Basis.from_euler(Vector3(deg_to_rad(pitch),deg_to_rad(yaw),0))
     for frame in 24:await RenderingServer.frame_post_draw
     var image:=get_viewport().get_texture().get_image()
     var filename: String="%s_%s_p%s_y%s.png"%[arm,location.name,pitch,yaw]
     image.save_png(output+"/"+filename)
     var small:=image.duplicate() as Image;small.resize(96,54,Image.INTERPOLATE_BILINEAR)
     var luma:=0.0;var dark:=0
     for y in small.get_height():
      for x in small.get_width():
       var colour:=small.get_pixel(x,y);var value:=colour.r*.2126+colour.g*.7152+colour.b*.0722
       luma+=value
       if value<.05:dark+=1
     var toward_sun: Vector3=sun.global_basis.z.normalized()
     var depth: float=-(camera.global_transform.affine_inverse()*player.global_position).z
     var distance: float=minf(camera.far,sun.directional_shadow_max_distance)
     var splits=[sun.directional_shadow_split_1,sun.directional_shadow_split_2,sun.directional_shadow_split_3,1.0]
     var cascade:=-1
     if depth>=camera.near:
      for i in splits.size():
       if depth<=distance*float(splits[i]):cascade=i;break
     records.append({"arm":arm,"location":location.name,"file":filename,"pitch":pitch,"yaw":yaw,"mean_luminance_srgb":luma/5184.0,"fraction_below_005":dark/5184.0,"camera_position":_xyz(camera.global_position),"camera_height_above_ground_m":1.7,"hour":sky.get("current_time"),"sun_altitude_deg":rad_to_deg(asin(toward_sun.y)),"sun_azimuth_deg":fposmod(rad_to_deg(atan2(toward_sun.x,toward_sun.z)),360),"sun_shadow_enabled":sun.shadow_enabled,"directional_shadow_mode":sun.directional_shadow_mode,"shadow_max_distance":sun.directional_shadow_max_distance,"pancake_size":sun.directional_shadow_pancake_size,"bias":sun.shadow_bias,"normal_bias":sun.shadow_normal_bias,"splits":splits,"player_view_depth":depth,"player_cascade_estimate":cascade,"cascade_note":"Estimate from camera depth and exposed split offsets; GPU cascade selection is not exposed. Player presentation follows the probe; authoritative simulation stays unchanged. Camera route is presentation-only.","sdfgi_cascades":sky.environment.sdfgi_cascades})
     _write("samples.json",records)
     print("WORLD_LIGHTING_SAMPLE ",arm," ",location.name," ",pitch," ",yaw," luma=",luma/5184.0)
     if OS.get_environment("OTHERREACH_LIGHTING_PSSM_DEBUG")=="1" and yaw==0 and pitch in [-60,0]:
      var debug_mode: int=get_viewport().debug_draw
      get_viewport().debug_draw=Viewport.DEBUG_DRAW_PSSM_SPLITS
      for frame in 3:await RenderingServer.frame_post_draw
      var debug_file: String="pssm_"+filename
      get_viewport().get_texture().get_image().save_png(output+"/"+debug_file)
      get_viewport().debug_draw=debug_mode
      records[-1].pssm_debug_image=debug_file
      records[-1].pssm_debug_note="Godot PSSM debug: red/green/blue/yellow near-to-far; custom shaders ignored in this separate diagnostic pass"
      _write("samples.json",records)
 _restore()
 if OS.get_environment("OTHERREACH_LIGHTING_LOD_MOTION")=="1" or OS.get_environment("OTHERREACH_LIGHTING_PSSM_DEBUG")=="1":await _lod_motion_probe()
 _write("complete.json",{"samples":records.size(),"status":"CAPTURED_REQUIRES_CLAUDE_REVIEW","locations":locations,"lit_edge":"50m east of spawn: observed camera-dependent lit/dark transition station; false proxy shadow is view-dependent, not a fixed overhead roof boundary"})
 get_tree().quit()

func _process(_delta: float):
 if camera:
  camera.global_transform=target
  if player:player.global_position=target.origin-Vector3.UP*1.7
  _apply()
  if mass_frames>0:mass_frames-=1

func _restore():
 for light in light_states:light.shadow_enabled=light_states[light].shadow;light.visible=light_states[light].visible
 for mesh in touched:
  if is_instance_valid(mesh):mesh.cast_shadow=geometry_states[mesh];mesh.layers=layer_states[mesh]
 touched.clear()
 sun.directional_shadow_pancake_size=original_pancake;sun.directional_shadow_mode=original_shadow_mode
 for property in environment_state:sky.environment.set(property,environment_state[property])
 for terrain in terrain_states:terrain.set("cast_shadows",terrain_states[terrain])
 if sky_fog:sky_fog.visible=sky_fog_visible
 if sky.compositor:
  for effect in sky.compositor.compositor_effects:effect.enabled=true

func _apply():
 if not sky:return
 if caster_groups.has(arm):
  for mesh in caster_groups[arm]:
   if not is_instance_valid(mesh):continue
   if geometry_states[mesh]==GeometryInstance3D.SHADOW_CASTING_SETTING_SHADOWS_ONLY:
    touched[mesh]=true
    if mesh.layers!=0:mesh.layers=0
   elif mass_frames>0 and mesh.cast_shadow!=GeometryInstance3D.SHADOW_CASTING_SETTING_OFF:
    touched[mesh]=true;mesh.cast_shadow=GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
 match arm:
  "sky_helper_shadows_off":
   if mass_frames>0:
    for mesh in helpers:
     if mesh.cast_shadow!=GeometryInstance3D.SHADOW_CASTING_SETTING_OFF:touched[mesh]=true;mesh.cast_shadow=GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
  "world_sun_shadows_off":sun.shadow_enabled=false
  "creator_shadows_off":
   for light in light_states:
    if light is DirectionalLight3D and light.get_viewport()!=main.get_viewport():light.shadow_enabled=false
  "all_directional_shadows_off":
   for light in light_states:
    if light is DirectionalLight3D:light.shadow_enabled=false
  "pancake_zero":sun.directional_shadow_pancake_size=0
  "shadow_single_split":sun.directional_shadow_mode=DirectionalLight3D.SHADOW_ORTHOGONAL
  "water_shadows_off":
   if water_mesh:touched[water_mesh]=true;water_mesh.cast_shadow=GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
  "legacy_terrain_shadows_off":
   if legacy_terrain:touched[legacy_terrain]=true;legacy_terrain.cast_shadow=GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
  "scatter_shadows_off":
   if mass_frames>0:
    for mesh in scatter_meshes:
     if is_instance_valid(mesh) and mesh.cast_shadow!=GeometryInstance3D.SHADOW_CASTING_SETTING_OFF:touched[mesh]=true;mesh.cast_shadow=GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
  "terrain_shadows_off":
   for terrain in terrain_states:terrain.set("cast_shadows",0)
  "sky_fog_off":
   if sky_fog:sky_fog.visible=false
  "local_lights_off":
   for light in light_states:
    if not light is DirectionalLight3D:light.visible=false
  "sdfgi_off":sky.environment.sdfgi_enabled=false
  "ssao_off":sky.environment.ssao_enabled=false
  "ssil_off":sky.environment.ssil_enabled=false
  "fog_off":sky.environment.fog_enabled=false;sky.environment.volumetric_fog_enabled=false
  "clouds_off":
   if sky.compositor:
    for effect in sky.compositor.compositor_effects:effect.enabled=false

func _inventory(locations: Array) -> Dictionary:
 var casters:=[];var lights:=[];var volumes:=[];var ray_casters:=[]
 for mesh in geometry_states:
  if not mesh.has_method("get_aabb"):continue
  var bounds: AABB=mesh.global_transform*mesh.get_aabb()
  var hits:=[]
  if mesh.cast_shadow!=0 and mesh.layers!=0 and mesh.is_visible_in_tree():
   for location in locations:
    var hit: Variant=bounds.grow(.01).intersects_ray(location.position-Vector3.UP*1.68,sun.global_basis.z.normalized())
    if hit is Vector3:hits.append({"location":location.name,"distance":location.position.distance_to(hit)})
   if not hits.is_empty():ray_casters.append({"path":str(mesh.get_path()),"type":mesh.get_class(),"mode":mesh.cast_shadow,"layers":mesh.layers,"aabb_position":_xyz(bounds.position),"aabb_size":_xyz(bounds.size),"sun_ray_aabb_hits":hits})
  var covers:=[]
  for location in locations:
   var p: Vector3=location.position
   if p.x>=bounds.position.x and p.x<=bounds.end.x and p.z>=bounds.position.z and p.z<=bounds.end.z and bounds.end.y>p.y:covers.append(location.name)
  if not covers.is_empty() or bounds.size.length()>500:
   casters.append({"path":str(mesh.get_path()),"type":mesh.get_class(),"cast_shadow":mesh.cast_shadow,"visible_in_tree":mesh.is_visible_in_tree(),"aabb_position":_xyz(bounds.position),"aabb_size":_xyz(bounds.size),"covers_above_camera":covers,"visibility_begin":mesh.visibility_range_begin,"visibility_end":mesh.visibility_range_end,"fade_mode":mesh.visibility_range_fade_mode})
 for light in light_states:lights.append({"path":str(light.get_path()),"type":light.get_class(),"position":_xyz(light.global_position),"energy":light.light_energy,"cull_mask":light.light_cull_mask,"shadows":light.shadow_enabled})
 for node in main.find_children("*","Node",true,false):
  if node is WorldEnvironment or node is ReflectionProbe or node is VoxelGI or node is LightmapGI:volumes.append({"path":str(node.get_path()),"type":node.get_class()})
 return {"ray_casters":ray_casters,"caster_group_counts":caster_groups.keys().reduce(func(acc,key):acc[key]=caster_groups[key].size();return acc,{}),"casters":casters,"lights":lights,"volumes":volumes,"lod_stipple":OS.get_environment("UNNAMED_LOD_STIPPLE"),"locations":locations,"environment":environment_state,"sunlight":"toward sun = positive light global Z"}

func _xyz(value: Vector3) -> Array:return [value.x,value.y,value.z]
func _write(name: String, value):
 var file:=FileAccess.open(output+"/"+name,FileAccess.WRITE);file.store_string(JSON.stringify(value,"  "));file.close()

var bisect_rows := []
func _bisect_shot(label: String, nodes: Array) -> float:
 _restore();arm="bisect";caster_groups[arm]=nodes;mass_frames=3
 for frame in 30:await RenderingServer.frame_post_draw
 var image:=get_viewport().get_texture().get_image()
 image.save_png(output+"/"+label+".png")
 image.resize(96,54,Image.INTERPOLATE_BILINEAR)
 var luma:=0.0
 for y in image.get_height():
  for x in image.get_width():
   var colour:=image.get_pixel(x,y);luma+=colour.r*.2126+colour.g*.7152+colour.b*.0722
 luma/=5184.0
 bisect_rows.append({"label":label,"disabled_meshes":nodes.size(),"luminance":luma,"camera":_xyz(camera.global_position),"hour":sky.get("current_time")})
 _write("bisect.json",bisect_rows)
 print("WORLD_LIGHTING_BISECT ",label," nodes=",nodes.size()," luma=",luma)
 return luma

func _bisect_hollow(location: Dictionary):
 target.origin=location.position;target.basis=Basis.from_euler(Vector3(deg_to_rad(-60),0,0))
 var candidates: Array=caster_groups["hollow_shadows_off"].duplicate()
 candidates.sort_custom(func(a,b):return str(a.get_path())<str(b.get_path()))
 var base: float=await _bisect_shot("bisect_baseline",[])
 var bright: float=await _bisect_shot("bisect_all_hollow",candidates)
 var threshold: float=base+(bright-base)*.65
 var far:=[];var near:=[];var standalone:=[];var outside:=[];var violations:=[]
 for mesh in candidates:
  if mesh.visibility_range_begin>0:far.append(mesh)
  elif mesh.visibility_range_end>0:near.append(mesh)
  else:standalone.append(mesh)
  var bounds: AABB=mesh.custom_aabb if mesh.custom_aabb.size!=Vector3.ZERO else mesh.get_aabb()
  var distance: float=target.origin.distance_to(mesh.to_global(bounds.get_center()))
  var in_range: bool=(distance>=mesh.visibility_range_begin and (mesh.visibility_range_end==0 or distance<mesh.visibility_range_end))
  if not in_range:
   outside.append(mesh)
   if geometry_states[mesh]!=0 and layer_states[mesh]!=0:violations.append(_mesh_record(mesh))
 _write("out_of_range_casting.json",violations)
 for group in [["far_lods",far],["near_lods",near],["standalone",standalone],["out_of_range",outside]]:
  await _bisect_shot("category_"+group[0],group[1])
 var depth:=0;var status:="SINGLE_CASTER"
 while candidates.size()>1:
  var split: int=candidates.size()/2
  var left: Array=candidates.slice(0,split);var right: Array=candidates.slice(split)
  var left_luma: float=await _bisect_shot("split_%02d_left"%depth,left)
  if left_luma>=threshold:candidates=left
  else:
   var right_luma: float=await _bisect_shot("split_%02d_right"%depth,right)
   if right_luma>=threshold:candidates=right
   else:status="MULTIPLE_OR_COLLECTIVE_CASTERS";break
  depth+=1
  if depth>16:status="DEPTH_LIMIT";break
 var culprit:=[]
 for mesh in candidates:culprit.append(_mesh_record(mesh))
 _write("culprit.json",{"status":status,"baseline":base,"all_hollow_disabled":bright,"threshold":threshold,"candidate_count":candidates.size(),"candidates":culprit})
 await _bisect_shot("bisect_restored",[])
 _write("complete.json",{"status":"DIAGNOSIS_REQUIRES_CLAUDE_REVIEW","samples":bisect_rows.size()})

func _mesh_record(mesh: GeometryInstance3D) -> Dictionary:
 var record: Dictionary={"path":str(mesh.get_path()),"mode":geometry_states[mesh],"layers":layer_states[mesh],"visible":mesh.is_visible_in_tree(),"position":_xyz(mesh.global_position),"begin":mesh.visibility_range_begin,"end":mesh.visibility_range_end,"shadow_lod_original":str(mesh.get_meta("shadow_lod_original","NONE")),"shadow_lod_watched":mesh.has_meta("shadow_lod_bounds")}
 if mesh is MeshInstance3D:
  record.mesh_resource=mesh.mesh.resource_path;record.surfaces=[]
  for index in mesh.mesh.get_surface_count():
   var material=mesh.get_active_material(index)
   var row: Dictionary={"surface":index,"resource":material.resource_path if material else "NONE","type":material.get_class() if material else "NONE"}
   if material is BaseMaterial3D:row.merge({"transparency":material.transparency,"alpha_scissor_threshold":material.alpha_scissor_threshold,"billboard_mode":material.billboard_mode,"cull_mode":material.cull_mode})
   if material is ShaderMaterial:row.shader=material.shader.resource_path
   record.surfaces.append(row)
 return record

func _shadow_proxy_probe(location: Dictionary):
 target.origin=location.position;target.basis=Basis.from_euler(Vector3(deg_to_rad(-60),0,0))
 var forge: MeshInstance3D
 for mesh in caster_groups["hollow_shadows_off"]:
  if str(mesh.get_path()).ends_with("/forge/bld_fab_ah_forge_shed/bld_fab_ah_forge_shed2/bld_fab_ah_forge_shed"):forge=mesh;break
 assert(forge!=null,"Known forge caster was not found")
 var visible: ArrayMesh=forge.mesh
 var proxy: ArrayMesh=visible.shadow_mesh
 var rows:=[]
 for surface in visible.get_surface_count():
  var a:=visible.surface_get_arrays(surface);var b:=proxy.surface_get_arrays(surface) if proxy else []
  rows.append({"surface":surface,"visible_vertices":a[Mesh.ARRAY_VERTEX].size(),"visible_indices":a[Mesh.ARRAY_INDEX].size(),"visible_format":visible.surface_get_format(surface),"proxy_vertices":b[Mesh.ARRAY_VERTEX].size() if proxy else 0,"proxy_indices":b[Mesh.ARRAY_INDEX].size() if proxy else 0,"proxy_format":proxy.surface_get_format(surface) if proxy else 0})
 _write("shadow_proxy.json",{"node":_mesh_record(forge),"visible_aabb":str(visible.get_aabb()),"proxy_aabb":str(proxy.get_aabb()) if proxy else "NONE","surfaces":rows})
 await _bisect_shot("proxy_baseline",[])
 visible.shadow_mesh=null
 await _bisect_shot("proxy_original_geometry",[])
 visible.shadow_mesh=proxy
 await _bisect_shot("proxy_restored",[])
 var lod_threshold: float=get_viewport().mesh_lod_threshold
 get_viewport().mesh_lod_threshold=0
 await _bisect_shot("proxy_no_auto_lod",[])
 get_viewport().mesh_lod_threshold=lod_threshold
 var normal_bias: float=sun.shadow_normal_bias
 sun.shadow_normal_bias=0
 await _bisect_shot("proxy_normal_bias_zero",[])
 sun.shadow_normal_bias=normal_bias
 await _bisect_shot("proxy_final_baseline",[])
 _write("complete.json",{"status":"PROXY_DIAGNOSIS_REQUIRES_REVIEW","samples":bisect_rows.size()})

func _lod_motion_probe():
 var options:=[]
 for mesh in geometry_states:
  var path:=str(mesh.get_path())
  if not "/Hollow/tree_" in path or mesh.visibility_range_begin!=0 or mesh.visibility_range_end<=0:continue
  var bounds: AABB=mesh.global_transform*(mesh.custom_aabb if mesh.custom_aabb.size!=Vector3.ZERO else mesh.get_aabb())
  var centre:=bounds.get_center()
  if bounds.size.y<1.5 or bounds.size.y>20 or Vector2(centre.x-430,centre.z-758).length()>180:continue
  options.append({"node":mesh,"centre":centre,"height":bounds.size.y,"path":path})
 if options.is_empty():_write("lod_motion.json",{"status":"NO_TREE_CANDIDATE"});return
 options.sort_custom(func(a,b):return a.height<b.height)
 var selected: Dictionary=options[0]
 var family: String=str(selected.path).split("/Hollow/")[1].split("/")[0]
 var prefix: String=str(main.get_node("Hollow").get_path())+"/"+family+"/"
 var meshes:=[];var settings:={}
 for mesh in geometry_states:
  if str(mesh.get_path()).begins_with(prefix):
   meshes.append(mesh)
   settings[mesh]=[mesh.visibility_range_begin_margin,mesh.visibility_range_end_margin,mesh.visibility_range_fade_mode]
 var centre: Vector3=selected.centre
 var boundary: float=selected.node.visibility_range_end
 var frames:=[]
 for mode in ["hard_switch","self_fade"]:
  for mesh in meshes:
   mesh.visibility_range_begin_margin=.1*mesh.visibility_range_begin if mode=="self_fade" and mesh.visibility_range_begin>0 else 0
   mesh.visibility_range_end_margin=.1*mesh.visibility_range_end if mode=="self_fade" and mesh.visibility_range_end>0 else 0
   mesh.visibility_range_fade_mode=GeometryInstance3D.VISIBILITY_RANGE_FADE_SELF if mode=="self_fade" else GeometryInstance3D.VISIBILITY_RANGE_FADE_DISABLED
  for index in 18:
   var distance: float=boundary+lerpf(-4.0,4.0,index/17.0)
   target=Transform3D(Basis.IDENTITY,centre+Vector3(0,.3,distance)).looking_at(centre,Vector3.UP)
   for frame in (30 if index==0 else 3):await RenderingServer.frame_post_draw
   var name_: String="lod_%s_%02d.png"%[mode,index]
   get_viewport().get_texture().get_image().save_png(output+"/"+name_)
   frames.append({"mode":mode,"frame":index,"distance":distance,"camera":_xyz(camera.global_position),"file":name_})
 for mesh in settings:
  mesh.visibility_range_begin_margin=settings[mesh][0];mesh.visibility_range_end_margin=settings[mesh][1];mesh.visibility_range_fade_mode=settings[mesh][2]
 _write("lod_motion.json",{"status":"CAPTURED_REQUIRES_VISUAL_REVIEW","family":family,"selected_mesh":selected.path,"boundary":boundary,"tree_height":selected.height,"frames":frames,"note":"Runtime comparison of the exact ArtLibrary UNNAMED_LOD_STIPPLE=0/1 visibility fade and margin settings; scatter unchanged; no setting adopted"})
