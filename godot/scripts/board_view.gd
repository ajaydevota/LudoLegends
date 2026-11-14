class_name BoardView
extends Node2D

## Draws the board artwork (assets/board.png) and the tokens on top of it.

signal token_tapped(token_index: int)

const BOARD_TEXTURE: Texture2D = preload("res://assets/board.png")
const COLORS := [
	Color("#F02B33"), # red    (top-left)
	Color("#078EFF"), # blue   (top-right)
	Color("#1FCA01"), # green  (bottom-left)
	Color("#FFB407"), # orange (bottom-right)
]
const CREAM := Color("#FBF6EA")
const INK := Color("#2A2620")
## Peg positions measured from the board artwork (cells, relative to the yard origin)
## so tokens sit exactly on the four pegs the image draws in every yard.
const YARD_SLOTS := [
	Vector2(2.19, 2.23), Vector2(3.72, 2.21),
	Vector2(2.19, 3.77), Vector2(3.72, 3.76),
]
## Direction each colour's finished tokens stack in, from the board centre.
const HOME_DIR := [Vector2(-1, 0), Vector2(0, -1), Vector2(1, 0), Vector2(0, 1)]

var game: LudoEngine
var legal: Array = []
var board_size := 600.0


func _ready() -> void:
	set_process_input(true)


func set_game(engine: LudoEngine) -> void:
	game = engine
	legal = []
	queue_redraw()


func set_legal(tokens: Array) -> void:
	legal = tokens.duplicate()
	queue_redraw()


func cell_size() -> float:
	return board_size / float(Topology.GRID)


func token_radius() -> float:
	# Same size as the pegs printed on the board, so a token never spills over a cell edge.
	return cell_size() * 0.42


func cell_center(x: int, y: int) -> Vector2:
	var c := cell_size()
	return Vector2((x + 0.5) * c, (y + 0.5) * c)


func yard_position(color: int, token_index: int) -> Vector2:
	var origin := Topology.yard_origin(color)
	var slot: Vector2 = YARD_SLOTS[token_index % 4]
	return Vector2(origin.x + slot.x, origin.y + slot.y) * cell_size()


func token_position(seat: Dictionary, token_index: int) -> Vector2:
	var color := int(seat["color"])
	var progress := int(seat["tokens"][token_index])
	if progress < 0:
		return yard_position(color, token_index)
	if progress >= Topology.PROGRESS_HOME:
		var center := cell_center(Topology.CENTER, Topology.CENTER)
		var direction: Vector2 = HOME_DIR[color]
		return center + direction * cell_size() * (0.34 + 0.22 * float(token_index))
	var cell := Topology.progress_cell(color, progress)
	return cell_center(cell.x, cell.y)


func _draw() -> void:
	if game == null:
		return
	draw_texture_rect(BOARD_TEXTURE, Rect2(Vector2.ZERO, Vector2(board_size, board_size)), false)
	_clean_yards()
	_draw_tokens()


## The artwork already shows four coloured pegs in each yard; paint the inner box
## in board cream so only the live tokens are visible.
func _clean_yards() -> void:
	var c := cell_size()
	var box := StyleBoxFlat.new()
	box.bg_color = CREAM
	box.set_corner_radius_all(int(c * 0.75))
	for color in 4:
		var origin := Topology.yard_origin(color)
		var rect := Rect2(Vector2((origin.x + 1) * c, (origin.y + 1) * c), Vector2(4 * c, 4 * c))
		draw_style_box(box, rect)


func _draw_tokens() -> void:
	var c := cell_size()
	var radius := token_radius()
	var groups := {}
	var order := []

	for seat in game.seats:
		var tokens: Array = seat["tokens"]
		for index in tokens.size():
			var pos := token_position(seat, index)
			var key := pos.round()
			if not groups.has(key):
				groups[key] = []
				order.append(key)
			groups[key].append({
				"pos": pos, "color": int(seat["color"]),
				"index": index, "seat": int(seat["index"]),
			})

	for key in order:
		var entries: Array = groups[key]
		var count := entries.size()
		for i in count:
			var entry: Dictionary = entries[i]
			var pos: Vector2 = entry["pos"]
			var draw_radius := radius
			if count > 1:
				var angle := TAU * float(i) / float(count) - PI * 0.5
				pos += Vector2(cos(angle), sin(angle)) * c * 0.15
				draw_radius = radius * 0.7
			var color: Color = COLORS[int(entry["color"])]
			var is_legal := int(entry["seat"]) == game.active_seat and legal.has(int(entry["index"]))
			if is_legal:
				draw_circle(pos, draw_radius * 1.16, Color("#FFFFFF"))
			draw_circle(pos, draw_radius, color)
			draw_circle(pos + Vector2(-draw_radius * 0.26, -draw_radius * 0.26), draw_radius * 0.32,
				Color(1, 1, 1, 0.5))
			draw_arc(pos, draw_radius, 0.0, TAU, 32, INK, maxf(1.0, c * 0.04))


func _input(event: InputEvent) -> void:
	if game == null or legal.is_empty():
		return
	var screen_pos := Vector2.INF
	if event is InputEventScreenTouch and event.pressed:
		screen_pos = event.position
	elif event is InputEventMouseButton and event.pressed and event.button_index == MOUSE_BUTTON_LEFT:
		screen_pos = event.position
	if screen_pos == Vector2.INF:
		return

	var local := get_global_transform_with_canvas().affine_inverse() * screen_pos
	var seat: Dictionary = game.seats[game.active_seat]
	var best := -1
	var best_distance := token_radius() * 2.2
	for index in legal.size():
		var token_index := int(legal[index])
		var distance := local.distance_to(token_position(seat, token_index))
		if distance < best_distance:
			best_distance = distance
			best = token_index
	if best >= 0:
		token_tapped.emit(best)
