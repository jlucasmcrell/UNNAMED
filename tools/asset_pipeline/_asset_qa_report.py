"""Library report for the asset QA signals written by _asset_qa.py.

Reads <out>/assets/<id>.json and writes, next to it:
  library_report.md     thresholds and reasons, calibration checks, flag counts by family / code / file / category,
                        and the worst cases per flag
  library_assets.csv    one row per asset: key measurements and its flag codes
  library_flags.csv     one row per flag
  library_summary.json  the counts and calibration results, for scripts

Flags are review triggers, not verdicts. The calibration section re-checks the known cases on every run.

Usage
  python _asset_qa_report.py [--out <assets>/_staging/qa]
"""
import argparse
import csv
import glob
import json
import os
import sys
import time
from collections import Counter, defaultdict

import _asset_qa as qa

FAMILIES = {
    "material": ("no_materials", "no_textures", "unbound_primitive", "no_texcoord0", "material_index_range",
                 "texture_index_range"),
    "lod": ("lod_over_budget", "lod_not_decreasing", "lod_bounds_drift"),
    "topology": ("open_surface", "non_manifold", "winding_conflict", "crumpled_surface", "detached_parts",
                 "floating_piece", "fragmented", "shattered", "degenerate_tris", "topology_empty"),
    "orientation": ("tilted", "diagonal", "yawed", "base_plane_tilt"),
    "placement": ("floating", "sunk", "oversize", "undersize"),
    "skin": ("joint_outside_mesh", "joint_above_mesh", "joints_dominate_nothing", "unweighted_vertices", "no_skin"),
    "io": ("unreadable", "lod0_missing", "qa_crash"),
}
FAMILY_OF = {code: fam for fam, codes in FAMILIES.items() for code in codes}
FILE_KEYS = ("lod0", "lod1", "lod2", "lod3", "rigged")
LOD_MATERIAL_CODES = set(FAMILIES["material"]) - {"texture_index_range"}
TOPOLOGY_CODES = set(FAMILIES["topology"])


def _flags(r, file_key=None, codes=None):
    return [f for f in r.get("flags", []) if (file_key is None or f["file"] == file_key)
            and (codes is None or f["code"] in codes)]


