class_name GameScreen
extends Control

## One match: animated dice, animated token movement, turn chip and result card.

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

const TOP_BAR := 92.0
const BOTTOM_BAR := 158.0
const PAD := 12.0

## Seconds per board cell while a token walks. Bigger = slower.
const STEP_TIME := 0.17
const DICE_TIME := 0.75

var seat_count := 4
var human_seats := 1
var mode := "computer"

var game: LudoEngine
var view: BoardView
var status_label: Label
var turn_chip: PanelContainer
var turn_chip_label: Label
var roll_button: Button
var dice: DiceView
var back_button: Button
var overlay: Control = null
var rng := RandomNumberGenerator.new()
var cooldown := 0.0
var pending_auto := -1
var busy := false


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
	back_button.pressed.connect(func() -> void:
		Sfx.play("click", -8.0)
		exit_requested.emit())
	add_child(back_button)

	status_label = Label.new()
	status_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	status_label.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	status_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	status_label.add_theme_font_size_override("font_size", 21)
	status_label.add_theme_color_override("font_color", INK)
	add_child(status_label)

	turn_chip = PanelContainer.new()
	add_child(turn_chip)
	turn_chip_label = Label.new()
	turn_chip_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	turn_chip_label.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	turn_chip_label.add_theme_font_size_override("font_size", 17)
	turn_chip_label.add_theme_color_override("font_color", Color("#FFFFFF"))
	turn_chip.add_child(turn_chip_label)

	roll_button = Button.new()
	roll_button.add_theme_stylebox_override("normal", _chip(Color(1, 1, 1, 0.09), 26))
	roll_button.add_theme_stylebox_override("hover", _chip(Color(1, 1, 1, 0.15), 26))
	roll_button.add_theme_stylebox_override("pressed", _chip(Color(1, 1, 1, 0.22), 26))
	roll_button.add_theme_stylebox_override("disabled", _chip(Color(1, 1, 1, 0.05), 26))
	roll_button.pressed.connect(_on_roll_pressed)
	add_child(roll_button)

	dice = DiceView.new()
	roll_button.add_child(dice)


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

	back_button.position = Vector2(14, 18)
	back_button.size = Vector2(58, 50)

	status_label.position = Vector2(84, 14)
	status_label.size = Vector2(maxf(120.0, w - 84 - 14), 56)

	var available_height := h - TOP_BAR - BOTTOM_BAR
	var board := minf(w - PAD * 2.0, available_height - PAD)
	board = maxf(board, 160.0)
	view.board_size = board
	view.position = Vector2((w - board) * 0.5, TOP_BAR + maxf(0.0, (available_height - board) * 0.5))
	view.queue_redraw()

	var button_width := minf(w - PAD * 2.0, 460.0)
	var button_height := 82.0
	roll_button.size = Vector2(button_width, button_height)
	roll_button.position = Vector2((w - button_width) * 0.5, h - button_height - 18.0)

	var dice_side := button_height - 20.0
	dice.size = Vector2(dice_side, dice_side)
	dice.position = Vector2((button_width - dice_side) * 0.5, (button_height - dice_side) * 0.5)

	var chip_width := minf(w - PAD * 2.0, 320.0)
	turn_chip.size = Vector2(chip_width, 40)
	turn_chip.position = Vector2((w - chip_width) * 0.5, roll_button.position.y - 48.0)


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
	busy = false
	dice.value = 1
	dice.rolling = false
	view.set_game(game)
	view.set_legal([])
	_clear_overlay()
	_refresh()


# ----------------------------------------------------------------------- turn

func _on_roll_pressed() -> void:
	if busy or game == null or game.is_over():
		return
	if game.phase != LudoEngine.Phase.AWAITING_ROLL:
		return
	if bool(game.active()["is_bot"]):
		return
	Sfx.play("click", -8.0)
	_start_roll()


func _start_roll() -> void:
	if busy or game == null or game.is_over():
		return
	if game.phase != LudoEngine.Phase.AWAITING_ROLL:
		return
	busy = true
	roll_button.disabled = true
	var die := rng.randi_range(1, 6)
	dice.rolling = true
	Sfx.play("dice_roll", -3.0)
	var elapsed := 0.0
	while elapsed < DICE_TIME:
		dice.value = rng.randi_range(1, 6)
		await get_tree().create_timer(0.07).timeout
		elapsed += 0.07
	dice.rolling = false
	dice.value = die
	Sfx.play("dice_land", -1.0)

	var result := game.roll(die)
	busy = false
	if not bool(result["ok"]):
		_refresh()
		return

	var legal: Array = result["legal"]
	view.set_legal(legal)
	pending_auto = -1
	if not game.is_over() and game.phase == LudoEngine.Phase.AWAITING_MOVE:
		if legal.size() == 1 and not bool(game.active()["is_bot"]):
			pending_auto = int(legal[0])
	elif game.phase == LudoEngine.Phase.AWAITING_ROLL:
		Sfx.play("turn", -7.0)
	_refresh()


