"""Check the reuse gate's standing rules (docs/WAVE_0_MODULAR_ASSET_STANDARD.md section 17). Exit 1 on any breach.

1. A procedural generator (tools/asset_pipeline/_procgen_*.py) imports the common library (procgen_lib) and defines
   none of its shared machinery itself (shader graph, material recipes, UV packing, baking, validation, glTF export,
   noise). Generators written before the gate are listed in the manifest's legacy_generators until they are ported.
2. Every generator is registered: it is a template named by an archetype, or a bespoke hero asset's generator.
3. An archetype with two costed assets is reusable only if the second asset's asset-specific lines and agent tokens
   (wall minutes where tokens were not measured) are each at most half the first's; otherwise its status must say
   not_yet_reusable.

    python _check_reuse_gate.py
"""
import glob
import json
import os
import re
import sys

TOOLS = os.path.dirname(os.path.abspath(__file__))
MANIFEST = os.path.join(os.path.dirname(os.path.dirname(TOOLS)), "assets", "manifests", "asset_production.json")
SHARED = re.compile(r"^\s*(?:def (?:set_device|run_bake|bake_\w*|pack_atlas|pack_islands|gltf_output_group|to_srgb|save_png|"
                    r"fbm3?|perlin3|voronoi3|vnoise|validate_glb|glb_checks)\s*\(|class (?:Graph|Val|Ctx)\b)", re.M)


def main():
    with open(MANIFEST, encoding="utf-8") as handle:
        doc = json.load(handle)
    legacy = set(doc.get("legacy_generators", {}).get("files", []))
    templates = {os.path.basename(str(a.get("template") or "").split(" ")[0]) for a in doc.get("archetypes", {}).values()}
    problems = []
    for path in sorted(glob.glob(os.path.join(TOOLS, "_procgen_*.py"))):
        name = os.path.basename(path)
        if name in legacy:
            print(f"  legacy   {name} (pre-gate; to be ported onto procgen_lib)")
            continue
        text = open(path, encoding="utf-8").read()
        if not re.search(r"^\s*(?:from procgen_lib\b|import procgen_lib\b)", text, re.M):
            problems.append(f"{name}: does not import procgen_lib")
        for match in SHARED.finditer(text):
            line = text.count("\n", 0, match.start()) + 1
            problems.append(f"{name}:{line}: re-implements shared machinery ({match.group(0).strip()[:40]}); import it from procgen_lib")
        if name not in templates and not re.search(r"^BESPOKE_HERO\s*=", text, re.M):
            problems.append(f"{name}: not an archetype's template in the manifest and not marked BESPOKE_HERO = '<asset id>'")
        print(f"  checked  {name}")
    for key, archetype in doc.get("archetypes", {}).items():
        cost = archetype.get("cost", [])
        if len(cost) < 2:
            continue
        first, second = cost[0], cost[1]
        # Lines always; effort as agent tokens, or wall minutes where tokens were not measured.
        effort = "agent_tokens" if first.get("agent_tokens") and second.get("agent_tokens") is not None else "wall_minutes"
        cheaper = all(second.get(m) is not None and first.get(m) and second[m] <= 0.5 * first[m]
                      for m in ("asset_specific_lines", effort))
        if not cheaper and archetype.get("status") != "not_yet_reusable":
            problems.append(f"archetype {key}: second asset is not materially cheaper than the first "
                            f"({second} vs {first}); status must be not_yet_reusable")
        print(f"  archetype {key}: second/first lines {second.get('asset_specific_lines')}/{first.get('asset_specific_lines')}, "
              f"tokens {second.get('agent_tokens')}/{first.get('agent_tokens')} -> {'reusable' if cheaper else 'NOT yet reusable'}")
    print(f"legacy generators still to port: {len(legacy)}; frozen backlog: {doc.get('frozen_backlog', {}).get('count')}")
    for problem in problems:
        print("  BREACH " + problem)
    print("RESULT: " + ("gate holds" if not problems else f"{len(problems)} breach(es)"))
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main())
