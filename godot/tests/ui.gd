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
	var screen = main.current
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
	var local = main.current
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

	print("UI RESULT failures=%d" % failures)
	quit(0 if failures == 0 else 1)
