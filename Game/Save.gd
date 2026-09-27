extends Node
var savePathPreset:String = "res://Presets/"
var savePathCharacter:String = "res://SavedCharacter/"
var ext:String = ".save"
func _ready():
	pass

func _saveCharacter(n:String,data:Dictionary):
	var fileSave = FileAccess.open(savePathCharacter+n+ext,FileAccess.WRITE)
	fileSave.store_line(JSON.stringify(data))

func _loadCharacter(n):
	var fileSave = FileAccess.open(savePathCharacter+n+ext,FileAccess.READ)
	var charData:Dictionary = JSON.parse_string(fileSave.get_line())
	return charData


func _savePreset(n:String,data:Dictionary):
	var fileSave = FileAccess.open(savePathPreset+n+ext,FileAccess.WRITE)
	fileSave.store_line(JSON.stringify(data))

func _loadPreset(n):
	var fileSave = FileAccess.open(savePathPreset+n+ext,FileAccess.READ)
	var charData:Dictionary = JSON.parse_string(fileSave.get_line())
	return charData

