// The Workbench's diagnostic tape: paper that feeds past the read head a notch a tick, with its
// sprocket holes and a row of punched data. Drawn whole, per pixel, on a ColorRect the size of
// the tape (set rect_px), in wb_tape.png's own colours; the printed line is a Label over it.
//
// The feed is the global sim_tick_phase, so the tape runs while the factory runs and stops
// when it is paused.
shader_type canvas_item;

// Ticks since the last wrap (Core's TickVisuals.TickPhase).
global uniform float sim_tick_phase;
// 0 under Reduced motion: the tape lies still and the head's light is steady.
global uniform float sim_motion;

uniform vec2 rect_px = vec2(800.0, 64.0);
// One sprocket hole every this many pixels; the tape feeds one in ticks_per_hole ticks (Core's
// TickVisuals.TapeTicksPerHole).
uniform float hole_pitch_px = 16.0;
uniform float ticks_per_hole = 4.0;
// Where the read head stands, in pixels from the tape's left end.
uniform float head_px = 20.0;
uniform vec4 paper_color : source_color = vec4(0.859, 0.796, 0.647, 1.0); // #dbcba5
uniform vec4 edge_color : source_color = vec4(0.690, 0.627, 0.486, 1.0);  // #b0a07c
uniform vec4 lip_color : source_color = vec4(0.933, 0.886, 0.761, 1.0);   // #eee2c2
uniform vec4 fold_color : source_color = vec4(0.808, 0.745, 0.596, 1.0);  // #cebe98
uniform vec4 hole_color : source_color = vec4(0.376, 0.329, 0.235, 1.0);  // #60543c
uniform vec4 head_color : source_color = vec4(0.941, 0.627, 0.251, 1.0);  // fire #f0a040

// Whole ticks, plus the tick in progress eased into its first half: the notch lands, then rests.
float ratchet(float phase) {
	return floor(phase) + smoothstep(0.0, 0.5, fract(phase));
}

void fragment() {
	vec2 p = floor(UV * rect_px);
	float h = rect_px.y;

	vec3 rgb = paper_color.rgb;
	if (p.y < 1.0 || p.y >= h - 1.0) {
		rgb = edge_color.rgb;
	} else if (p.y < 2.0) {
		rgb = lip_color.rgb;
	} else if (abs(p.y - floor(h * 0.5)) < 0.5) {
		rgb = fold_color.rgb;
	}

	// The paper runs left, so what is under a pixel now was further right on the roll.
	float fed = floor(ratchet(sim_tick_phase) * sim_motion / ticks_per_hole * hole_pitch_px);
	float roll_x = p.x + fed;

	// Sprocket holes, 3 px square, along both edges.
	float in_pitch = mod(roll_x, hole_pitch_px);
	bool hole_column = in_pitch >= 7.0 && in_pitch < 10.0;
	bool sprocket_row = (p.y >= 6.0 && p.y < 9.0) || (p.y >= h - 9.0 && p.y < h - 6.0);
	if (hole_column && sprocket_row) {
		rgb = hole_color.rgb;
	}

	// Punched data under the printed line: two columns to a sprocket hole, each punched or not.
	// The column's number wraps where the phase does, so the pattern never jumps.
	float column_px = hole_pitch_px * 0.5;
	float column = mod(floor(roll_x / column_px), 512.0);
	float in_column = mod(roll_x, column_px);
	bool punched = fract(sin(column * 12.9898) * 43758.5453) > 0.58;
	if (punched && in_column >= 3.0 && in_column < 5.0 && p.y >= h - 18.0 && p.y < h - 14.0) {
		rgb = edge_color.rgb;
	}

	// The read head: a warm line the paper passes under, brightest as each tick lands.
	float beat = mix(0.6, 1.0 - smoothstep(0.0, 0.7, fract(sim_tick_phase)), sim_motion);
	float from_head = abs(p.x + 0.5 - head_px);
	if (p.y >= 2.0 && p.y < h - 1.0) {
		if (from_head < 1.0) {
			rgb = mix(rgb, head_color.rgb, 0.35 + 0.55 * beat);
		} else if (from_head < 3.0) {
			rgb = mix(rgb, head_color.rgb, 0.22 * beat);
		}
	}

	COLOR = vec4(rgb, 1.0);
}
