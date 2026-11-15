class_name BoardView
extends Node2D

## Draws the board artwork (assets/board.png), the turn glow, and the tokens.
## Token movement is animated by pushing a list of points into animate_token().

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

## Peg positions measured from the board artwork (cells, relative to the yard origin).
const YARD_SLOTS := [
	Vector2(2.19, 2.23), Vector2(3.72, 2.21),
	Vector2(2.19, 3.77), Vector2(3.72, 3.76),
]
## Direction each colour's finished tokens stack in, from the board centre.
const HOME_DIR := [Vector2(-1, 0), Vector2(0, -1), Vector2(1, 0), Vector2(0, 1)]

var game: LudoEngine
var legal: Array = []
var board_size := 600.0

var _animation := {}
var _pulse := 0.0


func _ready() -> void:
	set_process_input(true)


func _process(delta: float) -> void:
	_pulse += delta
	if not _animation.is_empty():
		_animation["elapsed"] = float(_animation["elapsed"]) + delta
		if float(_animation["elapsed"]) >= float(_animation["duration"]):
			_animation = {}
		queue_redraw()
	elif game != null and not game.is_over():
		# Keeps the "it's your turn" glow breathing.
		queue_redraw()


func set_game(engine: LudoEngine) -> void:
	game = engine
	legal = []
	_animation = {}
	queue_redraw()


func set_legal(tokens: Array) -> void:
	legal = tokens.duplicate()
	queue_redraw()


func cell_size() -> float:
	return board_size / float(Topology.GRID)


func token_radius() -> float:
	return cell_size() * 0.42


func cell_center(x: int, y: int) -> Vector2:
	var c := cell_size()
	return Vector2((x + 0.5) * c, (y + 0.5) * c)


func yard_position(color: int, token_index: int) -> Vector2:
	var origin := Topology.yard_origin(color)
	var slot: Vector2 = YARD_SLOTS[token_index % 4]
	return Vector2(origin.x + slot.x, origin.y + slot.y) * cell_size()


## Centre of any progress value: ring cell, home column cell or the centre fan.
func progress_position(color: int, progress: int) -> Vector2:
	if progress < 0:
		return yard_position(color, 0)
	if progress >= Topology.PROGRESS_HOME:
		var center := cell_center(Topology.CENTER, Topology.CENTER)
		var direction: Vector2 = HOME_DIR[color]
		return center + direction * cell_size() * 0.34
	var cell := Topology.progress_cell(color, progress)
	return cell_center(cell.x, cell.y)


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


# ------------------------------------------------------------------ animation

func animate_token(seat_index: int, token_index: int, points: Array, duration: float) -> void:
	if points.is_empty():
		return
	_animation = {
		"seat": seat_index,
		"token": token_index,
		"points": points,
		"elapsed": 0.0,
		"duration": maxf(duration, 0.08),
	}
	queue_redraw()


func is_animating() -> bool:
	return not _animation.is_empty()


func animated_position() -> Vector2:
	if _animation.is_empty():
		return Vector2.ZERO
	var points: Array = _animation["points"]
	var count := points.size()
	if count == 1:
		return points[0]
	var t := clampf(float(_animation["elapsed"]) / float(_animation["duration"]), 0.0, 1.0)
	var span := float(count - 1)
	var index := int(t * span)
	if index >= count - 1:
		return points[count - 1]
	var fraction := t * span - float(index)
	return (points[index] as Vector2).lerp(points[index + 1], fraction)


# --------------------------------------------------------------------- render

func _draw() -> void:
	if game == null:
		return
	draw_texture_rect(BOARD_TEXTURE, Rect2(Vector2.ZERO, Vector2(board_size, board_size)), false)
	_clean_yards()
	_draw_turn_glow()
	_draw_tokens()
	_draw_moving_token()


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


func _draw_turn_glow() -> void:
	if game.is_over() or game.active_seat < 0:
		return
	var seat: Dictionary = game.seats[game.active_seat]
	var color: Color = COLORS[int(seat["color"])]
	var origin := Topology.yard_origin(int(seat["color"]))
	var c := cell_size()
	var rect := Rect2(Vector2(origin.x * c, origin.y * c), Vector2(6 * c, 6 * c)).grow(-c * 0.12)
	var glow := 0.45 + 0.4 * sin(_pulse * 3.4)
	draw_rect(rect, Color(color.r, color.g, color.b, glow * 0.30), false, maxf(3.0, c * 0.14))
	draw_rect(rect.grow(-c * 0.10), Color(1, 1, 1, glow * 0.55), false, maxf(1.5, c * 0.05))


func _draw_tokens() -> void:
	var c := cell_size()
	var radius := token_radius()
	var moving_seat := -1
	var moving_token := -1
	if not _animation.is_empty():
		moving_seat = int(_animation["seat"])
		moving_token = int(_animation["token"])

	var groups := {}
	var order := []
	for seat in game.seats:
		var tokens: Array = seat["tokens"]
		for index in tokens.size():
			if int(seat["index"]) == moving_seat and index == moving_token:
				continue
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
			var is_legal := int(entry["seat"]) == game.active_seat and legal.has(int(entry["index"]))
			_draw_token(pos, draw_radius, COLORS[int(entry["color"])], is_legal)


func _draw_moving_token() -> void:
	if _animation.is_empty():
		return
	var seat: Dictionary = game.seats[int(_animation["seat"])]
	var t := clampf(float(_animation["elapsed"]) / float(_animation["duration"]), 0.0, 1.0)
	var hop := 1.0 + 0.18 * sin(t * PI)
	_draw_token(animated_position(), token_radius() * hop, COLORS[int(seat["color"])], false)


func _draw_token(pos: Vector2, radius: float, color: Color, highlighted: bool) -> void:
	var c := cell_size()
	if highlighted:
		draw_circle(pos, radius * 1.16, Color("#FFFFFF"))
	draw_circle(pos, radius, color)
	draw_circle(pos + Vector2(-radius * 0.26, -radius * 0.26), radius * 0.32, Color(1, 1, 1, 0.5))
	draw_arc(pos, radius, 0.0, TAU, 32, INK, maxf(1.0, c * 0.04))


# ---------------------------------------------------------------------- input

func _input(event: InputEvent) -> void:
	if game == null or legal.is_empty() or is_animating():
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
