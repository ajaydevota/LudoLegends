extends SceneTree

## Headless UI test: builds the home screen, walks through every menu path and
## plays a few turns on the game screen to catch runtime errors.

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
		# _ready() is deferred during SceneTree._initialize; force it for the test.
		main._ready()
	_check(main.current != null, "home screen built")

	main._show_setup("computer")
	_check(main.current != null, "computer setup screen built")
	main._show_setup("local")
	_check(main.current != null, "local setup screen built")
	main._show_online("random")
	_check(main.current != null, "online placeholder built")

	main._start_game("computer", 2)
	var screen = main.current_game
	if screen.game == null:
		screen._ready()
	_check(screen is GameScreen, "computer game screen started")
	_check(screen.game.seats.size() == 2, "two seats for 2-player computer game")
	_check(screen.game.seats[1]["is_bot"], "second seat is a bot")
	_check(str(screen.game.seats[0]["name"]) == "You", "seat 0 named You")

	screen._do_roll(6)
	_check(screen.game.phase == LudoEngine.Phase.AWAITING_MOVE, "roll 6 allows a move")
	screen._do_move(0)
	_check(int(screen.game.seats[0]["tokens"][0]) == 0, "token left the base")
	_check(screen.view.legal.is_empty(), "legal highlights cleared after the move")

	main._start_game("local", 4)
	var local = main.current_game
	if local.game == null:
		local._ready()
	_check(local is GameScreen, "local game screen started")
	_check(local.game.seats.size() == 4, "four seats in a local game")
	var humans := 0
	for seat in local.game.seats:
		if not bool(seat["is_bot"]):
			humans += 1
	_check(humans == 4, "all four local seats are human")
	_check(str(local.game.seats[2]["name"]).ends_with("player"), "local seats use colour names")


	# ---- Layout sanity on a phone-sized screen -------------------------
	local.size = Vector2(720, 1280)
	local._layout()
	var board: float = local.view.board_size
	if board < 300.0 or board > 700.0:
		push_error("Board size %.1f is out of range for a 720x1280 screen" % board)
		failures += 1
	var view_x: float = local.view.position.x
	var expected_x: float = (720.0 - board) * 0.5
	if absf(view_x - expected_x) > 1.0:
		push_error("Board is not horizontally centred (%.1f vs %.1f)" % [view_x, expected_x])
		failures += 1
	if local.view.position.y + board > 1280.0 - 100.0:
		push_error("Board overlaps the dice bar")
		failures += 1
	if local.view.position.y < 80.0:
		push_error("Board overlaps the top bar")
		failures += 1
	if local.status_label.size.x < 300.0:
		push_error("Status label is too narrow (%.1f)" % local.status_label.size.x)
		failures += 1
	if local.roll_button.size.x < 200.0 or local.roll_button.position.y < 1100.0:
		push_error("Roll button is not laid out at the bottom")
		failures += 1
	if local.view.board_size <= 0.0:
		push_error("Board view has no size")
		failures += 1

	print("UI RESULT failures=%d" % failures)
	quit(0 if failures == 0 else 1)
