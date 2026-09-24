"""Tag Phase-1 concept prompts with the two failure modes that cost real rebuilds.

Both were found the hard way, from assets that were built, rendered, and wrong.

FINE_GEOMETRY_RECONSTRUCTION_RISK - `creature_bristleback_boar` failed its image-to-3D pass twice,
returning a tangle of flat shards with no boar in it, while the four archetypes with solid surfaces
(hound, husk, armour, spider) all reconstructed cleanly. The difference was that the boar was covered
in long, separate bristles. Fine hair strands are thinner than the reconstructor's sampling, so they
come back as disconnected sheets. Rebuilding from a prompt that described a solid matte hide with a
low ridge of short hair produced a correct boar.

NEGATION_PROMPT_RISK - `resource_ash_haft` was asked for as a haft "for a spear" that said "no metal,
no binding and no head", and the generator drew a complete spear including all three. Naming the
forbidden parts primes them; the corrected prompt describes only the bare shaft and never mentions a
spear or metal, and it worked.

Only the boar is a *confirmed* failure. Everything else tagged here is the same pattern, not a proven
defect, and this audit says which is which rather than flattening them together. A tag is not a
verdict that the asset is wrong - it is a flag that the prompt carries a known risk, which matters
before regenerating it or using it as a template.

Writes the tags back into the request entries and produces the audit document.

Usage:
    python _audit_prompt_risk.py --audit
    python _audit_prompt_risk.py --apply
"""
import argparse
import glob
import io
import json
import os
import re

ASSETS = r"W:\UNNAMED\assets"
REQUESTS = os.path.join(ASSETS, "requests")
DOC = r"W:\UNNAMED\docs\ASSET_PROMPT_RISK_AUDIT.md"

FINE = "FINE_GEOMETRY_RECONSTRUCTION_RISK"
NEGATION = "NEGATION_PROMPT_RISK"

# Words that describe geometry too fine for the reconstruction pass to resolve as a surface.
#
# The signal is fine geometry that covers a form, not fine geometry that is incidental to it: the
# boar failed because long bristles covered its whole body, while humanoid NPCs with visible hair on
# their heads reconstructed fine. `hair` is kept because it is the same pattern even at low weight,
# but two other candidates were deliberately dropped. `down` matched "down the shaft" far more often
# than it meant feathers, and `frond`/`leaf` were dropped because foliage reconstructs cleanly in this
# pipeline - tagging the whole Charwood kit would be noise, not signal.
FINE_PATTERNS = [
    r"\bbristl", r"\bbristly\b", r"\bfur\b", r"\bfurry\b", r"\bhair\b", r"\bhairy\b",
    r"\bmane\b", r"\bfeather", r"\bwispy\b", r"\bstrand", r"\bfibrous\b",
    r"\bshaggy\b", r"\btuft", r"\bfringe\b", r"\bfiligree\b", r"\bnetting\b", r"\blace\b",
    r"\btassel", r"\bfluffy\b", r"\bfluff\b", r"\bcobweb", r"\bspun\b", r"\bsilken\b",
]

# Phrases that name a thing in order to forbid it, which is how the spear happened.
NEGATION_PATTERNS = [
    r"\bno [a-z]+", r"\bnot [a-z]+", r"\bwithout\b", r"\bfree of\b", r"\babsence of\b",
    r"\bdevoid of\b", r"\bunadorned\b",
]

# Confirmed by a rebuild, not inferred from the pattern.
CONFIRMED = {
    "creature_bristleback_boar": (FINE, "Two builds from the original prompt returned flat shards; "
                                       "rebuilt correctly once the long bristles were removed."),
    "resource_ash_haft": (NEGATION, "The prompt said 'for a spear' and 'no metal, no binding and no "
                                    "head'; the concept came back as a complete spear with all "
                                    "three."),
}

# Phase-1 assets: the archetypes, weapons, NPCs, environment kit, resources and icons.
PHASE1_PREFIXES = ("creature_ash_ember_hound", "creature_bone_walker_husk",
                   "creature_animated_armour", "creature_bristleback_boar",
                   "creature_cave_hunting_spider", "weapon_arming_sword", "weapon_hunting_bow",
                   "weapon_march_spear", "npc_veth_magistrate", "npc_kal_smith",
                   "npc_siann_archivist", "npc_orenth_guide", "landmark_ashen_waystone",
                   "forge_shed", "longhouse", "building_well", "building_smithy", "building_lodge",
                   "prop_iron_vein_outcrop", "prop_blocked_shaft", "prop_quarry_winch",
                   "prop_quarry_rail_track", "landmark_quiet_stone", "landmark_foldscar_core",
                   "prop_cart_damaged_merchant", "resource_ash_haft", "resource_woundmoss",
                   "resource_iron_billet", "item_raw_iron_ore")


