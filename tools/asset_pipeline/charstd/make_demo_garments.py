"""The demo cast's garments (Phase B demo): one descriptor per garment in garments/<id>.json and its textures composed from a sourced
CC0 PBR set (garment_texture.py). Data only - the standard's own tools fit, check and build them. Re-runnable: it rewrites the
descriptors and textures from this table.

    python make_demo_garments.py
"""
import json
import os
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ASSETS = os.path.abspath(os.path.join(HERE, "..", "..", "..", "assets"))
MPFB = os.path.expandvars(r"%APPDATA%\Blender Foundation\Blender\5.2\extensions\.user\user_default\mpfb\data\clothes")
MATS = r"F:\Otherreach_External_Assets\materials"

SRC = {
    "wizard_robe": ("MaciekG (MakeHuman community)", "CC-BY 4.0", "Wizard Robe by MaciekG", "mage_robe_diffuse.png"),
    "elvs_male_trench_coat1": ("Elvaerwyn (MakeHuman community)", "CC-BY 4.0", "Elvs Male Trench Coat1 by Elvaerwyn", "trench1.jpg"),
    "medieval_dress_nonhistorical": ("punkduck (MakeHuman community)", "CC-BY 4.0", "Medieval Dress (non-historical) by punkduck", "medievaldress.png"),
    "female_knee_boots": ("punkduck (MakeHuman community)", "CC-BY 4.0", "female knee boots by punkduck", "kneeboots.png"),
    "elvs_mens_apron1": ("Elvaerwyn (MakeHuman community)", "CC-BY 4.0", "Elvs Mens Apron1 by Elvaerwyn", "mensaprontex1.png"),
    "powerman_bracers": ("culturalibre (MakeHuman community)", "CC-BY 4.0", "Powerman bracers by culturalibre", "powerman_bracers.png"),
    "leather_armor": ("MaciekG (MakeHuman community)", "CC-BY 4.0", "Leather Armor by MaciekG", "leather_armor_diffuse.png"),
    "leather_pants": ("MaciekG (MakeHuman community)", "CC-BY 4.0", "Leather Pants by MaciekG", "leather_pants_diffuse.png"),
    "boots_viking": ("RehmanPolanski (MakeHuman community)", "CC0 1.0", None, "BootsViking.png"),
    "male_boots": ("culturalibre (MakeHuman community)", "CC0 1.0", None, "boot.png"),
}

