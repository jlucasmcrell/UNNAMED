# Asset Known Limitations

**Date:** 2026-09-24

Per-asset facts that are easy to lose and would be expensive to rediscover. They are stored in
each asset's own metadata under `known_limitations` and `limitation_tags`, so anything reading
the asset sees them without having to find this document.

## `creature_cave_hunting_spider`

**`KNOWN_LIMITATION_ARTHROPOD_NEEDS_DEDICATED_RIG`**

Rigged on the quadruped plan: 18 bones, so the eight legs cannot be articulated independently and gait, attack and death read as a four-limbed creature with extra decoration. The creature is playable and its clips bind correctly. Fixing it needs a dedicated arthropod rig with per-leg chains, which is out of scope for Phase 1 and must not be improvised onto the shared quadruped skeleton.

## `item_raw_iron_ore`

**`CANONICAL_PHASE1_IRON_ORE`**

The canonical Phase-1 mining and crafting ore. The crafting chain resolves `item.material.iron_ore` to this asset as "exact", and it is the id in playable_prototype_assets.json. Measured 0.500 m. A rough rock with bright metallic inclusions, which is correct for raw ore.

## `resource_iron_ore`

**`VISUALLY_INCORRECT_FOR_ORE_DO_NOT_WIRE_INTO_MINING`**

Not ore. Rendered side by side against the canonical asset it is a stylised hexagonal crystal with orange veins inside a white frame - a fantasy mana crystal. Nothing consumes it: it is absent from the crafting chain and from playable_prototype_assets.json. It is recorded rather than deleted, per the maintenance brief, because the intent is to reclassify and recontextualise it later. Until then it must not be wired into Ashen Hollow mining, and it must not be reused as iron ore.

## The two ores, side by side

| | `item_raw_iron_ore` | `resource_iron_ore` |
|---|---|---|
| what it is | a rough rock with metallic ore inclusions | a stylised hexagonal crystal in a white frame |
| correct for ore | **yes** | no |
| referenced by the crafting chain | yes, as `"exact"` | no |
| referenced by the playable manifest | yes | no |
| measured size | 0.500 m | 0.200 m |
| action | keep, canonical | keep, do not wire into mining |

Both were rendered and looked at before this was written. The distinction is not inferred from
the names; the two assets do not look like the same class of object.

*Metadata tagged: yes.*
