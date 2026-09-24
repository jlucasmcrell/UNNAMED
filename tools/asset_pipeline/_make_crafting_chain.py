"""Assemble the prototype's gathering-to-crafting chain: which asset stands for which content id.

The sprint status lists this as the last piece of the blacksmithing chain. Every mesh it needs is
already built - ore, pick, ingot, anvil, hammer, tongs, bellows, chest, workbench - but a mesh is
not a gameplay object: `item.material.iron_ingot` is a row in a content file, and nothing joined
that row to `item_grey_iron_ingot`'s GLB, its sockets or its interaction anchor.

So this emits the join, as data, plus the two recipes `PROTOTYPE.md` section 4.1 authorises. It
deliberately contains no geometry and no positions: positions come from the socket manifests, which
already derive them from measured mesh bounds.

Three sources of truth, in order:

  PROTOTYPE.md 4.1/4.2  the recipe costs, node charge models, station gating and the 15 item ids
  assets/catalog.json   which of those ids has geometry, and how big it is
  assets/sockets/       the anchors and grips that make each one interactable

Two rules the file enforces rather than documents:

  1. A design id with no geometry is recorded as `absent`. It is not quietly mapped onto a
     convenient mesh, because that is how a prototype ends up shipping a "salve" that is a potion
     bottle and nobody notices until the icon does not match the model.
  2. A design id mapped onto different geometry is recorded as `substituted` with the reason. The
     substitution is a decision someone can review, not an omission.

Usage:
    python _make_crafting_chain.py            # verify and write the manifest
"""
import io
import json
import os
import sys

ASSETS = r"W:\UNNAMED\assets"
CATALOG = os.path.join(ASSETS, "catalog.json")
SOCKET_DIR = os.path.join(ASSETS, "sockets")
OUT = os.path.join(ASSETS, "manifests", "prototype_crafting_chain.json")

# ---------------------------------------------------------------------------------------------
# Design content id -> built asset. Status is one of:
#   exact        the built asset is the item the design names
#   substituted  usable geometry exists under a different id; the reason is recorded
#   absent       no geometry exists; the chain still works but nothing is drawn
# ---------------------------------------------------------------------------------------------
ITEMS = {
    "item.weapon.rusted_sword": ("weapon_rusted_militia_sword", "exact", ""),
    "item.weapon.hunting_bow": ("weapon_yew_longbow_warbow", "substituted",
                                "weapon_recurve_hunting_bow matches the name but is a modern "
                                "compound bow with a sight and stabiliser bars; the yew warbow is "
                                "the period-correct self bow and is already socketed."),
    "item.ammo.arrow_rough": (None, "absent",
                              "No arrow geometry exists anywhere in the library. The bow has "
                              "SOCK_ammo, so the socket is ready, but the visible projectile "
                              "must be built before a fired shot can be seen."),
    "item.armor.hide_vest": ("armour_chest_underlayer_gambeson_a", "substituted",
                             "The gambeson is a quilted cloth underlayer. It fits the body slot "
                             "and is already bound to the canonical skeleton, but it is cloth, "
                             "not hide."),
    "item.armor.hide_cap": ("item_steel_plate_helm", "substituted",
                            "Geometry exists but in the wrong material: a steel plate helm "
                            "standing in for a hide cap. Flagging rather than hiding it."),
    "item.consumable.salve_minor": ("item_health_potion", "substituted",
                                    "Same gameplay role (a healing consumable) and already "
                                    "engine-ready; the vessel reads as a potion bottle rather "
                                    "than a salve jar. reagent_ceramic_alchemy_jar is the closer "
                                    "silhouette if the bottle is rejected on sight."),
    "item.ammo.water_flask_charge": (None, "absent", "see item.tool.water_flask"),
    "item.tool.water_flask": ("prop_leather_waterskin_flask", "substituted",
                              "A leather waterskin, built as a prop rather than as an item: it "
                              "has no SOCK_interact_take, so it needs an anchor before it can be "
                              "picked up or refilled."),
    "item.material.herb_ashbloom": ("resource_healing_herb_leaf", "substituted",
                                    "The only built harvestable herb. Carries SOCK_harvest_point, "
                                    "which is what the gathering interaction needs."),
    "item.material.iron_ore": ("item_raw_iron_ore", "exact", ""),
    "item.material.iron_ingot": ("item_grey_iron_ingot", "exact", ""),
    "item.material.wolf_hide": ("resource_raw_hide", "exact", ""),
    "item.material.raw_meat": (None, "absent",
                               "Wolf drop. No meat geometry exists; prop_hanging_cured_hams is "
                               "cured meat on a hook, not a droppable item."),
    "item.trinket.wolf_fang": (None, "absent",
                               "Wolf drop and the only non-armour, non-weapon equipment slot. "
                               "No fang geometry exists."),
    "item.quest.halda_token": ("magic_amulet_copper", "substituted",
                               "A unique quest instance needs an unmistakable silhouette; a "
                               "copper amulet is the nearest built wearable and is a placeholder "
                               "rather than an authored quest item."),
    "item.tome.ember_primer": ("magic_grimoire_bound", "substituted",
                               "A bound grimoire stands in for the readable tome."),
}

