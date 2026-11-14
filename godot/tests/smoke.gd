extends SceneTree

## Headless smoke test: plays many full games with the AI driving every seat and
## checks the core invariants of the rules engine.

func _initialize() -> void:
	var rng := RandomNumberGenerator.new()
	rng.seed = 12345
	var failures := 0
	for game_index in 120:
		var seats := 2 + (game_index % 3)
		var game := LudoEngine.new(seats, 0)
		var guard := 0
		while not game.is_over() and guard < 20000:
			guard += 1
			if game.phase == LudoEngine.Phase.AWAITING_ROLL:
				game.roll(rng.randi_range(1, 6))
			elif game.phase == LudoEngine.Phase.AWAITING_MOVE:
				var move := LudoAI.choose_move(game)
				if move.is_empty():
					push_error("AwaitingMove with no legal move (game %d)" % game_index)
					failures += 1
					break
				game.move_token(int(move["token"]))
			for seat in game.seats:
				for progress in seat["tokens"]:
					var value := int(progress)
					if value < -1 or value > Topology.PROGRESS_HOME:
						push_error("Token out of range: %d" % value)
						failures += 1
		if not game.is_over():
			push_error("Game %d did not finish (guard hit)" % game_index)
			failures += 1
		elif game.winner_seat < 0:
			push_error("Game %d finished without a winner" % game_index)
			failures += 1

	# Deterministic rule checks
	var g2 := LudoEngine.new(4, 0)
	g2.roll(6)
	if g2.phase != LudoEngine.Phase.AWAITING_MOVE:
		push_error("A 6 with tokens in base must allow a move")
		failures += 1
	var moves := g2.legal_moves()
	if moves.size() != 4:
		push_error("Expected 4 legal base moves, got %d" % moves.size())
		failures += 1
	g2.move_token(0)
	if int(g2.seats[0]["tokens"][0]) != 0:
		push_error("Leaving base must land on progress 0")
		failures += 1
	if g2.active_seat != 0:
		push_error("A six must grant an extra turn")
		failures += 1

	# Exact roll to home: progress 55 + 3 must be illegal
	var g3 := LudoEngine.new(2, 0)
	g3.seats[0]["tokens"] = [55, -1, -1, -1]
	g3.phase = LudoEngine.Phase.AWAITING_MOVE
	g3.last_die = 3
	if not g3.legal_moves().is_empty():
		push_error("Overshooting home must be illegal")
		failures += 1
	g3.last_die = 2
	if g3.legal_moves().size() != 1:
		push_error("Exact roll to home must be legal")
		failures += 1


	# ---- Board geometry must match the reference board -------------------
	var expected_starts := [Vector2i(1, 6), Vector2i(8, 1), Vector2i(6, 13), Vector2i(13, 8)]
	for color in 4:
		var start_cell := Topology.progress_cell(color, 0)
		if start_cell != expected_starts[color]:
			push_error("Start square for colour %d is %s, expected %s" % [color, start_cell, expected_starts[color]])
			failures += 1
		var last_cell := Topology.progress_cell(color, Topology.LAST_TRACK)
		var entry_cell := Topology.progress_cell(color, Topology.LAST_TRACK + 1)
		if last_cell.distance_to(entry_cell) > 2.0:
			push_error("Home column entry for colour %d is not near the last ring cell" % color)
			failures += 1
		var column_cells := []
		for index in Topology.HOME_COL:
			column_cells.append(Topology.progress_cell(color, Topology.LAST_TRACK + 1 + index))
		if column_cells.size() != 5:
			push_error("Colour %d must have 5 home column cells" % color)
			failures += 1
		var home := Topology.progress_cell(color, Topology.PROGRESS_HOME)
		if home != Vector2i(7, 7):
			push_error("Home cell for colour %d must be the centre" % color)
			failures += 1
		# The start square is always a safe square.
		if not Topology.is_safe_cell(Topology.absolute_cell(color, 0)):
			push_error("Start square for colour %d is not marked safe" % color)
			failures += 1
		# Eight cells after the start is the second safe square of that colour.
		if not Topology.is_safe_cell(Topology.absolute_cell(color, 8)):
			push_error("Star square for colour %d is not marked safe" % color)
			failures += 1
	if Topology.SAFE_CELLS.size() != 8:
		push_error("There must be exactly 8 safe squares")
		failures += 1

	print("SMOKE RESULT failures=%d" % failures)
	quit(0 if failures == 0 else 1)
