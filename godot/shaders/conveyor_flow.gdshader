// A belt's running surface: bands of light that travel the lane at the speed of the goods on
// it. One MultiMesh draws every belt in the factory (Scripts/Visuals/TickVisualsNode): a 64 px
// quad per belt, turned to the belt's facing, its shape in INSTANCE_CUSTOM.r.
//
// The travel is the global sim_belt_phase (Core's TickVisuals.BeltPhase), so the bands stop
// with the clock and run at its speed. Positions are snapped to art pixels in the tile's own
// frame, which a quarter turn keeps on the grid.
shader_type canvas_item;

// How far along its cell a belt's surface has run, 0..1.
global uniform float sim_belt_phase;
// 0 under Reduced motion: the bands go, and the belt is its own picture again.
global uniform float sim_motion;

uniform float cell_px = 64.0;
// One band every this many pixels of lane: the art's own chevron spacing.
uniform float band_pitch_px = 16.0;
uniform vec4 band_light : source_color = vec4(0.604, 0.553, 0.522, 0.34); // iron #9a8d85
uniform vec4 band_shade : source_color = vec4(0.063, 0.059, 0.071, 0.30); // the rail's #100f12

// INSTANCE_CUSTOM.r: 0 straight, 1 corner turning left, 2 corner turning right (Core's BeltShape).
varying flat float shape;
varying vec2 local_px;

void vertex() {
	local_px = VERTEX;
	shape = INSTANCE_CUSTOM.r;
}

void fragment() {
	// The tile's frame: authored running east, +y down, the centre at the origin.
	vec2 p = floor(local_px) + 0.5;
	float half_cell = cell_px * 0.5;
	// The lane lies inside the rails: 5 px of rail on a 64 px tile.
	float lane_half = half_cell - 5.0;

	float travel; // pixels along the lane from where goods enter
	bool lane;
	if (shape < 0.5) {
		travel = p.x + half_cell;
		lane = abs(p.y) < lane_half;
	} else {
		// A left turn comes in from the north and leaves east, around the north-east corner; a
		// right turn is its mirror, from the south.
		vec2 q = vec2(p.x, shape < 1.5 ? p.y : -p.y);
		vec2 from_pivot = q - vec2(half_cell, -half_cell);
		float turned = (PI - atan(from_pivot.y, from_pivot.x)) / (PI * 0.5);
		travel = turned * cell_px;
		// The rails run down the two outer sides; the pivot's own post is not lane.
		lane = q.x > -lane_half && q.y < lane_half && length(from_pivot) > 6.0;
	}

	float run = floor(sim_belt_phase * cell_px);
	float at = mod(travel - run, band_pitch_px);

	vec4 band = vec4(0.0);
	if (lane) {
		if (at < 1.0) {
			band = band_shade;
		} else if (at < 3.0) {
			band = band_light;
		}
	}
	COLOR = vec4(band.rgb, band.a * sim_motion);
}
