"""The reuse gate: an asset is produced only once it is classified in assets/manifests/asset_production.json.

Owner directive 2026-09-25 (docs/WAVE_0_MODULAR_ASSET_STANDARD.md section 17). Every new asset is first classified as a
template variant, a new reusable archetype, a reconstruction-suitable unique asset or a genuinely bespoke hero asset.
The image-to-3D path takes only the last two; the first two are produced by a template on the common procedural
library. The unbuilt 3D concepts frozen on 2026-09-25 stay frozen until a cluster in the manifest releases them.

    from _reuse_gate import require
    require(asset_id, "reconstruction")     # raises GateRefused with the reason
    python _reuse_gate.py <asset_id> [reconstruction|procedural|concept]
"""
import json
import os
import sys

MANIFEST = os.path.join(os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))),
                        "assets", "manifests", "asset_production.json")
ROUTES = {
    "reconstruction": {"reconstruction_unique", "bespoke_hero"},
    "procedural": {"template_variant", "new_archetype", "bespoke_hero"},
    "concept": {"template_variant", "new_archetype", "reconstruction_unique", "bespoke_hero"},
}


class GateRefused(RuntimeError):
    pass


def check(asset_id, route, manifest=MANIFEST):
    """(True, why) when asset_id may be produced by route, else (False, why)."""
    with open(manifest, encoding="utf-8") as handle:
        doc = json.load(handle)
    frozen = set(doc.get("frozen_backlog", {}).get("ids", []))
    released = {i for c in doc.get("clusters", {}).values() if c.get("released") for i in c.get("ids", [])}
    if asset_id in frozen and asset_id not in released:
        return False, (f"{asset_id} is in the frozen backlog: cluster it into a production family in "
                       f"{os.path.basename(manifest)} ('clusters', released: true) first")
    if route == "concept" and asset_id.startswith("icon_"):
        return True, f"{asset_id}: 2D UI icon, exempt from 3D asset classification (not part of the frozen backlog)"
    entry = doc.get("assets", {}).get(asset_id)
    if entry is None:
        return False, (f"{asset_id} is not classified: add it to 'assets' in {os.path.basename(manifest)} as "
                       f"template_variant, new_archetype, reconstruction_unique or bespoke_hero, with why")
    allowed = ROUTES[route]
    if entry["class"] not in allowed:
        return False, (f"{asset_id} is a {entry['class']} ({entry.get('archetype')}); the {route} route takes only "
                       f"{', '.join(sorted(allowed))}")
    return True, f"{asset_id}: {entry['class']} via {route} ({entry.get('why', '')})"


def require(asset_id, route, manifest=MANIFEST):
    ok, why = check(asset_id, route, manifest)
    if not ok:
        raise GateRefused("reuse gate: " + why)
    return why


if __name__ == "__main__":
    ok, why = check(sys.argv[1], sys.argv[2] if len(sys.argv) > 2 else "reconstruction")
    print(("ALLOWED " if ok else "REFUSED ") + why)
    sys.exit(0 if ok else 1)
