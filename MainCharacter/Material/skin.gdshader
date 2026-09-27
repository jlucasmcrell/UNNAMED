// Ported to Godot 4 from the code Godot 3.2 generated for the original visual shader graph (Skin.tres).
// Same uniforms and the same math; Godot 4 cannot load the Godot 3 graph (its texture nodes and output ports changed).
shader_type spatial;
render_mode specular_schlick_ggx;

uniform vec4 facePaintColor : source_color;
uniform sampler2D albedo : source_color;
uniform float x;
uniform float eyeBrowHeight;
uniform float scale;
// Godot 3 imported the eyebrow and id masks without repeat; Godot 4 samplers repeat unless told not to,
// which tiles the eyebrows (placed with scaled UVs) all over the head.
uniform sampler2D eyebrows : source_color, repeat_disable;
uniform sampler2D subHair : source_color;
uniform sampler2D beard : source_color;
uniform vec4 skinTone : source_color;
uniform sampler2D darkSkinMask : source_color;
uniform sampler2D facePaint : source_color;
uniform sampler2D ors;
uniform float rPunch;
uniform float sPunch;
uniform sampler2D idMask : hint_default_black, repeat_disable;
uniform sampler2D normalDetail : hint_normal;
uniform sampler2D normal : hint_normal;
uniform float normalBlend;
uniform float normalScale;
uniform float scatt;
uniform vec4 transmision : source_color;




