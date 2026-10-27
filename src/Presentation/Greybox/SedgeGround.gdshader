shader_type spatial;
render_mode cull_back, depth_draw_opaque;
global uniform float weather_wet;
uniform sampler2D peat_albedo : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D peat_normal : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D salt_albedo : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D salt_normal : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform float tide_level = -0.60;
varying vec3 wp;
float hash(vec2 p) { return fract(sin(dot(p,vec2(127.1,311.7)))*43758.5453); }
float noise(vec2 p) { vec2 i=floor(p),f=fract(p);vec2 u=f*f*(3.0-2.0*f);return mix(mix(hash(i),hash(i+vec2(1,0)),u.x),mix(hash(i+vec2(0,1)),hash(i+vec2(1,1)),u.x),u.y); }
void vertex() { wp=(MODEL_MATRIX*vec4(VERTEX,1)).xyz; }
void fragment() {
    vec2 p=wp.xz;
    float macro=noise(p*.017),detail=noise(p*.32),small=noise(p*1.9);
    float salt=smoothstep(5050.0,5250.0,p.x+(macro-.5)*100.0)*smoothstep(.35,.9,wp.y);
    vec3 peat=texture(peat_albedo,p/2.0).rgb;
    vec3 crust=texture(salt_albedo,p/3.0).rgb;
    float crust_luma=dot(crust,vec3(.2126,.7152,.0722));
    crust=mix(crust,vec3(crust_luma*1.16,crust_luma*1.18,crust_luma*1.15),.85);
    // High salt rims dry pale; ebb mud stays dark, saturated and reflective between ridges.
    float wet=max(1.0-smoothstep(tide_level+.08,tide_level+1.35,wp.y),.46);
    wet=max(wet,weather_wet*.86);
    float pool=(1.0-smoothstep(.28,.41,detail+.16*(small-.5)))*wet;
    vec3 colour=mix(peat*.72,crust*vec3(1.20,1.23,1.16),salt);
    colour*=mix(.78,1.18,macro);
    colour=mix(colour,colour*vec3(.40,.49,.49),pool*.72);
    ALBEDO=colour;
    vec3 n=mix(texture(peat_normal,p/2.0).rgb,texture(salt_normal,p/3.0).rgb,salt);
    NORMAL_MAP=mix(n,vec3(.5,.5,1.0),pool*.80);
    NORMAL_MAP_DEPTH=1.15;
    ROUGHNESS=mix(.30,.78,salt)*(1.0-pool*.88);
    SPECULAR=.6;
    METALLIC=0.0;
}
