"""Bring the audit's Method section up to date, and remove an orphaned header.

Two defects found by re-reading the document rather than trusting that the earlier edits landed:

  The Method section still described only the first GLB pass. Everything done afterwards - the topology
  measurement, the render-resolution audit, the concept-coverage analysis and the `_superseded`
  discovery - was missing, so the section understated what the report's numbers rest on.

  A "Discard or replace" header was left with no bullets beneath it at the end of the file, where an
  earlier reorder moved content and stranded the heading.

Written as a file rather than through `python -c`, because a previous attempt passed content through a
double-quoted PowerShell string and PowerShell's backtick escape corrupted the document.
"""
import io

PATH = r"W:\UNNAMED\docs\ASSET_LIBRARY_FORENSIC_AUDIT_2026-09-24.md"

METHOD = """## Method

Every number in this report was measured in this session. Nothing was taken from an earlier summary,
and no asset was modified, moved or regenerated.

**Tools written for this audit**

- `tools/asset_pipeline/_glb_audit.py` - parses each GLB's JSON chunk directly and reports triangles,
  vertices, material count, used versus unused materials, primitives with no material bound, texture
  count, embedded image resolutions, skins, animations and the bounding box. **3,015 files read**
  across `assets/ready/` and `assets/rigged/`. This is what established the LOD material failure.
- `tools/asset_pipeline/_mesh_topology_audit.py` - reads vertex positions and triangle indices and
  counts shared edges, boundary edges, degenerate faces, non-manifold edges and faces that share an
  edge with the same winding. **Eleven assets compared** across the good and failed sets. This is what
  separated thin shells from broken normals, and found the vertex-to-triangle separation.
- `tools/asset_pipeline/_repair_audit_doc.py` - repairs this document after a shell escaping mistake.
  Included because the mistake and its detection are part of the record.

**Survey passes**

- All **500** `*_meta.json` in `assets/ready/` read for status, tier, date, generation model and
  transform data.
- All **422** images in `assets/review/` measured for pixel dimensions and write date, to test whether
  render resolution tracked asset quality or time. It did neither.
- All **801** concepts in `assets/concepts/` measured for resolution and matched against the 500 assets
  with a LOD0 mesh, to establish build coverage.
- `assets/_superseded/` walked to find what the pipeline had already rejected.

**Evidence inspected visually, not only measured**

- `assets/review/bible_batch/` - the per-asset review renders, including every asset named in the brief.
- `assets/concepts/` - the concepts for the failures, to determine whether the failures originated in
  the concept or the reconstruction. They originate in the reconstruction.
- Contact sheets written to `assets/_checkpoint/`: `concept_compare.png`, `review_latebatch.png`,
  `verify_acceptance.png`. The last was built specifically to check the acceptance-list recommendations
  before publishing them, and it caused four of them to be corrected.

**What was not done**

- No individual audit of all 585 LOD0 assets. Named assets were measured per-asset; the remainder were
  measured in aggregate by part type and by batch.
- No engine-side inspection. The lean/orientation and rig/bind reports are not visible in the source
  data and are recorded as unresolved rather than guessed at.
- No repair tested. Merge-by-distance is the most promising fix for the fragmented meshes and was not
  attempted, because testing it means modifying a mesh and this was an audit.
"""

with io.open(PATH, "rb") as handle:
    text = handle.read().decode("utf-8")

# Remove the orphaned header at the end of the file.
lines = text.rstrip("\n").split("\n")
while lines and (lines[-1].strip() == "" or lines[-1].strip().startswith("**Discard or replace**")):
    lines.pop()
text = "\n".join(lines) + "\n"

# Replace the stale Method section wholesale.
start = text.find("## Method")
if start < 0:
    raise SystemExit("Method section not found")
text = text[:start] + METHOD

with io.open(PATH, "w", encoding="utf-8", newline="\n") as handle:
    handle.write(text)

with io.open(PATH, "rb") as handle:
    raw = handle.read()
decoded = raw.decode("utf-8")
print(f"  lines            : {len(decoded.splitlines())}")
print(f"  valid UTF-8      : yes")
print(f"  replacement chars: {decoded.count(chr(0xFFFD))}")
print(f"  control bytes    : {sum(1 for b in raw if b < 9)}")
print(f"  ends with orphan : {decoded.rstrip().splitlines()[-1][:60]}")
