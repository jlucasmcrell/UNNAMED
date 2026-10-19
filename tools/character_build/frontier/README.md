# Frontier outfit pass

Author structured garments once per body family: feminine Average (`veth_f`)
and masculine Average (`veth_m`), under `sources/fits/<fit>/<part>/`. Keep the
approved bodies unchanged. Each Lean fit transfers from its family's Average.
Masculine shirt, jerkin, trousers and jack must have actual masculine sources;
the command checks their `authoring_fit` provenance. Unisex pieces may share
the original pattern. People's statures need no extra fitting.

From PowerShell, run the three rung-3 pieces with one command:

```powershell
& tools/character_build/frontier/process_frontier_outfit.ps1
```

The command stages feminine Average, masculine Average, feminine Lean, then
masculine Lean. Native Surface Deform handles structured cloth within a family;
native Mesh Deform handles loose pieces. Native Data Transfer supplies skin
weights for fabric. UV, neighbour and material correspondence restores reordered
seams. Jack lames retain their authored rigid joint attachments across the
shared family rig; the pass asserts those attachments remain unchanged.
Headwear follows the head.
People's stature remains uniform whole-body scale and needs no garment variant.

`frontier_region_masks.py` is the fixed slot-region table. Whole hidden body
meshes are disabled by the existing outfit runtime. Layered clothing is baked
into one skinned mesh per set, with hidden inner cloth cut at authoring time.

The same command renders front/side/back turnarounds and running poses for each
fit under the shared GPU lock, then assembles the actual screenshots into review
sheets. Personally inspect those sheets. Only visible clipping warrants a hand
fix to that family's Average source, followed by this same automatic pass.
`-NoRender` stages CPU-only outputs and leaves visual acceptance pending.

The BEAST workspace uses Blender 5.2, Godot 4.7.2 and
`D:\Otherreach_Scratch\frontier_native_20261018`. Output is isolated in
`region_candidate5`; the command does not promote an asset root or change shared
bindings. The native capture uses the integrated `integ_bodies_20261018` project.
Build its runtime before a capture if engine code changes.

Review 7 accepted the standard8 transfer and fit but rejected its garment
sources. The replacement sources are a licensed sewn leather jerkin and
trousers, a linen shirt with real sleeves, an open-face hood, shaped leather
boots. The early review10 rejected the slab mantle; it is omitted from both
review looks and cloak equipment visuals for the playtest. Scanned CC0
leather and wool use normal and roughness maps; the source pack's seam trim is
preserved. The old second-skin shirt author has been retired.

Source authors run in this order on the Average source: `bl_source_garments.py`
(`base_shirt`, `standing_cap`, `boots`), `bl_source_leather.py` (`vest`,
`trousers`), then `bl_source_gloves.py`, and
`bl_source_lames.py` for each of the three rung-3 pieces. These use the held
external source library and save editable blends and provenance beside each
source. Set `OTHERREACH_SOURCE_BODY=veth_m` to author the masculine source shirt,
jerkin, trousers and jack. They do not download or alter bodies.

Review 8 accepted those shapes and fit, and required a source material finish.
`bl_finish_source_materials.py -- --source-root <accepted-veth_f-snapshot>
--output-root <finished-veth_f-folder>` preserves the input snapshot and saves
the finish separately. Run it with Blender. It bakes the blue dye out of the
scanned linen, assigns brown-leather PBR to boots and scanned blackened-iron
PBR with UVs to the lames. The hood author folds its rear tip down and rounds
the crown with native modifiers before thickness. Inspect each finished Average source in
the game, copy it into `sources/fits/<fit>`, then use the same transfer
command below. No per-fit material or shape correction is part of that pass.

To transfer the entire wardrobe after source acceptance:

```powershell
& tools/character_build/frontier/process_frontier_outfit.ps1 -Parts base_shirt,trousers,vest,gloves,boots,standing_cap,lamework_jack,lame_coif,lame_sabatons
```

The jerkin source keeps a continuous shirt lining under its open armholes;
native Shrinkwrap fits the concealed lining inside the leather, preserving loose sleeves.
Its lining ends inside the belt, without cutting the cuffs.
Visual acceptance remains pending until all four native motion sheets pass.
No numerical clipping gate replaces looking at the rendered outfit.

The coif's plates use the same CC0 outer hood pattern and finished rear peak,
formed into overlapping curved horizontal lames before thickness. They are not cut
from the solidified cloth or independently warped after the hood finish.
After authoring a new coif, finish its PBR with `--parts lame_coif` and
`bl_finish_source_materials.py`, then run the same `-Parts lame_coif` transfer.
The source snapshot contains that Average family's wardrobe; finish
into a separate directory, then copy that finished piece into the source root.
