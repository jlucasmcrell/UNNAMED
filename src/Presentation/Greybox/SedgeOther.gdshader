shader_type spatial;
render_mode unshaded,blend_add,cull_disabled,depth_draw_never,shadows_disabled;
varying vec3 wp;
void vertex(){wp=(MODEL_MATRIX*vec4(VERTEX,1)).xyz;VERTEX.x+=sin(TIME*.37+VERTEX.y*.13+wp.x*.21)*.48;}
void fragment(){
    float edge=pow(max(0.0,1.0-abs(UV.x-.5)*2.0),2.4);
    float wave=.5+.5*sin(wp.y*.17-TIME*.52+sin(wp.x*.71+TIME*.21)*2.7);
    float strands=pow(.5+.5*sin(UV.x*33.0+wp.y*.085-TIME*.31),4.0);
    float vertical=smoothstep(0.0,.1,UV.y)*smoothstep(1.0,.88,UV.y);
    float a=edge*vertical*(.035+wave*.085+strands*.17);
    ALBEDO=vec3(.35,.065,.82)*(1.2+wave*1.4);
    ALPHA=a;
}
