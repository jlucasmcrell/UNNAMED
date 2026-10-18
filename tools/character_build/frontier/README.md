# Frontier outfit pass

Author each source once on feminine Average (`veth_f`), under the approved
external wardrobe root's `sources/fits/veth_f/<part>/`. Keep the approved bodies
unchanged. The other fits are generated from that source, never hand-authored.

From PowerShell, run the three rung-3 pieces with one command:

```powershell
& tools/character_build/frontier/process_frontier_outfit.ps1
```

The command uses native Blender Surface Deform to carry the source onto the
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

The current shirt is still awaiting visual acceptance. Earlier torn or
skin-breaking sheets remain rejected. No numerical clipping gate replaces
looking at the rendered outfit.
