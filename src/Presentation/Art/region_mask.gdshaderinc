// Body and garment region masks (CHARACTER_ANIMATION_STRATEGY.md 4.3 rule 6). UV2.x holds each vertex's region ID
// (asset_work/metahuman/tools/regions.json, written by bl_assemble.py and bl_garment.py); a set bit of region_hidden_mask hides that
// region. Its vertices become NaN, which drops every triangle touching it - in the shadow pass too - so a hidden region's border is
// conservative and the garment over it keeps the 15 mm margin. Outfit.cs sets the mask per mesh; 0 (every other model) hides nothing.
instance uniform int region_hidden_mask = 0;

void region_mask(inout vec3 vertex, vec2 uv2) {
	int region = clamp(int(uv2.x + 0.5), 0, 30);
	if ((region_hidden_mask & (1 << region)) != 0) {
		vertex = vec3(intBitsToFloat(0x7fc00000));
	}
}
