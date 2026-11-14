class_name LudoEngine
extends RefCounted

## Authoritative Ludo rules engine, ported from LudoLegends.Core.LudoGame (Unity C#).
## Every mutation goes through roll() / move_token(); both validate seat, phase,
## die value and target token before touching state.

enum Phase { AWAITING_ROLL, AWAITING_MOVE, FINISHED }

const EVENT_DICE := "diceRolled"
const EVENT_MOVED := "tokenMoved"
const EVENT_CAPTURED := "tokenCaptured"
const EVENT_HOME := "tokenHome"
const EVENT_NO_MOVES := "noLegalMoves"
const EVENT_FORFEIT := "turnForfeited"
const EVENT_TURN := "turnChanged"
const EVENT_PLAYER_FINISHED := "playerFinished"
const EVENT_GAME_OVER := "gameFinished"

var rules := {
	"tokens_per_player": 4,
	"max_sixes": 3,
	"extra_on_six": true,
	"extra_on_capture": true,
	"extra_on_home": true,
	"blocking": true,
	"exact_home": true,
	"six_to_leave": true,
}

var seats: Array = []
var phase: int = Phase.AWAITING_ROLL
var active_seat: int = 0
var last_die: int = 0
var consecutive_sixes: int = 0
var winner_seat: int = -1
var finished_count: int = 0
var finish_order: Array = []
var version: int = 0


func _init(seat_count: int = 4, human_seats: int = 1) -> void:
	var colors: Array = Topology.colors_for_seat_count(seat_count)
	var names := ["You", "Blue Bot", "Green Bot", "Yellow Bot"]
	for i in seat_count:
		var tokens: Array = []
		for t in int(rules["tokens_per_player"]):
			tokens.append(-1)
		seats.append({
			"index": i,
			"color": colors[i],
			"name": names[i] if i > 0 else "You",
			"is_bot": i >= human_seats,
			"tokens": tokens,
			"finished": false,
			"rank": 0,
		})


func is_over() -> bool:
	return phase == Phase.FINISHED


func seat_count() -> int:
	return seats.size()


func tokens_home(seat: Dictionary) -> int:
	var count := 0
	for progress in seat["tokens"]:
		if int(progress) == Topology.PROGRESS_HOME:
			count += 1
	return count


func active() -> Dictionary:
	return seats[active_seat]


## Rolls a given die value for the active seat.
func roll(die: int) -> Dictionary:
	var result := {"ok": false, "legal": [], "events": []}
	if phase == Phase.FINISHED:
		return _error(result, "gameFinished")
	if die < 1 or die > 6:
		return _error(result, "invalidValue")
	if phase != Phase.AWAITING_ROLL:
		return _error(result, "invalidPhase")

	last_die = die
	result["events"].append({"kind": EVENT_DICE, "seat": active_seat, "die": die})

	if die == 6:
		consecutive_sixes += 1
	else:
		consecutive_sixes = 0

	if die == 6 and consecutive_sixes >= int(rules["max_sixes"]):
		result["events"].append({"kind": EVENT_FORFEIT, "seat": active_seat, "die": die})
		_advance_turn(result)
		return _commit(result)

	var moves := compute_moves(active_seat, die)
	if moves.is_empty():
		result["events"].append({"kind": EVENT_NO_MOVES, "seat": active_seat, "die": die})
		_advance_turn(result)
		return _commit(result)

	phase = Phase.AWAITING_MOVE
	for move in moves:
		result["legal"].append(move["token"])
	return _commit(result)


## Moves a token for the active seat using the pending die value.
func move_token(token_index: int) -> Dictionary:
	var result := {"ok": false, "legal": [], "events": []}
	if phase == Phase.FINISHED:
		return _error(result, "gameFinished")
	if phase != Phase.AWAITING_MOVE:
		return _error(result, "invalidPhase")

	var option: Dictionary = {}
	for move in legal_moves():
		if int(move["token"]) == token_index:
			option = move
			break
	if option.is_empty():
		return _error(result, "invalidToken")

	var seat: Dictionary = seats[active_seat]
	var tokens: Array = seat["tokens"]
	var from_progress := int(tokens[token_index])
	var to_progress := int(option["to"])

	tokens[token_index] = to_progress
	var die := last_die
	last_die = 0
	phase = Phase.AWAITING_ROLL

	var captured: Array = []
	if to_progress <= Topology.LAST_TRACK:
		var dest_cell := Topology.absolute_cell(int(seat["color"]), to_progress)
		if not Topology.is_safe_cell(dest_cell):
			for other in seats:
				if int(other["index"]) == active_seat:
					continue
				var other_tokens: Array = other["tokens"]
				for t in other_tokens.size():
					var progress := int(other_tokens[t])
					if progress < 0 or progress > Topology.LAST_TRACK:
						continue
					if Topology.absolute_cell(int(other["color"]), progress) != dest_cell:
						continue
					other_tokens[t] = -1
					captured.append([int(other["index"]), t])
					result["events"].append({
						"kind": EVENT_CAPTURED, "seat": int(other["index"]), "token": t,
					})

	result["events"].append({
		"kind": EVENT_MOVED, "seat": active_seat, "token": token_index,
		"from": from_progress, "to": to_progress, "captured": captured.size(),
	})

	var reached_home := to_progress == Topology.PROGRESS_HOME
	if reached_home:
		result["events"].append({"kind": EVENT_HOME, "seat": active_seat, "token": token_index})

	var just_finished := false
	if not bool(seat["finished"]) and tokens_home(seat) == tokens.size():
		seat["finished"] = true
		just_finished = true
		finished_count += 1
		seat["rank"] = finished_count
		finish_order.append(active_seat)
		result["events"].append({"kind": EVENT_PLAYER_FINISHED, "seat": active_seat})

	if finished_count >= seat_count() - 1:
		if winner_seat < 0 and not finish_order.is_empty():
			winner_seat = int(finish_order[0])
		phase = Phase.FINISHED
		active_seat = -1
		result["events"].append({"kind": EVENT_GAME_OVER, "seat": winner_seat})
		return _commit(result)

	var extra := (bool(rules["extra_on_six"]) and die == 6) \
		or (bool(rules["extra_on_capture"]) and not captured.is_empty()) \
		or (bool(rules["extra_on_home"]) and reached_home)

	if extra and not just_finished and phase != Phase.FINISHED:
		phase = Phase.AWAITING_ROLL
		last_die = 0
		result["events"].append({"kind": EVENT_TURN, "seat": active_seat, "extra": true})
		return _commit(result)

	_advance_turn(result)
	return _commit(result)


