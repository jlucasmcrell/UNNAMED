"""Freeze the playable-prototype asset manifest from a curated selection.

The selection is deliberate, not derived: a game is made playable by a small set of COMPLETE
assets, so this lists only what the first playable prototype needs and records the real build
state of each. Every status field is read from disk rather than asserted, so the manifest cannot
claim something is rigged, socketed or validated when it is not.

Two authorities bound the content, and they disagree. `PROTOTYPE.md` §4 is the binding Phase-1
document (IMPLEMENTATION_PRECEDENCE §1 gives it authority over ROADMAP.md for Phase-1 content)
and asks for 4 cells of 200 m at Ashen Hollow, one outpost with two interiors, 3 NPCs, ONE wolf
archetype and TWO armour pieces. This sprint's brief asks for a full outfit, five enemies and an
environment kit. The brief is treated as the ceiling and PROTOTYPE as the floor: anything
PROTOTYPE requires is P0, anything only the brief requires is P1, and the tier is recorded so the
difference is visible rather than blurred.

Usage:
    python _freeze_playable_manifest.py
"""
import datetime
import io
import json
import os

ASSETS = r"W:\UNNAMED\assets"
CATALOG = os.path.join(ASSETS, "catalog.json")
AUDIT = os.path.join(ASSETS, "manifests", "scale_audit.json")
RIGGED = os.path.join(ASSETS, "rigged")
READY = os.path.join(ASSETS, "ready")
PROOFSET = os.path.join(ASSETS, "PROOFSET")
ANIM_READY = os.path.join(ASSETS, "animation", "ready", "humanoid")
OUT = os.path.join(ASSETS, "manifests", "playable_prototype_assets.json")

# Fit families that actually exist on disk. The animation doc names five more that do not.
FIT_BY_PREFIX = {
    "race_veth": "humanoid_standard", "race2_veth": "humanoid_standard",
    "racebody_veth": "humanoid_standard",
}