void fragment() {
// ColorUniform:139
	vec3 n_out139p0 = facePaintColor.rgb;
	float n_out139p1 = facePaintColor.a;

// Input:47
	vec3 n_out47p0 = vec3(UV, 0.0);

// TextureUniform:8
	vec3 n_out8p0;
	float n_out8p1;
	{
		vec4 n_tex_read = texture(albedo, n_out47p0.xy);
		n_out8p0 = n_tex_read.rgb;
		n_out8p1 = n_tex_read.a;
	}

// Input:78
	vec3 n_out78p0 = vec3(UV, 0.0);

// ScalarUniform:101
	float n_out101p0 = x;

// ScalarUniform:102
	float n_out102p0 = eyeBrowHeight;

// ScalarUniform:103
	float n_out103p0 = scale;

// Expression:100
	vec3 n_out100p0;
	n_out100p0 = vec3(0.0, 0.0, 0.0);
	{
		vec3 mult = n_out78p0*vec3(2.0,4.0,0.0)*n_out103p0;
		//float xx = n_out101p0;
		//float yy = n_out102p0;
		n_out100p0 = mult+vec3(n_out101p0,eyeBrowHeight,0.0);
	}

// TextureUniform:108
	vec3 n_out108p0;
	float n_out108p1;
	{
		vec4 n_tex_read = texture(eyebrows, n_out100p0.xy);
		n_out108p0 = n_tex_read.rgb;
		n_out108p1 = n_tex_read.a;
	}

// VectorOp:51
	vec3 n_out51p0 = n_out8p0 * n_out108p0;

// Input:106
	vec3 n_out106p0 = vec3(UV, 0.0);

// TextureUniform:48
	vec3 n_out48p0;
	float n_out48p1;
	{
		vec4 n_tex_read = texture(subHair, n_out106p0.xy);
		n_out48p0 = n_tex_read.rgb;
		n_out48p1 = n_tex_read.a;
	}

// VectorOp:115
	vec3 n_out115p0 = n_out51p0 * n_out48p0;

// TextureUniform:107
	vec3 n_out107p0;
	float n_out107p1;
	{
		vec4 n_tex_read = texture(beard, n_out106p0.xy);
		n_out107p0 = n_tex_read.rgb;
		n_out107p1 = n_tex_read.a;
	}

// VectorOp:104
	vec3 n_out104p0 = n_out115p0 * n_out107p0;

// ColorUniform:19
	vec3 n_out19p0 = skinTone.rgb;
	float n_out19p1 = skinTone.a;

// VectorOp:18
	vec3 n_out18p0 = n_out104p0 * n_out19p0;

// TextureUniform:124
	vec3 n_out124p0;
	float n_out124p1;
	{
		vec4 n_tex_read = texture(darkSkinMask, n_out106p0.xy);
		n_out124p0 = n_tex_read.rgb;
		n_out124p1 = n_tex_read.a;
	}

// ScalarOp:131
	float n_in131p1 = 0.08000;
	float n_out131p0 = dot(n_out124p0, vec3(0.333333, 0.333333, 0.333333)) * n_in131p1;

// ScalarFunc:130
	float n_out130p0 = 1.0 - n_out131p0;

// VectorMix:123
	vec3 n_out123p0 = mix(n_out104p0, n_out18p0, vec3(n_out130p0));

// Input:142
	vec3 n_out142p0 = vec3(UV, 0.0);

// TextureUniform:141
	vec3 n_out141p0;
	float n_out141p1;
	{
		vec4 n_tex_read = texture(facePaint, n_out142p0.xy);
		n_out141p0 = n_tex_read.rgb;
		n_out141p1 = n_tex_read.a;
	}

// VectorMix:145
	vec3 n_out145p0 = mix(n_out139p0, n_out123p0, n_out141p0);

// Input:17
	vec3 n_out17p0 = vec3(UV, 0.0);

// TextureUniform:15
	vec3 n_out15p0;
	float n_out15p1;
	{
		vec4 n_tex_read = texture(ors, n_out17p0.xy);
		n_out15p0 = n_tex_read.rgb;
		n_out15p1 = n_tex_read.a;
	}

// VectorDecompose:16
	float n_out16p0 = n_out15p0.x;
	float n_out16p1 = n_out15p0.y;
	float n_out16p2 = n_out15p0.z;

// ScalarUniform:23
	float n_out23p0 = rPunch;

// ScalarOp:22
	float n_out22p0 = n_out16p1 * n_out23p0;

// ScalarFunc:116
	float n_out116p0 = 1.0 - dot(n_out108p0, vec3(0.333333, 0.333333, 0.333333));

// ScalarOp:117
	float n_out117p0 = n_out22p0 + n_out116p0;

// ScalarFunc:119
	float n_out119p0 = 1.0 - dot(n_out48p0, vec3(0.333333, 0.333333, 0.333333));

// ScalarOp:118
	float n_out118p0 = n_out117p0 + n_out119p0;

// ScalarFunc:120
	float n_out120p0 = 1.0 - dot(n_out107p0, vec3(0.333333, 0.333333, 0.333333));

// ScalarOp:121
	float n_out121p0 = n_out118p0 + n_out120p0;

// ScalarFunc:147
	float n_out147p0 = 1.0 - dot(n_out141p0, vec3(0.333333, 0.333333, 0.333333));

// ScalarOp:146
	float n_out146p0 = n_out121p0 + n_out147p0;

// ScalarUniform:39
	float n_out39p0 = sPunch;

// ScalarOp:54
	float n_out54p0 = n_out15p1 * n_out39p0;

// Color:61
	vec3 n_out61p0 = vec3(0.021670, 0.090011, 0.156250);
	float n_out61p1 = 1.000000;

// Input:65
	vec3 n_out65p0 = vec3(UV, 0.0);

// TextureUniform:75
	vec3 n_out75p0;
	float n_out75p1;
	{
		vec4 n_tex_read = texture(idMask, n_out65p0.xy);
		n_out75p0 = n_tex_read.rgb;
		n_out75p1 = n_tex_read.a;
	}

// VectorMix:74
	vec3 n_in74p0 = vec3(0.00000, 0.00000, 0.00000);
	vec3 n_out74p0 = mix(n_in74p0, n_out61p0, n_out75p0);

// Input:6
	vec3 n_out6p0 = vec3(UV, 0.0);

// TextureUniform:133
	vec3 n_out133p0;
	float n_out133p1;
	{
		vec4 n_tex_read = texture(normalDetail, n_out6p0.xy);
		n_out133p0 = n_tex_read.rgb;
		n_out133p1 = n_tex_read.a;
	}

// TextureUniform:4
	vec3 n_out4p0;
	float n_out4p1;
	{
		vec4 n_tex_read = texture(normal, n_out6p0.xy);
		n_out4p0 = n_tex_read.rgb;
		n_out4p1 = n_tex_read.a;
	}

// ColorOp:136
	vec3 n_out136p0;
	{
		float base = n_out133p0.x;
		float blend = n_out4p0.x;
		if (base < 0.5) {
			n_out136p0.x = 2.0 * base * blend;
		} else {
			n_out136p0.x = 1.0 - 2.0 * (1.0 - blend) * (1.0 - base);
		}
	}
	{
		float base = n_out133p0.y;
		float blend = n_out4p0.y;
		if (base < 0.5) {
			n_out136p0.y = 2.0 * base * blend;
		} else {
			n_out136p0.y = 1.0 - 2.0 * (1.0 - blend) * (1.0 - base);
		}
	}
	{
		float base = n_out133p0.z;
		float blend = n_out4p0.z;
		if (base < 0.5) {
			n_out136p0.z = 2.0 * base * blend;
		} else {
			n_out136p0.z = 1.0 - 2.0 * (1.0 - blend) * (1.0 - base);
		}
	}

// ScalarUniform:135
	float n_out135p0 = normalBlend;

// VectorMix:134
	vec3 n_out134p0 = mix(n_out133p0, n_out136p0, vec3(n_out135p0));

// ScalarUniform:43
	float n_out43p0 = normalScale;

// ScalarUniform:122
	float n_out122p0 = scatt;

// ColorUniform:26
	vec3 n_out26p0 = transmision.rgb;
	float n_out26p1 = transmision.a;

// VectorOp:25
	vec3 n_out25p0 = vec3(n_out16p2) * n_out26p0;

// Scalar:52
	float n_out52p0 = 0.500000;

// Output:0
	ALBEDO = n_out145p0;
	ROUGHNESS = n_out146p0;
	SPECULAR = n_out54p0;
	EMISSION = n_out74p0;
	AO = n_out16p0;
	NORMAL_MAP = n_out134p0;
	NORMAL_MAP_DEPTH = n_out43p0;
	SSS_STRENGTH = n_out122p0;
	BACKLIGHT = n_out25p0;
	AO_LIGHT_AFFECT = n_out52p0;

}
