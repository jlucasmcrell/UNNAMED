"""Promote the minimal UI icon subset from concepts into HUD-ready textures.

Section 24 asks for only the icon subset a playable build needs - the four resources, the
interaction prompt, inventory and equipment, the three weapon families, the three proof spells and
the essential statuses - and explicitly not for a finished UI. Section 29 lists "minimal icon
subset" in the acceptance bundle, and until now zero of the 64 rendered icon concepts had been
promoted, so the count was 0 of everything.

Two of the required entries already existed as concepts and are reused rather than re-rendered,
which is the section 33 preference for making an existing asset usable. The rest were rendered and
are named here.

Sizes: icons are shipped at 128 px, which is a HUD-sized sprite, with the 256 px master beside it so
a larger slot does not have to upscale. An atlas is emitted as well because a HUD that draws twelve
separate textures per frame is twelve texture binds for twelve 128 px quads.

Usage:
    python _promote_ui_icons.py
"""
import io
import json
import os

from PIL import Image

ASSETS = os.environ.get("UNNAMED_ASSETS", r"W:\UNNAMED\assets")
CONCEPTS = os.path.join(ASSETS, "concepts")
OUT_DIR = os.path.join(ASSETS, "ui", "icons")
OUT_MANIFEST = os.path.join(ASSETS, "manifests", "ui_icons.json")

MASTER = 256
SHIPPED = 128
ATLAS_COLUMNS = 6

