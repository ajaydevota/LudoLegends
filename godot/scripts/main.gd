extends Control

## App router: home screen, mode setup, game screen.

const BG := Color("#12100E")
const CARD := Color("#1E1A16")
const INK := Color("#F4EEE4")
const MUTED := Color("#9C9285")
const GOLD := Color("#E9B44C")
const BLUE := Color("#2D6CDF")
const GREEN := Color("#1FA84F")
const RED := Color("#D9433F")
const ORANGE := Color("#E9A83C")

var current: Control = null
var current_game: GameScreen = null


func _ready() -> void:
	_show_home()


# ------------------------------------------------------------------ utilities

func _new_screen() -> Control:
	if current != null and is_instance_valid(current):
		current.queue_free()
	current_game = null
	var root := Control.new()
	add_child(root)
	root.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	current = root
	return root


func _label(text: String, size: int, color: Color = INK, align: int = HORIZONTAL_ALIGNMENT_CENTER) -> Label:
	var label := Label.new()
	label.text = text
	label.horizontal_alignment = align
	label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	label.add_theme_font_size_override("font_size", size)
	label.add_theme_color_override("font_color", color)
	return label


func _button(text: String, color: Color, font_size: int = 26, height: float = 82.0) -> Button:
	var button := Button.new()
	button.text = text
	button.custom_minimum_size = Vector2(0, height)
	button.add_theme_font_size_override("font_size", font_size)
	button.add_theme_color_override("font_color", Color("#FFFFFF"))
	button.add_theme_color_override("font_hover_color", Color("#FFFFFF"))
	button.add_theme_color_override("font_pressed_color", Color("#FFFFFF"))
	button.add_theme_stylebox_override("normal", _box(color))
	button.add_theme_stylebox_override("hover", _box(color.lightened(0.1)))
	button.add_theme_stylebox_override("pressed", _box(color.darkened(0.2)))
	button.add_theme_stylebox_override("focus", _box(color))
	return button


func _box(color: Color) -> StyleBoxFlat:
	var box := StyleBoxFlat.new()
	box.bg_color = color
	box.set_corner_radius_all(22)
	box.content_margin_left = 20
	box.content_margin_right = 20
	box.content_margin_top = 12
	box.content_margin_bottom = 12
	box.shadow_color = Color(0, 0, 0, 0.35)
	box.shadow_size = 6
	box.shadow_offset = Vector2(0, 3)
	return box


func _card() -> StyleBoxFlat:
	var box := StyleBoxFlat.new()
	box.bg_color = CARD
	box.set_corner_radius_all(26)
	box.content_margin_left = 26
	box.content_margin_right = 26
	box.content_margin_top = 26
	box.content_margin_bottom = 26
	box.border_width_left = 1
	box.border_width_right = 1
	box.border_width_top = 1
	box.border_width_bottom = 1
	box.border_color = Color(1, 1, 1, 0.06)
	return box


func _spacer(parent: Control, height: float) -> void:
	var spacer := Control.new()
	spacer.custom_minimum_size = Vector2(0, height)
	parent.add_child(spacer)


func _frame(root: Control, side_margin: int = 26) -> VBoxContainer:
	var bg := ColorRect.new()
	bg.color = BG
	bg.mouse_filter = Control.MOUSE_FILTER_IGNORE
	root.add_child(bg)
	bg.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)

	var margin := MarginContainer.new()
	root.add_child(margin)
	margin.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	margin.add_theme_constant_override("margin_left", side_margin)
	margin.add_theme_constant_override("margin_right", side_margin)
	margin.add_theme_constant_override("margin_top", 24)
	margin.add_theme_constant_override("margin_bottom", 24)

	var column := VBoxContainer.new()
	column.alignment = BoxContainer.ALIGNMENT_CENTER
	column.add_theme_constant_override("separation", 14)
	margin.add_child(column)
	return column