# id: archetype, role, layer, source, hides, material set, tint, tiles, detail, kind, offset, cut, why
GARMENTS = {
    "robe_magistrate_a": ("humanoid_masculine_a", "robe_coat", "outer", "wizard_robe",
                          ["chest_upper", "chest", "abdomen", "back_upper", "back", "shoulder_L", "shoulder_R", "groin", "thigh_L", "thigh_R",
                           "knee_L", "knee_R", "upperarm_L", "upperarm_R", "elbow_L", "elbow_R", "forearm_L", "forearm_R"],
                          "ambientcg__Fabric030", "0.07,0.07,0.08", 10, 0.0, "cloth_heavy", 0.0, None,
                          "Renn's magistrate robe: the wizard robe's full-length cut in charcoal wool (its own plaid dropped: detail 0). Hides the whole body under its high collar and long sleeves: the robe fits close at the chest and shoulders and the skin showed through (in-game stills, 2026-09-26)."),
    "shoes_magistrate_a": ("humanoid_masculine_a", "footwear", "base", "male_boots", ["foot_L", "foot_R"],
                           "ambientcg__Leather032", "0.05,0.045,0.04", 3, 0.8, "leather", 0.0, None, "Renn's black leather boots."),
    "coat_long_guide_a": ("humanoid_masculine_a", "robe_coat", "outer", "elvs_male_trench_coat1",
                          ["upperarm_L", "upperarm_R", "elbow_L", "elbow_R", "back_upper", "back"],
                          "ambientcg__Leather030", "0.24,0.17,0.11", 5, 0.55, "leather", 0.006, None,
                          "Tavar's long weathered leather coat (the trench coat's cut, open at the front over his shirt)."),
    "boots_tall_guide_a": ("humanoid_masculine_a", "footwear", "base", "boots_viking", ["foot_L", "foot_R", "shin_L", "shin_R"],
                           "ambientcg__Leather037", "0.17,0.12,0.08", 3, 0.7, "leather", 0.0, None, "Tavar's tall worn boots."),
    "dress_archivist_a": ("humanoid_feminine_a", "robe_coat", "outer", "medieval_dress_nonhistorical",
                          ["abdomen", "back", "groin", "thigh_L", "thigh_R", "knee_L", "knee_R", "upperarm_L", "upperarm_R",
                           "elbow_L", "elbow_R"],
                          "ambientcg__Fabric019", "0.80,0.77,0.70", 10, 0.45, "cloth", 0.0, None,
                          "Sel's long ivory wool dress with bell sleeves (the medieval dress's cut, re-materialled). Keeps the chest: its square neckline showed the hole where the chest was hidden (in-game stills, 2026-09-26)."),
    "boots_archivist_a": ("humanoid_feminine_a", "footwear", "base", "female_knee_boots", ["foot_L", "foot_R", "shin_L", "shin_R"],
                          "ambientcg__Leather032", "0.33,0.33,0.34", 3, 0.7, "leather", 0.0, None, "Sel's soft grey knee boots."),
    "apron_smith_a": ("humanoid_masculine_a", "outer_torso", "outer", "elvs_mens_apron1", [],
                      "ambientcg__Leather032", "0.11,0.085,0.07", 4, 0.6, "leather", 0.008, None,
                      "Kera's heavy scorched leather smithing apron over the bare torso (hides nothing: the apron is open at the sides)."),
    "bracers_smith_a": ("humanoid_masculine_a", "gloves", "outer", "powerman_bracers", [],
                        "ambientcg__Metal063", "0.20,0.18,0.16", 2, 0.5, "metal", 0.004, None, "Kera's iron-banded forearm bracers."),
    "pauldron_smith_a": ("humanoid_masculine_a", "shoulders", "outer", "leather_armor", [],
                         "ambientcg__Metal063", "0.22,0.19,0.16", 2, 0.6, "metal", 0.006,
                         {"keep_bones": ["shoulder.L", "upper_arm.L"], "why": "the left pauldron only, as in the concept (authored once)"},
                         "Kera's single iron pauldron on the left shoulder."),
    "trousers_smith_a": ("humanoid_masculine_a", "bottom", "base", "leather_pants",
                         ["groin", "thigh_L", "thigh_R", "knee_L", "knee_R", "shin_L", "shin_R"],
                         "ambientcg__Fabric061", "0.16,0.13,0.10", 8, 0.6, "cloth_heavy", 0.0, None, "Kera's heavy work trousers."),
    "boots_smith_a": ("humanoid_masculine_a", "footwear", "base", "boots_viking", ["foot_L", "foot_R", "shin_L", "shin_R"],
                      "ambientcg__Leather030", "0.14,0.10,0.07", 3, 0.7, "leather", 0.004, None, "Kera's heavy work boots."),
}

for gid, (arch, role, layer, src, hides, mset, tint, tiles, detail, kind, offset, cut, why) in GARMENTS.items():
    author, lic, credit, diffuse = SRC[src]
    tex = os.path.join(ASSETS, "_staging", "charstd", "garments", gid)
    subprocess.run([sys.executable, os.path.join(HERE, "garment_texture.py"), "--set", os.path.join(MATS, mset), "--out", tex,
                    "--src-diffuse", os.path.join(MPFB, src, diffuse), "--tiles", str(tiles), "--tint", tint, "--detail", str(detail),
                    "--rough-scale", "1.25" if kind in ("leather", "metal") else "1.0",
                    "--rough-min", {"leather": "0.62", "metal": "0.42", "cloth": "0.8", "cloth_heavy": "0.85"}[kind]],
                   check=True)
    rel = f"_staging/charstd/garments/{gid}"
    desc = {
        "id": gid, "archetype": arch, "doc": "docs/phase_b/remediation/CHARACTER_ASSET_STANDARD.md", "role": role, "layer": layer,
        "hides": hides, "replaces_roles": [],
        "source": {"kind": "mpfb_clothes", "asset": src, "author": author, "license": lic,
                   "attribution": f"{credit}, MakeHuman community assets, {lic}" if credit else None,
                   "url": "http://www.makehumancommunity.org/clothes.html",
                   "provenance": f"F:/Otherreach_External_3D/mpfb/community_clothes/{src}/PROVENANCE.json"},
        "material": {"kind": kind, "base_color": f"{rel}/base_color.png", "normal": f"{rel}/normal.png", "roughness_map": f"{rel}/roughness.png",
                     "texture_origin": f"ambientCG {mset.split('__')[1]} (CC0 1.0) repeated {tiles}x over the garment's UVs, tinted {tint}, "
                                       f"modulated by the source's own diffuse (detail {detail}); garment_texture.py"},
        "offset_m": offset, "authored_edits": [], "note": why,
    }
    if cut:
        desc["cut"] = cut
    json.dump(desc, open(os.path.join(HERE, "garments", gid + ".json"), "w"), indent=1)
    print("DESCRIPTOR", gid)