# slot id -> (concept id, source, what it is for)
#
# `source` is 'reused' when the concept was already in the library and 'rendered' when it was made
# for this set. Recording it means the two are never confused later.
SLOTS = {
    "ui.resource.health": ("icon_hud_health", "rendered",
                           "Health meter. PROTOTYPE.md section 4.1 lists a health/stamina/spell "
                           "resource bar."),
    "ui.resource.stamina": ("icon_hud_stamina", "rendered",
                            "Stamina meter."),
    "ui.resource.focus": ("icon_hud_focus", "rendered",
                          "Focus meter."),
    "ui.resource.resonance": ("icon_hud_resonance_strain", "rendered",
                              "Resonance/Strain. Otherreach has no mana pool, so this replaces the "
                              "third bar a fantasy HUD would have."),
    "ui.prompt.interact": ("icon_ui_interaction_prompt", "rendered",
                           "Interaction prompt."),
    "ui.panel.inventory": ("icon_ui_inventory", "rendered", "Inventory panel."),
    "ui.panel.equipment": ("icon_ui_equipment", "rendered", "Equipment panel."),
    "ui.weapon.one_handed": ("icon_weapon_sword_one_hand", "rendered",
                             "One-handed sword, the arming sword the player starts with."),
    "ui.weapon.bow": ("icon_weapon_bow", "rendered",
                      "Bow, matching weapon_hunting_bow."),
    "ui.weapon.polearm": ("icon_weapon_polearm_spear", "rendered",
                          "Polearm family."),
    "ui.weapon.march_spear": ("icon_weapon_march_spear", "rendered",
                              "The March Spear the player crafts in Quest 1. Its own slot because"
                              " the hotbar shows the crafted weapon, not the family."),
    # The three Phase-1 formulas from the bible's section 13. These were previously reused stock
    # concepts - a flame burst, a heal and a stone-skin ward - which were the right stand-ins for
    # PROTOTYPE.md's spells and the wrong ones for Force, Warding and Vital. A kinetic bolt is not
    # fire, a ward is not bark, and a thread is not a potion, so all three are rendered to match.
    "ui.formula.impulse_bolt": ("icon_formula_impulse_bolt", "rendered",
                                "formula.force.impulse_bolt - Force proof."),
    "ui.formula.brace_ward": ("icon_formula_brace_ward", "rendered",
                              "formula.warding.brace_ward - Warding proof."),
    "ui.formula.mending_thread": ("icon_formula_mending_thread", "rendered",
                                  "formula.vital.mending_thread - Vital proof."),
    # Statuses. The bible's section 19 names Bleeding, Wounded, Strained, Burning and Weakened;
    # the previous set had an oakskin buff instead, which belonged to a spell that no longer exists.
    "ui.status.burning": ("icon_burn_status_flame", "reused", "effect.burning."),
    "ui.status.bleeding": ("icon_bleed_status_wound", "reused", "effect.bleeding."),
    "ui.status.weakened": ("icon_weakened_status_arm", "reused", "effect.weakened."),
    "ui.status.wounded": ("icon_status_wounded", "rendered", "effect.wounded."),
    "ui.status.strained": ("icon_status_strained", "rendered", "effect.strained."),
    "ui.status.downed": ("icon_status_downed", "rendered",
                         "Downed. Also the enemy condition read-out the HUD shows instead of exact"
                         " health numbers."),
    # Hotbar slots 7 and 8 from the bible's section 19 example bar, which the previous set had no
    # icons for at all.
    "ui.item.restorative": ("icon_item_restorative", "rendered", "Hotbar 7, a field restorative."),
    "ui.item.torch": ("icon_item_torch", "rendered", "Hotbar 8, the carried torch."),
    # Companion state and navigation, section 19 and section 22.
    "ui.companion.follow": ("icon_companion_follow", "rendered", "Tavar: Follow."),
    "ui.companion.wait": ("icon_companion_wait", "rendered", "Tavar: Wait."),
    "ui.objective.tracked": ("icon_hud_objective", "rendered", "The tracked quest objective."),
    "ui.hud.compass": ("icon_hud_compass", "rendered",
                       "Heading cue. Section 22 prefers a compass over an omniscient minimap."),
    # The six slots the HUD/UI spec of 2026-09-23 requires and the set had no icon for. Read against
    # that spec, the set covered the eight hotbar slots, the four resources, the five statuses and the
    # companion orders, but not the remaining Phase-1 screens, the two death states past DOWNED, or
    # the third qualitative enemy condition.
    #
    # Spec section 10 requires five Phase-1 screens before M6 acceptance. Inventory and equipment
    # existed; character, journal and known-techniques did not.
    "ui.panel.character": ("icon_panel_character", "rendered",
                           "Character screen. Spec section 10.2, required before M6."),
    "ui.panel.journal": ("icon_panel_journal", "rendered",
                         "Journal. Spec sections 10.3 and 13 - the authoritative player-facing quest"
                         " record. Distinct from ui.objective.tracked, which is the single objective"
                         " shown on the HUD."),
    "ui.panel.techniques": ("icon_panel_techniques", "rendered",
                            "Known Magic / Techniques list. Spec sections 10.4 and 14. The panel, not"
                            " the individual formulas, which have their own three slots."),
    # Spec section 7 names three distinct terminal states: DOWNED, DYING, DEAD. The set had downed.
    "ui.status.dying": ("icon_status_dying", "rendered",
                        "Dying. Deliberately warm and slumped so it cannot be confused with dead at"
                        " a glance."),
    "ui.status.dead": ("icon_status_dead", "rendered",
                       "Dead. Horizontal, colourless and skull-marked, against dying's slumped and"
                       " warm silhouette."),
    # Spec section 5 lists four qualitative enemy conditions - Healthy, Wounded, Critical, Downed -
    # and the set had two. `critical` is the enemy's own condition and is not ui.status.wounded,
    # which is the player's bleeding gash.
    "ui.condition.critical": ("icon_condition_critical", "rendered",
                              "Critically wounded enemy. Spec section 5. Distinct from"
                              " ui.status.wounded."),
    # The Phase-1 items with no inventory icon (2026-09-25 polish pass): the 13 items content/ names with no icon
    # binding. Read against each item's own yaml (name, notes) so the icon draws what the item actually is.
    "ui.item.arrow_rough": ("icon_item_arrow_rough", "rendered", "item.ammo.arrow_rough: a rough hunting arrow."),
    "ui.item.hide_cap": ("icon_item_hide_cap", "rendered", "item.armor.hide_cap: tanned leather head armor."),
    "ui.item.hide_vest": ("icon_item_hide_vest", "rendered", "item.armor.hide_vest: tanned leather body armor."),
    "ui.item.ash_haft": ("icon_item_ash_haft", "rendered",
                        "item.material.ash_haft: a cut ash pole, feeds the March Spear."),
    "ui.item.ashbloom": ("icon_item_ashbloom", "rendered", "item.material.herb_ashbloom: a herb gathered by the stream."),
    "ui.item.iron_ingot": ("icon_item_iron_billet", "rendered",
                          "item.material.iron_ingot: the content bible's Iron Billet, smelted from ore."),
    "ui.item.iron_ore": ("icon_item_iron_ore", "rendered", "item.material.iron_ore: raw ore struck from the seam."),
    "ui.item.raw_meat": ("icon_item_raw_meat", "rendered", "item.material.raw_meat: a wolf drop, feeds companion trust."),
    "ui.item.wolf_hide": ("icon_item_wolf_hide", "rendered", "item.material.wolf_hide: a wolf drop."),
    "ui.item.halda_token": ("icon_item_halda_token", "rendered",
                           "item.quest.halda_token: the quest item, never dropped or sold."),
    "ui.item.resonance_primer": ("icon_item_resonance_primer", "rendered",
                                "item.tome.resonance_primer: the survey scholar's primer, teaches the three formulas."),
    "ui.item.water_flask": ("icon_item_water_flask", "rendered",
                           "item.tool.water_flask: one charge, refilled at the stream."),
    "ui.item.wolf_fang": ("icon_item_wolf_fang", "rendered", "item.trinket.wolf_fang: +2% crit, an amulet slot."),
}


