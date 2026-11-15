extends Node

## Tiny sound board: one voice pool, preloaded effects, fire-and-forget playback.

const STREAMS := {
	"click": preload("res://audio/click.wav"),
	"dice_roll": preload("res://audio/dice_roll.wav"),
	"dice_land": preload("res://audio/dice_land.wav"),
	"move": preload("res://audio/move.wav"),
	"capture": preload("res://audio/capture.wav"),
	"home": preload("res://audio/home.wav"),
	"turn": preload("res://audio/turn.wav"),
	"win": preload("res://audio/win.wav"),
}

const VOICES := 8

var _players: Array[AudioStreamPlayer] = []
var _next := 0


func _ready() -> void:
	for i in VOICES:
		var player := AudioStreamPlayer.new()
		player.bus = "Master"
		add_child(player)
		_players.append(player)


func play(effect: String, volume_db: float = -4.0) -> void:
	if not STREAMS.has(effect):
		return
	var player: AudioStreamPlayer = _players[_next]
	_next = (_next + 1) % _players.size()
	player.stream = STREAMS[effect]
	player.volume_db = volume_db
	player.play()
