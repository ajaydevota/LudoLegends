class_name GameScreen
extends Control

## One match: board, dice, turn flow, result overlay.

signal exit_requested

const COLOR_NAMES := ["Red", "Blue", "Green", "Orange"]
const COLORS := [
	Color("#F02B33"), Color("#078EFF"), Color("#1FCA01"), Color("#FFB407"),
]
const INK := Color("#F4EEE4")
const MUTED := Color("#9C9285")
const GOLD := Color("#E9B44C")
const BG := Color("#12100E")
const CARD := Color("#1E1A16")

const TOP_BAR := 96.0
const BOTTOM_BAR := 118.0
const PAD := 12.0

var seat_count := 4
var human_seats := 1
var mode := "computer"

var game: LudoEngine
var view: BoardView
var status_label: Label
var roll_button: Button
var back_button: Button
var overlay: Control = null
var rng := RandomNumberGenerator.new()
var cooldown := 0.0
var pending_auto := -1


func setup(count: int, humans: int, game_mode: String) -> void:
	seat_count = count
	human_seats = humans
	mode = game_mode


func _ready() -> void:
	rng.randomize()
	_build_ui()
	_new_game()
	call_deferred("_layout")


func _build_ui() -> void:
	var bg := ColorRect.new()
	bg.color = BG
	bg.mouse_filter = Control.MOUSE_FILTER_IGNORE
	add_child(bg)
	bg.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)

	view = BoardView.new()
	add_child(view)
	view.token_tapped.connect(_on_token_tapped)

	back_button = Button.new()
	back_button.text = "←"
	back_button.add_theme_font_size_override("font_size", 24)
	back_button.add_theme_color_override("font_color", INK)
	back_button.add_theme_stylebox_override("normal", _chip(Color(1, 1, 1, 0.08)))
	back_button.add_theme_stylebox_override("hover", _chip(Color(1, 1, 1, 0.16)))
	back_button.add_theme_stylebox_override("pressed", _chip(Color(1, 1, 1, 0.24)))
	back_button.pressed.connect(func() -> void: exit_requested.emit())
	add_child(back_button)

	status_label = Label.new()
	status_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	status_label.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	status_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	status_label.add_theme_font_size_override("font_size", 22)
	status_label.add_theme_color_override("font_color", INK)
	add_child(status_label)

	roll_button = Button.new()
	roll_button.text = "ROLL"
	roll_button.add_theme_font_size_override("font_size", 30)
	roll_button.add_theme_color_override("font_color", Color("#FFFFFF"))
	roll_button.add_theme_stylebox_override("normal", _chip(GOLD.darkened(0.05), 24))
	roll_button.add_theme_stylebox_override("hover", _chip(GOLD, 24))
	roll_button.add_theme_stylebox_override("pressed", _chip(GOLD.darkened(0.2), 24))
	roll_button.add_theme_stylebox_override("disabled", _chip(Color(1, 1, 1, 0.07), 24))
	roll_button.pressed.connect(_on_roll_pressed)
	add_child(roll_button)


func _chip(color: Color, radius: int = 16) -> StyleBoxFlat:
	var box := StyleBoxFlat.new()
	box.bg_color = color
	box.set_corner_radius_all(radius)
	box.content_margin_left = 14
	box.content_margin_right = 14
	box.content_margin_top = 8
	box.content_margin_bottom = 8
	return box


func _notification(what: int) -> void:
	if what == NOTIFICATION_RESIZED and is_instance_valid(view):
		_layout()


func _screen_size() -> Vector2:
	if is_inside_tree():
		var rect := get_viewport_rect().size
		if rect.x > 1.0 and rect.y > 1.0:
			return rect
	return size


func _layout() -> void:
	if not is_instance_valid(view):
		return
	var s := _screen_size()
	var w := s.x
	var h := s.y
	if w <= 1.0 or h <= 1.0:
		return

	back_button.position = Vector2(14, 20)
	back_button.size = Vector2(58, 50)

	status_label.position = Vector2(84, 20)
	status_label.size = Vector2(maxf(120.0, w - 84 - 16), 50)

	var available_height := h - TOP_BAR - BOTTOM_BAR
	var board := minf(w - PAD * 2.0, available_height - PAD)
	board = maxf(board, 160.0)
	view.board_size = board
	view.position = Vector2((w - board) * 0.5, TOP_BAR + maxf(0.0, (available_height - board) * 0.5))
	view.queue_redraw()

	var button_width := minf(w - PAD * 2.0, 460.0)
	roll_button.size = Vector2(button_width, 82)
	roll_button.position = Vector2((w - button_width) * 0.5, h - BOTTOM_BAR + 18)


func _new_game() -> void:
	game = LudoEngine.new(seat_count, human_seats)
	for i in game.seats.size():
		var seat: Dictionary = game.seats[i]
		var color := int(seat["color"])
		if mode == "local":
			seat["name"] = "%s player" % COLOR_NAMES[color]
		elif i == 0:
			seat["name"] = "You"
		else:
			seat["name"] = "Bot %s" % COLOR_NAMES[color]
	cooldown = 0.0
	pending_auto = -1
	view.set_game(game)
	view.set_legal([])
	roll_button.text = "ROLL"
	roll_button.disabled = false
	_clear_overlay()
	_refresh()


func _on_roll_pressed() -> void:
	if game == null or game.is_over():
		return
	if game.phase != LudoEngine.Phase.AWAITING_ROLL:
		return
	if bool(game.active()["is_bot"]):
		return
	if cooldown > 0.0:
		return
	_do_roll(rng.randi_range(1, 6))


