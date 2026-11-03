// Scavenge "CRT" look over the tactical map (ScavengeJuice): scanlines, a slight barrel curve, a touch of colour
// fringe at the edges and a slow background wobble. Drawn on a layer under the HUD, so text stays sharp.
// Mild by default; curve and wobble go to 0 with Reduce motion.
shader_type canvas_item;

uniform sampler2D screen : hint_screen_texture, filter_linear_mipmap;
uniform float scanlines : hint_range(0.0, 1.0) = 0.12;
uniform float curve : hint_range(0.0, 0.2) = 0.035;
uniform float wobble : hint_range(0.0, 0.01) = 0.0012;
uniform float fringe : hint_range(0.0, 0.01) = 0.0015;
uniform float vignette : hint_range(0.0, 1.0) = 0.22;

void fragment() {
	vec2 uv = SCREEN_UV;
	vec2 c = uv - 0.5;
	// Barrel: the corners bend out a little.
	uv = 0.5 + c * (1.0 + curve * dot(c, c));
	// A slow wobble, rows sliding a hair against each other.
	uv.x += wobble * sin(uv.y * 18.0 + TIME * 1.7);
	uv.y += wobble * 0.6 * sin(uv.x * 11.0 + TIME * 1.3);
	vec2 edge = c * fringe * 2.0;
	vec3 col = vec3(
		texture(screen, uv + edge).r,
		texture(screen, uv).g,
		texture(screen, uv - edge).b);
	// Scanlines in screen pixels, steady (no flicker).
	float line = 0.5 + 0.5 * sin(FRAGCOORD.y * 3.14159);
	col *= 1.0 - scanlines * (1.0 - line);
	col *= 1.0 - vignette * smoothstep(0.25, 0.75, length(c));
	// Outside the curved screen: black.
	float inside = step(0.0, uv.x) * step(uv.x, 1.0) * step(0.0, uv.y) * step(uv.y, 1.0);
	COLOR = vec4(col * inside, 1.0);
}
