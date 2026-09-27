extends Camera3D
var last_position:Vector2
@onready var character = GameInstance.currentPlayer
@onready var initPos:Vector3 = position
var zoomTarget:Vector3 ;var zoomOffset = Vector3(0,0.28,0) 
var zoomSpeed:float=0.2 
func _ready():
	if character:
		_setZoomTarget()
		
func _setZoomTarget():
	var y =character.get_node("MainRig/Skeleton3D/attachHead").transform.origin +zoomOffset
	zoomTarget=Vector3(0,y.y,y.z)
	
func _input(event):
	if !character:
		return
	
	if Input.is_action_just_pressed("rmb"):
		last_position = get_viewport().get_mouse_position()
		
	if Input.is_action_pressed("rmb"):
		var delta = get_viewport().get_mouse_position() - last_position
		last_position = get_viewport().get_mouse_position()
		character.rotate_y(delta.x*0.01)



	if Input.is_action_pressed("mWheelUp"):
		_setZoomTarget()
		if position.z>=0.3:
			last_position = get_viewport().get_mouse_position()
			position=lerp(position,zoomTarget,zoomSpeed)
		
	if Input.is_action_pressed("mWheelDown"):

		if position.z<=2:
			position=lerp(position,zoomTarget,-zoomSpeed)
