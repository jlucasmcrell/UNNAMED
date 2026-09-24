"""Classify every built asset's real-world scale against declared semantic expectations.

Why this exists
---------------
The 3D pipeline scaled each asset by its LONGEST AXIS to a per-category default
(`UNIT_HINT_MAP` in `_blender_cleanup.py`: prop 0.5, weapon 1.2, creature 1.8, building 4.0).
That is right for a standalone barrel and wrong for anything whose size carries meaning, so a
birch tree, an oak door and a coastal ship all measure exactly 0.5 m and a bollock dagger
measures 1.2 m.

Two independent tests are reported, because they answer different questions:

1. **The normalisation signature.** Objective, no judgement: does the measured longest axis sit
   exactly on a category default? If so the asset's size was decided by its category and not by
   what the object is. This needs no expectation table and cannot be argued with.

2. **The semantic comparison.** Measured longest axis against `semantic_dimensions.json`.
   Requires a declared expectation, which is why UNKNOWN_SCALE is a first-class result rather
   than a failure: an honest unknown beats a fabricated expectation.

An asset can pass the semantic test and still carry the normalisation signature when its category
default happens to be about right (a prop that really is 0.5 m). That is reported rather than
hidden, because it means the size is right by luck.

Usage:
    python _audit_semantic_scale.py
    python _audit_semantic_scale.py --family weapon prop --json-out review/scale_audit.json
"""
import argparse
import collections
import datetime
import io
import json
import os

ASSETS = r"W:\UNNAMED\assets"
CATALOG = os.path.join(ASSETS, "catalog.json")
REGISTRY = os.path.join(ASSETS, "manifests", "semantic_dimensions.json")
REPORT = r"W:\UNNAMED\docs\SCALE_AUDIT_REPORT.md"

# Lifted from _blender_cleanup.py UNIT_HINT_MAP. Any asset whose longest axis lands on one of
# these has been sized by its category rather than by what it depicts.
CATEGORY_DEFAULTS = {
    "weapon": 1.2, "shield": 0.8, "tool": 0.6, "prop": 0.5, "icon": 0.3,
    "creature": 1.8, "character": 1.8, "building": 4.0,
    "weapon_component": 0.30, "armour": 0.40, "magic_component": 0.10,
}
SIGNATURE_EPSILON = 0.002


def family_of(asset_id):
    return asset_id.split("_")[0]


def stale_catalog_entries(catalog):
    """Catalog dimensions that disagree with the asset's own _meta.json.

    The audit measures every asset from catalog.json, and _catalog_assets.py builds that file from
    each _meta.json. Both are caches. A rescale updates the meta and the geometry but not the
    catalog, so an un-rebuilt catalog reports a pre-rescale size, the audit compares that stale
    number against the expectation table, and _rescale_glb.py applies the resulting factor to
    geometry that was already correct - scaling it a second time.

    That happened: two ingots already rescaled to 0.30 m were re-scaled to 0.18 m. So the audit
    checks its own input first and refuses to report rather than producing a table that silently
    authorises a wrong correction.
    """
    stale = []
    for entry in catalog["assets"]:
        meta_path = os.path.join(ASSETS, "ready", entry["id"], f"{entry['id']}_meta.json")
        if not os.path.exists(meta_path):
            continue
        dims = entry.get("dimensions_m")
        with io.open(meta_path, encoding="utf-8") as handle:
            meta = json.load(handle)
        meta_dims = (meta.get("transform") or {}).get("dimensions")
        if not dims or not meta_dims:
            continue
        if abs(max(dims) - max(meta_dims)) > 0.005:
            stale.append((entry["id"], max(dims), max(meta_dims)))
    return stale


def resolve_expectation(asset_id, registry):
    """Return (longest_m, confidence, source, note) or None. Most specific rule wins."""
    override = registry["assets"].get(asset_id)
    if override:
        return (override["longest_m"], override.get("confidence", "high"),
                "asset override", override.get("note", ""))

    family = family_of(asset_id)
    for rule in registry["subject_rules"]:
        if rule["family"] != family:
            continue
        for keyword in rule["match"]:
            # Asset ids are underscore-separated, so a multi-word keyword like "ice axe" or
            # "main gauche" never matches unless its spaces are normalised to underscores.
            if keyword.replace(" ", "_") in asset_id:
                return (rule["longest_m"], rule.get("confidence", "medium"),
                        f"subject rule '{keyword}'", rule.get("note", ""))

    fallback = registry["family_defaults"].get(family)
    if fallback:
        return (fallback["longest_m"], fallback.get("confidence", "low"),
                f"family default '{family}_'", fallback.get("note", ""))
    return None


