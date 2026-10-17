"""WP17 step 5: the body presets as content (content/bodies/<type>_<build>.yaml, kind body), from the builds, the measured models and the
clipping gate's verdict.

python body_content.py --summaries <dir with builds_m.json, builds_f.json> --gate <INTERSECTIONS_<date>.json> --content <repo>/content

A core build (lean, average, heavy) is offered; an extreme (very_lean, very_heavy) is offered only when the gate passed it for that body.
Measurements are the MetaHuman model's tape girths (build_bodies.py), in whole millimetres. Rest offsets are empty: a build changes no
joint (bl_preset_shapes.py carries every preset onto the Veth skeleton); a later people with other proportions lists its own.
"""
import argparse
import json
import os

HERE = os.path.dirname(os.path.abspath(__file__))
TYPES = {"m": ("veth_m", "Masculine"), "f": ("veth_f", "Feminine")}
EXTREMES = ("very_lean", "very_heavy")

ap = argparse.ArgumentParser()
ap.add_argument("--summaries", required=True)
ap.add_argument("--gate", required=True)
ap.add_argument("--content", required=True)
a = ap.parse_args()
builds = json.load(open(os.path.join(HERE, "builds.json")))["builds"]
verdict = json.load(open(a.gate))["verdict"]
out = os.path.join(a.content, "bodies")
os.makedirs(out, exist_ok=True)
for sex, (type_id, type_label) in TYPES.items():
    summary = json.load(open(os.path.join(a.summaries, f"builds_{sex}.json")))
    for order, build in enumerate(builds, start=1):
        asset = f"mh_veth_{sex}" if build["id"] == "average" else f"mh_veth_{sex}_{build['id']}"
        girths = summary["average_girths_cm"] if build["id"] == "average" else summary["builds"][build["id"]]["girths_cm"]
        offered = build["id"] not in EXTREMES or verdict[asset]["pass"]
        gate = "passed" if verdict[asset]["pass"] else f"failed ({len(verdict[asset]['faults'])} faults)"
        lines = [
            f"id: body.{type_id}.{build['id']}",
            "kind: body",
            "schema: 1",
            f"display_key: body.{type_id}.{build['id']}.name",
            "tags: [body]",
            f"name: {type_label}, {build['label'].lower()}",
            "notes: " + json.dumps(f"WP17 (owner 2026-10-17). The Veth {type_label.lower()} body, {build['label'].lower()} build - the owner's blend "
            f"({'Blend_M = Victor x Trey' if sex == 'm' else 'Blend_F = Sunita x Tuya'}) moved along the Creator's Fat/Muscularity sliders by "
            f"{build['sliders'][sex]['fat']:+.2f}/{build['sliders'][sex]['muscularity']:+.2f} (tools/character_build/builds.json). Models "
            f"{asset} (MetaHuman) and the production MPFB build; garments baked per body. Clipping gate: {gate}."
            + ("" if offered else " Not offered until the gate passes it.")),
            "people_ref: people.veth",
            f"type: {type_id}",
            f"type_label: {type_label}",
            f"build: {build['id']}",
            f"build_label: {build['label']}",
            f"order: {order}",
            f"offered: {'true' if offered else 'false'}",
            f"arm_splay_deg: {build['arm_splay_deg'][sex]}",
            "measurements_mm: { " + ", ".join(f"{k}: {round(v * 10)}" for k, v in girths.items()) + " }",
            "rest_offsets_mm: {}",
        ]
        open(os.path.join(out, f"{type_id}_{build['id']}.yaml"), "w", encoding="utf-8").write("\n".join(lines) + "\n")
        print("BODY", type_id, build["id"], "offered" if offered else "held back", gate)
