#!/usr/bin/env bash
# WP17 step 3: bake the player outfit - authored once on the Average body (mh_veth_<sex>) - onto one build body, inner layer first,
# through bl_garment.py: refit by the shared topology's per-triangle frames from the Average to the build (the body's shape carried
# into the cloth), then the layer clearance and push-out over the build and the layers beneath, weights from the nearest surface
# beneath. One variant per (people, sex, build); nothing morphs at runtime.
# The garment is the same garment on every build, so it hides the same body regions it hides on the Average (its masks are taken
# from the Average's fit, not re-guessed from the build's rest pose, where a heavy arm lying on the trunk hides the armpit band).
# Headwear is not refit: the head is the same on every build, so the cap stays exactly as authored on its one bone (a rigid piece
# refit from the body would follow the neck and shoulders instead).
# Usage: bake_outfit.sh <sex m|f> <target asset, e.g. mh_veth_m_heavy>
set -e
s=$1
target=$2
average=mh_veth_$s
T=/c/Otherreach_Recovery/asset_work/metahuman
W=$T/work/drape
B="/c/Program Files/Blender Foundation/Blender 5.2/blender.exe"
M() { cygpath -m "$1"; }
fit() {  # id source-dir [below] [rigid]: the spec's fit is the target for a rigid piece (no refit), hides the Average's
  tmp=$(mktemp -d)
  cp "$2/$1.glb" "$tmp/"
  python - "$(M $2/$1.json)" "$(M $W/rigged/${1}__$average/${1}__${average}_garment.json)" "$(M $tmp/$1.json)" "${4:+$target}" <<'EOF'
import json, sys
spec, average, out, rigid = json.load(open(sys.argv[1])), json.load(open(sys.argv[2])), sys.argv[3], sys.argv[4]
spec["hides"] = average["hides"]
if rigid:
    spec["fit"] = rigid
json.dump(spec, open(out, "w"), indent=1)
EOF
  "$B" -b --factory-startup --python "$(M $T/tools/bl_garment.py)" -- --source "$(M $tmp/$1.glb)" --spec "$(M $tmp/$1.json)" \
    --target $target --assembled "$(M $T/work/assembled)" --out "$(M $W/rigged)" ${3:+--below $3} 2>&1 | grep -E "Traceback|Error|sits" || true
  rm -rf "$tmp"
  [ -f "$W/rigged/${1}__$target/${1}__${target}_garment.json" ] || { echo "BAKE FAILED $1 on $target"; exit 1; }
}
fit player_shirt_$s $W/v4_player_shirt_$s
fit player_trousers_$s $W/v4_player_trousers_$s player_shirt_$s
fit player_boots_$s $W/src_$s player_shirt_$s,player_trousers_$s
fit player_jerkin_$s $W/v4_player_jerkin_$s player_shirt_$s,player_trousers_$s
fit player_hide_cap_$s $W/src_$s "" rigid
echo "BAKED $target"
