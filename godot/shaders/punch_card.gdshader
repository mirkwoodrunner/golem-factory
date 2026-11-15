// The Workbench's read head on a socket: a glow down the socket's edge and light through the
// card's perforations while the golem is on that card, and a sweep across the card as the head
// arrives. Drawn on a ColorRect laid over the socket (set rect_px to its size); clear
// everywhere else, so the card under it is untouched.
//
// Which socket the head is on is Core's (WorkbenchSession.ReadHeadRow) and so is the sweep
// (WorkbenchReadHead.Sweep); the glow breathes with the global sim_tick_phase, so it holds
// steady while the factory is paused.
shader_type canvas_item;

// Ticks since the last wrap (Core's TickVisuals.TickPhase).
global uniform float sim_tick_phase;
// 0 under Reduced motion: the glow is steady.
global uniform float sim_motion;

uniform vec2 rect_px = vec2(240.0, 44.0);
// 1 while the read head is on this socket.
uniform float head = 0.0;
// 1 while a card sits in the socket: its perforations light.
uniform float has_card = 0.0;
// How far across the sweep has got, 0..1; 1 is over.
uniform float sweep = 1.0;
uniform vec4 glow_color : source_color = vec4(1.0, 0.878, 0.541, 1.0);    // fire #ffe08a
uniform vec4 ember_color : source_color = vec4(0.941, 0.627, 0.251, 1.0); // fire #f0a040

// Whether card-face pixel `p` is one of wb_card_face.png's punched holes: a column 4 px in from
// the left, a 2 px hole every 4 px. The face is 9-sliced with 10 px margins and its middle
// tiled, so a row is found the way the slicing finds it.
bool perforation(vec2 p, float height) {
	if (p.x < 4.0 || p.x >= 6.0) {
		return false;
	}
	float row = p.y;
	if (p.y >= height - 10.0) {
		row = p.y - height + 32.0;
	} else if (p.y >= 10.0) {
		row = 10.0 + mod(p.y - 10.0, 12.0);
	}
	return row >= 7.0 && row < 29.0 && mod(row - 7.0, 4.0) < 2.0;
}

void fragment() {
	vec2 p = floor(UV * rect_px);
	vec3 rgb = ember_color.rgb;
	float alpha = 0.0;

	if (head > 0.5) {
		// Brightest as each tick lands, never dark.
		float beat = mix(0.85, 1.0 - 0.3 * smoothstep(0.0, 0.8, fract(sim_tick_phase)), sim_motion);
		if (p.x < 2.0) {
			rgb = glow_color.rgb;
			alpha = 0.95 * beat;
		} else if (p.x < 4.0) {
			alpha = 0.60 * beat;
		} else if (p.x < 8.0) {
			alpha = 0.32 * beat;
		} else if (p.x < 14.0) {
			alpha = 0.14 * beat;
		}
		if (has_card > 0.5 && perforation(p, rect_px.y)) {
			rgb = glow_color.rgb;
			alpha = 1.0;
		}
	}

	if (sweep < 1.0) {
		// A bright line crossing left to right, three steps of ember behind it.
		float behind = floor(sweep * rect_px.x) - p.x;
		if (behind >= 0.0 && behind < 2.0) {
			rgb = glow_color.rgb;
			alpha = max(alpha, 0.85);
		} else if (behind >= 2.0 && behind < 20.0) {
			float trail = 0.30 * (1.0 - floor((behind - 2.0) / 6.0) / 3.0);
			if (trail > alpha) {
				rgb = ember_color.rgb;
				alpha = trail;
			}
		}
	}

	COLOR = vec4(rgb, alpha);
}
