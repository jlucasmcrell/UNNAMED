# Playable asset gap report

Rewritten at the end of the second sprint round, against the acceptance target in
`DEEPSEEK_PLAYABLE_ASSET_SPRINT_PROMPT.md` §29, checked against the live `assets/catalog.json`,
`assets/manifests/` and the Godot validators rather than against plan documents.

The success sentence this measures against is:

> Claude can build M3–M6 without waiting for the asset pipeline to invent a basic player, enemy,
> weapon, building, material, interaction prop, or animation.

**Verdict: true for every category that sentence names.** All seven — player, enemy, weapon,
building, material, interaction prop and animation — are now deliverable. What is left is narrower:
one missing arrow mesh, two missing world dependencies of the crafting chain, and validation work
that only an in-engine scene can do.

## Summary

| Target area | State | Blocking? |
|---|---|---|
| Player (Veth body) | **done** — built, bound to skeleton v2, Godot-validated at 1.80 m | no |
| Player outfit | 7 of 7 pieces built, generated fit | no |
| Animation | **17 player clips**, 47 clips total, library and state machine proven in Godot | no |
| Weapons (3 families) | **done** — correct lengths, authored sockets, plus the starter sword | no |
| Enemies (5) | **done** — 6 clips each, 30 creature clips, all verified | no |
| NPCs (3–5) | 8 rigged humanoids exist; **none validated against current morphology** | no |
| World structures | **done** — 13 kit pieces, 8 assembly recipes | no |
| World modular kit | **done** — interface dimensions exact to the 3.00 m module | no |
| Foliage | **done** — 14 assets, correct scale, collision policy, instance-clean | no |
| Rocks | **done** — 3-piece set, 0.60–4.00 m | no |
| Road / bridge / well / signpost | **done** | no |
| PBR materials | **done** — 10 promoted, Godot-validated | no |
| Crafting support | **done** — chain assembled; pick, ore, ingot, anvil, station, 2 recipes | no |
| Interaction props | **done** — 29 objects with anchors, collision and LODs | no |
| Magic VFX | **done** — 3 proof effects, cast charge, Strain curve, cast clip | no |
| UI icons | **done** — 17 slots promoted from 0 | no |
| Audio | **no library exists at all** | no (documented gap) |
| Arrows | **no geometry exists** | **yes** |
| Stream and lit forge | **no asset exists** | **yes** |

## 1. What changed in this round

Five of the previous round's five outstanding items are closed. Two new gaps were found by checking
rather than by building, and both are recorded in the status document rather than here:

- A stale-cache bug had silently scaled two ingots to 0.18 m; the audit now refuses to run against a
  stale catalog.
- **No clip declared to loop actually looped in the engine.** glTF carries no loop flag, so Godot's
  importer produces `LOOP_NONE` for every clip. Twenty-two clips were affected and every per-clip
  check passed.
- The prototype's starter sword had no grip socket.
- The VFX generator's `smoothstep` saturated an inverted falloff, filling the ward sprite
  edge-to-edge with bark.

## 2. Player

| Item | Required | Actual | Gap |
|---|---|---|---|
| Veth production body | 1 | `race2_veth_bindpose`, 1.80 m, base on the ground plane, 52-bone skeleton v2 | none |
| Wearable outfit | 1 coherent set | 7 of 7 pieces | coverage metadata not emitted |
| Locomotion and general animation | idle, walk, run, sprint, interact, pickup, hit, death | all present | none |
| Combat animation | sword ready/attack/block, polearm ready/thrust, bow ready/draw-release | all present | none |

The one measured limitation is unchanged and is honest: **only 3 of 20 finger bones carry skin
weight** on the character, because the generated mesh stands with its arms further down than the
contracted A-pose. The hand, wrist and forearm are weighted and the grip sockets are present, so a
weapon can be held; finger articulation is not yet believable. A mesh generated in a tighter A-pose
is the follow-up.