def is_phase1(asset_id):
    return any(asset_id == p or asset_id.startswith(p) for p in PHASE1_PREFIXES)


def load_all():
    """Every request entry that carries a prompt, keyed by id, with the file it came from."""
    entries = {}
    for path in sorted(glob.glob(os.path.join(REQUESTS, "*.json"))):
        try:
            with io.open(path, encoding="utf-8") as handle:
                document = json.load(handle)
        except (OSError, ValueError):
            continue
        if isinstance(document, dict):
            document = document.get("requests") or document.get("entries") or []
        if not isinstance(document, list):
            continue
        for entry in document:
            if not isinstance(entry, dict) or "prompt" not in entry:
                continue
            asset_id = entry.get("id") or entry.get("asset_id")
            if not asset_id:
                continue
            entries.setdefault(asset_id, {"entry": entry, "file": os.path.basename(path)})
    return entries


def classify(prompt):
    lowered = prompt.lower()
    fine = sorted({m.group(0) for p in FINE_PATTERNS for m in re.finditer(p, lowered)})
    negation = sorted({m.group(0) for p in NEGATION_PATTERNS for m in re.finditer(p, lowered)})
    return fine, negation


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--audit", action="store_true")
    parser.add_argument("--apply", action="store_true")
    args = parser.parse_args()

    entries = load_all()
    phase1 = {k: v for k, v in entries.items() if is_phase1(k)}
    print(f"  prompts indexed   : {len(entries)}")
    print(f"  Phase-1 prompts   : {len(phase1)}")
    print()

    rows = []
    for asset_id in sorted(phase1):
        record = phase1[asset_id]
        prompt = record["entry"]["prompt"]
        fine, negation = classify(prompt)
        tags = []
        if fine:
            tags.append(FINE)
        if negation:
            tags.append(NEGATION)
        confirmed = CONFIRMED.get(asset_id)
        rows.append({
            "asset_id": asset_id,
            "file": record["file"],
            "fine_hits": fine,
            "negation_hits": negation,
            "tags": tags,
            "confirmed": confirmed[0] if confirmed else None,
            "confirmed_reason": confirmed[1] if confirmed else None,
            "prompt": prompt,
        })
        if tags:
            mark = "CONFIRMED" if confirmed else "suspected"
            print(f"  [{mark:>9}] {asset_id}")
            for tag in tags:
                hits = fine if tag == FINE else negation
                print(f"              {tag}: {', '.join(hits)}")

    tagged = [r for r in rows if r["tags"]]
    print()
    print(f"  tagged            : {len(tagged)} of {len(rows)}")
    print(f"  confirmed failures: {len([r for r in rows if r['confirmed']])}")

    if args.apply:
        for row in rows:
            record = phase1[row["asset_id"]]
            if row["tags"]:
                record["entry"]["risk_tags"] = row["tags"]
                if row["confirmed"]:
                    record["entry"]["risk_confirmed"] = row["confirmed_reason"]
            elif "risk_tags" in record["entry"]:
                del record["entry"]["risk_tags"]
        touched = set(r["file"] for r in rows)
        for name in sorted(touched):
            path = os.path.join(REQUESTS, name)
            with io.open(path, encoding="utf-8") as handle:
                document = json.load(handle)
            if isinstance(document, dict):
                key = "requests" if "requests" in document else "entries"
                document[key] = [phase1[i]["entry"] if i in phase1 else e
                                 for e in document[key] for i in [e.get("id") or e.get("asset_id")]]
            else:
                document = [phase1[e.get("id") or e.get("asset_id")]["entry"]
                            if (e.get("id") or e.get("asset_id")) in phase1 else e
                            for e in document]
            with io.open(path, "w", encoding="utf-8") as handle:
                json.dump(document, handle, indent=2)
                handle.write("\n")
        print(f"  tags written back to {len(touched)} request file(s)")

    write_doc(rows, args.apply)
    return 0