func _back_button(root: Control, action: Callable) -> void:
	var button := Button.new()
	button.text = "←"
	button.add_theme_font_size_override("font_size", 24)
	button.add_theme_color_override("font_color", INK)
	button.add_theme_stylebox_override("normal", _box(Color(1, 1, 1, 0.07)))
	button.add_theme_stylebox_override("hover", _box(Color(1, 1, 1, 0.14)))
	button.add_theme_stylebox_override("pressed", _box(Color(1, 1, 1, 0.2)))
	button.pressed.connect(func() -> void:
		Sfx.play("click", -8.0)
		action.call())
	root.add_child(button)
	button.set_anchors_and_offsets_preset(Control.PRESET_TOP_LEFT)
	button.position = Vector2(20, 20)
	button.size = Vector2(60, 52)


# ---------------------------------------------------------------- home screen

func _show_home() -> void:
	var root := _new_screen()
	var column := _frame(root)

	_spacer(column, 30)
	column.add_child(_label("LUDO", 76, GOLD))
	column.add_child(_label("L E G E N D S", 20, MUTED))
	_spacer(column, 34)

	var computer := _button("Play vs Computer", BLUE)
	computer.pressed.connect(_show_setup.bind("computer"))
	column.add_child(computer)

	var local := _button("Local — play with family", GREEN)
	local.pressed.connect(func() -> void:
		Sfx.play("click", -8.0)
		_show_setup("local"))
	column.add_child(local)

	var random := _button("Online — random player", RED)
	random.pressed.connect(func() -> void:
		Sfx.play("click", -8.0)
		_show_online("random"))
	column.add_child(random)

	var friend := _button("Online — invite friend", ORANGE)
	friend.pressed.connect(func() -> void:
		Sfx.play("click", -8.0)
		_show_online("friend"))
	column.add_child(friend)

	_spacer(column, 22)
	column.add_child(_label("Offline modes work without internet", 16, MUTED))


# --------------------------------------------------------------- setup screen

func _show_setup(mode: String) -> void:
	var root := _new_screen()
	_back_button(root, _show_home)
	var column := _frame(root)

	column.add_child(_label("Play vs Computer" if mode == "computer" else "Local game", 32, GOLD))
	column.add_child(_label("Choose the number of players", 17, MUTED))
	_spacer(column, 20)

	for count in [2, 3, 4]:
		var label := "%d players" % count
		if mode == "computer":
			label = "You + %d bot%s" % [count - 1, "" if count == 2 else "s"]
		var button := _button(label, BLUE if mode == "computer" else GREEN, 25, 78)
		button.pressed.connect(func() -> void:
			Sfx.play("click", -8.0)
			_start_game(mode, count))
		column.add_child(button)


func _start_game(mode: String, seat_count: int) -> void:
	var game_screen := GameScreen.new()
	game_screen.setup(seat_count, 1 if mode == "computer" else seat_count, mode)
	game_screen.exit_requested.connect(_show_home)
	var root := _new_screen()
	root.add_child(game_screen)
	game_screen.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	current_game = game_screen


# -------------------------------------------------------------- online screen

func _show_online(mode: String) -> void:
	var root := _new_screen()
	_back_button(root, _show_home)
	var column := _frame(root)

	column.add_child(_label("Online — random player" if mode == "random" else "Online — invite friend", 26, GOLD))
	_spacer(column, 12)

	var panel := PanelContainer.new()
	panel.add_theme_stylebox_override("panel", _card())
	column.add_child(panel)

	var inner := VBoxContainer.new()
	inner.add_theme_constant_override("separation", 12)
	panel.add_child(inner)
	inner.add_child(_label("Server required", 20, INK))
	inner.add_child(_label(
		"Online play needs the game server that ships in this repo (server/ folder, " +
		"ASP.NET Core with WebSocket rooms). Once it is deployed, the app connects to it " +
		"and this screen becomes the room lobby.",
		16, MUTED))