func _do_move(token_index: int) -> void:
	if busy or game == null or game.is_over():
		return
	busy = true
	var seat_index := game.active_seat
	var seat: Dictionary = game.seats[seat_index]
	var color := int(seat["color"])
	var from := int(seat["tokens"][token_index])
	var start_point: Vector2 = view.yard_position(color, token_index) if from < 0 \
		else view.progress_position(color, from)

	var result := game.move_token(token_index)
	if not bool(result["ok"]):
		busy = false
		return

	var to := int(seat["tokens"][token_index])
	var points: Array = [start_point]
	for step in range(maxi(from + 1, 0), to + 1):
		points.append(view.progress_position(color, step))
	var steps := maxi(points.size() - 1, 1)
	view.set_legal([])
	Sfx.play("move", -8.0)
	view.animate_token(seat_index, token_index, points, maxf(0.26, STEP_TIME * float(steps)))
	while view.is_animating():
		await get_tree().process_frame

	var captured := false
	var reached_home := false
	var finished := false
	for event in result["events"]:
		match str(event["kind"]):
			LudoEngine.EVENT_CAPTURED:
				captured = true
			LudoEngine.EVENT_HOME:
				reached_home = true
			LudoEngine.EVENT_GAME_OVER:
				finished = true
	if captured:
		Sfx.play("capture", -2.0)
	if reached_home:
		Sfx.play("home", -3.0)
	if finished:
		Sfx.play("win", -1.0)
	busy = false
	_refresh()


func _on_token_tapped(token_index: int) -> void:
	if busy or game == null or game.is_over():
		return
	if game.phase != LudoEngine.Phase.AWAITING_MOVE:
		return
	if bool(game.active()["is_bot"]):
		return
	Sfx.play("click", -10.0)
	_do_move(token_index)


func _process(delta: float) -> void:
	if game == null or game.is_over() or busy:
		return
	if cooldown > 0.0:
		cooldown = maxf(0.0, cooldown - delta)
		return

	if game.phase == LudoEngine.Phase.AWAITING_ROLL:
		if bool(game.active()["is_bot"]):
			cooldown = 0.9
			_start_roll()
	elif game.phase == LudoEngine.Phase.AWAITING_MOVE:
		if bool(game.active()["is_bot"]):
			cooldown = 0.7
			var move := LudoAI.choose_move(game)
			if not move.is_empty():
				_do_move(int(move["token"]))
		elif pending_auto >= 0:
			cooldown = 0.5
			var token := pending_auto
			pending_auto = -1
			_do_move(token)


func _refresh() -> void:
	if game.is_over():
		roll_button.disabled = true
		dice.dim = true
		turn_chip.visible = false
		_show_result()
		return

	var seat := game.active()
	var is_bot := bool(seat["is_bot"])
	var seat_name := str(seat["name"])
	var home_count := game.tokens_home(seat)
	var color: Color = COLORS[int(seat["color"])]

	dice.dim = is_bot
	turn_chip.visible = true
	var chip_box := _chip(color, 20)
	chip_box.content_margin_top = 4
	chip_box.content_margin_bottom = 4
	turn_chip.add_theme_stylebox_override("panel", chip_box)
	if is_bot:
		turn_chip_label.text = "%s · bot" % seat_name.to_upper()
	else:
		turn_chip_label.text = ("YOUR TURN" if mode == "computer" else "%s TURN" % seat_name.to_upper())
	turn_chip_label.add_theme_color_override("font_color", Color("#FFFFFF"))

	if game.phase == LudoEngine.Phase.AWAITING_ROLL:
		if is_bot:
			status_label.text = "%s is rolling…   %d/4 home" % [seat_name, home_count]
			roll_button.disabled = true
		else:
			status_label.text = "Tap the dice to roll   ·   %d/4 home" % home_count
			roll_button.disabled = false
	elif game.phase == LudoEngine.Phase.AWAITING_MOVE:
		if is_bot:
			status_label.text = "%s is moving…" % seat_name
			roll_button.disabled = true
		else:
			status_label.text = "Rolled %d — tap a glowing token" % game.last_die
			roll_button.disabled = true
	status_label.add_theme_color_override("font_color", color.lightened(0.3))


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
	again.pressed.connect(func() -> void:
		Sfx.play("click", -8.0)
		_new_game())
	column.add_child(again)

	var home := Button.new()
	home.text = "Home"
	home.custom_minimum_size = Vector2(0, 64)
	home.add_theme_font_size_override("font_size", 22)
	home.add_theme_color_override("font_color", INK)
	home.add_theme_stylebox_override("normal", _chip(Color(1, 1, 1, 0.08), 20))
	home.add_theme_stylebox_override("hover", _chip(Color(1, 1, 1, 0.16), 20))
	home.add_theme_stylebox_override("pressed", _chip(Color(1, 1, 1, 0.24), 20))
	home.pressed.connect(func() -> void:
		Sfx.play("click", -8.0)
		exit_requested.emit())
	column.add_child(home)


func _clear_overlay() -> void:
	if overlay != null and is_instance_valid(overlay):
		overlay.queue_free()
	overlay = null
