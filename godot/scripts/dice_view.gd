class_name DiceView
extends Control

## A dice face drawn with pips. Set `rolling` to true for a shake animation.

var value := 1:
	set(new_value):
		value = clampi(new_value, 1, 6)
		queue_redraw()

var rolling := false:
	set(new_value):
		rolling = new_value
		queue_redraw()

var face_color := Color("#FFFFFF")
var pip_color := Color("#221E19")
var dim := false:
	set(new_value):
		dim = new_value
		queue_redraw()

var _shake := 0.0


func _process(delta: float) -> void:
	if rolling:
		_shake += delta * 26.0
		queue_redraw()


func _draw() -> void:
	var side := minf(size.x, size.y)
	if side <= 0.0:
		return
	var origin := (size - Vector2(side, side)) * 0.5
	if rolling:
		origin += Vector2(sin(_shake) * side * 0.06, cos(_shake * 1.3) * side * 0.05)

	var body := Rect2(origin, Vector2(side, side))
	var box := StyleBoxFlat.new()
	box.bg_color = face_color if not dim else face_color.darkened(0.25)
	box.set_corner_radius_all(int(side * 0.22))
	draw_style_box(box, body)

	var radius := side * 0.085
	var left := origin.x + side * 0.27
	var mid := origin.x + side * 0.5
	var right := origin.x + side * 0.73
	var top := origin.y + side * 0.27
	var center := origin.y + side * 0.5
	var bottom := origin.y + side * 0.73

	var pips: Array = []
	match value:
		1:
			pips = [Vector2(mid, center)]
		2:
			pips = [Vector2(left, top), Vector2(right, bottom)]
		3:
			pips = [Vector2(left, top), Vector2(mid, center), Vector2(right, bottom)]
		4:
			pips = [Vector2(left, top), Vector2(right, top), Vector2(left, bottom), Vector2(right, bottom)]
		5:
			pips = [Vector2(left, top), Vector2(right, top), Vector2(mid, center),
				Vector2(left, bottom), Vector2(right, bottom)]
		6:
			pips = [Vector2(left, top), Vector2(right, top), Vector2(left, center),
				Vector2(right, center), Vector2(left, bottom), Vector2(right, bottom)]

	for pip in pips:
		draw_circle(pip, radius, pip_color)