def write_doc(rows, applied):
    tagged = [r for r in rows if r["tags"]]
    lines = [
        "# Asset Prompt Risk Audit",
        "",
        "**Date:** 2026-09-24",
        "**Scope:** the Phase-1 prompt library",
        "",
        "Two prompt failure modes cost real rebuilds during the Phase-1 asset sprint. This records them",
        "against every Phase-1 prompt so the same shape is not reused as a template.",
        "",
        "A tag is **not** a verdict that the asset is wrong. Only one asset in this library is a",
        "*confirmed* failure of each mode, and the difference between confirmed and suspected is kept",
        "visible below rather than flattened.",
        "",
        "## The two modes",
        "",
        f"### `{FINE}` - fine geometry the rebuild cannot resolve",
        "",
        "`creature_bristleback_boar` failed its image-to-3D pass **twice**, returning a tangle of flat",
        "grey shards with no boar in it. The concept was good - a proper wild boar with tusks, snout,",
        "ears and a mane ridge. The other four archetypes reconstructed cleanly, and the difference is",
        "the surface: hound (short coat), husk (bare bone), armour (hard plate), spider (smooth chitin)",
        "are all solid, high-contrast forms, while the boar was covered in long separate bristles. Hair",
        "strands are thinner than the reconstructor's sampling, so they return as disconnected sheets.",
        "",
        "The fix was to describe a **solid matte hide with a low ridge of short stiff hair**. The",
        "rebuilt boar reads unmistakably as a boar.",
        "",
        f"### `{NEGATION}` - naming a part in order to forbid it",
        "",
        "`resource_ash_haft` wanted a raw crafting material: the stave the March Spear is built from.",
        "Its prompt said a haft \"for a spear\" and carried \"no metal, no binding and no head\". The",
        "concept came back as a **completed spear with a metal head, a binding collar and a butt cap** -",
        "all three forbidden parts, drawn.",
        "",
        "The fix was to describe only the bare shaft and never mention a spear, a head or metal. That",
        "produced exactly the pale knot-marked stave that was wanted.",
        "",
        "**Write what you want to see, and describe a surface the reconstructor can resolve.**",
        "",
        "## Confirmed",
        "",
        "| asset | mode | evidence |",
        "|---|---|---|",
    ]
    for row in rows:
        if row["confirmed"]:
            lines.append(f"| `{row['asset_id']}` | `{row['confirmed']}` | {row['confirmed_reason']} |")
    lines += [
        "",
        "## Tagged: same pattern, not proven",
        "",
        "These prompts contain the pattern. They have not been rebuilt to test it, so they are flagged",
        "rather than declared broken.",
        "",
        "| asset | request file | tag | matched text |",
        "|---|---|---|---|",
    ]
    for row in sorted(tagged, key=lambda r: r["asset_id"]):
        if row["confirmed"]:
            continue
        for tag in row["tags"]:
            hits = row["fine_hits"] if tag == FINE else row["negation_hits"]
            lines.append(f"| `{row['asset_id']}` | `{row['file']}` | `{tag}` | {', '.join(hits)} |")
    if not any(r["tags"] and not r["confirmed"] for r in rows):
        lines.append("| _none_ | | | |")

    lines += [
        "",
        "## Clean",
        "",
        f"{len(rows) - len(tagged)} of {len(rows)} Phase-1 prompts carry neither pattern.",
        "",
        "## Known limitation",
        "",
        "A prompt id that appears in more than one request file is tagged only in the first file it is",
        "found in, because the sweep indexes prompts by id. `weapon_arming_sword` is the case in point:",
        "it is tagged in `_remaining_hq.json` and untagged in `phase_a_props_weapons_creature.json`,",
        "where the two entries carry different text. Re-running the sweep after an id becomes unique",
        "resolves it; it is recorded here rather than silently left inconsistent.",
        "",
        "## What to do with a tag",
        "",
        f"- Before **regenerating** a tagged asset, rewrite the prompt first: replace fine separated",
        "  geometry with a solid surface, and replace every negation with a positive description of",
        "  what should be there.",
        "- Do **not** reuse a tagged prompt as a template for a new asset.",
        "- A tagged asset that already built correctly is fine to keep and use. The tag describes the",
        "  prompt, not the mesh.",
        "",
        f"*Tags written back into the request entries: {'yes' if applied else 'no (audit only)'}.*",
        "",
    ]
    os.makedirs(os.path.dirname(DOC), exist_ok=True)
    with io.open(DOC, "w", encoding="utf-8") as handle:
        handle.write("\n".join(lines))
    print(f"  wrote {DOC}")


if __name__ == "__main__":
    raise SystemExit(main())
