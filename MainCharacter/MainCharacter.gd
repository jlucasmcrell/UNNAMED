@tool
extends Node3D

@onready var sk:Skeleton3D = $MainRig/Skeleton3D
var parts:Array = ["head","torso","arms","legs"]
var bonesMod:Array = ["spine_3","spine_2","spine_1","thigh_l","thigh_r","shin_l","forearm_l","forearm_r","shin_r","upper_arm_l","upper_arm_r","foot_l","foot_r","shoulder_l","shoulder_r"]

var faceCam:Camera3D ;var faceViewport:SubViewport

@onready var matSkin = ResourceLoader.load("res://MainCharacter/Material/Skin.tres")
@onready var matCornea = ResourceLoader.load("res://MainCharacter/Material/Cornea.tres")
@onready var matEye = ResourceLoader.load("res://MainCharacter/Material/Eye.tres")
@onready var matUnderwear = ResourceLoader.load("res://MainCharacter/Material/Underwear.tres")
@onready var matHair = ResourceLoader.load("res://MainCharacter/Material/Hair.tres")
var headMat:Material; var head:MeshInstance3D ; var currentHair:MeshInstance3D ; var currentBeard:MeshInstance3D
var torsoMat:Material; 
var armsMat:Material; var legsMat:Material ; var beardMat:Material ; var hairMat:Material
var arrayMat:Array=[]
var blinkDelta:float=0.0; var blink:bool = false;

var characterData:Dictionary={
	colors={
		hair={},
		beard={}
	},
	shapes={},
	meshes={},
	materialParameters={}
}
var preset:Dictionary={
	bonesY={},
	shapes={},
	sliders={},
	colorPresets={}
}
func _ready():
#	if not Engine.editor_hint:	
	$AnimationPlayer.get_animation("idle").loop_mode=Animation.LOOP_LINEAR
	$AnimationPlayer.play("idle")
#	$AnimationPlayer.play("idle2")
	_generate()
	$MouseTarget.set_as_top_level(true)
	faceCam=$faceViewport/faceCam
	faceViewport = $faceViewport


func _generate():
	$MainRig/Skeleton3D/dummy.queue_free()
	for p in parts:
		var scenePart:Node3D = ResourceLoader.load("res://MainCharacter/Mesh/Parts/"+p+".glb").instantiate()
		var mesh:MeshInstance3D = scenePart.get_child(0).get_child(0).duplicate()
		
		#----------------------Assign material
		var mat:ShaderMaterial = matSkin.duplicate()
		mesh.set_surface_override_material(0,mat)
		#----------------------MESH RENAME BECAUSE IT WAS IMPOSSIBLE TO IMPORT IT AS "HEAD"
		if mesh.name=="head":
#			mesh.name="head"
			headMat=mat
			mat.set_shader_parameter("hairMask",ResourceLoader.load("res://MainCharacter/Material/Textures/subHair/subHair1_mask.jpg"))
		else:
			var whiteMask = ResourceLoader.load("res://MainCharacter/Material/Textures/body/null_white.jpg")
			mat.set_shader_parameter("hairMask",whiteMask)
			mat.set_shader_parameter("subBeard",whiteMask)
			mat.set_shader_parameter("darkSkinMask",ResourceLoader.load("res://MainCharacter/Material/Textures/body/null_black.jpg"))
			mat.set_shader_parameter("eyebrows",whiteMask)
			mat.set_shader_parameter("facePaint",whiteMask)
		if mesh.name=="torso":
			torsoMat=mat
		if mesh.name=="arms":
			armsMat=mat
		if mesh.name=="legs":
			legsMat=mat
		#Assign textures to material
		mat.set_shader_parameter("albedo",ResourceLoader.load("res://MainCharacter/Material/Textures/body/"+p+"_a.jpg"))
		mat.set_shader_parameter("ors",ResourceLoader.load("res://MainCharacter/Material/Textures/body/"+p+"_orsc.jpg"))
		mat.set_shader_parameter("normal",ResourceLoader.load("res://MainCharacter/Material/Textures/body/"+p+"_n.jpg"))
		mat.set_shader_parameter("normalDetail",ResourceLoader.load("res://MainCharacter/Material/Textures/body/"+p+"_n_detail.jpg"))
		
		
		
		if p =="head":
			mesh.set_surface_override_material(1,matEye)
			mesh.set_surface_override_material(2,matCornea)
		if p =="legs":
			mesh.set_surface_override_material(1,matUnderwear)
		sk.add_child(mesh)
	#--HAIR
	head=sk.get_node("head")
	arrayMat=[headMat,torsoMat,armsMat,legsMat]

