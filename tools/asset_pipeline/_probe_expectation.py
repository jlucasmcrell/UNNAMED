"""Ask the scale audit's own resolver what it expects for a given asset.

Reasoning about the resolution order was not matching what the audit printed, so this calls the real
function instead of reimplementing it. Guessing at why a rule did not apply is how the wrong file
gets edited twice.

`resolve_expectation` only considers a subject rule whose `family` field equals `family_of(asset_id)`,
and `family_of` is just the id prefix. So a rule declared with a semantic family such as "prop" is
invisible on an asset whose id starts `landmark_`. This prints the prefix, the resolved expectation
and its source together, which makes that mismatch obvious instead of silent.
"""
import importlib.util
import io
import json
import sys

TOOL = r"W:\UNNAMED\tools\asset_pipeline\_audit_semantic_scale.py"
spec = importlib.util.spec_from_file_location("audit", TOOL)
audit = importlib.util.module_from_spec(spec)
sys.argv = ["audit"]
spec.loader.exec_module(audit)

with io.open(audit.REGISTRY, encoding="utf-8") as handle:
    registry = json.load(handle)

ids = sys.argv[1:] or [
    "npc_kal_smith", "race_kal_representative", "racebody_kal_pose",
    "creature_dune_jackal_scout", "landmark_ashen_waystone", "landmark_quiet_stone",
    "landmark_foldscar_core", "prop_iron_vein_outcrop", "resource_ash_haft",
    "weapon_hunting_bow", "building_smithy",
]
print(f"  {'asset id':<32} {'prefix':<12} {'expected':>9}  source")
print("  " + "-" * 78)
for asset_id in ids:
    expected = audit.resolve_expectation(asset_id, registry)
    longest = expected[0] if expected else None
    source = expected[2] if expected else "-"
    print(f"  {asset_id:<32} {audit.family_of(asset_id):<12} {str(longest):>9}  {source}")
