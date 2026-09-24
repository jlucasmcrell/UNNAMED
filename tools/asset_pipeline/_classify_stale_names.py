"""Classify assets whose NAMES no longer match current Otherreach design.

Two explicit instructions from the sprint prompt drive this:

  - Section 17: Otherreach no longer uses generic mana as the core magic resource, so mana-named
    assets must be marked `STALE_DESIGN_NAME` rather than silently treated as canon. The bottle
    geometry may still be useful.
  - Section 18: the library still carries stock-fantasy `dwarven_*` / `elven_*` names, and
    Otherreach has no dwarves or elves. The geometry may be good. "Do NOT delete good meshes just
    for naming" - so this classifies, it never deletes or renames.

A third case is reported rather than asserted. The icon set carries eight `icon_school_*` magic
schools, but the current design has three tiny representative domains (ember, mend, ward) plus
Resonance/Strain. Whether those icons are stale or simply unused is an art-direction call, so they
are flagged `NEEDS_DESIGN_REVIEW` with the evidence, not classified as wrong.

Deliberately no renaming happens here. A rename has to reassign cultural identity and update
metadata (section 18), and doing that silently would be exactly the "let old concept-era names
become canonical content IDs" failure the prompt warns about.

Usage:
    python _classify_stale_names.py
"""
import collections
import datetime
import io
import json
import os
import re
import sys

ASSETS = r"W:\UNNAMED\assets"
CATALOG = os.path.join(ASSETS, "catalog.json")
CONCEPTS = os.path.join(ASSETS, "concepts")
READY = os.path.join(ASSETS, "ready")
OUT = os.path.join(ASSETS, "manifests", "stale_naming.json")

# (classification, pattern, rationale). Patterns are matched against the asset id.
RULES = [
    ("STALE_DESIGN_NAME", r"mana",
     "Otherreach uses Resonance/Strain, not a generic mana pool. Keep the geometry; never ship it "
     "under mana terminology."),
    ("REUSABLE_GEOMETRY_NEEDS_RECONTEXTUALIZATION", r"dwarv|elven|\belf\b|\bdwarf\b|gnome",
     "Otherreach has no stock fantasy dwarves or elves. The mesh may be good; it needs a new "
     "cultural identity before it enters gameplay content."),
    ("NEEDS_DESIGN_REVIEW", r"^icon_school_",
     "Eight magic schools predate the current three-representative-domain design (ember, mend, "
     "ward). May be stale, or simply unused."),
    ("NEEDS_DESIGN_REVIEW", r"^raceclass_",
     "Cancelled by sprint section 20: these are art-direction and costume boards, not 32 required "
     "production characters, and some combinations are conceptually stale."),
]


def main():
    catalog = json.load(io.open(CATALOG, encoding="utf-8"))
    built = {e["id"] for e in catalog["assets"]}
    concepts = {f[:-4] for f in os.listdir(CONCEPTS) if f.endswith(".png")}
    all_ids = sorted(built | concepts)

    findings = []
    for asset_id in all_ids:
        for classification, pattern, rationale in RULES:
            if re.search(pattern, asset_id):
                findings.append({
                    "asset_id": asset_id,
                    "classification": classification,
                    "matched": pattern,
                    "rationale": rationale,
                    "built": asset_id in built,
                    "has_ready_dir": os.path.isdir(os.path.join(READY, asset_id)),
                    "has_concept": asset_id in concepts,
                    "action": ("retain mesh, rename and reassign identity before gameplay use"
                               if classification == "REUSABLE_GEOMETRY_NEEDS_RECONTEXTUALIZATION"
                               else "retain, do not ship under this name"
                               if classification == "STALE_DESIGN_NAME"
                               else "art-direction decision required"),
                    "renamed": False,
                    "deleted": False,
                })

    by_class = collections.Counter(f["classification"] for f in findings)
    doc = {
        "version": 1,
        "generated": datetime.datetime.now().isoformat(timespec="seconds"),
        "comment": [
            "Names that no longer match current design. Nothing here is renamed or deleted:",
            "section 18 forbids discarding good meshes over naming, and a correct rename has to",
            "reassign cultural identity and update metadata rather than swap a string.",
            "",
            "Classes: STALE_DESIGN_NAME (do not ship under this name),",
            "REUSABLE_GEOMETRY_NEEDS_RECONTEXTUALIZATION (mesh is fine, identity is not),",
            "NEEDS_DESIGN_REVIEW (an art-direction call, not a defect).",
        ],
        "totals": {"classified": len(findings), **by_class},
        "findings": findings,
    }
    with io.open(OUT, "w", encoding="utf-8") as handle:
        json.dump(doc, handle, indent=2)

    print(f"  classified {len(findings)} assets across {len(all_ids)} known ids")
    for classification, count in by_class.most_common():
        print(f"    {classification:<45} {count}")
    print()
    for classification in ("STALE_DESIGN_NAME", "REUSABLE_GEOMETRY_NEEDS_RECONTEXTUALIZATION"):
        group = [f for f in findings if f["classification"] == classification]
        if group:
            print(f"  {classification}:")
            for f in group:
                print(f"    {f['asset_id']:<44} built={str(f['built']):<5} deleted={f['deleted']}")
    review = [f for f in findings if f["classification"] == "NEEDS_DESIGN_REVIEW"]
    if review:
        print(f"\n  NEEDS_DESIGN_REVIEW ({len(review)}): "
              + ", ".join(f["asset_id"] for f in review[:8])
              + (" ..." if len(review) > 8 else ""))
    print(f"\n  wrote {OUT}")
    print("  nothing renamed, nothing deleted")
    return 0


if __name__ == "__main__":
    sys.exit(main())