func _setMaterialParameter(param:String,part:String,col):
	var mat:ShaderMaterial
	if part=="eyes":
		mat=matEye
	elif part=="hair":
		mat=hairMat
	elif part=="beard":
		mat=beardMat
	else:
		mat=headMat
	mat.set_shader_parameter(param,col)
	
func _setParameterTexture(v,part,prop):
	var mat = sk.get_node(part).get_surface_override_material(0)
	# Sliders pass floats, and Godot 4's str(1.0) is "1.0", not Godot 3's "1".
	mat.set_shader_parameter(prop,ResourceLoader.load("res://MainCharacter/Material/Textures/"+prop+"/"+prop+str(int(v))+"_mask.jpg"))

func _setChubbiness(v):
	for mesh in sk.get_children():
		if v>=0 and v<=0.5:
			mesh.set("blend_shapes/skinny",remap(v,0,0.5,1,0))
			mesh.set("blend_shapes/chubby",0)
		elif v>0.5 and v<=1:
			mesh.set("blend_shapes/chubby",remap(v,0.5,1,0,1))
			mesh.set("blend_shapes/skinny",0)
		for i in arrayMat.size():
			if v>=0.5:
				var value = remap(v,0.5,1,1,0)
				arrayMat[i].set_shader_parameter("normalBlend",value)
			else:
				var value = remap(v,0,0.5,0.5,1)
				arrayMat[i].set_shader_parameter("normalBlend",value)
				

func _setBlendShape(part:String,prop:String,v:float):
	await get_tree().process_frame
	var n = sk.find_child(part+"?",true,false)
	n.set("blend_shapes/"+prop,v)


func _setMesh(part,v):
	var n = sk.find_child(part+"?",true,false)
	if n:
		n.queue_free()
		await n.tree_exited
	
	
	var scene = ResourceLoader.load("res://MainCharacter/Mesh/Parts/"+part+"/"+part+str(int(v))+".glb").instantiate()
	var mesh = scene.get_child(0).get_child(0).duplicate()
	match part:
		"hair":
			currentHair=mesh
			if !hairMat:
				hairMat = matHair.duplicate()
			
			mesh.set_surface_override_material(0,hairMat)
			if mesh.name=="hair3":
				_setParameterTexture(v,"head","subHair")
			elif mesh.name=="hair0":
				_setParameterTexture(v,"head","subHair")
			else:
				_setParameterTexture(1,"head","subHair")
		"beard":
			currentBeard=mesh
			if !beardMat:
				beardMat=matHair.duplicate()
			mesh.set_surface_override_material(0,beardMat)	

	sk.add_child(mesh)
	_matchBlendShapes(mesh)
	
func _matchBlendShapes(mesh):
	for bs in head.mesh.get_blend_shape_count():
		var bsName = head.mesh.get_blend_shape_name(bs)
		mesh.set("blend_shapes/"+bsName,head.get("blend_shapes/"+bsName))


func _setSkinColor(c):
	c = _convertColorFromJson(c)	
	var sum = c[0]+c[1]+c[2]
	var roughTweak = remap(sum,0,3,1,0.5)
	for mat in arrayMat:
		print(mat)
		mat.set_shader_parameter("skinTone",c)
		mat.set_shader_parameter("rPunch",roughTweak)	



func _on_blinkTimer_timeout():
	$blinkAnim.start()
	randomize()
	$blinkTimer.wait_time=randf_range(2,4)
	


func _on_blinkAnim_timeout():
	if blinkDelta<=0.1:
		if blink:
			$blinkAnim.stop()
		blink=false	
	if blinkDelta>=1.0:
		blink=true
	if !blink:
		blinkDelta+=0.2
	else:
		blinkDelta-=0.2
	head.set("blend_shapes/blink",blinkDelta)

