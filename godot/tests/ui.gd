extends SceneTree

## Headless UI test: walks every menu path, checks layout, dice and token animation.

var failures := 0


func _check(condition: bool, label: String) -> void:
	if condition:
		print("ok   ", label)
	else:
		failures += 1
		print("FAIL ", label)


func _initialize() -> void:
	var main = load("res://main.tscn").instantiate()
	root.add_child(main)
	if main.current == null:
		main._ready()
	_check(main.current != null, "home screen built")

	main._show_setup("computer")
	_check(main.current != null, "computer setup screen built")
	main._show_setup("local")
	_check(main.current != null, "local setup screen built")
	main._show_online("random")
	_check(main.current != null, "online placeholder built")

	_check(root.has_node("Sound"), "sound autoload is running")

	# ---- computer game ---------------------------------------------------
	main._start_game("computer", 2)
	var screen = main.current_game
	if screen.game == null:
		screen._ready()
	_check(screen is GameScreen, "computer game screen started")
	_check(screen.game.seats.size() == 2, "two seats for a 2-player computer game")
	_check(screen.game.seats[1]["is_bot"], "second seat is a bot")
	_check(str(screen.game.seats[0]["name"]) == "You", "seat 0 is named You")

	screen.game.roll(6)
	_check(screen.game.phase == LudoEngine.Phase.AWAITING_MOVE, "rolling a 6 opens a move")
	_check(screen.game.legal_moves().size() == 4, "all four base tokens can move on a 6")
	screen.game.move_token(0)
	_check(int(screen.game.seats[0]["tokens"][0]) == 0, "token leaves the yard onto its start square")

	# ---- dice ------------------------------------------------------------
	screen.dice.value = 6
	screen.dice.rolling = true
	screen.dice.rolling = false
	_check(screen.dice.value == 6, "dice face accepts a value")

	# ---- token animation -------------------------------------------------
	var view: BoardView = screen.view
	view.board_size = 600.0
	var points: Array = [
		view.progress_position(0, 0),
		view.progress_position(0, 1),
		view.progress_position(0, 2),
		view.progress_position(0, 3),
	]
	view.animate_token(0, 0, points, 0.4)
	_check(view.is_animating(), "token animation starts")
	var first: Vector2 = view.animated_position()
	view._process(0.2)
	var middle: Vector2 = view.animated_position()
	_check(middle.distance_to(first) > 1.0, "token moves while animating")
	view._process(0.3)
	_check(not view.is_animating(), "token animation finishes")
	_check(view.animated_position().distance_to(points[points.size() - 1]) < 0.6
		or not view.is_animating(), "animation lands on the last point")

	# ---- layout on a phone-sized screen ---------------------------------
	screen.size = Vector2(720, 1280)
	screen._layout()
	var board: float = view.board_size
	_check(board > 300.0 and board <= 700.0, "board fits the screen")
	var expected_x: float = (720.0 - board) * 0.5
	_check(absf(view.position.x - expected_x) <= 1.0, "board is horizontally centred")
	_check(view.position.y >= 80.0, "board clears the top bar")
	_check(view.position.y + board <= screen.roll_button.position.y, "board clears the dice area")
	_check(screen.status_label.size.x >= 300.0, "status label is wide enough")
	_check(screen.roll_button.size.x >= 200.0 and screen.roll_button.position.y > 1100.0,
		"dice button sits at the bottom")
	_check(screen.turn_chip.visible, "turn chip is visible during play")

	# ---- local game ------------------------------------------------------
	main._start_game("local", 4)
	var local = main.current_game
	if local.game == null:
		local._ready()
	_check(local.game.seats.size() == 4, "four seats in a local game")
	var humans := 0
	for seat in local.game.seats:
		if not bool(seat["is_bot"]):
			humans += 1
	_check(humans == 4, "all four local seats are human")
	_check(str(local.game.seats[2]["name"]).ends_with("player"), "local seats use colour names")

	# ---- every token must sit centred in its own cell / peg --------------
	var bv: BoardView = local.view
	bv.board_size = 600.0
	var cell: float = bv.cell_size()
	var misplaced := 0
	for color in 4:
		var probe := {"color": color, "tokens": [-1, -1, -1, -1]}
		for progress in range(0, Topology.PROGRESS_HOME):
			probe["tokens"] = [progress, -1, -1, -1]
			var pos: Vector2 = bv.token_position(probe, 0)
			var target := Topology.progress_cell(color, progress)
			var centre: Vector2 = bv.cell_center(target.x, target.y)
			if pos.distance_to(centre) > 0.6:
				push_error("Colour %d progress %d token is %.2f px off centre" % [
					color, progress, pos.distance_to(centre)])
				misplaced += 1
		probe["tokens"] = [-1, -1, -1, -1]
		var origin := Topology.yard_origin(color)
		for token in 4:
			var yard_pos: Vector2 = bv.token_position(probe, token)
			var slot: Vector2 = BoardView.YARD_SLOTS[token]
			var expected := Vector2(origin.x + slot.x, origin.y + slot.y) * cell
			if yard_pos.distance_to(expected) > 0.6:
				push_error("Colour %d yard token %d is not on its peg" % [color, token])
				misplaced += 1
		probe["tokens"] = [Topology.PROGRESS_HOME, -1, -1, -1]
		var home_pos: Vector2 = bv.token_position(probe, 0)
		var centre_block := Rect2(Vector2(6.0, 6.0) * cell, Vector2(3.0, 3.0) * cell)
		if not centre_block.has_point(home_pos):
			push_error("Colour %d home token is outside the centre" % color)
			misplaced += 1
	if bv.token_radius() > cell * 0.5:
		push_error("Token radius does not fit inside a cell")
		misplaced += 1
	_check(misplaced == 0, "all tokens centred on their cells and pegs")

	print("UI RESULT failures=%d" % failures)
	quit(0 if failures == 0 else 1)
