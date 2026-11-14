extends Control

## App router: home skin, mode setup, game screen.

const BG := Color("#14110E")
const INK := Color("#F5EFE3")
const DIM := Color("#A79C8A")
const RED := Color("#E2453C")
const BLUE := Color("#2F6FE0")
const GREEN := Color("#28A745")
const YELLOW := Color("#F2B01E")
const GOLD := Color("#E2A03C")

var current: Control = null


func _ready() -> void:
	_show_home()


func _swap(new_screen: Control) -> void:
	if current != null and is_instance_valid(current):
		current.queue_free()
	current = new_screen
	add_child(current)
	current.set_anchors_preset(Control.PRESET_FULL_RECT)


func _background(parent: Control) -> void:
	var bg := ColorRect.new()
	bg.color = BG
	bg.set_anchors_preset(Control.PRESET_FULL_RECT)
	bg.mouse_filter = Control.MOUSE_FILTER_IGNORE
	parent.add_child(bg)


func _label(text: String, size: int, color: Color = INK, align: int = HORIZONTAL_ALIGNMENT_CENTER) -> Label:
	var label := Label.new()
	label.text = text
	label.horizontal_alignment = align
	label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	label.add_theme_font_size_override("font_size", size)
	label.add_theme_color_override("font_color", color)
	return label


func _button(text: String, color: Color, size: int = 26, height: float = 76.0) -> Button:
	var button := Button.new()
	button.text = text
	button.custom_minimum_size = Vector2(0, height)
	button.add_theme_font_size_override("font_size", size)
	button.add_theme_color_override("font_color", INK)
	button.add_theme_color_override("font_hover_color", INK)
	button.add_theme_color_override("font_pressed_color", INK)
	button.add_theme_stylebox_override("normal", _box(color))
	button.add_theme_stylebox_override("hover", _box(color.lightened(0.12)))
	button.add_theme_stylebox_override("pressed", _box(color.darkened(0.18)))
	button.add_theme_stylebox_override("focus", _box(color, 3))
	return button


func _box(color: Color, border: int = 0) -> StyleBoxFlat:
	var box := StyleBoxFlat.new()
	box.bg_color = color
	box.set_corner_radius_all(18)
	box.content_margin_left = 18
	box.content_margin_right = 18
	box.content_margin_top = 10
	box.content_margin_bottom = 10
	if border > 0:
		box.border_width_left = border
		box.border_width_right = border
		box.border_width_top = border
		box.border_width_bottom = border
		box.border_color = INK
	return box


func _column(parent: Control, separation: int = 14) -> VBoxContainer:
	var margin := MarginContainer.new()
	margin.set_anchors_preset(Control.PRESET_FULL_RECT)
	margin.add_theme_constant_override("margin_left", 26)
	margin.add_theme_constant_override("margin_right", 26)
	margin.add_theme_constant_override("margin_top", 30)
	margin.add_theme_constant_override("margin_bottom", 30)
	parent.add_child(margin)
	var vbox := VBoxContainer.new()
	vbox.alignment = BoxContainer.ALIGNMENT_CENTER
	vbox.add_theme_constant_override("separation", separation)
	margin.add_child(vbox)
	return vbox


# ---------------------------------------------------------------- home screen

func _show_home() -> void:
	var root := Control.new()
	_background(root)
	var vbox := _column(root, 16)

	vbox.add_child(_label("LUDO", 64, GOLD))
	vbox.add_child(_label("LEGENDS", 34, INK))
	var spacer := Control.new()
	spacer.custom_minimum_size = Vector2(0, 22)
	vbox.add_child(spacer)

	var computer := _button("Play vs Computer", BLUE, 28, 84)
	computer.pressed.connect(_show_setup.bind("computer"))
	vbox.add_child(computer)

	var local := _button("Local — play with family", GREEN, 26, 84)
	local.pressed.connect(_show_setup.bind("local"))
	vbox.add_child(local)

	var random := _button("Online — random player", RED, 26, 76)
	random.pressed.connect(_show_online.bind("random"))
	vbox.add_child(random)

	var friend := _button("Online — invite friend", YELLOW.darkened(0.15), 26, 76)
	friend.pressed.connect(_show_online.bind("friend"))
	vbox.add_child(friend)

	var spacer2 := Control.new()
	spacer2.custom_minimum_size = Vector2(0, 14)
	vbox.add_child(spacer2)
	vbox.add_child(_label("Offline modes work without internet.", 18, DIM))

	_swap(root)


# --------------------------------------------------------------- setup screen

func _show_setup(mode: String) -> void:
	var root := Control.new()
	_background(root)
	var vbox := _column(root, 14)

	var title := "Play vs Computer" if mode == "computer" else "Local game"
	vbox.add_child(_label(title, 34, GOLD))
	vbox.add_child(_label("Kitne players?", 22, DIM))

	var spacer := Control.new()
	spacer.custom_minimum_size = Vector2(0, 12)
	vbox.add_child(spacer)

	for count in [2, 3, 4]:
		var label := "%d players" % count
		if mode == "computer":
			label = "You + %d bot%s" % [count - 1, "" if count == 2 else "s"]
		var button := _button(label, BLUE if mode == "computer" else GREEN, 26, 78)
		button.pressed.connect(_start_game.bind(mode, count))
		vbox.add_child(button)

	var spacer2 := Control.new()
	spacer2.custom_minimum_size = Vector2(0, 16)
	vbox.add_child(spacer2)
	var back := _button("Back", Color("#3A3630"), 22, 60)
	back.pressed.connect(_show_home)
	vbox.add_child(back)

	_swap(root)


func _start_game(mode: String, seat_count: int) -> void:
	var game_screen := GameScreen.new()
	game_screen.setup(seat_count, 1 if mode == "computer" else seat_count, mode)
	game_screen.exit_requested.connect(_show_home)
	_swap(game_screen)


# -------------------------------------------------------------- online screens

func _show_online(mode: String) -> void:
	var root := Control.new()
	_background(root)
	var vbox := _column(root, 14)

	var title := "Online — random player" if mode == "random" else "Online — invite friend"
	vbox.add_child(_label(title, 30, GOLD))
	vbox.add_child(_label(
		"Online khelne ke liye game server chahiye.\n\n" +
		"Server code isi repo ke server/ folder me hai (ASP.NET Core + WebSocket rooms). " +
		"Jab wo kisi host par deploy ho jaye, yahan uska URL daal ke khelna shuru ho jayega.",
		20, INK))

	var spacer := Control.new()
	spacer.custom_minimum_size = Vector2(0, 18)
	vbox.add_child(spacer)

	var back := _button("Back", Color("#3A3630"), 22, 64)
	back.pressed.connect(_show_home)
	vbox.add_child(back)

	_swap(root)
