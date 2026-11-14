class_name GameScreen
extends Control

## One match: board, dice, turn flow. Human seats roll by tapping; bots play themselves.

signal exit_requested

const COLOR_NAMES := ["Red", "Blue", "Green", "Yellow"]
const INK := Color("#F5EFE3")

var seat_count := 4
var human_seats := 1
var mode := "computer"

var game: LudoEngine
var view: BoardView
var status_label: Label
var roll_button: Button
var back_button: Button
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
	_layout()
	_new_game()


func _build_ui() -> void:
	var bg := ColorRect.new()
	bg.color = Color("#14110E")
	bg.set_anchors_preset(Control.PRESET_FULL_RECT)
	bg.mouse_filter = Control.MOUSE_FILTER_IGNORE
	add_child(bg)

	view = BoardView.new()
	add_child(view)
	view.token_tapped.connect(_on_token_tapped)

	status_label = Label.new()
	status_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	status_label.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	status_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	status_label.add_theme_font_size_override("font_size", 24)
	status_label.add_theme_color_override("font_color", INK)
	add_child(status_label)

	roll_button = Button.new()
	roll_button.text = "ROLL"
	roll_button.add_theme_font_size_override("font_size", 32)
	roll_button.pressed.connect(_on_roll_pressed)
	add_child(roll_button)

	back_button = Button.new()
	back_button.text = "←"
	back_button.add_theme_font_size_override("font_size", 26)
	back_button.pressed.connect(func() -> void: exit_requested.emit())
	add_child(back_button)


func _notification(what: int) -> void:
	if what == NOTIFICATION_RESIZED and is_instance_valid(view):
		_layout()


func _layout() -> void:
	var w := size.x
	var h := size.y
	if w <= 0.0 or h <= 0.0:
		return
	var margin := 14.0
	var top := 92.0
	var bottom := 150.0
	var board := minf(w - margin * 2.0, h - top - bottom - margin)
	board = maxf(board, 220.0)
	view.board_size = board
	view.position = Vector2((w - board) * 0.5, top + maxf(0.0, (h - top - bottom - board) * 0.5))
	view.queue_redraw()

	back_button.position = Vector2(margin, margin)
	back_button.size = Vector2(64, 56)

	status_label.position = Vector2(margin + 70.0, margin)
	status_label.size = Vector2(w - margin * 2.0 - 70.0, 76)

	var button_width := minf(w - margin * 2.0, 440.0)
	roll_button.size = Vector2(button_width, 84)
	roll_button.position = Vector2((w - button_width) * 0.5, h - bottom + 26)


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
		var winner := int(game.winner_seat)
		var winner_name: String = "Nobody" if winner < 0 else str(game.seats[winner]["name"])
		status_label.text = "%s won!\nTap ← to play again." % winner_name
		roll_button.disabled = true
		roll_button.text = "GAME OVER"
		return

	var seat := game.active()
	var is_bot := bool(seat["is_bot"])
	var seat_name := str(seat["name"])
	var home_count := game.tokens_home(seat)

	if game.phase == LudoEngine.Phase.AWAITING_ROLL:
		if is_bot:
			status_label.text = "%s is rolling…  (%d/4 home)" % [seat_name, home_count]
			roll_button.disabled = true
		else:
			var who := "Your turn" if mode == "computer" else "%s's turn" % seat_name
			status_label.text = "%s — tap ROLL  (%d/4 home)" % [who, home_count]
			roll_button.disabled = false
	elif game.phase == LudoEngine.Phase.AWAITING_MOVE:
		if is_bot:
			status_label.text = "%s is moving…" % seat_name
			roll_button.disabled = true
		else:
			status_label.text = "Rolled %d — tap a glowing token" % game.last_die
			roll_button.disabled = true