func _setBone(b:String,data):
	var poses := BoneCustomPoses.of(sk)
	var t:Transform3D
	var bone = sk.find_bone(b)
	t=t.translated(Vector3(0,data.bonesY[b],0))
	poses.set_bone_custom_pose(bone,t)
	var t4 = poses.get_bone_custom_pose(sk.find_bone("thigh_r")) *poses.get_bone_custom_pose(sk.find_bone("shin_r"))
	poses.set_bone_custom_pose(sk.find_bone("spine"),t4)
	


func _saveBlendShapes():
	for b in head.mesh.get_blend_shape_count():
		var bs = head.mesh.get_blend_shape_name(b)
		if bs!="chubby" and bs!="blink":
			preset.shapes[bs]= head.get("blend_shapes/"+bs)
	for b in sk.get_node("torso").mesh.get_blend_shape_count():
		var bs = sk.get_node("torso").mesh.get_blend_shape_name(b)
		preset.shapes[bs]=sk.get_node("torso").get("blend_shapes/"+bs)
		characterData.shapes[bs]=sk.get_node("torso").get("blend_shapes/"+bs)
func _saveBones():
	var poses := BoneCustomPoses.of(sk)
	for b in bonesMod:
		preset.bonesY[b] = poses.get_bone_custom_pose(sk.find_bone(b)).origin.y
	preset["headBone"] = _transformToJson(poses.get_bone_custom_pose(sk.find_bone("head")))
	
func _load(data):
	_loadBones(data)
	_loadBlendShapes(data)
	
func _loadBones(data):
	for b in bonesMod:
		_setBone(b,data)
		
	BoneCustomPoses.of(sk).set_bone_custom_pose(sk.find_bone("head"),_transformFromJson(data.headBone))

# The head bone's custom pose is kept as Godot 3's str(Transform) wrote it - the basis row by row,
# " - ", then the origin - so the shipped presets and saves still load. Godot 4's str() differs.
func _transformToJson(t:Transform3D) -> String:
	var b := t.basis
	return "%s, %s, %s, %s, %s, %s, %s, %s, %s - %s, %s, %s" % [b.x.x, b.y.x, b.z.x, b.x.y, b.y.y, b.z.y, b.x.z, b.y.z, b.z.z, t.origin.x, t.origin.y, t.origin.z]

func _transformFromJson(s:String) -> Transform3D:
	var parts = s.split(" - ")
	var v = Array(parts[0].split(",")).map(func(n): return n.to_float())
	var o = Array(parts[1].split(",")).map(func(n): return n.to_float()) if parts.size() > 1 else [0.0, 0.0, 0.0]
	var rows = Basis(Vector3(v[0],v[1],v[2]), Vector3(v[3],v[4],v[5]), Vector3(v[6],v[7],v[8]))
	return Transform3D(rows.transposed(), Vector3(o[0],o[1],o[2]))

func _loadBlendShapes(data):
	for b in data.shapes:
		head.set("blend_shapes/"+b,data.shapes[b])

