#!/usr/bin/env bash
# WP17: re-drape the player trousers on the Average fit body (mh_veth_<sex>), the innermost layer of outfit v8 (nothing beneath). The v7 pair (GarmentCode
# Pants width 1.12, flare 0.8) hung its crotch 12-19 cm under the body's: the 12 % ease widened the crotch curve and the soft wool (tension
# 25) stretched under gravity, so any stride pulled the cloth between the legs into a web. This pair is drafted fitted (width 1.0,
# flare 0.9, gc/designs/trousers_fit.json, on the body's own measurements), its waistband set at the body's own natural waist
# (garment_drape.py --waist-to-body: GarmentCode put it at its mean body's waist height, 4-7 cm low on the Veth) and draped stiffer.
# Usage: drape_trousers.sh <sex m|f>   -> work/drape/v6_player_trousers_<sex>/ (source and spec for bake_outfit.sh)
set -e
s=$1
T=/c/Otherreach_Recovery/asset_work/metahuman
W=$T/work/drape
B="/c/Program Files/Blender Foundation/Blender 5.2/blender.exe"
P=/c/Otherreach_External_Assets/PolyHaven/textures
M() { cygpath -m "$1"; }
id=player_trousers_$s
out=$W/v6_$id
mkdir -p "$out"
"$B" -b --factory-startup --python "$(M $T/tools/bl_garment.py)" -- --drape-body mh_veth_$s \
  --assembled "$(M $T/work/assembled)" --out "$(M $W/scene_v6_$id)" 2>&1 | grep -E "DRAPE|Traceback" | cut -c1-160
"$B" -b --factory-startup --python "$(M /c/af/assetforge/blender/garment_drape.py)" -- --panels "$(M $W/gc/trousers_rise_$s/panels.json)" \
  --out "$(M $out)" --name $id --fit-blend "$(M $W/scene_v6_$id/mh_veth_${s}_drape.blend)" --scale 1.0 --waist-to-body --sew-force 0 --bending 4 \
  --stiffness 60 --compression 6 --smooth 8 --texture "$(M $W/fabric/wool_brown_albedo.jpg)" --normal "$(M $(ls $P/caban/*nor_gl*4k* | head -1))" \
  --roughness "$(M $W/fabric/wool_brown_roughness.png)" --tile-m 0.5 --tris 12000 > "$out.log" 2>&1
python - "$(M $W/v4_$id/$id.json)" "$(M $out/$id.json)" <<'EOF'
import json, sys
spec = json.load(open(sys.argv[1]))
spec["licence"]["verified_from"] = spec["licence"]["verified_from"].replace("gc/trousers_", "gc/trousers_rise_")
spec["layer"] = 1
spec["own_legs"] = True
spec["hides"] = ["pelvis", "glutes", "thigh_l", "thigh_r", "calf_l", "calf_r", "ankle_l", "ankle_r"]
spec["tuck"] = ["player_boots_" + sys.argv[2].rsplit("_", 1)[1][0]]
spec["provenance"] += " Re-drafted fitted and re-draped stiffer for WP17 (tools/character_build/drape_trousers.sh)."
json.dump(spec, open(sys.argv[2], "w"), indent=1)
EOF
python -c "import json,sys;d=json.load(open(sys.argv[1]));print('$id', 'seams mm', d.get('seam_gap_mm_when_welded'), 'tris', d.get('game_triangles'), 'on body', d.get('on_body_fraction'))" "$(M $out/${id}_drape_report.json)"