def main():
    os.makedirs(OUT_DIR, exist_ok=True)
    icons = {}
    problems = []
    tiles = []

    for slot, (concept, source, note) in sorted(SLOTS.items()):
        path = os.path.join(CONCEPTS, f"{concept}.png")
        if not os.path.exists(path):
            problems.append(f"{slot}: concept '{concept}.png' is missing; render it first")
            continue
        image = Image.open(path).convert("RGB")
        master = image.resize((MASTER, MASTER), Image.LANCZOS)
        shipped = image.resize((SHIPPED, SHIPPED), Image.LANCZOS)
        master_name = f"{slot.replace('.', '_')}_{MASTER}.png"
        shipped_name = f"{slot.replace('.', '_')}_{SHIPPED}.png"
        master.save(os.path.join(OUT_DIR, master_name))
        shipped.save(os.path.join(OUT_DIR, shipped_name))
        tiles.append(shipped)
        icons[slot] = {
            "slot": slot,
            "concept": concept,
            "source": source,
            "sprite": f"ui/icons/{shipped_name}",
            "sprite_px": SHIPPED,
            "master": f"ui/icons/{master_name}",
            "master_px": MASTER,
            "alpha": "none",
            "note": note,
        }

    if problems:
        for problem in problems:
            print(f"  FAIL  {problem}")
        print(f"\n  {len(problems)} problem(s); nothing promoted")
        return 1

    rows = (len(tiles) + ATLAS_COLUMNS - 1) // ATLAS_COLUMNS
    atlas = Image.new("RGB", (ATLAS_COLUMNS * SHIPPED, rows * SHIPPED), (0, 0, 0))
    for index, tile in enumerate(tiles):
        atlas.paste(tile, ((index % ATLAS_COLUMNS) * SHIPPED, (index // ATLAS_COLUMNS) * SHIPPED))
    atlas_name = "ui_icon_atlas.png"
    atlas.save(os.path.join(OUT_DIR, atlas_name))

    doc = {
        "version": 2,
        "comment": [
            "The minimal UI icon subset a playable build needs: the four resources, the interaction",
            "prompt, inventory and equipment, the weapon families including the crafted March Spear,",
            "the three Phase-1 formulas, the statuses the bible names, the hotbar's restorative and",
            "torch, companion Follow/Wait, the tracked objective and the compass. Not a finished UI.",
            "",
            "The formula, wounded, strained, downed, restorative, torch, march spear, companion,",
            "objective and compass icons are rendered for this set. The rest were already in the",
            "concept library and are reused; 'source' records which is which.",
            "",
            "The three formula icons replaced reused stock concepts. A flame burst stood in for",
            "Force, a stone-skin plating for Warding and a generic heal for Vital, which was right",
            "for PROTOTYPE.md's ember/oakskin/salve spells and wrong for the bible's Force, Warding",
            "and Vital domains - a kinetic bolt is not fire and a ward is not bark.",
            "",
            "Icons are opaque: cutting alpha out of a painted concept produces ragged edges at HUD",
            "size, so the shipped sprites keep their painted background and are drawn as framed",
            "tiles. 'alpha: none' is declared rather than left to be discovered.",
        ],
        "authority": "PHASE1_ASHEN_HOLLOW_PLAYABLE_CONTENT_BIBLE.md sections 19 and 22, "
                     "sprint brief section 24",
        "supersedes": "the PROTOTYPE.md section 4.1 spell set (ember bolt, oakskin, salve)",
        "shipped_px": SHIPPED,
        "master_px": MASTER,
        "atlas": {
            "path": f"ui/icons/{atlas_name}",
            "columns": ATLAS_COLUMNS,
            "rows": rows,
            "cell_px": SHIPPED,
            "order": sorted(SLOTS),
        },
        "icons": icons,
        "summary": {
            "slots": len(icons),
            "rendered": sum(1 for i in icons.values() if i["source"] == "rendered"),
            "reused": sum(1 for i in icons.values() if i["source"] == "reused"),
        },
    }
    os.makedirs(os.path.dirname(OUT_MANIFEST), exist_ok=True)
    with io.open(OUT_MANIFEST, "w", encoding="utf-8") as handle:
        json.dump(doc, handle, indent=2)
        handle.write("\n")

    print(f"  {'slot':<26} {'source':<10} concept")
    print("  " + "-" * 74)
    for slot in sorted(icons):
        entry = icons[slot]
        print(f"  {slot:<26} {entry['source']:<10} {entry['concept']}")
    summary = doc["summary"]
    print()
    print(f"  {summary['slots']} slots: {summary['rendered']} rendered, "
          f"{summary['reused']} reused")
    print(f"  atlas {ATLAS_COLUMNS}x{rows} at {SHIPPED} px -> {os.path.join(OUT_DIR, atlas_name)}")
    print(f"  wrote {OUT_MANIFEST}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
