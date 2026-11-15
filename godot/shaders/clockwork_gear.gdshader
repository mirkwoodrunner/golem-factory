// A brass gear that turns on the factory's clock: one notch a tick, like an escapement, so it
// speeds up, slows down and stops with the simulation (the global sim_tick_phase).
//
// The gear is worked out per art pixel from its outline, never rotated from a picture, so every
// angle is a clean pixel drawing and the light stays in the upper left as it turns
// (docs/art-style-bible.md §0.15). Used two ways: on a Control (the HUD's and the Workbench's
// gears; set rect_px to the control's size), and in a MultiMesh with `instanced` on (every belt
// splitter's hub in one draw, Scripts/Visuals/TickVisualsNode), where INSTANCE_CUSTOM.r is the
// way it turns (+1 or -1) and .g its starting angle, in turns.
shader_type canvas_item;

// Ticks since the last wrap (Core's TickVisuals.TickPhase).
global uniform float sim_tick_phase;
// 0 under Reduced motion: the gear holds still.
global uniform float sim_motion;

uniform bool instanced = false;
// The control's size in pixels; unused when instanced, where the mesh is already in pixels.
uniform vec2 rect_px = vec2(32.0);
// A full turn takes 1 / turns_per_tick ticks (Core's TickVisuals.GearTicksPerTurn).
uniform float turns_per_tick = 0.015625;
uniform float direction = 1.0;
uniform float teeth = 8.0;
uniform float radius_px = 12.0;
uniform float tooth_px = 3.0;
// The axle's hole; 0 for none.
uniform float bore_px = 2.0;
// Above 0 the wheel is open between its hub and rim, with this many spokes.
uniform float spokes = 0.0;
uniform vec4 body_color : source_color = vec4(0.769, 0.584, 0.267, 1.0);    // brass #c49544
uniform vec4 light_color : source_color = vec4(0.886, 0.761, 0.478, 1.0);   // brass #e2c27a
uniform vec4 shade_color : source_color = vec4(0.588, 0.471, 0.314, 1.0);   // brass #967850
uniform vec4 outline_color : source_color = vec4(0.184, 0.129, 0.110, 1.0); // outline #2f211c

varying flat vec2 turning; // x: direction, y: starting angle in turns
varying vec2 local_px;

void vertex() {
	if (instanced) {
		local_px = VERTEX;
		turning = INSTANCE_CUSTOM.rg;
	} else {
		local_px = (UV - vec2(0.5)) * rect_px;
		turning = vec2(direction, 0.0);
	}
}

// Whole ticks, plus the tick in progress eased into its first half: the notch lands, then rests.
float ratchet(float phase) {
	return floor(phase) + smoothstep(0.0, 0.5, fract(phase));
}

bool solid(vec2 p, float angle) {
	float r = length(p);
	if (r >= radius_px || r < bore_px) {
		return false;
	}
	float root = radius_px - tooth_px;
	float a = atan(p.y, p.x) - angle;
	if (r >= root) {
		// A tooth narrows toward its tip.
		return cos(a * teeth) > mix(-0.15, 0.45, (r - root) / tooth_px);
	}
	if (spokes > 0.5 && r > bore_px + 3.0 && r < root - 3.0) {
		// Open, but for the spokes: 3 px wide whatever the radius.
		return abs(sin(a * spokes * 0.5)) * r < 1.5;
	}
	return true;
}

void fragment() {
	vec2 p = floor(local_px) + 0.5;
	float turn = ratchet(sim_tick_phase) * turns_per_tick * turning.x * sim_motion + turning.y;
	float angle = fract(turn) * TAU;

	vec4 c = vec4(0.0);
	if (solid(p, angle)) {
		bool edge = !solid(p + vec2(1.0, 0.0), angle) || !solid(p - vec2(1.0, 0.0), angle)
			|| !solid(p + vec2(0.0, 1.0), angle) || !solid(p - vec2(0.0, 1.0), angle);
		if (edge) {
			c = outline_color;
		} else if (!solid(p - vec2(2.0), angle)) {
			c = light_color; // the outline is up and to the left: this face catches the light
		} else if (!solid(p + vec2(2.0), angle)) {
			c = shade_color;
		} else {
			c = body_color;
		}
	}
	COLOR = c * COLOR;
}
