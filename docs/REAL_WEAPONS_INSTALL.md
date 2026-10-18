# Real weapons: sources, rebuild and install (2026-10-18, branch `worker/real-weapons`)

The placeholder weapons (`weapon_bp_rusted_sword`, `weapon_bp_hunting_bow`, `weapon_bp_march_spear`: first-party procedural geometry) are replaced by
real models. **Item ids are unchanged** (`item.weapon.rusted_sword`, `hunting_bow`, `haldas_bow`, `march_spear`); only the model ids in the art
bindings changed. No generative AI touched any mesh, texture or socket.

| item id | was | now | source |
|---|---|---|---|
| `item.weapon.rusted_sword` | `weapon_bp_rusted_sword` | `weapon_real_arming_sword` | Cosmos premium MVV SwordSet sword (photogrammetry), scaled 1.2x to 0.89 m |
| `item.weapon.hunting_bow`, `haldas_bow` | `weapon_bp_hunting_bow` | `weapon_real_longbow` | hand-modelled self longbow, 1.78 m, braced, string and horn nocks; Poly Haven CC0 textures |
| `item.weapon.march_spear`, companion weapon | `weapon_bp_march_spear` | `weapon_real_hunting_spear` | hand-modelled ash shaft 2.0 m, forged leaf head 0.30 m, leather grip wrap; Poly Haven CC0 textures |

`src/Presentation/Art/art_bindings.json` (the shared bindings; it named `weapon_arming_sword` / `weapon_yew_longbow_warbow` /
`weapon_boar_spear_hunting`) and `content/_art_bindings.json` (the region overlay) both point at the new ids.

Staged but not bound to an item id (the game has no item for them yet; the models, LODs and sockets are ready):

| asset id | source | note |
|---|---|---|
| `weapon_real_arming_sword_sheathed` | Cosmos MVV sword + MVV leather sheath | sword drawn home in the scabbard; hang from `SOCK_carry_hip` |
| `weapon_real_axe` | Cosmos MVV Axe | 0.67 m hand axe |
| `weapon_real_knife` | Cosmos ADA knife | a *table* knife (0.20 m); kept for loot/utility, not a combat blade |
| `weapon_real_estoc` | Poly Haven `antique_estoc` (CC0) | 1.49 m; later loot |
| `weapon_real_kite_shield` | Poly Haven `kite_shield` (CC0) | scaled to 1.0 m; the game has a shield HUD slot (4) but **no shield state**: `Held` is Nothing/Sword/Bow/Spear and `Avatar.Arm` has no shield part, so the shield is staged with an off-hand grip socket, not drawn in the block pose |
| `weapon_real_quiver` | hand-modelled leather quiver, 12 fletched arrows | back/hip carry |

## Conventions (same as the weapons they replace)

Y-up GLB, one mesh node named after the asset, origin at the butt (pommel, spear butt, bow's lower tip), long axis +Y, flat face along X
(bow, shield: depth along Z), guard along Z. Sockets are sibling nodes rotated `(0.5, 0.5, 0.5, 0.5)`, i.e. the socket's +X (the primary the game
reads in `HeldWeapon.Mount`) runs along the weapon toward its head and +Y along glTF +Z. Every weapon has `SOCK_grip_primary`; the spear also has
`SOCK_grip_secondary`; bows have `SOCK_ammo` (nocking point); all have `SOCK_attach` (balance point, where a strap or loop holds it) and, where
meaningful, `SOCK_head` / `SOCK_pommel`; the sheathed sword has `SOCK_carry_hip` (scabbard throat). Sizes are metres, no node scale.
LODs: `_lod1` and `_lod2` carry the same material at half and quarter texture size. LOD0 textures are 2048 px for the scanned blades, 1024 px for
the thin hand-modelled pieces.

## Carry sockets on the bodies

`tools/asset_pipeline/weapon_carry_sockets.json` gives, for the MetaHuman skeleton (`mh`) and the production (MPFB-derived) skeleton (`prod`), where
each weapon rides: sword at the left hip (anchor `thigh_l`), bow, spear, quiver, shield on the back (anchor spine bone), axe at the right hip
(anchor `thigh_r`), in body space with the weapon's long-axis direction. The game itself has no sheathed or stowed state (the only held states are
Sword, Bow and Spear), so these are data plus the render harness; wiring a stowed carry is a game-code change that was out of scope ("change only
bindings and files"). Hand grips are unchanged: the MetaHuman uses `SOCK_hand_r`, production `SOCK_hand.R`, with the existing grip frames in the
bindings.

## Rebuild (Blender 5.2)

```
blender -b -P tools/asset_pipeline/real_weapons.py -- <out_dir> all        # or any of: cos_sword cos_sword_sheathed cos_axe cos_knife estoc shield spear longbow quiver
```
Inputs: the Cosmos zips unzipped under `RW_SRC` (default `D:\Otherreach_Scratch\weapons\src`: only the FBX plus the `_B` and `_ORM` PNGs are
needed) and `C:\Otherreach_External_Assets\PolyHaven`. Render check (GPU lock!): `tools/asset_pipeline/weapon_carry_sheet.gd`, run with
`godot --path <empty project> --resolution 1100x1500 --script weapon_carry_sheet.gd -- <asset_root> <asset_root> weapon_carry_sockets.json <out> <mh_m|mh_f|prod_m|prod_f>`.

Material note: Godot draws a fully metallic surface with only the sky to reflect, so scanned steel read black (the old "thin black stick"). The
builder caps the metal channel of the ORM pack at 0.22 and lifts the albedo 1.55x; the spear head uses a plain 0.25-metal iron.

## Install (the asset base is outside git)

Copy each folder under `D:\Otherreach_Scratch\weapons\staging\ready\` to `<asset root>\ready\` (the next release's `-AssetBase`, `primary`), e.g.
```
robocopy D:\Otherreach_Scratch\weapons\staging\ready <asset root>\primary\ready /E
```
Nothing else changes: the bindings in this branch already name the new ids. Roll back by reverting the two bindings files (the `weapon_bp_*` folders
stay installed untouched).

## Licences / credits

- Cosmos items (sword, sheath, axe, knife): Cosmos premium licence (owned): game use only, ship packed, never as extractable files, never into AI.
  Keep out of any public mirror.
- Poly Haven models and textures (estoc, kite shield, rough_wood, medieval_wood, brown_leather, fabric_leather_01/02): CC0, no credit required.
- The Moonbrand arming sword (Sketchfab, CC-BY) was **not** used (the Cosmos sword was preferred and fits); no credit line is added for it here.
