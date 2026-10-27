shader_type spatial;
render_mode cull_back, depth_draw_opaque;
uniform sampler2D stone_albedo : source_color,filter_linear_mipmap_anisotropic,repeat_enable;
uniform sampler2D stone_normal : hint_normal,filter_linear_mipmap_anisotropic,repeat_enable;
varying vec3 wp;
varying vec3 wn;
float hash(vec2 p){return fract(sin(dot(p,vec2(127.1,311.7)))*43758.5453);}
void vertex(){wp=(MODEL_MATRIX*vec4(VERTEX,1)).xyz;wn=normalize(MODEL_NORMAL_MATRIX*NORMAL);}
void fragment(){
    vec3 w=pow(abs(wn),vec3(5));w/=max(w.x+w.y+w.z,.0001);
    vec3 a=texture(stone_albedo,wp.zy/3.5).rgb*w.x+texture(stone_albedo,wp.xz/3.5).rgb*w.y+texture(stone_albedo,wp.xy/3.5).rgb*w.z;
    float fleck=hash(floor(wp.xz*2.0)+floor(wp.y*4.0));
    float strand=.5+.5*sin(wp.x*4.7+sin(wp.z*1.8)*2.0);
    float tidal=1.0-smoothstep(.70,4.4,wp.y+strand*.5);
    float weed=tidal*smoothstep(.44,.74,strand)*smoothstep(.1,1.0,wp.y+2.0);
    a=mix(a,a*vec3(.29,.39,.33),tidal*.80);
    a=mix(a,vec3(.075,.105,.054),weed*.65);
    float line=1.0-smoothstep(.04,.16,abs(wp.y-.77+sin(wp.x*.9)*.035));
    a=mix(a,vec3(.47,.48,.37),line*.55);
    ALBEDO=a*mix(.88,1.10,fleck);
    NORMAL_MAP=texture(stone_normal,wp.xy/3.5).rgb;NORMAL_MAP_DEPTH=.85;
    ROUGHNESS=mix(.85,.29,tidal);SPECULAR=.48;
}
