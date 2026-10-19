# Frontier outfit pass

Author each source once on feminine Average (`veth_f`), under the approved
external wardrobe root's `sources/fits/veth_f/<part>/`. Keep the approved bodies
unchanged. The other fits are generated from that source, never hand-authored.

From PowerShell, run the three rung-3 pieces with one command:

```powershell
& tools/character_build/frontier/process_frontier_outfit.ps1
```

The command uses native Blender Mesh Deform wrap to carry the source onto the
other three approved fits, then native Data Transfer for skin weights. Native
UV correspondence restores vertex order where glTF reordered seam vertices.
Disconnected lames retain one rigid attachment; headwear follows the head.
People's stature remains uniform whole-body scale and needs no garment variant.

`frontier_region_masks.py` is the fixed slot-region table. Whole hidden body
meshes are disabled by the existing outfit runtime. Layered clothing is baked
into one skinned mesh per set, with hidden inner cloth cut at authoring time.

The same command renders front/side/back turnarounds and running poses for each
fit under the shared GPU lock, then assembles the actual screenshots into review
sheets. Personally inspect those sheets. Only visible clipping warrants a hand
fix to the feminine Average source, followed by this same automatic pass.
`-NoRender` stages CPU-only outputs and leaves visual acceptance pending.

The BEAST workspace uses Blender 5.2, Godot 4.7.2 and
`D:\Otherreach_Scratch\frontier_native_20261018`. Output is isolated in
`region_candidate3`; the command does not promote an asset root or change shared
bindings. The native capture uses the frozen `posture_native_20261018` project.
Build its runtime before a capture if engine code changes.

Review 7 accepted the standard8 transfer and fit but rejected its garment
sources. The replacement sources are a licensed sewn leather jerkin and
trousers, a linen shirt with real sleeves, an open-face hood, shaped leather
boots, and a short wool mantle draped once over the base outfit with Blender
cloth. The mantle uses primary torso and clavicle binding after Data Transfer;
skin corrective and twist helpers are removed from its weights. Scanned CC0
leather and wool use normal and roughness maps; the source pack's seam trim is
preserved. The old second-skin shirt author has been retired.

Source authors run in this order on feminine Average: `bl_source_garments.py`
(`base_shirt`, `standing_cap`, `boots`), `bl_source_leather.py` (`vest`,
`trousers`), then `bl_source_garments.py cloak`, `bl_source_gloves.py`, and
`bl_source_lames.py` for each of the three rung-3 pieces. These use the held
external source library and save editable blends and provenance beside each
source. They do not download, alter bodies, or fit other bodies.

Review 8 accepted those shapes and fit, and required a source material finish.
`bl_finish_source_materials.py -- --source-root <accepted-veth_f-snapshot>
--output-root <finished-veth_f-folder>` preserves the input snapshot and saves
the finish separately. Run it with Blender. It bakes the blue dye out of the
scanned linen, assigns brown-leather PBR to boots and scanned blackened-iron
PBR with UVs to the lames, lowers the rear hood peak, and binds
the mantle drape to primary torso/shoulder joints and gives its outer panels
source-pattern clearance for the run, retaining the neck opening height. Inspect the finished
feminine Average source in
the game, copy it into `sources/fits/veth_f`, then use the same transfer
command below. No per-fit material or shape correction is part of that pass.

To transfer the entire wardrobe after source acceptance:

```powershell
& tools/character_build/frontier/process_frontier_outfit.ps1 -Parts base_shirt,trousers,vest,gloves,boots,standing_cap,lamework_jack,lame_coif,lame_sabatons,cloak
```

The mantle goes last: Data Transfer uses the transferred vest, then the same
primary torso/shoulder filter is applied for each fit. The jerkin source uses
Blender smooth Mask for its concealed linen armhole seam; it avoids the
ragged boundary produced by deleting whole faces.
Visual acceptance remains pending until all four native motion sheets pass.
No numerical clipping gate replaces looking at the rendered outfit.

The coif's plates use the same CC0 outer hood pattern and finished rear peak,
formed into connected native plate hulls before thickness. They are not cut
from the solidified cloth or independently warped after the hood finish.
After authoring a new coif, finish its PBR with `ONLY_PART=lame_coif` and
`bl_finish_source_materials.py`, then run the same `-Parts lame_coif` transfer.
The source snapshot must contain the full feminine Average wardrobe; finish
into a separate directory, then copy that finished piece into the source root.