# The enemy PROTOTYPE.md section 4.1 selects, and the built creature that represents it.
CREATURES = {
    "creature.beast.wolf_grey": ("creature_frost_wolf", "substituted",
                                 "The built wolf is the frost_wolf asset: right body plan, right "
                                 "size at 1.30 m, six clips. A grey-coat variant is a material "
                                 "job, not a mesh job."),
}

# Named NPCs. These are candidates only: section 19 of the brief forbids using an NPC mesh simply
# because it exists, and requires every chosen one to be checked against current race morphology.
# That check has not been done, so this records candidates and says so.
NPCS = {
    "npc.keeper_halda": {
        "role": "Quest giver, longhouse.",
        "candidates": ["npc_veth_magistrate", "npc_mor_witness"],
        "resolved": None,
        "outstanding": "Requires a current-morphology check before selection (brief section 19).",
    },
    "npc.smith_orren": {
        "role": "Forge shed owner, merchant, crafting-station access.",
        "candidates": ["npc_kal_smith", "npc_constructed_engineer"],
        "resolved": None,
        "outstanding": "npc_kal_smith is exactly the right role but section 19 names old Kal as "
                       "one of four morphology families not to reuse unchecked.",
    },
    "npc.warden_kesh": {
        "role": "Companion-to-be. Must share the player's skeleton family to reuse the player "
                "clip set.",
        "candidates": ["npc_veth_magistrate", "npc_orenth_guide"],
        "resolved": None,
        "outstanding": "A companion needs locomotion, so the choice is constrained by which NPC "
                       "mesh is bound to humanoid_standard.",
    },
}

# Gathering nodes. Charge models are PROTOTYPE.md section 4.1; the anchor is a real socket.
NODES = {
    "node.ore.iron_seam": {
        "asset_id": "resource_iron_ore_chunk",
        "socket": "SOCK_harvest_point",
        "resource_ref": "item.material.iron_ore",
        "charges": 3,
        "respawn": None,
        "respawn_note": "Finite: three charges, then depleted for the prototype's duration.",
        "station": False,
    },
    "node.herb.ashbloom": {
        "asset_id": "resource_healing_herb_leaf",
        "socket": "SOCK_harvest_point",
        "resource_ref": "item.material.herb_ashbloom",
        "charges": 3,
        "respawn": {"after_world_days": 1},
        "respawn_note": "Respawns at +1 world_time day. Three charges matches the quest step that "
                        "picks three and empties the node.",
        "station": False,
    },
}

