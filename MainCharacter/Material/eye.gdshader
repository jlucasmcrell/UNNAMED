// Ported to Godot 4 from the code Godot 3.2 generated for the original visual shader graph (Eye.tres).
// Same uniforms and the same math; Godot 4 cannot load the Godot 3 graph (its texture nodes and output ports changed).
shader_type spatial;
render_mode specular_schlick_ggx;

uniform sampler2D eyeAlbedo : source_color;
uniform vec4 eyeColor2 : source_color;
uniform sampler2D irisMask : source_color;
uniform vec4 eyeColor1 : source_color;
uniform sampler2D eyeOrm : source_color;
uniform sampler2D eyeNormal;




void fragment() {
// Input:17
	vec3 n_out17p0 = vec3(UV, 0.0);

// Texture:16
	vec4 eyeAlbedo_read = texture(eyeAlbedo, n_out17p0.xy);
	vec3 n_out16p0 = eyeAlbedo_read.rgb;
	float n_out16p1 = eyeAlbedo_read.a;

// ColorUniform:25
	vec3 n_out25p0 = eyeColor2.rgb;
	float n_out25p1 = eyeColor2.a;

// VectorOp:33
	vec3 n_out33p0 = n_out16p0 * n_out25p0;

// Texture:36
	vec4 irisMask_read = texture(irisMask, n_out17p0.xy);
	vec3 n_out36p0 = irisMask_read.rgb;
	float n_out36p1 = irisMask_read.a;

// VectorMix:34
	vec3 n_out34p0 = mix(n_out16p0, n_out33p0, n_out36p0);

// ColorUniform:35
	vec3 n_out35p0 = eyeColor1.rgb;
	float n_out35p1 = eyeColor1.a;

// Input:20
	vec3 n_out20p0 = vec3(UV, 0.0);

// Texture:21
	vec4 eyeOrm_read = texture(eyeOrm, n_out20p0.xy);
	vec3 n_out21p0 = eyeOrm_read.rgb;
	float n_out21p1 = eyeOrm_read.a;

// VectorDecompose:22
	float n_out22p0 = n_out21p0.x;
	float n_out22p1 = n_out21p0.y;
	float n_out22p2 = n_out21p0.z;

// VectorMix:37
	vec3 n_out37p0 = mix(n_out34p0, n_out35p0, vec3(n_out22p2));

// Input:19
	vec3 n_out19p0 = vec3(UV, 0.0);

// Texture:18
	vec4 eyeNormal_read = texture(eyeNormal, n_out19p0.xy);
	vec3 n_out18p0 = eyeNormal_read.rgb;
	float n_out18p1 = eyeNormal_read.a;

// Scalar:23
	float n_out23p0 = 1.000000;

// Output:0
	ALBEDO = n_out37p0;
	NORMAL_MAP = n_out18p0;
	AO_LIGHT_AFFECT = n_out23p0;

}
