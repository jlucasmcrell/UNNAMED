shader_type spatial;
render_mode cull_back, depth_draw_opaque;
uniform sampler2D source_albedo : source_color,filter_linear_mipmap_anisotropic,repeat_enable;
uniform sampler2D source_normal : hint_normal,filter_linear_mipmap_anisotropic,repeat_enable;
uniform sampler2D source_roughness_tex : filter_linear_mipmap_anisotropic,repeat_enable;
uniform sampler2D source_metallic_tex : filter_linear_mipmap_anisotropic,repeat_enable;
uniform bool has_roughness = false;
uniform bool has_metallic = false;
uniform bool use_vertex_colour = false;
uniform int roughness_channel = 1;
uniform int metallic_channel = 2;
uniform vec4 source_colour : source_color = vec4(1.0);
uniform bool has_albedo = false;
uniform bool has_normal = false;
uniform float normal_strength = 1.0;
uniform float source_roughness = .8;
uniform float source_metallic = 0.0;
varying vec3 wp;
void vertex(){wp=(MODEL_MATRIX*vec4(VERTEX,1)).xyz;}
float channel(vec4 value,int c){
    if(c==0){return value.r;}if(c==1){return value.g;}if(c==2){return value.b;}if(c==3){return value.a;}
    return dot(value.rgb,vec3(.333333));
}
void fragment(){
    vec3 a=source_colour.rgb;
    if(has_albedo){a*=texture(source_albedo,UV).rgb;}
    if(use_vertex_colour){a*=COLOR.rgb;}
    float runnel=.5+.5*sin(wp.x*.73+sin(wp.z*.4)*2.0+sin(wp.x*2.6)*.35);
    float lower=1.0-smoothstep(2.0,37.0,wp.y+runnel*5.0);
    float wet=1.0-smoothstep(.65,4.7,wp.y+runnel*.6);
    float weed=wet*smoothstep(.50,.72,runnel);
    a=mix(a,a*vec3(.42,.54,.44),lower*.65);
    a=mix(a,vec3(.045,.075,.029),weed*.68);
    float salt=(1.0-smoothstep(.07,.26,abs(wp.y-.82+sin(wp.x*.9)*.035)))*.70;
    salt+=smoothstep(6.0,16.0,wp.y)*(1.0-smoothstep(18.0,31.0,wp.y))*smoothstep(.60,.92,runnel)*.3;
    a=mix(a,vec3(.58,.59,.50),salt);
    ALBEDO=a;
    if(has_normal){NORMAL_MAP=texture(source_normal,UV).rgb;NORMAL_MAP_DEPTH=normal_strength;}
    float rough=source_roughness;
    float metal=source_metallic;
    if(has_roughness){rough*=channel(texture(source_roughness_tex,UV),roughness_channel);}
    if(has_metallic){metal*=channel(texture(source_metallic_tex,UV),metallic_channel);}
    ROUGHNESS=mix(rough,.24,wet);METALLIC=metal;SPECULAR=.48;
}
