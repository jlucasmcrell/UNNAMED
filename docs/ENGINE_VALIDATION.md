# OTHERREACH — Godot Engine Validation Spike

**Status:** Required evidence checkpoint before deep 3D commitment  
**Target timing:** After foundational M2/M2b/M2c work and before or at the very beginning of major M3 world/presentation expansion

## 1. Why this exists

Otherreach is systems-heavy but also intends a large, dense first-person 3D world.

Godot remains the working engine, but the decision should be validated with representative load rather than internet reputation or optimism.

## 2. Stress-test scene

Build one deliberately demanding **2 km × 2 km** prototype region containing representative production assets.

Target ingredients:

- real terrain;
- 50,000+ instanced trees/bushes/rocks/clutter;
- dozens of real AI-generated/cleaned PBR assets;
- a small settlement;
- 10–20 buildings;
- several usable interiors;
- directional sunlight/shadows;
- fog/atmospherics;
- water;
- representative texture sizes;
- 20–40 animated nearby humanoids/creatures;
- additional abstract distant NPCs;
- chunked navigation;
- cell streaming;
- LOD/HLOD;
- occlusion culling;
- rapid traversal through cell boundaries;
- aggressive asset load/unload;
- persistent state save/reload.

## 3. Test on representative hardware

Do not use only a 5090-class machine.

The 4070 Ti-class system is a better first reality check. Later add a lower target-spec machine representative of intended Steam minimum/recommended requirements.

## 4. Measure

Capture:

- CPU frame time;
- GPU frame time;
- average/1% low FPS;
- VRAM;
- system RAM;
- draw calls;
- visible object count;
- navigation cost;
- streaming hitch duration;
- initial load;
- editor responsiveness;
- import/build times.

## 5. Failure criteria

A failure is not merely “FPS below target.”

Also investigate whether achieving acceptable performance requires:

- excessive engine hacks;
- fragile third-party dependencies;
- manually authoring every optimization;
- unacceptable editor instability;
- unacceptable streaming hitches;
- asset-quality reductions inconsistent with the visual target.

## 6. Terrain risk

Terrain is a particular validation target.

Evaluate the selected terrain solution for:

- large region support;
- LOD;
- collision;
- foliage integration;
- runtime streaming;
- nav compatibility;
- save/persistent modification requirements;
- future engine-version compatibility.

## 7. Asset source-of-truth rule

Keep production assets engine-neutral:

**generation → Blender cleanup → GLB/glTF + standard PBR maps → engine import**

Do not make Godot-import artifacts the only surviving source.

This preserves an escape path if the engine decision changes.

## 8. Decision outcome

After the spike, record one of:

- **Validated:** proceed with Godot.
- **Validated with constraints:** proceed and document hard budgets.
- **Conditional:** targeted engine/plugin work required before further content.
- **Rejected:** evaluate migration while authoritative game logic is still engine-independent.

## 9. Architecture advantage

The current engine-independent authoritative C# domain is intentionally valuable here.

Do not weaken that separation before this validation checkpoint.
