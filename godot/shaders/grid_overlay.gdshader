// The build grid: cell lines, coordinate ticks and the cursor's brackets, drawn on the floor
// around the build cursor (Scripts/Visuals/GridOverlayNode). Everything is worked out from the
// fragment's world position, so there is no texture and the lines are the same art-pixel lines at
// every zoom: one world pixel is one art pixel (docs/art-style-bible.md §0.2), and a position is
// snapped to it before anything is decided, so nothing here is ever smoothed.
//
// The node only hands over where the cursor is and the colour Core's BuildGhostVisuals gives the
// ghost; the shader decides no placement rule.
shader_type canvas_item;
render_mode unshaded;

// The centre of the cursor's cell, in world pixels.
uniform vec2 cursor_px = vec2(0.0);
// The ghost's colour this frame (valid amber, blocked red with its pulse, can't-pay steel).
uniform vec4 cursor_color : source_color = vec4(1.0, 0.80, 0.36, 0.85);
// How far out from the cell the brackets stand: Core's GridOverlayRules.SnapOffset.
uniform float snap_px = 0.0;
// 1 while a build tool is in hand: the pool of grid around the cursor, and its brackets.
uniform float show_cursor = 1.0;
// 1: every cell boundary in view, at full strength (the spatial hash's cells are these cells).
uniform float debug_cells = 0.0;

uniform float cell_px = 64.0;
uniform float reach_cells = 4.5;
// A longer tick at every corner whose column and row both divide by this.
uniform float major_every = 4.0;
uniform vec4 line_color : source_color = vec4(0.886, 0.761, 0.478, 1.0);  // brass #e2c27a
uniform vec4 debug_color : source_color = vec4(0.369, 0.839, 0.784, 1.0); // aether #5ed6c8

varying vec2 world_px;

void vertex() {
	world_px = (MODEL_MATRIX * vec4(VERTEX, 0.0, 1.0)).xy;
}

void fragment() {
	// World pixels a screen pixel covers: above 1 the camera is zoomed out past one art pixel,
	// and a line must widen to stay a whole screen pixel instead of flickering away.
	float zoom_out = max(fwidth(world_px.x), fwidth(world_px.y));
	float line_w = max(1.0, ceil(zoom_out));
	vec2 p = floor(world_px) + 0.5;

	// A cell's centre is on a multiple of cell_px, so its corners are half a cell off.
	vec2 g = p + cell_px * 0.5;
	vec2 corner = floor(g / cell_px + 0.5);
	vec2 d = g - corner * cell_px; // signed distance to the nearest corner, per axis

	bool on_v = d.x >= 0.0 && d.x < line_w; // the vertical line through that corner
	bool on_h = d.y >= 0.0 && d.y < line_w;

	vec2 major_at = abs(mod(corner + 0.5, major_every) - 0.5);
	bool major = major_at.x < 0.25 && major_at.y < 0.25;
	float tick_len = (major ? 9.0 : 4.0) * line_w;
	bool tick = (on_v && abs(d.y) < tick_len) || (on_h && abs(d.x) < tick_len);

	// Lines are dotted two on, two off, measured from the corner so every cell matches; solid
	// once zoomed out, where a dot would be less than a pixel.
	float along = on_v ? d.y : d.x;
	bool dot_on = zoom_out > 1.0 || mod(floor(along), 4.0) < 2.0;
	bool line = (on_v || on_h) && dot_on;

	vec2 from_cursor = abs(p - cursor_px);
	float cheb = max(from_cursor.x, from_cursor.y);

	vec3 rgb = line_color.rgb;
	float alpha = 0.0;

	if (debug_cells > 0.5) {
		if (on_v || on_h) {
			rgb = debug_color.rgb;
			alpha = tick ? 0.85 : 0.45;
		}
	}

	if (show_cursor > 0.5) {
		// The pool: strongest at the cursor, gone at reach_cells, in four flat steps.
		float fall = clamp(1.0 - length(p - cursor_px) / (reach_cells * cell_px), 0.0, 1.0);
		fall = ceil(fall * 4.0) / 4.0;
		float layer = tick ? (major ? 0.90 : 0.70) : (line ? 0.32 : 0.0);
		if (layer * fall > alpha) {
			alpha = layer * fall;
			rgb = line_color.rgb;
		}

		// The cursor cell's own corners wear the ghost's colour.
		float half_cell = cell_px * 0.5;
		if (tick && cheb < half_cell + 10.0 * line_w && cheb > half_cell - 10.0 * line_w) {
			rgb = cursor_color.rgb;
			alpha = max(alpha, cursor_color.a);
		}

		// Brackets, a pixel clear of the cell, closing onto it as the cursor snaps.
		float inner = half_cell + line_w + floor(snap_px);
		float outer = inner + 2.0 * line_w;
		float arm = 12.0 * line_w;
		if (cheb >= inner && cheb < outer && min(from_cursor.x, from_cursor.y) >= outer - arm) {
			rgb = cursor_color.rgb;
			alpha = cursor_color.a;
		}

		// Nothing under the ghost itself: the tile there is the ghost's.
		if (cheb < half_cell) {
			alpha = 0.0;
		}
	}

	COLOR = vec4(rgb, alpha);
}
