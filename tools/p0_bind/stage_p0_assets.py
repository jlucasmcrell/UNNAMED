"""Stage the P0 models as a new asset root: <root>/ready/<id>/<id>.glb (+ _lod1/_lod2), the layout ArtLibrary reads.

python stage_p0_assets.py <staging root>

Sources, in order of preference: Codex's Godot_Ready models (the three LOD files become the game's three levels); DeepSeek's *_game.glb files
(task 19 joined each asset's LODs into extra mesh nodes of ONE file, which the game would draw on top of each other, so they are split here
into <id>.glb / _lod1 / _lod2 by choosing the scene's root nodes - the binary is shared and untouched); the torch converted from the OGA .blend
by blend_to_glb.py. Nothing is edited at its source and nothing here touches another lane's asset root. Re-run to swap in revised models:
every entry names its source, and the game binds by ID only.
"""
import json, os, shutil, struct, sys

CODEX = r"C:\Otherreach_External_Assets\Modelled_20261018\Godot_Ready\Models"
DS = r"C:\Otherreach_External_Assets\Sourced_DS_20261018"

# staged id -> codex key
CODEX_IDS = """barrowback_hide_folded deer_hide_folded fellhorn_hide_folded small_hide_folded wolf_hide_folded brass_key cinder_sac glass_marble
hide_boots_pair iron_billet iron_ingot lantern_ledger larder_snare_ready larder_snare_sprung pelt_snare_ready pelt_snare_sprung quench_trough
smith_bellows smith_tongs spider_silk_hank stopped_watch timber_shield_equipped unreadable_page wolf_fang workbench drying_herb_bundle
""".split()

# staged id -> DeepSeek *_game.glb (relative to DS)
DS_IDS = {
    "flora_ashbloom": "polyhaven/flower_gazania/flower_gazania_game.glb",
    "flora_woundmoss": "polyhaven/moss_01/moss_01_game.glb",
    "flora_woundmoss_bare": "polyhaven/bark_debris_01/bark_debris_01_game.glb",
    "flora_bog_cotton": "polyhaven/grass_medium_02/grass_medium_02_game.glb",
    "flora_bog_cotton_spent": "polyhaven/grass_bermuda_01/grass_bermuda_01_game.glb",
    "flora_young_ash_stand": "polyhaven/tree_small_02/tree_small_02_game.glb",
    "item_water_flask": "polyhaven/metal_jug/metal_jug_game.glb",
    "item_salve_minor": "polyhaven/ceramic_pot/ceramic_pot_game.glb",
    "item_salve_ember": "polyhaven/brass_pot_01/brass_pot_01_game.glb",
    "item_tome_set": "polyhaven/book_encyclopedia_set_01/book_encyclopedia_set_01_game.glb",
}
# staged id -> a GLB made by blend_to_glb.py (path under the work dir)
CONVERTED = {"item_torch": "torch_alt.glb"}


def read_glb(path):
    b = open(path, "rb").read()
    n = struct.unpack("<I", b[12:16])[0]
    doc = json.loads(b[20:20 + n])
    return doc, b[20 + n:]  # the rest: the BIN chunk header and data, kept verbatim


def write_glb(path, doc, rest):
    j = json.dumps(doc, separators=(",", ":")).encode()
    j += b" " * (-len(j) % 4)
    total = 12 + 8 + len(j) + len(rest)
    with open(path, "wb") as f:
        f.write(struct.pack("<III", 0x46546C67, 2, total) + struct.pack("<II", len(j), 0x4E4F534A) + j + rest)


def split_levels(src, dst_dir, sid):
    doc, rest = read_glb(src)
    nodes = doc["nodes"]
    top = doc["scenes"][doc.get("scene", 0)]["nodes"]
    names = {i: nodes[i].get("name", "") for i in top}
    levels = {}
    for i, name in names.items():
        tail = name.rsplit("_LOD", 1)
        levels.setdefault(int(tail[1]) if len(tail) == 2 and tail[1].isdigit() else 0, []).append(i)
    for lod, keep in sorted(levels.items()):
        doc["scenes"][doc.get("scene", 0)]["nodes"] = keep
        write_glb(os.path.join(dst_dir, f"{sid}.glb" if lod == 0 else f"{sid}_lod{lod}.glb"), doc, rest)
    return sorted(levels)


def main(root, work=r"D:\Otherreach_Scratch\bind_p0\work"):
    index = {}
    for key in CODEX_IDS:
        out = os.path.join(root, "ready", key)
        os.makedirs(out, exist_ok=True)
        src = os.path.join(CODEX, key)
        for lod in range(3):
            # the Godot import beside each GLB is an editor cache, not a model; the GLBs carry their textures embedded
            shutil.copyfile(os.path.join(src, f"{key}_LOD{lod}.glb"), os.path.join(out, f"{key}.glb" if lod == 0 else f"{key}_lod{lod}.glb"))
        index[key] = {"source": "codex", "file": os.path.join(src, f"{key}_LOD0.glb")}
    for sid, rel in DS_IDS.items():
        out = os.path.join(root, "ready", sid)
        os.makedirs(out, exist_ok=True)
        levels = split_levels(os.path.join(DS, rel.replace("/", os.sep)), out, sid)
        index[sid] = {"source": "deepseek", "file": os.path.join(DS, rel.replace("/", os.sep)), "levels": levels}
    for sid, name in CONVERTED.items():
        out = os.path.join(root, "ready", sid)
        os.makedirs(out, exist_ok=True)
        shutil.copyfile(os.path.join(work, name), os.path.join(out, sid + ".glb"))
        index[sid] = {"source": "oga-cc0 converted by blend_to_glb.py", "file": os.path.join(work, name)}
    with open(os.path.join(root, "p0_stage_index.json"), "w", encoding="utf-8") as f:
        json.dump(index, f, indent=1)
    print(f"staged {len(index)} models into {root}")


if __name__ == "__main__":
    main(sys.argv[1])
