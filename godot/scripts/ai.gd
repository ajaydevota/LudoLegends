class_name LudoAI
extends RefCounted

## Heuristic Ludo AI, ported from LudoLegends.Core.LudoAi.
## Scores every legal move with a small set of weights; ties break on token index.

const W_CAPTURE := 1000
const W_REACH_HOME := 800
const W_LEAVE_BASE := 220
const W_ENTER_HOME := 300
const W_LAND_SAFE := 160
const W_PROGRESS := 1


static func choose_move(game: LudoEngine) -> Dictionary:
	var moves: Array = game.legal_moves()
	if moves.is_empty():
		return {}
	var best: Dictionary = {}
	var best_score := -2147483648
	for move in moves:
		var score := score_move(game, move)
		if score > best_score:
			best_score = score
			best = move
	return best


static func score_move(game: LudoEngine, move: Dictionary) -> int:
	var seat: Dictionary = game.seats[game.active_seat]
	var score := 0
	if bool(move["captures"]):
		score += W_CAPTURE
	if bool(move["reaches_home"]):
		score += W_REACH_HOME
	if bool(move["leaves_base"]):
		score += W_LEAVE_BASE
	if bool(move["enters_home"]):
		score += W_ENTER_HOME
	var to_progress := int(move["to"])
	if to_progress <= Topology.LAST_TRACK:
		if Topology.is_safe_cell(Topology.absolute_cell(int(seat["color"]), to_progress)):
			score += W_LAND_SAFE
	score += to_progress * W_PROGRESS
	return score
