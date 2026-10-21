"""Mark the canon index by what is actually written: an entry claims the concept whose name it carries.

Spec 41 pass 2 Â§1. Idempotent: run after every batch of entries. A concept stays missing when no entry names it, which is what the coverage
report counts.
"""

import pathlib

import yaml

ROOT = pathlib.Path(r"C:\Otherreach_Build\ds_lore_wt")
LORE = ROOT / "content" / "lore"
INDEX = LORE / "_canon_index.yaml"

entries = {}
for path in sorted(LORE.glob("*.yaml")):
    if path.name.startswith("_"):
        continue
    doc = yaml.safe_load(path.read_text(encoding="utf-8"))
    entries[doc["id"]] = doc

by_slug = {entry.split(".", 1)[1]: entry for entry in entries}
doc = yaml.safe_load(INDEX.read_text(encoding="utf-8"))


def claim(slug):
    if slug in by_slug:
        return by_slug[slug]
    if "the_" + slug in by_slug:
        return by_slug["the_" + slug]
    if slug.startswith("the_") and slug[4:] in by_slug:
        return by_slug[slug[4:]]
    for name, entry in by_slug.items():
        if name.replace("_", "") == slug.replace("_", ""):
            return entry
    return None


# An entry whose file name and the canon's name for the thing differ: the entry says what the concept is, in the journal's words.
ALIAS = {
    "blackvein_cut": "lore.blackvein",
    "ash_ember_hound": "lore.ember_hounds",
    "foldscar": "lore.tavar_and_the_fold",
    "kal_maker": "lore.the_kals_maker",
}


claimed = set()
for concept in doc["concepts"]:
    slug = concept["id"].split(".", 1)[1]
    entry = entries[ALIAS[slug]]["id"] if slug in ALIAS and ALIAS[slug] in entries else claim(slug)
    if entry is None:
        concept.pop("covered_by", None)
        concept["status"] = "missing"
        continue
    concept["covered_by"] = [entry]
    concept["status"] = concept.get("status") if concept.get("status") in ("covered", "partly") else "covered"
    claimed.add(entry)

# An entry the index does not name is an entry nobody can find in the map: say so here rather than let the report fail silently.
unclaimed = sorted(set(entries) - claimed)
if unclaimed:
    doc["unclaimed_entries"] = unclaimed
else:
    doc.pop("unclaimed_entries", None)

INDEX.write_text(yaml.safe_dump(doc, sort_keys=False, allow_unicode=True, width=200), encoding="utf-8")
counts = {status: sum(1 for c in doc["concepts"] if c["status"] == status) for status in ("covered", "partly", "missing")}
print("concepts", len(doc["concepts"]), counts, "unclaimed", len(unclaimed))
if unclaimed:
    print("unclaimed:", ", ".join(unclaimed))
