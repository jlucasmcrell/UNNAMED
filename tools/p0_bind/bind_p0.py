"""Bind the staged P0 models (stage_p0_assets.py) into content/_art_bindings.json, the overlay the playtest runs with. Idempotent.

python bind_p0.py <content/_art_bindings.json>

Every binding is by staged model ID only, so a revised model swaps by re-running stage_p0_assets.py. `scale` enlarges a thing too small to be
seen at its authored size (stated in the entry's note); `tint` / `spent_tint` multiply the model's albedo through a material override.
"""
import json, sys

HIDE = lambda m, s, n: {"model": m, "scale": s, "note": n}
GROUND = {
    "item.material.wolf_hide": HIDE("wolf_hide_folded", 0.45, "Codex's folded wolf hide, 1.16 m authored, drawn at 0.45 for a dropped item."),
    "item.material.deer_hide": HIDE("deer_hide_folded", 0.45, "Codex's folded deer hide, 1.22 m authored, drawn at 0.45."),
    "item.material.small_hide": {"model": "small_hide_folded", "note": "Codex's folded small hide, authored size."},
    "item.material.fellhorn_hide": HIDE("fellhorn_hide_folded", 0.4, "Codex's folded fellhorn hide, 1.4 m authored, drawn at 0.4."),
    "item.material.barrowback_hide": HIDE("barrowback_hide_folded", 0.45, "Codex's folded barrowback hide, 1.03 m authored, drawn at 0.45."),
    "item.armor.hide_boots": {"model": "hide_boots_pair", "note": "Codex's unrigged boots, off-body item geometry."},
    "item.armor.timber_shield": {"model": "timber_shield_equipped", "note": "Codex's timber shield, ground drop (no shield slot is drawn on the body yet)."},
    "item.material.cinder_sac": {"model": "cinder_sac", "scale": 1.5, "note": "Codex's cinder sac, drawn at 1.5."},
    "item.material.iron_ingot": {"model": "iron_ingot", "scale": 1.3, "note": "Codex's iron ingot (being revised); drawn at 1.3."},
    "item.material.spider_silk": {"model": "spider_silk_hank", "scale": 3.0, "note": "Codex's silk hank, 17 cm authored, drawn at 3."},
    "item.trinket.wolf_fang": {"model": "wolf_fang", "scale": 3.5, "note": "Codex's wolf fang, 7 cm authored, drawn at 3.5."},
    "item.material.herb_ashbloom": {"model": "drying_herb_bundle", "tint": [0.6, 0.6, 0.58], "note": "Codex's herb bundle (being revised), ash-grey tint."},
    "item.material.herb_woundmoss": {"model": "drying_herb_bundle", "tint": [0.82, 0.98, 0.84], "note": "Codex's herb bundle, pale green tint."},
    "item.material.boneleaf": {"model": "drying_herb_bundle", "tint": [1.0, 0.98, 0.88], "note": "Codex's herb bundle, bone-pale tint."},
    "item.material.bog_cotton": {"model": "drying_herb_bundle", "tint": [1.2, 1.2, 1.15], "note": "Codex's herb bundle, white tint."},
    "item.tool.torch": {"model": "item_torch", "note": "OGA CC0 torch (torch_alt), converted by tools/p0_bind/blend_to_glb.py."},
    "item.tool.water_flask": {"model": "item_water_flask", "note": "Poly Haven CC0 metal jug (DeepSeek's _game.glb)."},
    "item.consumable.salve_minor": {"model": "item_salve_minor", "note": "Poly Haven CC0 ceramic pot."},
    "item.consumable.salve_ember": {"model": "item_salve_ember", "tint": [1.0, 0.8, 0.65], "note": "Poly Haven CC0 brass pot, warm tint."},
}
for tome in ("bench_patterns", "field_notes", "herbal_remedies", "resonance_primer"):
    GROUND[f"item.tome.{tome}"] = {"model": "item_tome_set", "note": "Poly Haven CC0 book set stands in for a single tome."}

NODES = {
    "node.firstplay.ashbloom": {"model": "flora_ashbloom", "tint": [0.62, 0.62, 0.6], "spent": "flora_ashbloom", "spent_tint": [0.2, 0.16, 0.13], "sound": "herb",
                                "note": "DeepSeek's Poly Haven gazania, ash-grey by material override; spent is the same mesh scorched."},
    "node.herb.woundmoss": {"model": "flora_woundmoss", "tint": [0.85, 1.0, 0.85], "spent": "flora_woundmoss_bare", "sound": "herb",
                            "note": "Poly Haven moss_01 cushion; spent, the bare bark debris it grew on."},
    "node.fibre.bog_cotton": {"model": "flora_bog_cotton", "tint": [1.2, 1.2, 1.15], "spent": "flora_bog_cotton_spent", "spent_tint": [0.9, 0.78, 0.5], "sound": "fibre",
                              "note": "Poly Haven grass_medium_02 with a white seed-head tint; spent, straw-coloured bermuda grass."},
    "node.trap.larder_snare": {"model": "larder_snare_ready", "spent": "larder_snare_sprung", "sound": "wood", "note": "Codex's snare, ready and sprung (being revised)."},
    "node.trap.pelt_snare": {"model": "pelt_snare_ready", "spent": "pelt_snare_sprung", "sound": "wood", "note": "Codex's snare, ready and sprung (being revised)."},
}
STATIONS = {
    "workbench": {"model": "workbench", "note": "Codex's workbench with vice and racks (replaces the tavern table)."},
}
STATION_EXTRA = {"forge": {"beside": "smith_bellows", "beside_offset": [1.1, 0, 0.4]}}
STRUCTURES = {"ah_furn_forge_trough": {"model": "quench_trough", "note": "Codex's quench trough (was a wooden bucket)."}}
CONTAINERS = {
    "container.story.brass_key": {"model": "brass_key", "scale": 2.5, "sound": "chest", "note": "M1 relic: Codex's brass key, 13 cm authored, drawn at 2.5 to be seen."},
    "container.story.glass_marble": {"model": "glass_marble", "scale": 8.0, "sound": "chest", "note": "M1 relic: Codex's marble, 18 mm authored, drawn at 8."},
    "container.story.lantern_ledger": {"model": "lantern_ledger", "sound": "chest", "note": "M1 relic: Codex's ledger, authored size."},
    "container.story.stopped_watch": {"model": "stopped_watch", "scale": 4.0, "sound": "chest", "note": "M1 relic: Codex's watch, 5 cm authored, drawn at 4."},
    "container.story.unreadable_page": {"model": "unreadable_page", "scale": 1.6, "sound": "chest", "note": "M1 relic: Codex's page, authored size x1.6."},
}


def main(path):
    d = json.loads(open(path, "rb").read().decode("utf-8-sig"))
    d.setdefault("ground_items", {}).update(GROUND)
    d["nodes"].update(NODES)
    for k, v in STATIONS.items():
        d["stations"][k] = {**d["stations"].get(k, {}), **v}
    for k, v in STATION_EXTRA.items():
        d["stations"][k] = {**d["stations"][k], **v}
    for k, v in STRUCTURES.items():
        d["structures"][k] = {**d["structures"][k], **v}
    d["containers"].update(CONTAINERS)
    with open(path, "wb") as f:
        f.write((json.dumps(d, indent=2, ensure_ascii=False) + "\n").replace("\n", "\r\n").encode())


if __name__ == "__main__":
    main(sys.argv[1])
