class_name BoardView
extends Node2D

## Draws the 15x15 Ludo board and every token, and turns taps into token selections.

signal token_tapped(token_index: int)

const COLORS := [
	Color("#F02B33"), # red    (top-left)
	Color("#078EFF"), # blue   (top-right)
	Color("#1FCA01"), # green  (bottom-left)
	Color("#FFB407"), # orange (bottom-right)
]
const BOARD_BG := Color("#FBF6EA")
const CELL_BG := Color("#FFFFFF")
const CELL_LINE := Color("#C9C2B2")
const INK := Color("#3A3630")

var game: LudoEngine
var legal: Array = []
var board_size: float = 600.0
var highlight: int = -1


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


func cell_rect(x: int, y: int) -> Rect2:
	var c := cell_size()
	return Rect2(Vector2(x * c, y * c), Vector2(c, c))


func cell_center(x: int, y: int) -> Vector2:
	var c := cell_size()
	return Vector2((x + 0.5) * c, (y + 0.5) * c)


func token_radius() -> float:
	return cell_size() * 0.31


func _draw() -> void:
	if game == null:
		return
	var c := cell_size()
	draw_rect(Rect2(Vector2.ZERO, Vector2(board_size, board_size)), BOARD_BG, true)

	_draw_yards(c)
	_draw_track(c)
	_draw_home_columns(c)
	_draw_center(c)
	_draw_tokens(c)


func _draw_yards(c: float) -> void:
	for color in 4:
		var origin := Topology.yard_origin(color)
		var outer := Rect2(Vector2(origin.x * c, origin.y * c), Vector2(6 * c, 6 * c))
		draw_rect(outer, COLORS[color], true)
		var inner := Rect2(outer.position + Vector2(c, c), Vector2(4 * c, 4 * c))
		draw_rect(inner, BOARD_BG, true)
		for token in 4:
			var slot := Topology.base_slot(color, token)
			var center := cell_center(slot.x, slot.y)
			draw_circle(center, token_radius(), COLORS[color])
			draw_circle(center, token_radius() * 0.45, BOARD_BG)


func _draw_track(c: float) -> void:
	for cell in Topology.TRACK:
		var x := Topology.track_x(cell)
		var y := Topology.track_y(cell)
		var rect := cell_rect(x, y)
		var fill := CELL_BG
		if Topology.is_start_cell(cell):
			var owner_color := _color_of_start(cell)
			fill = COLORS[owner_color]
		draw_rect(rect, fill, true)
		draw_rect(rect, CELL_LINE, false, maxf(1.0, c * 0.05))
		if Topology.is_safe_cell(cell) and not Topology.is_start_cell(cell):
			draw_circle(rect.get_center(), c * 0.14, INK)


func _draw_home_columns(c: float) -> void:
	for color in 4:
		for index in Topology.HOME_COL:
			var rect := cell_rect(Topology.HOME_COL_X[color][index], Topology.HOME_COL_Y[color][index])
			draw_rect(rect, COLORS[color], true)
			draw_rect(rect, CELL_LINE, false, maxf(1.0, c * 0.05))


func _draw_center(c: float) -> void:
	var origin := Vector2(Topology.CENTER - 1, Topology.CENTER - 1) * c
	var corner := Vector2(3 * c, 3 * c)
	var top_left := origin
	var top_right := origin + Vector2(corner.x, 0)
	var bottom_right := origin + corner
	var bottom_left := origin + Vector2(0, corner.y)
	var mid_top := origin + Vector2(corner.x * 0.5, 0)
	var mid_right := origin + Vector2(corner.x, corner.y * 0.5)
	var center := origin + corner * 0.5

	# Centre home triangles: left = red, top = blue, right = orange, bottom = green.
	draw_colored_polygon(PackedVector2Array([top_left, bottom_left, center]), COLORS[0])
	draw_colored_polygon(PackedVector2Array([top_left, top_right, center]), COLORS[1])
	draw_colored_polygon(PackedVector2Array([top_right, bottom_right, center]), COLORS[3])
	draw_colored_polygon(PackedVector2Array([bottom_left, bottom_right, center]), COLORS[2])
	draw_rect(Rect2(origin, corner), CELL_LINE, false, maxf(1.0, c * 0.05))


func _draw_tokens(c: float) -> void:
	# Occupancy map so stacked tokens fan out a little instead of hiding each other.
	var occupancy := {}
	var placements: Array = []
	for seat in game.seats:
		var color := int(seat["color"])
		var tokens: Array = seat["tokens"]
		for index in tokens.size():
			var progress := int(tokens[index])
			var pos: Vector2
			if progress < 0:
				var slot := Topology.base_slot(color, index)
				pos = cell_center(slot.x, slot.y)
			elif progress >= Topology.PROGRESS_HOME:
				pos = cell_center(Topology.CENTER, Topology.CENTER)
			else:
				var cell := Topology.progress_cell(color, progress)
				pos = cell_center(cell.x, cell.y)
			var key := pos.round()
			var count := int(occupancy.get(key, 0))
			occupancy[key] = count + 1
			placements.append({"pos": pos, "count": count, "color": color, "index": index,
				"seat": int(seat["index"])})

	for entry in placements:
		var pos: Vector2 = entry["pos"]
		var count := int(entry["count"])
		if count > 0:
			var spread := c * 0.18 * count
			pos += Vector2(spread, spread * 0.5)
		var radius := token_radius()
		var color: Color = COLORS[int(entry["color"])]
		var is_legal := int(entry["seat"]) == game.active_seat and legal.has(int(entry["index"]))
		draw_circle(pos, radius, color)
		draw_circle(pos, radius * 0.5, color.lightened(0.45))
		draw_arc(pos, radius, 0.0, TAU, 24, INK, maxf(1.0, c * 0.045))
		if is_legal:
			draw_arc(pos, radius * 1.45, 0.0, TAU, 32, Color("#111111"), maxf(2.0, c * 0.08))


func _color_of_start(cell: int) -> int:
	for color in 4:
		if Topology.START_OFFSETS[color] == cell:
			return color
	return 0


func _input(event: InputEvent) -> void:
	if game == null or legal.is_empty():
		return
	var pos := Vector2.INF
	if event is InputEventScreenTouch and event.pressed:
		pos = event.position
	elif event is InputEventMouseButton and event.pressed and event.button_index == MOUSE_BUTTON_LEFT:
		pos = event.position
	if pos == Vector2.INF:
		return
	var local := get_global_transform_with_canvas().affine_inverse() * pos
	var radius := token_radius() * 1.9
	var best := -1
	var best_distance := radius
	for seat in game.seats:
		if int(seat["index"]) != game.active_seat:
			continue
		var tokens: Array = seat["tokens"]
		for index in legal.size():
			var token_index := int(legal[index])
			var progress := int(tokens[token_index])
			var target: Vector2
			if progress < 0:
				var slot := Topology.base_slot(int(seat["color"]), token_index)
				target = cell_center(slot.x, slot.y)
			elif progress >= Topology.PROGRESS_HOME:
				target = cell_center(Topology.CENTER, Topology.CENTER)
			else:
				var cell := Topology.progress_cell(int(seat["color"]), progress)
				target = cell_center(cell.x, cell.y)
			var distance := local.distance_to(target)
			if distance < best_distance:
				best_distance = distance
				best = token_index
	if best >= 0:
		token_tapped.emit(best)
