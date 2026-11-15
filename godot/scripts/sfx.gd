class_name Sfx
extends RefCounted

## Plays a sound through the "Sound" autoload without depending on the autoload
## being registered as a global identifier (keeps scripts testable head-less).


static func play(effect: String, volume_db: float = -4.0) -> void:
	var loop := Engine.get_main_loop()
	if loop == null:
		return
	var tree := loop as SceneTree
	if tree == null or tree.root == null:
		return
	var node := tree.root.get_node_or_null("Sound")
	if node != null and node.has_method("play"):
		node.play(effect, volume_db)
