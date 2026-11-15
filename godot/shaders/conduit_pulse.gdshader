// Steam in a hot pipe: glints that run along every pipe that carries steam. One MultiMesh draws
// them all (Scripts/Visuals/TickVisualsNode): a 64 px quad per hot pipe, turned as the pipe's
// piece is, with the piece in INSTANCE_CUSTOM.
//
// A glint's place is a wave across the floor (east plus south), not a path through the network,
// so it crosses from one piece to the next without the shader knowing how they join. It runs on
// the global sim_tick_phase, so it stops with the clock. Unshaded: steam glows in a dark room.
shader_type canvas_item;
render_mode unshaded;

// Ticks since the last wrap (Core's TickVisuals.TickPhase).
global uniform float sim_tick_phase;
// 0 under Reduced motion: no glints; the pipe still wears its hot picture.
global uniform float sim_motion;

uniform float cell_px = 64.0;
// A glint travels pulse_cells cells in pulse_ticks ticks, one glint every pulse_cells cells.
// Both are Core's (TickVisuals.ConduitPulseTicks, ConduitPulseCells).
uniform float pulse_ticks = 32.0;
uniform float pulse_cells = 4.0;
uniform vec4 glint_color : source_color = vec4(1.0, 0.878, 0.541, 1.0);  // fire #ffe08a
uniform vec4 glow_color : source_color = vec4(0.941, 0.627, 0.251, 1.0); // fire #f0a040

// INSTANCE_CUSTOM: r the piece (0 straight, 1 end, 2 corner, 3 tee, 4 cross), g the cell's
// place in the wave (column plus row, wrapped to pulse_cells), b the piece's quarter turns.
varying flat vec3 piece;
varying vec2 local_px;

void vertex() {
	local_px = VERTEX;
	piece = INSTANCE_CUSTOM.rgb;
}

// One arm of a pipe: `along` runs out from the tile's centre, `across` over the pipe's width.
// Returns 0 off the pipe, 1 on its body, 2 on the highlight the hot picture paints.
float arm(float along, float across) {
	// The body is 21 px wide, a pixel above centre; its flange sits 24 to 29 px out.
	if (across < -11.0 || across > 10.0 || (along > 24.0 && along < 29.0)) {
		return 0.0;
	}
	return across < -7.0 && across > -10.0 ? 2.0 : 1.0;
}

void fragment() {
	// The piece's frame: authored open to the east, +y down, the centre at the origin.
	vec2 p = floor(local_px) + 0.5;

	float on = 0.0;
	if (piece.x < 0.5) {
		// Straight: one body from edge to edge.
		on = arm(abs(p.x), p.y);
	} else {
		// A fitting sits in the middle 27 px; the arms start beyond it.
		bool west = piece.x > 3.5;
		bool north = piece.x > 1.5;
		bool south = piece.x > 2.5;
		if (p.x > 13.0) {
			on = arm(p.x, p.y);
		} else if (west && p.x < -14.0) {
			on = arm(-p.x, p.y);
		} else if (north && p.y < -14.0) {
			on = arm(-p.y, p.x);
		} else if (south && p.y > 13.0) {
			on = arm(p.y, p.x);
		}
	}

	// The same pixel in the floor's frame, to place it in the wave.
	vec2 w = p;
	if (piece.z > 2.5) {
		w = vec2(p.y, -p.x);
	} else if (piece.z > 1.5) {
		w = -p;
	} else if (piece.z > 0.5) {
		w = vec2(-p.y, p.x);
	}
	float spacing = pulse_cells * cell_px;
	float run = floor(sim_tick_phase / pulse_ticks * spacing);
	float behind = mod(run - (piece.y * cell_px + w.x + w.y), spacing);

	// The glint's head, then three steps of afterglow.
	vec4 c = vec4(0.0);
	if (on > 0.5) {
		if (behind < 5.0) {
			c = on > 1.5 ? vec4(1.0, 1.0, 1.0, 0.95) : vec4(glint_color.rgb, 0.50);
		} else if (behind < 29.0) {
			float step_down = 1.0 - floor((behind - 5.0) / 8.0) / 3.0;
			c = on > 1.5 ? vec4(glint_color.rgb, 0.85 * step_down) : vec4(glow_color.rgb, 0.28 * step_down);
		}
	}
	COLOR = vec4(c.rgb, c.a * sim_motion);
}
