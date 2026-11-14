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
const YARD_SLOTS := [
	Vector2(1.5, 1.5), Vector2(4.5, 1.5),
	Vector2(1.5, 4.5), Vector2(4.5, 4.5),
]

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
	return cell_size() * 0.33


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
		var angle := TAU * float(color) / 4.0 + TAU * float(token_index) / 18.0
		return center + Vector2(cos(angle), sin(angle)) * cell_size() * 0.33
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
	var occupancy := {}
	var placements: Array = []

	for seat in game.seats:
		var tokens: Array = seat["tokens"]
		for index in tokens.size():
			var pos := token_position(seat, index)
			var key := pos.round()
			var count := int(occupancy.get(key, 0))
			occupancy[key] = count + 1
			placements.append({
				"pos": pos, "count": count, "color": int(seat["color"]),
				"index": index, "seat": int(seat["index"]),
			})

	for entry in placements:
		var pos: Vector2 = entry["pos"]
		var count := int(entry["count"])
		if count > 0:
			pos += Vector2(c * 0.16 * count, c * 0.09 * count)
		var color: Color = COLORS[int(entry["color"])]
		var is_legal := int(entry["seat"]) == game.active_seat and legal.has(int(entry["index"]))
		draw_circle(pos, radius * 1.12, INK)
		draw_circle(pos, radius, color)
		draw_circle(pos + Vector2(-radius * 0.22, -radius * 0.22), radius * 0.34, Color(1, 1, 1, 0.55))
		if is_legal:
			draw_arc(pos, radius * 1.5, 0.0, TAU, 40, Color("#FFFFFF"), maxf(2.0, c * 0.09))
			draw_arc(pos, radius * 1.72, 0.0, TAU, 40, Color("#2A2620"), maxf(1.0, c * 0.035))


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