# (asset_id, tier, group, gameplay_role, limitations)
# tier: P0 = required by PROTOTYPE.md, P1 = required by this sprint's brief, P2 = desirable.
SELECTION = [
    # ---- player ----
    ("race2_veth_bindpose", "P0", "player",
     "The player character. Full-body authority: authoritative in both third person and first person.",
     "Bound to the canonical 52-bone skeleton and Godot-validated at 1.80 m with its base on the "
     "ground and 5 equipment sockets. KNOWN LIMITATION: only 3 of 20 finger bones carry skin "
     "weight, because the generated mesh stands with its arms further down (0.51 width ratio) "
     "than the contracted A-pose (0.65), so the outer digits miss the weight transfer. The hand "
     "itself is weighted and grip sockets are present, so a weapon can be held; finger "
     "articulation is not yet believable. A fuller A-pose mesh is a P1 follow-up."),

    # ---- the five enemies (all existing meshes, three rig plans already built) ----
    ("creature_frost_wolf", "P0", "enemy",
     "Fast pack predator. The PROTOTYPE.md wolf_grey archetype and the combat-tuning baseline.",
     "Rescaled to 1.30 m and re-rigged (quadruped, 18 bones, 0% unweighted). Six clips: idle, "
     "walk, run, attack, hit, death."),
    ("creature_bone_walker_husk", "P1", "enemy",
     "Light humanoid enemy. Cheap to animate; contrasts with the wolf in silhouette and threat.",
     "1.80 m, humanoid creature rig (20 bones, 0% unweighted). Six clips."),
    ("creature_animated_armour", "P1", "enemy",
     "Heavy humanoid enemy. Armoured, slow, high stagger resistance.",
     "Rescaled to 2.00 m and re-rigged. Six clips. Empty armour: needs a plausible interior read "
     "when damaged."),
    ("creature_highland_brown_bear", "P1", "enemy",
     "Heavy quadruped brute. Damage-sponge melee check.",
     "Rescaled to 2.00 m and re-rigged (quadruped). Six clips."),
    ("creature_great_river_serpent", "P1", "enemy",
     "Distinctly nonhuman enemy. Uses the worm plan, so it is the one enemy whose motion is not a "
     "biped or quadruped derivative.",
     "Rescaled to 3.00 m and re-rigged (worm, 5 bones). Six clips. Five bones is adequate for a "
     "serpent and not for fine articulation."),

    # ---- three prototype weapon families ----
    ("weapon_arming_sword", "P0", "weapon",
     "One-handed sword. The PROTOTYPE.md item.weapon.rusted_sword slot and the one_hand_blade animation family.",
     "1.00 m, correct for a one-handed sword. Sockets authored and Godot-validated: "
     "SOCK_grip_primary (0.80 m along the hilt, primary faces the blade), SOCK_pommel, "
     "SOCK_attach at the balance point. The generator produced it point-down, which the socket "
     "facing records."),
    ("weapon_yew_longbow_warbow", "P0", "weapon",
     "Bow. The PROTOTYPE.md item.weapon.hunting_bow slot and the bow animation family.",
     "1.70 m. Chosen over weapon_recurve_hunting_bow after inspecting both: that one is a modern "
     "compound target bow with stabiliser bars and a sight, which is anachronistic for the setting "
     "and 0.97 m wide. Sockets: SOCK_grip_primary at the measured riser, SOCK_ammo at the nocking "
     "point, SOCK_attach."),
    ("weapon_boar_spear_hunting", "P1", "weapon",
     "Polearm / spear. The third family, exercising two-handed grip and secondary-hand IK.",
     "2.20 m. Five sockets: SOCK_grip_primary at 0.70 m and SOCK_grip_secondary at 1.10 m, which "
     "is the 0.40 m spacing runtime IK needs for a two-handed weapon, plus SOCK_head, SOCK_pommel "
     "and SOCK_attach. Collision proxies are retained as world-object reference geometry; the held "
     "hitbox is authored in the combat system per the modular standard."),

    # ---- armour: the two PROTOTYPE pieces exist as proof assets; the rest is the brief's outfit ----
    ("armour_chest_underlayer_gambeson_a", "P0", "armour",
     "Torso underlayer for the hide_vest slot. Layer offset 0.008 m.",
     "Built against the canonical fit boundary but still uses generated fit, not the Method C transfer."),
    ("armour_chest_plate_base_a", "P0", "armour",
     "Chest protection. Layer offset 0.024 m; covers chest, back and abdomen as metadata, not as mesh slots.",
     "Generated fit rather than canonical; the Method A to Method C transfer is outstanding. Emits no coverage metadata yet."),
    ("armour_gorget_plate_a", "P1", "armour",
     "Neck and throat protection. A midline gap piece; needs no LOD chain.",
     "Generated fit. Coverage metadata not yet emitted by any tool."),
    ("armour_leg_garment_a", "P1", "armour",
     "Leg garment, waist to ankle. Completes the outfit's lower half.",
     "1.05 m, rescaled from the generator's 0.40 m armour default. Generated fit, not canonical."),
    ("item_leather_boots_pair", "P1", "armour",
     "Footwear. Required for believable locomotion contact and foot IK.",
     "0.32 m. Generated fit."),
    ("armour_glove_pair_a", "P1", "armour",
     "Gloves. Required for a believable weapon grip at close camera.",
     "0.26 m. Generated fit. Body finger weighting is a separate known limitation."),
    ("item_steel_plate_helm", "P2", "armour",
     "Optional helmet for the hide_cap slot.",
     "0.30 m. Generated fit; needs coverage metadata before it can reduce head damage."),

    # ---- blacksmithing chain (the library already supports this unusually well) ----
    ("prop_blacksmith_anvil_stump", "P0", "station",
     "The anvil. NOTE: no anvil exists in the design docs at all, so this asset is the only source of truth for one.",
     "Scale normalised to 0.5 m; an anvil is ~0.7 m."),
    ("tool_blacksmith_hammer", "P0", "tool",
     "The smiths_hammer recipe tool. Required, not consumed.", "Scale normalised to 0.6 m."),
    ("tool_blacksmith_tongs", "P1", "tool", "Forge handling tool.", "Scale normalised to 0.6 m."),
    ("prop_forge_double_bellows", "P1", "station", "Forge air supply; sells the forge interior.",
     "Scale normalised to 0.5 m."),
    ("item_raw_iron_ore", "P0", "resource",
     "Raw ore, the gathering output that feeds the smithing chain.", "Scale normalised to 0.5 m; a hand-held ore chunk is ~0.2 m."),
    ("herb_bitterroot", "P0", "resource",
     "Herb node output for recipe.alchemy.salve_minor.", "Scale normalised to 0.5 m."),
    ("container_chest_iron_banded", "P0", "container",
     "The den cache chest (cont.den_cache). Lootable world container.", "Scale normalised to 0.5 m; a chest is ~0.8 m."),

    # ---- NPCs (all eight exist, rigged, at correct humanoid scale) ----
    ("npc_kal_smith", "P0", "npc", "smith_orren, the forge NPC. Also the PC-1 blacksmith.",
     "Kal biology requires dorsal wings, which neither this mesh nor the compact_broad rig has."),
    ("npc_veth_magistrate", "P0", "npc", "keeper_halda, the outpost NPC.", ""),
    ("npc_orenth_guide", "P1", "npc", "warden_kesh, the companion.", ""),

    # ---- environment: PROTOTYPE needs an outpost with two interiors; the kit is the brief's ----
    ("flora_oak_tree", "P0", "vegetation",
     "Treeline and the hollow's canopy. Oak.", "FAIL_SCALE: built at 0.5 m. A mature oak is ~14 m."),
    ("flora_pine_tree", "P0", "vegetation", "Conifer for the treeline and upland.", "FAIL_SCALE: built at 0.5 m."),
    ("flora_dead_tree", "P0", "vegetation", "Silhouette interest and the melancholic read the tone calls for.",
     "FAIL_SCALE: built at 0.5 m."),
]


