# Native modular outfit masking

The approved body geometry, skin weights, inverse binds and animation remain the
reference. Garments bind by joint name, rather than assuming the exporter's joint
numbers match the body's. Dressing filters covered triangles on each instance;
unequipping restores the original mesh resource and current morph values.
Garments may carry their own cloth joints: only positively weighted `cloth_*`
joints and their cloth parents are appended, preserving existing rests, poses and
bind indices. Missing anatomical joints are rejected. The unused cloth joints
remain after unequipping; their simulators and worn meshes are removed.

`OutfitMask.cs` uses Godot's packed mesh serialization (`_surfaces`), changing
only triangle indices. It retains vertex, normal, UV, skin and morph buffers,
materials, native index LOD distances and the authored separate character LODs.
Rebuilding decoded arrays was rejected because it re-quantized packed normals.
Masking topology also handles standard hair and underwear materials, which cannot
read custom shader instance uniforms. Fully covered surfaces retain one degenerate
triangle, avoiding Godot's interpretation of empty indices as unindexed geometry.

Garment sidecars retain `hides_mask` for legacy region masks. `occlusion_mask`
selects the ten per-source flags encoded as `-(flags + 1)` in UV2.y. These flags
change visibility only. They do not change the body shape, rig or pose. Loose
cloaks hide no underlying surfaces, and headwear does not hide face geometry.

## Native checks

Build Presentation, then run the installed Godot console executable with:

```
--headless --path src/Presentation -- --anim-sheet <output>
--anim-profiles --anim-mask-check --anim-pose-frames --anim-states idle,walk,run
--asset-root <candidate-primary> --bindings-overlay <candidate-bindings>
--visual tier=high,barrowworks=0,player=metahuman_f
```

The mask check runs native positive/negative controls for indexed 16/32-bit and
unindexed geometry, absent/wrong/partial/full masks, eight skin influences, morph
values, native LODs and original-resource integrity. Garment-joint controls cover
parent recursion, repeated attachment, an unweighted missing joint, refusal to add
an anatomical joint, and unchanged existing rests/poses. It also compares every
non-index attribute, morph array and material on the actual dressed body.
`--anim-equipment-probe` additionally exercises dress, change, unequip and repeat
without altering the pose, checking resolved garment IDs and native named binds.

Headless checks validate data and setup only. Rendered motion, exposed mask
boundaries and interlayer clipping require the separate art review.
`--anim-pose-frames` writes the final `SkeletonUpdated` matrices for every captured
view, including native cloth simulation, to `profile_pose_frames.json`. For CPU
physics checks, Godot's `--headless --fixed-fps 60` advances the same native
modifiers using a fixed delta without waiting for real-time frames. The candidate
wardrobe remains unaccepted until those gates pass on every offered body/LOD.
