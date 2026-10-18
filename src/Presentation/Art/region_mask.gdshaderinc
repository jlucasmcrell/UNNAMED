// Body and garment region masks (CHARACTER_ANIMATION_STRATEGY.md 4.3 rule 6). UV2.x holds each vertex's region ID
// (asset_work/metahuman/tools/regions.json, written by bl_assemble.py and bl_garment.py); a set bit of region_hidden_mask hides that
// region. Its vertices become NaN, which drops every triangle touching it - in the shadow pass too - so a hidden region's border is
// conservative and the garment over it keeps the 15 mm margin. Outfit.cs sets the mask per mesh; 0 (every other model) hides nothing.
instance uniform int region_hidden_mask = 0;
// Optional source-vertex masks baked for a modular wardrobe. Negative UV2.y encodes -(flags + 1);
// legacy assets use non-negative UV2.y. Ten bits stay exact even in half-float UV attributes.
instance uniform int source_hidden_mask = 0;

void region_mask(inout vec3 vertex, vec2 uv2) {
	int region = clamp(int(uv2.x + 0.5), 0, 30);
	int source_flags = uv2.y < 0.0 ? int(-uv2.y - 1.0 + 0.5) : 0;
	if ((region_hidden_mask & (1 << region)) != 0 || (source_hidden_mask & source_flags) != 0) {
		vertex = vec3(intBitsToFloat(0x7fc00000));
	}
}