func _loadFeatures():
	_setSkinColor(characterData.colors.skinColor)
	_setChubbiness(characterData.shapes.chubby)
	
	_setMesh("beard",characterData.meshes.beard)
	_setMesh("hair",characterData.meshes.hair)
	
	_setParameterTexture(characterData.materialParameters["beard"],"head","beard")
	_setParameterTexture(characterData.materialParameters["facePaint"],"head","facePaint")
	_setParameterTexture(characterData.materialParameters["eyebrows"],"head","eyebrows")
	
	_setMaterialParameter("eyeBrowHeight","head",characterData.materialParameters["eyeBrowHeight"])
	_setMaterialParameter("facePaintColor","head",_convertColorFromJson(characterData.colors["facePaintColor"]))
	_setMaterialParameter("rootColor","hair",_convertColorFromJson(characterData.colors.hair.rootColor))
	_setMaterialParameter("hairColor","hair",_convertColorFromJson(characterData.colors.hair.hairColor))
	_setMaterialParameter("tipColor","hair",_convertColorFromJson(characterData.colors.hair.tipColor))
	_setMaterialParameter("rootColor","beard",_convertColorFromJson(characterData.colors.beard.rootColor))
	_setMaterialParameter("hairColor","beard",_convertColorFromJson(characterData.colors.beard.hairColor))
	_setMaterialParameter("tipColor","beard",_convertColorFromJson(characterData.colors.beard.tipColor))
	_setMaterialParameter("eyeColor1","eyes",_convertColorFromJson(characterData.colors.eyeColor1))
	_setMaterialParameter("eyeColor1","eyes",_convertColorFromJson(characterData.colors.eyeColor1))
	_setMaterialParameter("eyeColor1","eyes",_convertColorFromJson(characterData.colors.eyeColor1))
	_setMaterialParameter("rootOffset","hair",characterData.materialParameters.hairRootOffset)
	_setMaterialParameter("tipOffset","hair",characterData.materialParameters.hairTipOffset)
	_setMaterialParameter("rootOffset","beard",characterData.materialParameters.beardRootOffset)
	_setMaterialParameter("tipOffset","beard",characterData.materialParameters.beardTipOffset)
	
	
func _saveCharacterData():
	_saveBones()
	_saveBlendShapes()
	characterData["bonesY"]=preset.bonesY
	characterData["shapes"]=preset.shapes
	characterData["headBone"]=_transformToJson(BoneCustomPoses.of(sk).get_bone_custom_pose(sk.find_bone("head")))
	characterData.colors["skinColor"] = headMat.get_shader_parameter("skinTone")
	characterData.colors["facePaintColor"] = headMat.get_shader_parameter("facePaintColor")
	characterData.colors.hair["rootColor"]=hairMat.get_shader_parameter("rootColor")
	characterData.colors.hair["hairColor"]=hairMat.get_shader_parameter("hairColor")
	characterData.colors.hair["tipColor"]=hairMat.get_shader_parameter("tipColor")
	characterData.colors.beard["rootColor"]=beardMat.get_shader_parameter("rootColor")
	characterData.colors.beard["hairColor"]=beardMat.get_shader_parameter("hairColor")
	characterData.colors.beard["tipColor"]=beardMat.get_shader_parameter("tipColor")
	characterData.colors["eyeColor1"]=matEye.get_shader_parameter("eyeColor1")
	characterData.colors["eyeColor2"]=matEye.get_shader_parameter("eyeColor1")
	
	characterData.meshes["hair"] = currentHair.name.substr(currentHair.name.length()-1)
	characterData.meshes["beard"] = currentBeard.name.substr(currentBeard.name.length()-1)
	
	characterData.materialParameters["eyebrows"]= _getStreamTexture("eyebrows")
	characterData.materialParameters["facePaint"]= _getStreamTexture("facePaint")
	characterData.materialParameters["beard"]= _getStreamTexture("beard")
	
	characterData.materialParameters["eyeBrowHeight"] = headMat.get_shader_parameter("eyeBrowHeight")
	characterData.materialParameters["hairTipOffset"] = hairMat.get_shader_parameter("tipOffset")
	characterData.materialParameters["hairRootOffset"] = hairMat.get_shader_parameter("rootOffset")
	characterData.materialParameters["beardTipOffset"] = beardMat.get_shader_parameter("tipOffset")
	characterData.materialParameters["beardRootOffset"] = beardMat.get_shader_parameter("rootOffset")
	
	Save._saveCharacter(characterData.name,characterData)



func _getStreamTexture(param):
	#res://.import/eyebrows1_mask.jpg-ab34963aa93c4c5a3912c2deb8558ff1.s3tc.stex
	var split =headMat.get_shader_parameter(param).get_load_path().split("_")[0]
	return _getLastCharacter(split)
	
func _getLastCharacter(string:String):
	return string.substr(string.length()-1)

func _convertColorFromJson(c):
	# Godot 3 saved colours as "r,g,b,a"; Godot 4's JSON writes "(r, g, b, a)".
	if typeof(c)==TYPE_STRING:
		var split = c.trim_prefix("(").trim_suffix(")").split(",")
		c=Color(split[0].to_float(),split[1].to_float(),split[2].to_float())
	return c