def calibration(results):
    """Known cases from the remediation brief and its checker. Each returns (expectation, passed, evidence). Every
    case is reported on every run: an asset missing from the output folder (an --ids run into a fresh folder) is a
    FAIL with 'asset missing', so the total never changes."""
    checks = []

    def get(aid):
        return results.get(aid)

    for aid in ("container_barrel_oak", "resource_iron_billet"):
        r = get(aid)
        if r is None:
            checks.append((f"{aid} GOOD: no LOD0 topology flag", False, "asset missing"))
            continue
        f = _flags(r, "lod0", TOPOLOGY_CODES)
        t = r["files"]["lod0"]["topology"]
        checks.append((f"{aid} GOOD: no LOD0 topology flag (open edges allowed)", not f,
                       f"flags {[x['code'] for x in f] or 'none'}; boundary {t['boundary_share']}, "
                       f"fold {t['fold_share']}, winding {t['winding_conflict_share']}"))
    for aid in ("prop_quarry_winch", "prop_blocked_shaft"):
        r = get(aid)
        if r is None:
            checks.append((f"{aid} FAILED: LOD0 topology flagged", False, "asset missing"))
            continue
        f = _flags(r, "lod0", TOPOLOGY_CODES)
        t = r["files"]["lod0"]["topology"]
        checks.append((f"{aid} FAILED: LOD0 topology flagged", bool(f),
                       f"flags {[x['code'] for x in f] or 'none'}; fold {t['fold_share']}, "
                       f"non-manifold {t['nonmanifold_share']}, winding {t['winding_conflict_share']}"))
    r = get("container_chest_iron_banded")
    name = "container_chest_iron_banded tilted ~30 deg: 'tilted' flag with 25-35 deg"
    if r is None:
        checks.append((name, False, "asset missing"))
    else:
        f = _flags(r, "lod0", {"tilted"})
        v = f[0]["value"] if f else None
        checks.append((name, bool(f) and 25 <= v <= 35, f"measured {v} deg" if f else "no tilted flag"))
    lod_files = [(aid, k) for aid, r in results.items() for k in ("lod1", "lod2", "lod3") if k in r.get("files", {})]
    missing = [f"{aid}:{k}" for aid, k in lod_files if not _flags(results[aid], k, LOD_MATERIAL_CODES)]
    checks.append((f"every current _lod file fails the LOD material check ({len(lod_files)} files)", not missing,
                   f"{len(lod_files) - len(missing)} of {len(lod_files)} fail" +
                   (f"; not failing: {', '.join(missing[:10])}" if missing else "")))
    r = get("npc_kal_smith")
    name = "npc_kal_smith: joints above / outside its mesh flagged"
    if r is None:
        checks.append((name, False, "asset missing"))
    else:
        f = _flags(r, "rigged", {"joint_above_mesh", "joint_outside_mesh"})
        wa = (r["files"].get("rigged", {}).get("skin") or {}).get("worst_above") or {}
        checks.append((name, any(x["code"] == "joint_above_mesh" for x in f),
                       f"flags {[x['code'] for x in f] or 'none'}; worst above {wa.get('joint')} "
                       f"{wa.get('above_top_frac')}"))
    r = get("npc_veth_magistrate")
    name = "npc_veth_magistrate: no joint above / outside flag"
    if r is None:
        checks.append((name, False, "asset missing"))
    else:
        f = _flags(r, "rigged", {"joint_above_mesh", "joint_outside_mesh"})
        sk = r["files"].get("rigged", {}).get("skin") or {}
        checks.append((name, not f, f"flags {[x['code'] for x in f] or 'none'}; worst gap "
                       f"{(sk.get('worst_gap') or {}).get('gap_frac')}, worst above "
                       f"{(sk.get('worst_above') or {}).get('above_top_frac')}"))
    r = get("npc_ondrek_stonecarver")
    name = "npc_ondrek_stonecarver: floating rock chunks flagged 'floating_piece' on LOD0"
    if r is None:
        checks.append((name, False, "asset missing"))
    else:
        f = _flags(r, "lod0", {"floating_piece"})
        det = (r["files"]["lod0"].get("topology") or {}).get("detached") or {}
        checks.append((name, bool(f), f"{len(det.get('parts', []))} detached piece(s), share "
                       f"{det.get('detached_area_share')}, farthest {det.get('max_dist_gaps')} gaps"))
    r = get("prop_brass_balance_scales")
    name = "prop_brass_balance_scales: weights resting on the ground not flagged detached / floating on LOD0"
    if r is None:
        checks.append((name, False, "asset missing"))
    else:
        f = _flags(r, "lod0", {"detached_parts", "floating_piece"})
        det = (r["files"]["lod0"].get("topology") or {}).get("detached") or {}
        checks.append((name, not f, f"flags {[x['code'] for x in f] or 'none'}; resting pieces "
                       f"{len(det.get('grounded_parts', []))} (share {det.get('grounded_area_share')})"))
    r = get("creature_highland_brown_bear")
    name = "creature_highland_brown_bear (rigged asset): LOD0 footprint 'yawed' ~39 deg"
    if r is None:
        checks.append((name, False, "asset missing"))
    else:
        f = _flags(r, "lod0", {"yawed"})
        checks.append((name, bool(f), f"measured {f[0]['value']} deg" if f else "no yawed flag"))
    return checks


def _g(d, *path):
    for p in path:
        if d is None:
            return None
        d = d.get(p) if isinstance(d, dict) else None
    return d


