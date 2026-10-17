#!/usr/bin/env bash
# WP17 outfit v8: the player vest (jerkin), layer 5, re-draped as leather rather than cloth. The v7 vest was draped at cloth bending
# (8) and compression (2) and pushed onto the shirt, so it shrink-wrapped the bust and belly (the abs and navel showed through the
# leather) and its hem knotted. Here the same pattern (GarmentCode jerkin, gc/jerkin_<sex>, drafted on the fit body) drapes stiff
# over the v8 layers beneath (trousers, boots, shirt), takes 3 mm of thickness with a closed rim (bl_thicken.py), and the leather
# scan tiles at 0.25 m (it read oversized at 0.5).
# Usage: drape_vest.sh <sex m|f>   -> work/drape/v8/vest_<sex>/ (source and spec for bake_outfit.sh)
set -e
s=$1
T=/c/Otherreach_Recovery/asset_work/metahuman
W=$T/work/drape
B="/c/Program Files/Blender Foundation/Blender 5.2/blender.exe"
P=/c/Otherreach_External_Assets/PolyHaven/textures
M() { cygpath -m "$1"; }
id=player_jerkin_$s
out=$W/v8/vest_$s
mkdir -p "$out"
"$B" -b --factory-startup --python "$(M $T/tools/bl_garment.py)" -- --drape-body mh_veth_$s --below player_trousers_$s,player_boots_$s,player_shirt_$s \
  --rigged "$(M $W/rigged)" --assembled "$(M $T/work/assembled)" --out "$(M $W/v8/scene_vest_$s)" 2>&1 | grep -E "DRAPE|Traceback" | cut -c1-160
"$B" -b --factory-startup --python "$(M /c/af/assetforge/blender/garment_drape.py)" -- --panels "$(M $W/gc/jerkin_$s/panels.json)" \
  --out "$(M $out/drape)" --name $id --fit-blend "$(M $W/v8/scene_vest_$s/mh_veth_${s}_drape.blend)" --scale 1.0 --sew-force 0 --bending 20 \
  --stiffness 80 --compression 20 --smooth 12 --texture "$(M $W/fabric/leather_brown_albedo.jpg)" --normal "$(M $P/brown_leather/brown_leather_nor_gl_4k.jpg)" \
  --roughness "$(M $W/fabric/leather_brown_roughness.png)" --tile-m 0.25 --tris 6000 > "$out.log" 2>&1
"$B" -b --factory-startup --python "$(M $(dirname "$0")/bl_thicken.py)" -- --source "$(M $out/drape/$id.glb)" --out "$(M $out/$id.glb)" --mm 3 2>&1 | grep -E "THICK|Traceback"
python - "$(M $W/v4_$id/$id.json)" "$(M $out/$id.json)" <<'PY'
import json, sys
spec = json.load(open(sys.argv[1]))
spec["provenance"] += " Re-draped stiff over outfit v8's layers with 3 mm thickness for WP17 (tools/character_build/drape_vest.sh)."
json.dump(spec, open(sys.argv[2], "w"), indent=1)
PY
python -c "import json,sys;d=json.load(open(sys.argv[1]));print('$id', 'seams mm', d.get('seam_gap_mm_when_welded'), 'tris', d.get('game_triangles'), 'on body', d.get('on_body_fraction'))" "$(M $out/drape/${id}_drape_report.json)"
