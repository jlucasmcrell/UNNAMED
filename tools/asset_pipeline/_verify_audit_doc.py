"""Final verification of the audit document. Run as a file so no shell can reinterpret its contents."""

import io

PATH = r"W:\UNNAMED\docs\ASSET_LIBRARY_FORENSIC_AUDIT_2026-09-24.md"

with io.open(PATH, "rb") as handle:
    raw = handle.read()
text = raw.decode("utf-8")

print("  === file integrity ===")
print(f"  size            : {len(raw) // 1024} KB")
print(f"  lines           : {len(text.splitlines())}")
print(f"  valid UTF-8     : yes")
bad = sorted({hex(b) for b in raw if b < 0x20 and b != 0x0A})
print(f"  control bytes   : {bad or 'none'}")
print(f"  replacement chr : {text.count(chr(0xFFFD))}")

print()
print("  === the repairs landed (code spans intact) ===")
for needle in ["`assets/concepts/`", "`forge_shed`", "`building_smithy`", "`prop_blocked_shaft`",
               "`bible_batch`", "`prop_quarry_winch`"]:
    print(f"  {'OK  ' if needle in text else 'MISS'}  {needle}")

print()
print("  === every measured finding present ===")
checks = [
    ("3,015", "GLB files inspected"),
    ("1,455", "LOD files with no material"),
    ("585", "LOD0 files"),
    ("500", "manifests read"),
    ("Pixal3D", "generation model"),
    ("39,680", "early median triangles"),
    ("39,516", "late median triangles"),
    ("0.57", "verts/tris, cleanest asset"),
    ("1.21", "verts/tris, worst asset"),
    ("building_beam", "12-triangle box"),
    ("163", "unbuilt 3D concepts"),
    ("reconstruction_failed", "archived failures"),
    ("85", "rigged assets"),
    ("2048", "texture resolution"),
    ("520", "smallest review render"),
    ("880", "latest review render"),
    ("1536", "concept resolution"),
    ("lean", "tier analysis"),
    ("validated structurally", "QUALITY_TIERS quote"),
    ("422", "renders audited"),
    ("801", "concepts counted"),
    ("merge-by-distance", "repair path"),
]
missing = [label for needle, label in checks if needle not in text]
for needle, label in checks:
    if needle not in text:
        print(f"  MISS  {label}")
print(f"  {len(checks) - len(missing)}/{len(checks)} present")
if missing:
    print(f"  MISSING: {missing}")

print()
print("  === required structure ===")
for part in ["## Part 1", "## Part 2", "## Part 3", "## Part 4", "## Part 5", "## Part 6",
             "## Part 7", "## Part 8", "## Method"]:
    print(f"  {'OK  ' if part in text else 'MISS'}  {part}")

print()
print("  === subsections ===")
for line in text.splitlines():
    if line.startswith("### "):
        print(f"   {line}")
