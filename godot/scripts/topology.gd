class_name Topology
extends RefCounted

## Geometry of the traditional 15x15 Ludo board.
## Ported from LudoLegends.Core.BoardTopology (Unity C#).
##
## Progress model: -1 = yard, 0..51 = main ring, 52..56 = home column, 57 = home.
## Absolute ring cell = (START_OFFSETS[color] + progress) % 52.

const GRID := 15
const TRACK := 52
const LAST_TRACK := 51
const HOME_COL := 5
const PROGRESS_HOME := 57
const CENTER := 7

const RING_X := [6, 7, 8, 8, 8, 8, 8, 8, 9, 10, 11, 12, 13, 14, 14, 14, 13, 12, 11, 10, 9, 8, 8, 8, 8, 8, 8, 7, 6, 6, 6, 6, 6, 6, 5, 4, 3, 2, 1, 0, 0, 0, 1, 2, 3, 4, 5, 6, 6, 6, 6, 6]
const RING_Y := [0, 0, 0, 1, 2, 3, 4, 5, 6, 6, 6, 6, 6, 6, 7, 8, 8, 8, 8, 8, 8, 9, 10, 11, 12, 13, 14, 14, 14, 13, 12, 11, 10, 9, 8, 8, 8, 8, 8, 8, 7, 6, 6, 6, 6, 6, 6, 5, 4, 3, 2, 1]

## Red, Blue, Green, Yellow (clockwise from top-left).
const START_OFFSETS := [41, 2, 15, 28]
const SAFE_CELLS := [2, 10, 15, 23, 28, 36, 41, 49]
const YARD_X := [0, 9, 9, 0]
const YARD_Y := [0, 0, 9, 9]
const HOME_COL_X := [[1, 2, 3, 4, 5], [7, 7, 7, 7, 7], [13, 12, 11, 10, 9], [7, 7, 7, 7, 7]]
const HOME_COL_Y := [[7, 7, 7, 7, 7], [1, 2, 3, 4, 5], [7, 7, 7, 7, 7], [13, 12, 11, 10, 9]]
const BASE_SLOT_X := [1, 4, 1, 4]
const BASE_SLOT_Y := [1, 1, 4, 4]


static func start_offset(color: int) -> int:
	return START_OFFSETS[color]


static func absolute_cell(color: int, progress: int) -> int:
	return posmod(START_OFFSETS[color] + progress, TRACK)


static func track_x(cell: int) -> int:
	return RING_X[cell]


static func track_y(cell: int) -> int:
	return RING_Y[cell]


static func is_safe_cell(cell: int) -> bool:
	return SAFE_CELLS.has(cell)


static func is_start_cell(cell: int) -> bool:
	return START_OFFSETS.has(cell)


static func yard_origin(color: int) -> Vector2i:
	return Vector2i(YARD_X[color], YARD_Y[color])


static func base_slot(color: int, token: int) -> Vector2i:
	return Vector2i(YARD_X[color] + BASE_SLOT_X[token], YARD_Y[color] + BASE_SLOT_Y[token])


static func progress_cell(color: int, progress: int) -> Vector2i:
	if progress <= LAST_TRACK:
		var cell := absolute_cell(color, progress)
		return Vector2i(RING_X[cell], RING_Y[cell])
	if progress < PROGRESS_HOME:
		var index := progress - (LAST_TRACK + 1)
		return Vector2i(HOME_COL_X[color][index], HOME_COL_Y[color][index])
	return Vector2i(CENTER, CENTER)


static func colors_for_seat_count(count: int) -> Array:
	match count:
		2:
			return [0, 2]
		3:
			return [0, 1, 2]
		_:
			return [0, 1, 2, 3]