# The one crafting station, and the kit assembly that supplies the building it stands in.
STATION = {
    "station.forge_shed": {
        "station_type": "forge",
        "assembly": "forge_shed",
        "assembly_manifest": "kit_assemblies.json",
        "fixtures": [
            {"asset_id": "prop_blacksmith_anvil_stump", "socket": "SOCK_interact_anvil",
             "role": "anvil"},
            {"asset_id": "prop_forge_double_bellows", "socket": "SOCK_interact_handle",
             "role": "bellows"},
            {"asset_id": "prop_carpenters_workbench", "socket": "SOCK_interact_workbench",
             "role": "bench"},
        ],
        "tools": [
            {"asset_id": "tool_blacksmith_hammer", "socket": "SOCK_grip_primary"},
            {"asset_id": "tool_blacksmith_tongs", "socket": "SOCK_grip_primary"},
        ],
        "note": "Both prototype recipes are station-gated by station_type, per PROTOTYPE.md "
                "section 4.1. The shed also needs a lit forge: prop_iron_brazier or "
                "prop_campfire_tripod are the nearest built fixtures, and neither is a forge.",
    },
}

TOOL = {
    "asset_id": "tool_mining_pick",
    "sockets": ["SOCK_grip_primary", "SOCK_head", "SOCK_pommel", "SOCK_attach"],
    "role": "Gathering tool for node.ore.iron_seam. Also the melee fallback if the sword is lost.",
}

LOOT_SOURCES = {
    "cont.den_cache": {
        "asset_id": "container_chest_iron_banded",
        "socket": "SOCK_interact_chest_lid",
        "contents": ["item.material.iron_ingot x3", "item.ammo.arrow_rough x12",
                     "item.consumable.salve_minor x2"],
        "note": "The only source of iron_ingot, which is what gates recipe.smithing.sword_temper.",
    },
    "wolf_corpse": {
        "asset_id": "creature_frost_wolf",
        "socket": None,
        "contents": ["item.material.raw_meat 70%", "item.material.wolf_hide 45%",
                     "item.trinket.wolf_fang 15%"],
        "note": "Corpse loot is taken from the creature; no container mesh is needed.",
    },
}

# The two recipes PROTOTYPE.md section 4.1 authorises. Costs are quoted, not invented.
RECIPES = {
    "recipe.alchemy.salve_minor": {
        "technique": "alchemy",
        "station_type": "forge",
        "inputs": [
            {"content_id": "item.material.herb_ashbloom", "count": 2},
            {"content_id": "item.tool.water_flask", "count": 1, "consumes": "charge",
             "note": "Consumes a flask charge, not the flask. Refilled at the stream."},
        ],
        "output": {"content_id": "item.consumable.salve_minor", "count": 1},
        "effect": "Heals 18 HP over 6 s.",
        "proves": "Resource consumption with exact counts, one of which is a charge rather than "
                  "an item.",
    },
    "recipe.smithing.sword_temper": {
        "technique": "smithing",
        "station_type": "forge",
        "inputs": [
            {"content_id": "item.material.iron_ingot", "count": 1},
            {"content_id": "item.consumable.salve_minor", "count": 1},
        ],
        "target": {
            "content_id": "item.weapon.rusted_sword",
            "selection": "the equipped instance, not a definition",
            "mutates": "instance",
            "modifier": {"damage": "+2"},
        },
        "output": None,
        "effect": "The named sword *instance* gains +2 damage. A second, untempered sword is "
                  "unaffected, which is what proves a modifier can attach to an instance.",
        "proves": "Crafting can mutate an existing item instance rather than only create new ones.",
    },
}

# What the loop still needs that is neither an item nor a recipe.
WORLD_REQUIREMENTS = [
    {"id": "stream_refill", "state": "absent",
     "note": "item.tool.water_flask refills at the stream (a refill interaction, not a node). "
             "There is no water feature in the library: building_well is a well. The alchemy "
             "recipe is gated behind finding water, so this is a real dependency."},
    {"id": "forge_fire", "state": "absent",
     "note": "The forge shed has a building and bellows but no lit forge fixture."},
]


