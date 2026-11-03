// A big hit's flash (ScavengeJuice): a tinted vignette that closes in from the edges, the centre stays clear.
// Strength is tweened 0 -> peak -> 0 in about a quarter second; flashes are capped (FlashGate) against strobing.
shader_type canvas_item;

uniform vec4 tint : source_color = vec4(1.0, 0.25, 0.15, 1.0);
uniform float strength : hint_range(0.0, 1.0) = 0.0;

void fragment() {
	float d = length(UV - 0.5) * 1.414;
	float ring = smoothstep(0.35, 1.0, d);
	COLOR = vec4(tint.rgb, tint.a * strength * (0.15 + 0.85 * ring));
}