def asset_row(r):
    f0 = r.get("files", {}).get("lod0") or {}
    t = f0.get("topology") or {}
    o = f0.get("orientation") or {}
    rig = r.get("files", {}).get("rigged") or {}
    sk = rig.get("skin") or {}
    row = {
        "asset_id": r["asset_id"], "category": r.get("category"), "meta_rigged": r.get("meta_rigged"),
        "flags": r.get("flag_count", 0),
        "fail_flags": sum(1 for f in r.get("flags", []) if f["severity"] == "fail"),
        "lod0_tris": t.get("triangles"), "unwelded_vert_per_tri": t.get("unwelded_vert_per_tri"),
        "welded_vert_per_tri": t.get("welded_vert_per_tri"), "boundary_share": t.get("boundary_share"),
        "nonmanifold_share": t.get("nonmanifold_share"), "dup_directed_share": t.get("dup_directed_share"),
        "winding_conflict_share": t.get("winding_conflict_share"), "fold_share": t.get("fold_share"),
        "components": t.get("components"), "largest_component_share": t.get("largest_component_share"),
        "largest_component_area_share": t.get("largest_component_area_share"),
        "detached_area_share": _g(t, "detached", "detached_area_share"), "degenerate_share": t.get("degenerate_share"),
        "tilt_deg": _g(o, "up", "tilt_deg"), "pitch_toward_pos_z_deg": _g(o, "up", "pitch_toward_pos_z_deg"),
        "roll_toward_pos_x_deg": _g(o, "up", "roll_toward_pos_x_deg"),
        "major_axis_off_world_deg": (o.get("principal_axes") or [{}])[0].get("deg_off_nearest_world_axis"),
        "elongation": o.get("elongation"), "footprint_yaw_off_axis_deg": _g(o, "footprint", "deg_off_axis"),
        "footprint_fill": _g(o, "footprint", "fill"), "base_plane_tilt_deg": o.get("base_plane_tilt_deg"),
        "min_y": f0["bounds"]["min"][1] if f0.get("bounds") else None,
        "scale_ratio": _g(f0, "placement", "scale_ratio"), "target_size_m": r.get("target_size_m"),
        "rig_worst_gap_frac": _g(sk, "worst_gap", "gap_frac"), "rig_outside_joints": len(sk.get("outside_joints", []))
        if sk else None,
        "rig_worst_above_frac": _g(sk, "worst_above", "above_top_frac"),
        "rig_zero_dominance_joints": len(sk.get("zero_dominance_joints", [])) if sk else None,
    }
    for k in ("lod1", "lod2", "lod3"):
        lf = r.get("files", {}).get(k) or {}
        row[f"{k}_budget_factor"] = _g(lf, "lod_budget", "factor")
        row[f"{k}_bounds_drift"] = lf.get("lod_bounds_drift")
    row["flag_codes"] = ";".join(r.get("flag_codes", []))
    return row