def load_sockets():
    """Every socket manifest, keyed by asset id. Standalone per-asset files win over the
    aggregated ones, because they are what the authoring tool writes."""
    out = {}
    for name in sorted(os.listdir(SOCKET_DIR)):
        if not name.endswith(".json"):
            continue
        with io.open(os.path.join(SOCKET_DIR, name), encoding="utf-8") as handle:
            data = json.load(handle)
        if "asset_id" in data:
            out[data["asset_id"]] = data
    for aggregated, key in (("weapon_sockets.json", "weapons"),
                            ("interaction_sockets.json", "props")):
        path = os.path.join(SOCKET_DIR, aggregated)
        with io.open(path, encoding="utf-8") as handle:
            data = json.load(handle)
        for asset_id, entry in (data.get(key) or {}).items():
            out.setdefault(asset_id, entry)
    return out


def main():
    with io.open(CATALOG, encoding="utf-8") as handle:
        catalog = json.load(handle)
    by_id = {a["id"]: a for a in catalog["assets"]}
    sockets = load_sockets()

    problems = []

    def require_asset(asset_id, where):
        if asset_id is None:
            return None
        entry = by_id.get(asset_id)
        if entry is None:
            problems.append(f"{where}: asset '{asset_id}' is not in the catalog")
            return None
        return entry

    def require_socket(asset_id, socket, where):
        if asset_id is None:
            return
        entry = sockets.get(asset_id)
        if entry is None:
            problems.append(f"{where}: '{asset_id}' has no socket manifest")
            return
        if socket not in (entry.get("sockets") or {}):
            problems.append(f"{where}: '{asset_id}' has no socket '{socket}' "
                            f"(has {sorted((entry.get('sockets') or {}))})")

    # --- verify every reference before writing anything -------------------------------------
    for content_id, (asset_id, status, _note) in ITEMS.items():
        require_asset(asset_id, f"item {content_id}")
        if status != "absent" and asset_id is None:
            problems.append(f"item {content_id}: status '{status}' but no asset id")
        if status == "absent" and asset_id is not None:
            problems.append(f"item {content_id}: status 'absent' but an asset id is given")

    for content_id, (asset_id, _status, _note) in CREATURES.items():
        require_asset(asset_id, f"creature {content_id}")

    for node_id, node in NODES.items():
        if require_asset(node["asset_id"], f"node {node_id}"):
            require_socket(node["asset_id"], node["socket"], f"node {node_id}")
        if node["resource_ref"] not in ITEMS:
            problems.append(f"node {node_id}: resource '{node['resource_ref']}' has no item entry")

    for station_id, station in STATION.items():
        assemblies = os.path.join(ASSETS, "manifests", station["assembly_manifest"])
        with io.open(assemblies, encoding="utf-8") as handle:
            names = json.load(handle)["assemblies"]
        if station["assembly"] not in names:
            problems.append(f"station {station_id}: assembly '{station['assembly']}' is not in "
                            f"{station['assembly_manifest']}")
        for fixture in station["fixtures"] + station["tools"]:
            if require_asset(fixture["asset_id"], f"station {station_id} fixture"):
                require_socket(fixture["asset_id"], fixture["socket"], f"station {station_id}")

    require_asset(TOOL["asset_id"], "gathering tool")
    for socket in TOOL["sockets"]:
        require_socket(TOOL["asset_id"], socket, "gathering tool")

    for source_id, source in LOOT_SOURCES.items():
        require_asset(source["asset_id"], f"loot source {source_id}")
        if source["socket"]:
            require_socket(source["asset_id"], source["socket"], f"loot source {source_id}")

    for recipe_id, recipe in RECIPES.items():
        for side in ("inputs",):
            for entry in recipe[side]:
                if entry["content_id"] not in ITEMS:
                    problems.append(f"{recipe_id}: {side} '{entry['content_id']}' has no item entry")
        output = recipe.get("output")
        if output and output["content_id"] not in ITEMS:
            problems.append(f"{recipe_id}: output '{output['content_id']}' has no item entry")
        target = recipe.get("target")
        if target and target["content_id"] not in ITEMS:
            problems.append(f"{recipe_id}: target '{target['content_id']}' has no item entry")

    # --- the manifest ------------------------------------------------------------------------
    def item_entries():
        out = {}
        for content_id, (asset_id, status, note) in ITEMS.items():
            entry = by_id.get(asset_id) if asset_id else None
            out[content_id] = {
                "asset_id": asset_id,
                "geometry_status": status,
                "ready_glb": entry["base_glb"] if entry else None,
                "dimensions_m": entry["dimensions_m"] if entry else None,
                "sockets": sorted((sockets.get(asset_id) or {}).get("sockets") or {}) if asset_id
                           else [],
                "note": note,
            }
        return out

    counts = {"exact": 0, "substituted": 0, "absent": 0}
    for _content_id, (_asset_id, status, _note) in ITEMS.items():
        counts[status] += 1

    doc = {
        "version": 1,
        "comment": [
            "The prototype's gather-to-craft chain, assembled. Geometry already existed for every",
            "step; what did not exist was the join between a content id in PROTOTYPE.md and the",
            "GLB, sockets and interaction anchor that make it real.",
            "",
            "geometry_status is the honest field:",
            "  exact        the built asset is the item the design names",
            "  substituted  usable geometry exists under a different id, reason recorded",
            "  absent       no geometry exists - the chain still runs, but nothing is drawn",
            "",
            "Nothing here is a position. Anchors come from assets/sockets/, which derives them from",
            "measured mesh bounds so they follow a rescale instead of drifting from it.",
            "",
            "Regenerate with _make_crafting_chain.py, which fails rather than writing a manifest",
            "that references an asset or a socket that does not exist.",
        ],
        "authority": "PROTOTYPE.md sections 4.1 and 4.2",
        "generated_from": {
            "catalog": "assets/catalog.json",
            "sockets": "assets/sockets/",
            "assemblies": "assets/manifests/kit_assemblies.json",
        },
        "recipes": RECIPES,
        "station": STATION,
        "nodes": NODES,
        "gathering_tool": TOOL,
        "loot_sources": LOOT_SOURCES,
        "items": item_entries(),
        "creatures": {cid: {"asset_id": a, "geometry_status": s, "note": n}
                      for cid, (a, s, n) in CREATURES.items()},
        "npcs": NPCS,
        "world_requirements": WORLD_REQUIREMENTS,
        "coverage": {
            "content_items": len(ITEMS),
            "exact": counts["exact"],
            "substituted": counts["substituted"],
            "absent": counts["absent"],
            "recipes": len(RECIPES),
            "nodes": len(NODES),
            "stations": len(STATION),
        },
    }

    print(f"  {'content id':<34} {'status':<12} asset")
    print("  " + "-" * 78)
    for content_id in sorted(ITEMS):
        asset_id, status, _note = ITEMS[content_id]
        print(f"  {content_id:<34} {status:<12} {asset_id or '(none)'}")

    print()
    coverage = doc["coverage"]
    print(f"  {coverage['content_items']} content items: {coverage['exact']} exact, "
          f"{coverage['substituted']} substituted, {coverage['absent']} absent")
    print(f"  {coverage['recipes']} recipes, {coverage['nodes']} gathering nodes, "
          f"{coverage['stations']} station")

    if problems:
        print()
        for problem in problems:
            print(f"  FAIL  {problem}")
        print(f"\n  {len(problems)} reference problem(s); manifest not written")
        return 1

    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    with io.open(OUT, "w", encoding="utf-8") as handle:
        json.dump(doc, handle, indent=2)
        handle.write("\n")
    print(f"  wrote {OUT}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