The animation set shares the canonical A-pose as its rest pose, so an action clip's first and last
frames have the arms 45° out rather than at the character's side. In gameplay that is masked by
blending from the locomotion state, but it is visible in a raw preview and is recorded as a
limitation of the reference-pose base rather than of the clips.

## 3. Weapons

Four weapons are now gameplay objects rather than geometry.

| Family | Asset | Length | Sockets |
|---|---|---:|---|
| One-handed sword (prototype starter) | `weapon_rusted_militia_sword` | 1.00 m | grip, pommel, attach |
| One-handed sword (brief) | `weapon_arming_sword` | 1.00 m | grip, pommel, attach |
| Bow | `weapon_yew_longbow_warbow` | 1.70 m | grip, ammo, attach |
| Polearm | `weapon_boar_spear_hunting` | 2.20 m | grip, secondary grip, head, pommel, attach |

The spear's two grips are **0.40 m apart**, which is what runtime two-handed IK needs. Every socket
is verified to sit on the mesh: its position is the centroid of the vertices in its band and at
least twelve vertices must be present, so a socket cannot silently float in mid-air.

## 4. Enemies

Five enemies, three rig plans, six clips each, 30 creature clips all verified as animation-only with
closed loop seams. All five were rescaled from a uniform 1.80 m to their real sizes and re-rigged at
the new size, because a skinned GLB carries inverse bind matrices in the old scale. All five report
**0% unweighted vertices**.

Not done: **per-bone hit regions.** The creatures carry collision hull and box proxies, but
locational damage would need region metadata mapped onto the creature skeletons, which is
gameplay-side work.

## 5. World

Complete. 13 modular kit pieces verified against their declared dimensions, 8 assembly recipes, 10
production PBR materials, 14 foliage assets, a 3-piece rock set, road, bridge, well and signpost.

Two things worth carrying forward:

- **The door frame's opening is 1.10 × 2.20 m**, which clears the 2.05 m door leaf *and* a 1.80 m
  Veth with headroom. Every dimension in the kit is an interface, not a preference: a wall must be
  exactly 3.00 m or three of them do not span 9.00 m.
- **The tree collision was a real gameplay bug** that this round did not introduce but did confirm:
  every foliage asset had carried collision built from its whole bounding volume, so a 14 m oak had
  a 12.67 × 14.00 × 13.74 m convex hull — a block of solid air. Collision is now built from the part
  the player touches, recorded as `collision_policy`.

## 6. Crafting support — the chain is assembled

`assets/manifests/prototype_crafting_chain.json` is the join between the sixteen content ids in
`PROTOTYPE.md` §4.2 and the geometry, sockets and anchors that make them real, with the two
authorised recipes, the station, the two gathering nodes and both loot sources.

| Required | State |
|---|---|
| Mining pick | built and socketed |
| Ore | exists, with `SOCK_harvest_point` |
| Ingot | four grades, all corrected to 0.30 m |
| Anvil, hammer, tongs, bellows, workbench | exist and socketed |
| 2 recipes | both defined, costs quoted from PROTOTYPE.md |
| Station | `station.forge_shed`, with three socketed fixtures |

**Four content items have no geometry and are recorded as `absent` rather than substituted:**
`item.ammo.arrow_rough`, `item.material.raw_meat`, `item.trinket.wolf_fang`, and a water-flask
charge (a state, not an object).

## 7. Interaction props

**29 objects** carry anchors derived from measured mesh bounds rather than typed in, so an anchor
follows a rescale instead of drifting from it. Every anchor carries a full orthonormal basis,
because a point plus one axis cannot express roll.

Not done: **the chest lid is not a separate moving part.** The door works as a hinged leaf because
the whole mesh *is* the leaf, but a chest cannot open. Splitting a lid out of a single generated
mesh is surface surgery, which §5 rules out as a production method, so this needs an authored chest
rather than a repaired one.

## 8. Magic VFX

