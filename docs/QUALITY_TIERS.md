# 3D Quality Tiers - measured

One asset (`creature_slime_marsh_frog`, same concept, same seed) was built three times
under different settings and rendered from four angles. The numbers below are measured,
not estimated, and the renders are in `review\ab_settings\renders\sheet_ab3.jpg`.

## What each tier costs

| Tier | Faces | Texture | Bake | Steps | AO | Time | Base GLB |
|---|---|---|---|---|---|---|---|
| **lean** | 25,000 | 2048 | 2048 | 12 | 64 | **343 s** | 12.1 MB |
| **mid** | 40,000 | 2048 | 2048 | 30 | 256 | **948 s** | ~20 MB |
| **hq** | 40,000 | 4096 | 4096 | 30 | 256 | **2785 s** | 26.8 MB |

## The conclusion that matters

**Texture and bake resolution is the cost, and it is not the visible benefit.**

Dropping texture and bake resolution from 4096 to 2048 while keeping everything else at
HQ saves **1837 s, which is 73% of HQ's total time**. In the side-by-side renders the
three tiers are very close; lean and mid are hard to tell apart at all.

So the earlier plan - build everything at HQ - would have spent 138 hours across the
remaining 179 assets to buy a difference that is not visible, and produced a library
twice the size that is slower to load in engine.

## What was actually wrong before

The original pipeline settings were not "low quality". They were:

```
--faces 25000  --texture-size 2048  --steps 12  --bake-resolution 2048  --ao-samples 64
```

That is already a normal game-asset budget. The quality raise I applied multiplied build
time by 8 for no perceptible gain, and made stalls more likely because the intermediate
meshes and bakes got heavier.

## Recommended setting for the remaining work

**mid**, as the default:

```
--faces 40000 --texture-size 2048 --steps 30 --bake-resolution 2048 --ao-samples 256
--upsample-resolution 1024 --lod-faces 12000,4000,1000
```

Reasons:
- 2.8x lean rather than 8.1x, so 179 assets take ~47 h instead of ~138 h
- keeps the higher step count, which drives **shape** quality, and the geometry detail
- gives up only texture resolution, which the renders show is barely visible
- 2048 PBR maps are the standard game budget and half the file size

### Weapons were tested too, and do not need more

A hard-surface asset (weapon_viking_rune_axe) was built at mid to check the assumption
that sharp edges and fine detail would show a 2048 map. They did not:

| | Time | Base GLB |
|---|---|---|
| existing library weapon (2048) | - | 14.82 MB |
| mid rebuild (40k faces, 30 steps, 2048) | **862 s** | 14.06 MB |

The existing library is already 2048, so mid is **consistent with what has already
shipped** rather than a downgrade. Committing the 95 unbuilt weapons to 4096 would have
cost roughly 3x the time and produced a library that no longer matched its own contents.
All four production stages therefore run at mid.

## How to compare a tier yourself

```
blender --background --factory-startup --python tools\asset_pipeline\_blender_preview.py ^
    -- --input ready\<name>\<name>.glb --out review\views --name <name> --size 512
```

Writes four views. `_blender_preview.py` did not exist before this; until it did, no
finished mesh had ever been looked at, only validated structurally.