def main():
    catalog = {e["id"]: e for e in json.load(io.open(CATALOG, encoding="utf-8"))["assets"]}
    audit = {r["asset_id"]: r for r in
             json.load(io.open(AUDIT, encoding="utf-8"))["results"]}
    rigged = set(os.listdir(RIGGED)) if os.path.isdir(RIGGED) else set()
    proofset = set(os.listdir(PROOFSET)) if os.path.isdir(PROOFSET) else set()
    anim_clips = sorted(os.listdir(ANIM_READY)) if os.path.isdir(ANIM_READY) else []

    entries = []
    for asset_id, tier, group, role, limitation in SELECTION:
        entry = catalog.get(asset_id)
        rig_dir = os.path.join(RIGGED, asset_id)
        ready_dir = os.path.join(READY, asset_id)
        a = audit.get(asset_id, {})

        built = entry is not None and os.path.isdir(ready_dir)
        files = sorted(os.listdir(ready_dir)) if os.path.isdir(ready_dir) else []

        entry_out = {
            "asset_id": asset_id,
            "tier": tier,
            "group": group,
            "gameplay_role": role,
            "category": entry.get("category") if entry else None,
            "source_concept": (entry or {}).get("source", {}).get("concept"),
            "scale": {
                "audit_verdict": a.get("verdict", "NOT_BUILT"),
                "measured_longest_m": a.get("measured_longest_m"),
                "expected_longest_m": a.get("expected_longest_m"),
                "category_normalised_to": a.get("category_normalised_to"),
                "note": a.get("note", ""),
            },
            "fit_family": FIT_BY_PREFIX.get(asset_id, "n/a"),
            "ready_glb": (os.path.join(asset_id, f"{asset_id}.glb") if built else None),
            "rigged_glb": (os.path.join(asset_id, f"{asset_id}_rigged.glb") if asset_id in rigged else None),
            "animation_state": ("clips_available" if anim_clips else "none"),
            "lod_state": (f"{len([f for f in files if '_lod' in f])} lods" if built else "none"),
            "collision_state": (
                "hull+box" if built and any("collision_hull" in f for f in files)
                else "box" if built and any("collision_box" in f for f in files)
                else "none" if built else "none"),
            "socket_state": ("sockets present" if built and any("sockets" in f for f in files)
                             else "none" if built else "none"),
            # No `godot_validated` here. It used to be written as `asset_id in proofset`, which is a
            # different fact under a misleading name - and `in_proofset` already records it one line
            # down. Validation truth belongs to the asset's own metadata, written only by
            # _godot_validate_assets.py; this manifest reads it into the summary below rather than
            # storing a second copy that can drift.
            "in_proofset": asset_id in proofset,
            "known_limitations": limitation,
        }
        entries.append(entry_out)

    # Validation is read from the authoritative per-asset metadata, not stored per entry. A summary
    # count is a consumption of that truth and is recomputed on every freeze, so it cannot go stale
    # the way a stored boolean does.
    validated = 0
    for entry_out in entries:
        meta_path = os.path.join(READY, entry_out["asset_id"], f"{entry_out['asset_id']}_meta.json")
        if not os.path.exists(meta_path):
            continue
        with io.open(meta_path, encoding="utf-8") as handle:
            if json.load(handle).get("godot_validated") is True:
                validated += 1

    summary = {
        "total": len(entries),
        "built": sum(1 for e in entries if e["ready_glb"]),
        "not_built": sum(1 for e in entries if not e["ready_glb"]),
        "rigged": sum(1 for e in entries if e["rigged_glb"]),
        "godot_validated": validated,
        "godot_validated_source": ("ready/<id>/<id>_meta.json, written only by "
                                   "_godot_validate_assets.py"),
        "by_tier": {t: sum(1 for e in entries if e["tier"] == t) for t in ("P0", "P1", "P2")},
        "scale_failures": sum(1 for e in entries
                              if e["scale"]["audit_verdict"] in ("FAIL_SCALE", "SUSPECT_SCALE")),
    }

    doc = {
        "version": 1,
        "frozen": datetime.datetime.now().isoformat(timespec="seconds"),
        "comment": [
            "The small set of assets gameplay work should rely on through M6.",
            "",
            "Tiers: P0 = required by PROTOTYPE.md, the binding Phase-1 document. P1 = required by",
            "the asset sprint brief, which exceeds PROTOTYPE.md's scope. P2 = desirable.",
            "PROTOTYPE.md §1 gives itself authority over ROADMAP.md for Phase-1 content, so where",
            "the brief and PROTOTYPE.md disagree the tier records which is which rather than",
            "silently picking one.",
            "",
            "Every status field is read from disk at freeze time, not asserted. 'NOT YET BUILT'",
            "means exactly that. Regenerate with _freeze_playable_manifest.py.",
            "",
            "Scale context: 449 of 463 built assets carry the category-normalisation signature,",
            "so measured dimensions are categorically wrong for most of this list. See",
            "docs/SCALE_AUDIT_REPORT.md.",
        ],
        "summary": summary,
        "entries": entries,
    }

    with io.open(OUT, "w", encoding="utf-8") as handle:
        json.dump(doc, handle, indent=2)

    print(f"  wrote {OUT}")
    print(f"  {summary['total']} entries: {summary['built']} built, {summary['not_built']} not built")
    print(f"    rigged {summary['rigged']}, godot-validated {summary['godot_validated']}")
    print(f"    by tier {summary['by_tier']}")
    print(f"    carrying a scale problem: {summary['scale_failures']}")
    for e in entries:
        if not e["ready_glb"]:
            print(f"    NOT BUILT: {e['asset_id']} ({e['tier']})")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