## Legal moves for the active seat with the pending die value.
func legal_moves() -> Array:
	if phase != Phase.AWAITING_MOVE or last_die < 1:
		return []
	return compute_moves(active_seat, last_die)


func compute_moves(seat_index: int, die: int) -> Array:
	var options: Array = []
	if seat_index < 0 or seat_index >= seats.size() or die < 1 or die > 6:
		return options
	var seat: Dictionary = seats[seat_index]
	var tokens: Array = seat["tokens"]
	for token in tokens.size():
		var option := build_move(seat_index, token, die)
		if not option.is_empty():
			options.append(option)
	return options


## Builds the move for one token, or {} when it is illegal.
## This is the single place where movement legality is decided.
func build_move(seat_index: int, token_index: int, die: int) -> Dictionary:
	var seat: Dictionary = seats[seat_index]
	var color := int(seat["color"])
	var progress := int(seat["tokens"][token_index])
	var option := {"token": token_index, "from": progress, "to": progress,
		"leaves_base": false, "enters_home": false, "reaches_home": false, "captures": false}

	if progress < 0:
		if bool(rules["six_to_leave"]) and die != 6:
			return {}
		var start_cell := Topology.absolute_cell(color, 0)
		if _is_blocked_by_opponent(seat_index, start_cell):
			return {}
		option["to"] = 0
		option["leaves_base"] = true
		option["captures"] = count_capturable_at(seat_index, start_cell) > 0
		return option

	var target := progress + die
	if bool(rules["exact_home"]) and target > Topology.PROGRESS_HOME:
		return {}
	if target > Topology.PROGRESS_HOME:
		target = Topology.PROGRESS_HOME

	var step := maxi(progress + 1, 0)
	while step <= target and step <= Topology.LAST_TRACK:
		var cell := Topology.absolute_cell(color, step)
		if _is_blocked_by_opponent(seat_index, cell):
			return {}
		step += 1

	option["to"] = target
	option["enters_home"] = progress <= Topology.LAST_TRACK and target > Topology.LAST_TRACK
	option["reaches_home"] = target == Topology.PROGRESS_HOME
	if target <= Topology.LAST_TRACK:
		var dest_cell := Topology.absolute_cell(color, target)
		option["captures"] = not Topology.is_safe_cell(dest_cell) \
			and count_capturable_at(seat_index, dest_cell) > 0
	return option


func count_capturable_at(seat_index: int, absolute_cell: int) -> int:
	var count := 0
	for other in seats:
		if int(other["index"]) == seat_index:
			continue
		var other_tokens: Array = other["tokens"]
		for t in other_tokens.size():
			var progress := int(other_tokens[t])
			if progress < 0 or progress > Topology.LAST_TRACK:
				continue
			if Topology.absolute_cell(int(other["color"]), progress) == absolute_cell:
				count += 1
	return count


## True when an opposing stack of two or more tokens occupies the cell.
func _is_blocked_by_opponent(seat_index: int, absolute_cell: int) -> bool:
	if not bool(rules["blocking"]):
		return false
	for other in seats:
		if int(other["index"]) == seat_index:
			continue
		var on_cell := 0
		var other_tokens: Array = other["tokens"]
		for t in other_tokens.size():
			var progress := int(other_tokens[t])
			if progress < 0 or progress > Topology.LAST_TRACK:
				continue
			if Topology.absolute_cell(int(other["color"]), progress) == absolute_cell:
				on_cell += 1
		if on_cell >= 2:
			return true
	return false


func _advance_turn(result: Dictionary) -> void:
	var start := 0 if active_seat < 0 else active_seat
	for i in range(1, seat_count() + 1):
		var next := (start + i) % seat_count()
		if bool(seats[next]["finished"]):
			continue
		active_seat = next
		phase = Phase.AWAITING_ROLL
		last_die = 0
		consecutive_sixes = 0
		result["events"].append({"kind": EVENT_TURN, "seat": next})
		return
	phase = Phase.FINISHED
	active_seat = -1


func _commit(result: Dictionary) -> Dictionary:
	version += 1
	result["ok"] = true
	return result


func _error(result: Dictionary, status: String) -> Dictionary:
	result["status"] = status
	return result