def classify(measured, expected, tolerances):
    ratio = measured / expected if expected else 0.0
    if tolerances["pass_ratio_low"] <= ratio <= tolerances["pass_ratio_high"]:
        return "PASS_SCALE", ratio
    if tolerances["suspect_ratio_low"] <= ratio <= tolerances["suspect_ratio_high"]:
        return "SUSPECT_SCALE", ratio
    return "FAIL_SCALE", ratio


def category_signature(measured):
    for category, default in CATEGORY_DEFAULTS.items():
        if abs(measured - default) < SIGNATURE_EPSILON:
            return category, default
    return None, None


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--catalog", default=CATALOG)
    parser.add_argument("--registry", default=REGISTRY)
    parser.add_argument("--report", default=REPORT)
    parser.add_argument("--json-out", default=os.path.join(ASSETS, "manifests", "scale_audit.json"))
    parser.add_argument("--family", nargs="*", default=None,
                        help="Limit to these asset-id families")
    args = parser.parse_args()

    catalog = json.load(io.open(args.catalog, encoding="utf-8"))
    registry = json.load(io.open(args.registry, encoding="utf-8"))
    tolerances = registry["tolerances"]

    stale = stale_catalog_entries(catalog)
    if stale:
        print(f"  catalog is stale for {len(stale)} asset(s); it disagrees with the geometry it "
              f"describes:")
        for asset_id, catalog_m, meta_m in stale[:10]:
            print(f"    {asset_id:<44} catalog {catalog_m:.4f} m, meta {meta_m:.4f} m")
        if len(stale) > 10:
            print(f"    ... and {len(stale) - 10} more")
        print("  run _catalog_assets.py first: auditing a stale catalog would authorise a rescale "
              "of geometry that is already the right size.")
        return 1

    results = []
    for entry in catalog["assets"]:
        asset_id = entry["id"]
        if args.family and family_of(asset_id) not in args.family:
            continue
        dims = entry.get("dimensions_m") or [0.0, 0.0, 0.0]
        measured = max(dims)
        sig_category, sig_default = category_signature(measured)

        expectation = resolve_expectation(asset_id, registry)
        if expectation is None:
            verdict, ratio, expected, confidence, source, note = (
                "UNKNOWN_SCALE", None, None, None, "no expectation declared", "")
        else:
            expected, confidence, source, note = expectation
            verdict, ratio = classify(measured, expected, tolerances)

        results.append({
            "asset_id": asset_id,
            "family": family_of(asset_id),
            "verdict": verdict,
            "measured_longest_m": round(measured, 4),
            "measured_dims_m": [round(v, 4) for v in dims],
            "expected_longest_m": expected,
            "ratio": round(ratio, 3) if ratio is not None else None,
            "confidence": confidence,
            "expectation_source": source,
            "note": note,
            "category_normalised_to": sig_category,
            "category_default_m": sig_default,
            "target_size_m": entry.get("target_size_m"),
            "category": entry.get("category"),
        })

    counts = collections.Counter(r["verdict"] for r in results)
    normalised = [r for r in results if r["category_normalised_to"]]

    by_family = collections.defaultdict(lambda: collections.Counter())
    for r in results:
        by_family[r["family"]][r["verdict"]] += 1

    # ---- console ----
    print(f"  audited {len(results)} of {catalog['asset_count']} catalogued assets")
    for verdict in ("PASS_SCALE", "SUSPECT_SCALE", "FAIL_SCALE", "UNKNOWN_SCALE"):
        print(f"    {verdict:<15} {counts.get(verdict, 0):>4}")
    print(f"  carrying the category-normalisation signature: {len(normalised)}"
          f" ({100.0 * len(normalised) / max(len(results), 1):.1f}%)")

    # ---- report ----
    out = []
    add = out.append
    add("# Semantic scale audit")
    add("")
    add(f"Generated {datetime.datetime.now().strftime('%Y-%m-%d %H:%M')} from "
        f"`assets/catalog.json` (built {catalog['generated']}) against "
        f"`assets/manifests/semantic_dimensions.json`.")
    add("")
    add("## The problem")
    add("")
    add("The 3D pipeline scaled every asset by its **longest axis** to a per-category default "
        "(`UNIT_HINT_MAP` in `_blender_cleanup.py`: prop 0.5 m, weapon 1.2 m, creature 1.8 m, "
        "building 4.0 m). Size therefore records the asset's *category*, not what the object is. "
        "A birch tree, an oak door and a coastal ship all measure exactly 0.5 m; a bollock "
        "dagger measures 1.2 m.")
    add("")
    add("## Two independent tests")
    add("")
    add("1. **Normalisation signature** — objective, needs no expectation table. Does the "
        "measured longest axis land exactly on a category default? If so, the category decided "
        "the size. This cannot be argued with.")
    add("2. **Semantic comparison** — measured against a declared expected size. `UNKNOWN_SCALE` "
        "is a real result, not a failure: an honest unknown beats a fabricated expectation.")
    add("")
    add("## Results")
    add("")
    add(f"- Audited: **{len(results)}** of {catalog['asset_count']} catalogued assets")
    for verdict in ("PASS_SCALE", "SUSPECT_SCALE", "FAIL_SCALE", "UNKNOWN_SCALE"):
        add(f"- **{verdict}**: {counts.get(verdict, 0)}")
    add(f"- Carrying the **category-normalisation signature**: **{len(normalised)}** "
        f"({100.0 * len(normalised) / max(len(results), 1):.1f}%)")
    add("")
    add("Expected/measured ratio bands: PASS "
        f"{tolerances['pass_ratio_low']}-{tolerances['pass_ratio_high']}, SUSPECT "
        f"{tolerances['suspect_ratio_low']}-{tolerances['suspect_ratio_high']}, outside that "
        "FAIL.")
    add("")
    add("## By family")
    add("")
    add("| Family | assets | PASS | SUSPECT | FAIL | UNKNOWN |")
    add("|---|---:|---:|---:|---:|---:|")
    for family in sorted(by_family, key=lambda f: -sum(by_family[f].values())):
        c = by_family[family]
        add(f"| `{family}_` | {sum(c.values())} | {c.get('PASS_SCALE', 0)} | "
            f"{c.get('SUSPECT_SCALE', 0)} | {c.get('FAIL_SCALE', 0)} | "
            f"{c.get('UNKNOWN_SCALE', 0)} |")
    add("")

    worst = sorted([r for r in results if r["ratio"] is not None],
                   key=lambda r: r["ratio"])
    add("## Worst offenders (most undersized vs expectation)")
    add("")
    add("| Asset | Measured | Expected | Ratio | Category-normalised to |")
    add("|---|---:|---:|---:|---|")
    for r in worst[:25]:
        sig = (f"{r['category_normalised_to']} ({r['category_default_m']} m)"
               if r["category_normalised_to"] else "-")
        add(f"| `{r['asset_id']}` | {r['measured_longest_m']} | {r['expected_longest_m']} | "
            f"{r['ratio']} | {sig} |")
    add("")

    oversized = sorted([r for r in results if r["ratio"] is not None],
                       key=lambda r: -r["ratio"])
    add("## Worst offenders (most oversized vs expectation)")
    add("")
    add("| Asset | Measured | Expected | Ratio | Category-normalised to |")
    add("|---|---:|---:|---:|---|")
    for r in oversized[:25]:
        sig = (f"{r['category_normalised_to']} ({r['category_default_m']} m)"
               if r["category_normalised_to"] else "-")
        add(f"| `{r['asset_id']}` | {r['measured_longest_m']} | {r['expected_longest_m']} | "
            f"{r['ratio']} | {sig} |")
    add("")

    add("## Full listing")
    add("")
    add("| Asset | Verdict | Measured | Expected | Ratio | Confidence | Expectation source |")
    add("|---|---|---:|---:|---:|---|---|")
    for r in sorted(results, key=lambda r: (r["verdict"], r["family"], r["asset_id"])):
        exp = r["expected_longest_m"] if r["expected_longest_m"] is not None else "-"
        ratio = r["ratio"] if r["ratio"] is not None else "-"
        add(f"| `{r['asset_id']}` | {r['verdict']} | {r['measured_longest_m']} | {exp} | "
            f"{ratio} | {r['confidence'] or '-'} | {r['expectation_source']} |")
    add("")

    with io.open(args.report, "w", encoding="utf-8") as handle:
        handle.write("\n".join(out))
    with io.open(args.json_out, "w", encoding="utf-8") as handle:
        json.dump({"generated": datetime.datetime.now().isoformat(timespec="seconds"),
                   "counts": dict(counts),
                   "category_normalised": len(normalised),
                   "results": results}, handle, indent=2)

    print(f"  report -> {args.report}")
    print(f"  json   -> {args.json_out}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