func _do_roll(die: int) -> void:
	var result := game.roll(die)
	if not bool(result["ok"]):
		return
	roll_button.text = "DICE  %d" % die
	var legal: Array = result["legal"]
	view.set_legal(legal)
	pending_auto = -1
	if not game.is_over() and game.phase == LudoEngine.Phase.AWAITING_MOVE:
		if legal.size() == 1 and not bool(game.active()["is_bot"]):
			pending_auto = int(legal[0])
	_refresh()


func _do_move(token_index: int) -> void:
	var result := game.move_token(token_index)
	if not bool(result["ok"]):
		return
	view.set_legal([])
	pending_auto = -1
	_refresh()


func _on_token_tapped(token_index: int) -> void:
	if game == null or game.is_over():
		return
	if game.phase != LudoEngine.Phase.AWAITING_MOVE:
		return
	if bool(game.active()["is_bot"]):
		return
	_do_move(token_index)


func _process(delta: float) -> void:
	if game == null or game.is_over():
		return
	if cooldown > 0.0:
		cooldown = maxf(0.0, cooldown - delta)
		return

	if game.phase == LudoEngine.Phase.AWAITING_ROLL:
		if bool(game.active()["is_bot"]):
			cooldown = 0.65
			_do_roll(rng.randi_range(1, 6))
	elif game.phase == LudoEngine.Phase.AWAITING_MOVE:
		if bool(game.active()["is_bot"]):
			cooldown = 0.5
			var move := LudoAI.choose_move(game)
			if not move.is_empty():
				_do_move(int(move["token"]))
		elif pending_auto >= 0:
			cooldown = 0.35
			var token := pending_auto
			pending_auto = -1
			_do_move(token)


func _refresh() -> void:
	if game.is_over():
		roll_button.disabled = true
		roll_button.text = "GAME OVER"
		_show_result()
		return

	var seat := game.active()
	var is_bot := bool(seat["is_bot"])
	var seat_name := str(seat["name"])
	var home_count := game.tokens_home(seat)
	var tint: Color = COLORS[int(seat["color"])]

	if game.phase == LudoEngine.Phase.AWAITING_ROLL:
		if is_bot:
			status_label.text = "%s is rolling…   %d/4 home" % [seat_name, home_count]
			roll_button.disabled = true
		else:
			var who := "Your turn" if mode == "computer" else "%s's turn" % seat_name
			status_label.text = "%s — tap ROLL   %d/4 home" % [who, home_count]
			roll_button.disabled = false
	elif game.phase == LudoEngine.Phase.AWAITING_MOVE:
		if is_bot:
			status_label.text = "%s is moving…" % seat_name
			roll_button.disabled = true
		else:
			status_label.text = "Rolled %d — tap a glowing token" % game.last_die
			roll_button.disabled = true
	status_label.add_theme_color_override("font_color", tint.lightened(0.25))


# ------------------------------------------------------------ result overlay

func _show_result() -> void:
	if overlay != null and is_instance_valid(overlay):
		return
	var winner := int(game.winner_seat)
	var winner_name: String = "Nobody" if winner < 0 else str(game.seats[winner]["name"])
	var winner_color: Color = MUTED if winner < 0 else COLORS[int(game.seats[winner]["color"])]

	overlay = Control.new()
	add_child(overlay)
	overlay.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)

	var dim := ColorRect.new()
	dim.color = Color(0, 0, 0, 0.62)
	overlay.add_child(dim)
	dim.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)

	var center := CenterContainer.new()
	overlay.add_child(center)
	center.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)

	var card := PanelContainer.new()
	var box := StyleBoxFlat.new()
	box.bg_color = CARD
	box.set_corner_radius_all(26)
	box.content_margin_left = 28
	box.content_margin_right = 28
	box.content_margin_top = 28
	box.content_margin_bottom = 28
	card.add_theme_stylebox_override("panel", box)
	card.custom_minimum_size = Vector2(300, 0)
	center.add_child(card)

	var column := VBoxContainer.new()
	column.add_theme_constant_override("separation", 14)
	card.add_child(column)

	var title := Label.new()
	title.text = "%s won!" % winner_name
	title.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	title.add_theme_font_size_override("font_size", 30)
	title.add_theme_color_override("font_color", winner_color)
	column.add_child(title)

	var again := Button.new()
	again.text = "Play again"
	again.custom_minimum_size = Vector2(0, 72)
	again.add_theme_font_size_override("font_size", 24)
	again.add_theme_color_override("font_color", Color("#FFFFFF"))
	again.add_theme_stylebox_override("normal", _chip(GOLD.darkened(0.05), 20))
	again.add_theme_stylebox_override("hover", _chip(GOLD, 20))
	again.add_theme_stylebox_override("pressed", _chip(GOLD.darkened(0.2), 20))
	again.pressed.connect(_new_game)
	column.add_child(again)

	var home := Button.new()
	home.text = "Home"
	home.custom_minimum_size = Vector2(0, 64)
	home.add_theme_font_size_override("font_size", 22)
	home.add_theme_color_override("font_color", INK)
	home.add_theme_stylebox_override("normal", _chip(Color(1, 1, 1, 0.08), 20))
	home.add_theme_stylebox_override("hover", _chip(Color(1, 1, 1, 0.16), 20))
	home.add_theme_stylebox_override("pressed", _chip(Color(1, 1, 1, 0.24), 20))
	home.pressed.connect(func() -> void: exit_requested.emit())
	column.add_child(home)


func _clear_overlay() -> void:
	if overlay != null and is_instance_valid(overlay):
		overlay.queue_free()
	overlay = null
