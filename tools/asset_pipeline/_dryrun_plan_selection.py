"""Dry-run every 3D stage's asset selection, without generating anything.

Confirms each stage's --only prefix matches the concepts it is meant to and that
category inference assigns the right target size, so the overnight run cannot
silently select nothing or scale assets wrongly.

Usage:
    python _dryrun_plan_selection.py
"""
import json
import os
import sys

TOOL_DIR = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, TOOL_DIR)

import importlib.util

spec = importlib.util.spec_from_file_location("ma", os.path.join(TOOL_DIR, "_make_assets.py"))
make_assets = importlib.util.module_from_spec(spec)
spec.loader.exec_module(make_assets)

ASSETS = r"W:\UNNAMED\assets"
CONCEPTS = os.path.join(ASSETS, "concepts")
PLAN = os.path.join(ASSETS, "requests", "overnight_plan.json")

with open(PLAN, encoding="utf-8") as handle:
    plan = json.load(handle)

available = sorted(os.path.splitext(name)[0]
                   for name in os.listdir(CONCEPTS) if name.lower().endswith(".png"))
print(f"{len(available)} concept(s) on disk\n")

overlap = {}
for stage in plan["stages"]:
    if stage["kind"] != "assets3d":
        continue
    prefix = os.path.normcase(stage.get("only", ""))
    selected = [name for name in available if os.path.normcase(name).startswith(prefix)]
    categories = {}
    for name in selected:
        category = make_assets.infer_category(name, "prop")
        categories[category] = categories.get(category, 0) + 1
    for name in selected:
        overlap.setdefault(name, []).append(stage["name"])
    print(f"{stage['name']:<18} --only {stage.get('only',''):<12} "
          f"selects {len(selected):>3}  {categories}")

print()
clashes = {name: stages for name, stages in overlap.items() if len(stages) > 1}
print(f"assets selected by more than one stage: {len(clashes)}")
for name, stages in list(clashes.items())[:5]:
    print(f"  {name}: {stages}")

unselected = [name for name in available
              if name not in overlap
              and not name.startswith(("icon_", "resource_", "item_", "material_"))]
print(f"\n3D-eligible concepts not selected by any stage: {len(unselected)}")
for name in unselected[:10]:
    print(f"  {name}")