def _fmt(v):
    if isinstance(v, float):
        return f"{v:.4g}"
    return "" if v is None else str(v)


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--out", default=os.path.join(qa.ASSETS, "_staging", "qa"))
    a = ap.parse_args(argv)
    paths = sorted(glob.glob(os.path.join(a.out, "assets", "*.json")))
    if not paths:
        print(f"no asset JSON under {a.out}\\assets")
        return 1
    results = {}
    for p in paths:
        with open(p, encoding="utf-8") as h:
            r = json.load(h)
        results[r["asset_id"]] = r

    flags = [(aid, f) for aid, r in results.items() for f in r.get("flags", [])]
    by_code_file = defaultdict(Counter)
    assets_by_code = defaultdict(set)
    assets_by_family = defaultdict(set)
    files_seen = Counter()
    for aid, r in results.items():
        for k in r.get("files", {}):
            files_seen[k] += 1
    for aid, f in flags:
        by_code_file[f["code"]][f["file"]] += 1
        assets_by_code[f["code"]].add(aid)
        assets_by_family[FAMILY_OF.get(f["code"], "other")].add(aid)
    checks = calibration(results)
    cats = Counter(r.get("category") for r in results.values())
    cat_family = defaultdict(Counter)
    for fam, ids in assets_by_family.items():
        for aid in ids:
            cat_family[results[aid].get("category")][fam] += 1

    # CSVs
    rows = [asset_row(results[aid]) for aid in sorted(results)]
    with open(os.path.join(a.out, "library_assets.csv"), "w", newline="", encoding="utf-8") as h:
        w = csv.DictWriter(h, fieldnames=list(rows[0].keys()))
        w.writeheader()
        w.writerows(rows)
    with open(os.path.join(a.out, "library_flags.csv"), "w", newline="", encoding="utf-8") as h:
        w = csv.writer(h)
        w.writerow(["asset_id", "category", "file", "family", "code", "severity", "value", "threshold", "message"])
        for aid, f in sorted(flags, key=lambda x: (x[0], x[1]["file"], x[1]["code"])):
            w.writerow([aid, results[aid].get("category"), f["file"], FAMILY_OF.get(f["code"], "other"), f["code"],
                        f["severity"], f["value"], f["threshold"], f["message"]])

    summary = {
        "generated": time.strftime("%Y-%m-%d %H:%M:%S"), "qa_version": qa.QA_VERSION, "assets": len(results),
        "files": dict(files_seen), "flags": len(flags),
        "assets_with_flags": sum(1 for r in results.values() if r.get("flags")),
        "flag_counts": {code: {"files": sum(c.values()), "assets": len(assets_by_code[code]), "by_file": dict(c)}
                        for code, c in sorted(by_code_file.items())},
        "family_assets": {fam: len(ids) for fam, ids in sorted(assets_by_family.items())},
        "calibration": [{"check": c, "pass": p, "evidence": e} for c, p, e in checks],
        "thresholds": {k: {"value": v, "why": why} for k, (v, why) in qa.THRESHOLDS.items()},
    }
    with open(os.path.join(a.out, "library_summary.json"), "w", encoding="utf-8") as h:
        json.dump(summary, h, indent=1)
        h.write("\n")

    # Markdown
    L = []
    L.append("# Asset QA baseline: objective quality signals")
    L.append("")
    L.append(f"Generated {summary['generated']} by `tools/asset_pipeline/_asset_qa.py` (v{qa.QA_VERSION}) over "
             f"`assets/ready` and `assets/rigged`. Flags are **review triggers, not rejections**.")
    L.append("")
    L.append(f"- Assets: {len(results)}; files measured: " +
             ", ".join(f"{k} {files_seen[k]}" for k in FILE_KEYS if files_seen[k]) +
             ". Collision proxies are not measured.")
    L.append(f"- Flags raised: {len(flags)}; assets with at least one flag: {summary['assets_with_flags']}.")
    L.append("- Frame: glTF as the game draws it (+Y up, +Z forward, metres), node transforms applied, skinned "
             "meshes posed by joint x inverse bind. Topology is measured on geometry welded by position at "
             f"{qa.WELD_QUANTUM_M:g} m, because LOD0 meshes are split into UV-chart islands.")
    L.append("")
    L.append("## Calibration against the known cases")
    L.append("")
    L.append("| Expectation | Result | Evidence |")
    L.append("|---|---|---|")
    for c, p, e in checks:
        L.append(f"| {c} | {'PASS' if p else '**FAIL**'} | {e} |")
    L.append("")
    L.append("## Assets flagged, by family and file")
    L.append("")
    L.append("Asset counts. LOD0 is the only file the game draws; LOD1-3 are counted together.")
    L.append("")
    L.append("| Family | Any file | LOD0 | LOD1-3 | Rigged | Codes |")
    L.append("|---|---|---|---|---|---|")
    fam_file = defaultdict(lambda: defaultdict(set))
    for aid, f in flags:
        kind = "lods" if f["file"] in ("lod1", "lod2", "lod3") else f["file"]
        fam_file[FAMILY_OF.get(f["code"], "other")][kind].add(aid)
    for fam, codes in FAMILIES.items():
        n = len(assets_by_family.get(fam, ()))
        present = [c for c in codes if c in by_code_file]
        ff = fam_file[fam]
        L.append(f"| {fam} | {n} | {len(ff['lod0'])} | {len(ff['lods'])} | {len(ff['rigged'])} | "
                 f"{', '.join(present) or '-'} |")
    L.append("")
    L.append("## Flag counts by code and file")
    L.append("")
    L.append("| Code | Family | Severity | Assets | " + " | ".join(FILE_KEYS) + " |")
    L.append("|---|---|---|---|" + "---|" * len(FILE_KEYS))
    sev = {f["code"]: f["severity"] for _, f in flags}
    for code in sorted(by_code_file, key=lambda c: (list(FAMILIES).index(FAMILY_OF.get(c, "io"))
                                                    if FAMILY_OF.get(c) in FAMILIES else 99, c)):
        c = by_code_file[code]
        L.append(f"| {code} | {FAMILY_OF.get(code, 'other')} | {sev[code]} | {len(assets_by_code[code])} | " +
                 " | ".join(str(c.get(k, "")) for k in FILE_KEYS) + " |")
    L.append("")
    L.append("## Assets flagged per family, by category (any file)")
    L.append("")
    fams = list(FAMILIES)
    L.append("| Category | Assets | " + " | ".join(fams) + " |")
    L.append("|---|---|" + "---|" * len(fams))
    for cat, n in cats.most_common():
        L.append(f"| {cat} | {n} | " + " | ".join(str(cat_family[cat].get(f, "")) for f in fams) + " |")
    L.append("")
    L.append("## Thresholds and why")
    L.append("")
    L.append("| Threshold | Value | Why |")
    L.append("|---|---|---|")
    for k, (v, why) in qa.THRESHOLDS.items():
        L.append(f"| `{k}` | {v} | {why} |")
    L.append("")
    L.append("## Known limitations")
    L.append("")
    L.append("Examples were measured on the 2026-09-24 library.")
    L.append("")
    L.append("- **Rig fit: a joint inside the wrong body part is not caught.** The skin test only asks whether a joint "
             "is near the surface (`joint_gap_frac`) or enclosed by it (26 rays), and whether it sits above the top. "
             "A joint buried in the torso passes both. `npc_ondrek_stonecarver`'s upper_arm, forearm and hand joints "
             "sit inside the torso about 0.4 m inboard of the arms, and only foot.R is flagged. A clean skin result "
             "is not proof of a good fit. Checking fit needs each joint compared with the vertices it drives.")
    L.append("- **Flat-face `tilted` on characters and creatures is low-confidence.** Every LOD0 gets the four "
             "orientation checks, rigged assets included; the skinned rigged GLB gets `yawed` and `diagonal` only. "
             "On organic bodies the flat-face up follows body shape: upright `npc_kal_smith` reads 49.9 deg from a "
             "2.5% face cluster, and `creature_forest_praying_mantis` reads 48 deg from its sloped abdomen. Footprint "
             "`yawed` is reliable there (brown bear 38.6 deg, bog tortoise 43.5 deg, both visibly diagonal).")
    L.append("- **`base_plane_tilt` on legged figures** fits a plane through feet and shins. It catches creatures "
             "modelled standing on end (`creature_giant_stag_beetle`, `creature_twilight_moth`), but also fires on "
             "the upright `creature_animated_armour` (45.9 deg).")
    L.append("- **Detached pieces.** Gaps are measured between surface samples no more than half a gap apart, so "
             "low-poly parts that touch face to face link. A piece resting on the ground (lowest point within one gap "
             "of y = 0) counts as placed only when the main body rests there too. Sets whose main item hovers "
             "therefore stay flagged (`travel_crampons`: main crampon 1.7 cm = 1.1 gaps up; "
             "`travel_climbing_pitons`: 1.45 gaps). A separate sheet standing on the ground beside a body is "
             "excused as placed (`item_tideglass_pane`'s side panel, 6.7%; the asset still carries "
             "`crumpled_surface`). Parts that hang free by design are flagged (`magic_bell_ritual`'s clapper, 5.8 "
             "gaps).")
    L.append("")
    L.append("## Worst cases per flag (LOD0 and rigged)")
    L.append("")
    for code in sorted(by_code_file):
        items = [(aid, f) for aid, f in flags if f["code"] == code and f["file"] in ("lod0", "rigged")]
        if not items or code in LOD_MATERIAL_CODES:
            continue
        items.sort(key=lambda x: -abs(x[1]["value"] - x[1]["threshold"])
                   if isinstance(x[1]["value"], (int, float)) and isinstance(x[1]["threshold"], (int, float)) else 0)
        L.append(f"**{code}** ({len(items)} files): " + "; ".join(
            f"`{aid}` {f['file']} {_fmt(f['value'])}" for aid, f in items[:12]))
        L.append("")
    notes = Counter()
    for r in results.values():
        for n in r.get("notes", []):
            notes[n.split(":", 1)[-1].strip()] += 1
    if notes:
        L.append("## Notes")
        L.append("")
        for n, c in notes.most_common():
            L.append(f"- {c} x {n}")
        L.append("")
    L.append("Per-asset measurements: `assets/<id>.json`. Tables: `library_assets.csv`, `library_flags.csv`. "
             "Counts: `library_summary.json`.")
    with open(os.path.join(a.out, "library_report.md"), "w", encoding="utf-8") as h:
        h.write("\n".join(L) + "\n")
    passed = sum(1 for _, p, _ in checks if p)
    print(f"  report: {os.path.join(a.out, 'library_report.md')}  calibration {passed}/{len(checks)} pass, "
          f"{len(flags)} flags on {summary['assets_with_flags']} of {len(results)} assets")
    return 0 if passed == len(checks) else 2


if __name__ == "__main__":
    sys.exit(main())