Six effects, bound to the three spells `PROTOTYPE.md` names, with the cast origin on `SOCK_hand_r`.
Every atlas is a flipbook with straight alpha; every effect declares its grid, cell size, frame
count, fps, loop, ttl, blend mode and bound clip event, so gameplay binds to the contract rather
than to a filename.

Not done: **no effect has been instanced in a Godot scene.** They are assets and a contract, and
`godot_validated` is false for all six.

## 9. UI

Seventeen slots promoted from zero: four resources, the interaction prompt, two panels, three weapon
families, three proof spells and four statuses. Seven reuse concepts that already existed.

Not done: **this is not a finished UI.** No health bar frame, no 9-slice panel, no font, no layout.
Section 24 asks for the icon subset only.

## 10. Audio — a clean, total gap

**No audio library exists.** A repository-wide search for `.wav`, `.ogg` and `.mp3` under
`W:\UNNAMED` returns nothing, and there is no `assets/audio` tree. Per §25 this is reported rather
than built. The minimum eventual set is footsteps, sword swing, melee impact, bow draw/release,
creature attack, creature death, pickup, interaction click, spell cast, spell impact, forest/wind
ambience and small-settlement ambience. **License and source are an owner decision** — this is the
one area where the asset pipeline cannot proceed without direction.

## 11. Stale naming

Classified in `assets/manifests/stale_naming.json`. Nothing was renamed or deleted, per §18.

- `STALE_DESIGN_NAME` (2): `item_mana_potion`, `item_mana_elixir_bottle`. Otherreach uses
  Resonance/Strain; the bottle geometry may still be useful.
- `REUSABLE_GEOMETRY_NEEDS_RECONTEXTUALIZATION` (4): `prop_dwarven_forge_lantern`,
  `weapon_dwarven_axe_engraved`, `weapon_elven_glaive_hooked`, `weapon_elven_recurve_bow`.
- `NEEDS_DESIGN_REVIEW` (40): the eight `icon_school_*` schools and the 32 cancelled `raceclass_`
  boards, the latter cancelled by §20.

## 12. NPCs

The prototype names three: `npc.keeper_halda`, `npc.smith_orren` and `npc.warden_kesh`. Eight rigged
humanoids exist, and **none has been checked against current race morphology** — which §19 requires
before selection, and which explicitly names old Kal, humanoid-handed Vaskaal, outdated Siann and
outdated Ondrek as families not to reuse unchecked.

Candidates are recorded in the crafting-chain manifest and deliberately left unresolved, because
resolving them is a morphology judgement rather than a build. `npc.warden_kesh` is the constrained
one: a companion needs to walk, so the choice is limited to whichever mesh shares the player's
skeleton family and can therefore reuse the seventeen player clips.

## 13. Blockers

1. **No arrow geometry.** The prototype's ranged path cannot be drawn, and ammunition consumption is
   a mandatory prototype test.
2. **No water feature and no lit forge fixture.** Both are dependencies of the crafting chain.
3. **72 assets have no declared size expectation and 51 fail theirs.** None is in the playable
   subset.
4. **Audio is a total gap and needs an owner decision.**
5. **Nothing has been validated inside a running scene.** Everything is engine-verified as a
   resource; no character has walked under the state machine and no VFX has been instanced.

## 14. What is not a gap

Worth stating so effort is not spent twice:

- **Skeletons.** Four fit families at skeleton v2, 52 bones, verified per family.
- **Enemy geometry and rigs.** Five enemies, three shared plans, 0% unweighted.
- **The animation pipeline.** Seventeen player clips and thirty creature clips, all through Blender
  to animation-only GLB to a verified Godot library.
- **Architecture.** Thirteen kit pieces and eight assemblies, dimension-exact.
- **World materials.** Ten tileable PBR materials with declared tile sizes and measured seams.
- **Raw material.** 485 built assets, 759 concepts. Quantity has not been the constraint for two
  rounds.
