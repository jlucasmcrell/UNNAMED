# Animation Source Audit -- player motion remediation

Read-only audit + web research. Scope: why the owner rejected current player motion (running looks
unnatural, stance is unnatural, motion snags/sticks, sword combat uses one repeated swing), and what the
best commercially-safe source is for every player-visible motion state. No game files were changed by this
audit; this document is the only file written.

**Headline finding**: most of the sourcing work needed to fix this was already done in a prior pass
(`assets/animation/review/retarget/B12_CATALOGUE.md`, 2026-09-25) -- 32+ CC0 clips are already retargeted,
QA-checked (foot slide, skin stretch, joint bend, loop seam) and sitting in
`assets/animation/clips/anim.player.veth_wanderer.ext_*.json`. The primary problem is not a licensing gap,
it is **integration**: `art_bindings.json`'s `clips=ext` override only rebinds 8 of the player's ~20 states
(`idle, walk, sprint, crouch_idle, crouch_walk, hit, death, interact`), and `SkinnedFigure.cs`'s state
machine (`Animate()`) has no code path at all for several already-retargeted states (`idle_alt`,
`strafe_left/right`, `walk_back`, `jump_start`, `fall`, `land`, `sword_ready`, `sword_block`, `spear_ready`,
`spear_thrust`, `bow_ready`, `bow_draw`, `cast`) -- those clip IDs are bound in the base
`people.player.clips` table to **procedural** clips only, and the retargeted `ext_*` mocap for the same
states is never referenced anywhere in `src/**/*.cs`. Sword combat's "one repeated swing" is a code gap,
not a sourcing gap: `SkinnedFigure.cs:220-227` hardcodes a single literal clip id per weapon
(`"sword_attack"`, `"spear_thrust"`, `"bow_draw"`) with no variant index, even though three-plus attack
clips already exist on disk for sword.

## 1. How the game currently plays the player (with file:line)

Source: `src/Presentation/Art/SkinnedFigure.cs`, `SkinnedModel.cs`, `ArtBindings.cs`, `PostureModifier.cs`,
`BodyModifiers.cs`, `VisualOptions.cs`, `art_bindings.json`; combat timing from
`src/World/Runtime/Combat.cs`, `src/Domain/Combat/Combat.cs`, `src/Domain/Magic/Magic.cs`,
`src/Application/PlayerMotion.cs`.

### 1.1 State machine and clip selection

- All player animation selection is a hand-written `if`/`switch` cascade in
  `SkinnedFigure.Animate()` (SkinnedFigure.cs:192-285) -- there is no `AnimationTree` or data-driven state
  machine anywhere in `src/Presentation` (confirmed by a full-tree grep). `art_bindings.json` only maps a
  state name to a clip ID once at load (`ArtBindings.cs:184-188, 249-268`); the *set* of states and the
  branch order are fixed in code.
- Locomotion is chosen purely by scalar speed thresholds, no hysteresis:
  `> 4.2 m/s -> sprint, > 2.2 m/s -> run, else walk` (SkinnedFigure.cs:275-280), then
  `m.Play(gait, 0.2f, Mathf.Clamp(speed / pace, 0.6f, 1.6f))` (SkinnedFigure.cs:281). `Play()` only actually
  restarts the crossfade when the state name changes (`SkinnedModel.cs:76-79`), but speed hovering at 2.2 or
  4.2 m/s (normal during acceleration/deceleration, e.g. sprint-to-stop) will flip the state back and forth
  with no deadband, re-triggering a fresh 0.2 s blend from a partially-blended pose each time.
- Weapon/cast selection is a single fixed lookup, not a table or a variant pool:
  `Held.Sword -> "sword_attack"`, `Held.Spear -> "spear_thrust"`, `Held.Bow -> "bow_draw"`,
  `Casting -> "cast"` (SkinnedFigure.cs:220-227). There is no RNG, no combo counter, no per-swing index
  anywhere in this file or in `SkinnedModel.cs`. **This is the exact mechanism of "sword combat uses one
  repeated swing"**: no matter how many clips are bound under other names, the code only ever asks the
  `AnimationPlayer` for the literal string `"sword_attack"`.
- Attack/guard clips are not played forward; they are scrubbed with `AnimationPlayer.Seek()` every frame to
  a fraction of the clip (`SkinnedModel.Hold`, SkinnedModel.cs:87-101), driven by the domain's real phase
  progress (`Main.cs` computes `progress` from `CombatPhase`/`PhaseTicksLeft`, consumed via
  `CombatStance(Phase, Progress, Held, Guarding, Casting)`, `Avatar.cs:277`). `SkinnedFigure.cs:212-219`
  maps `CombatPhase.Windup/Active/Recovery` onto a **hardcoded** 0-45% / 45-70% / 70-100% span of whichever
  clip is selected, for every weapon, regardless of that weapon's real windup:active:recovery tick ratio
  (see 1.3). This happens to match the proportional convention the `ext_*` clips' own events were written
  under ("commit 45%, hit window 51-70%, recovery 70%", B12_CATALOGUE.md's combat notes), so it is not
  itself broken for sword/spear -- but see 1.4 for why it still causes visible snapping.
- Guard/ready poses: only reachable while `s.Guarding && Phase == Idle`
  (SkinnedFigure.cs:238-246): `Held.Bow -> "bow_ready"`, `Held.Spear -> "spear_ready"`, otherwise
  `"sword_block"`. **`"sword_ready"` is never requested by any code path** even though it is bound in
  `art_bindings.json`'s base `people.player.clips` table -- it is a fully orphaned clip id. A player who is
  simply standing still with a sword drawn, not actively guarding, falls through every branch and plays the
  same `"idle"` clip as an unarmed player (SkinnedFigure.cs:284) -- the same generic idle pose regardless of
  what is in the character's hands. **This is the most likely mechanism behind "stance is unnatural."**
- A separate, simpler `"attack"` state and `Strike()` method exist (SkinnedFigure.cs:129, 200-209) but are
  only ever called for NPC companions (`NpcsView.cs:98`), never for the player -- confirmed dead for this
  audit's purposes, not a second combat path fighting the real one.
- Crouch is checked *after* the weapon-action branches (SkinnedFigure.cs:220-232 run before 264), so an
  attack while crouched always shows the standing attack clip with only the procedural pelvis/thigh bend
  (`PostureModifier`) layered on top -- there is no crouched attack pose at all, adaptive or otherwise.
- `SkinnedModel.cs:57`'s default-loop guess (`"idle" or "walk" or "run" or "sprint" or "talk" or "work"`)
  omits `crouch_idle`, `crouch_walk`, and every weapon-ready state; if a clip's own `Loop` metadata is ever
  missing for one of those, `AnimationPlayer` will freeze on the last frame instead of looping.

### 1.2 What is procedural, and why

- **Crouch bend and jump tuck**: always procedural, regardless of visual tier. `PostureModifier`
  (`PostureModifier.cs:9-11, 34-49`) rotates pelvis/thigh/shin by scalar `Crouch`/`Tuck` values with its own
  comment: *"The asset set has no crouch or jump clips; this stands in until it does."* -- a stale comment:
  crouch clips exist and are bound (`ext_crouch_idle_ual`/`ext_crouch_walk_ual`); jump clips were retargeted
  in B12 (`ext_jump_start_ual`, `ext_fall_ual`, `ext_land_ual`) but were never wired to a state, so the tuck
  fallback is still load-bearing for jump/fall/land today.
- **`run` and every weapon state ("the weapon grips are calibrated to them")**: this is the stated reason in
  `art_bindings.json`'s own comment (`people_clips_by_visual_option.clips=ext.player.note`). It is a real,
  specific reason for the *weapon* states (grip transforms in `art_bindings.json`'s `people.player.grip` /
  `off_grip` were measured against the procedural clips' own hand trajectories) but it does not logically
  extend to **`run`**, which holds no weapon-grip contact point at all. B12_CATALOGUE.md's own arm-flare
  measurement table backs this up from the other direction: the current procedural `run.glb` already has
  the *lowest* arm flare of the three candidates (12.0/13.0 deg vs KayKit's 42/73 and UAL's 40/44), so
  flare is not why the owner calls it unnatural -- something else is (see 1.3/1.4).
- All procedural player clips (`run`, `sword_attack`, `sword_ready`, `sword_block`, `spear_ready`,
  `spear_thrust`, `bow_ready`, `bow_draw`, `cast`, plus the base-tier `idle`/`walk`/`sprint`/`crouch_idle`/
  `crouch_walk` used before `clips=ext` overrides them) are generated by
  `tools/asset_pipeline/_make_creature_motion.py --plan person` + `_blender_anim_creature.py`: a scripted
  pose synthesizer ("arms measured from hanging, blows and guards on SkinnedFigure's phase map"), not mocap
  and not hand-keyframed. It is deterministic and clean (no foot slide, no self-intersection by
  construction) but has none of mocap's weight transfer, spinal counter-rotation, or contact-timing
  irregularity -- exactly the kind of motion a human eye reads as synthetic/unnatural even when every joint
  angle is individually plausible.
- **Foot IK** exists and is wired into real gameplay via `BodyModifiers.PlantFeet`
  (`BodyModifiers.cs:16-45, 138-203`), a `TwoBoneIK3D` per leg through a custom `FootPlanting` skeleton
  modifier, applied to the player at `Main.cs:404-405`, gated by `VisualOptions.BodyModifiers`
  (`VisualOptions.cs:75`) which is **on** in every named quality tier (low/medium/high/ultra,
  `VisualOptions.cs:66-69`) -- it fades to 0 while airborne (`BodyModifiers.cs:176-179`).
- **Root motion is deliberately not used**: no reference anywhere in `src/Presentation`.
  `SkinnedFigure.Pose()` sets `Position = feet` directly from the simulation every frame
  (SkinnedFigure.cs:174); clips only ever pose bones. `B12_CATALOGUE.md:19-22` documents that every shipped
  locomotion loop carries zero native root-node motion by design; the one place real root motion is kept on
  purpose is `ext_dodge` (a genuine root dash, explicitly flagged and kept).

### 1.3 The gameplay-side timing the animation must follow (Domain/Application, read-only)

Tick rate is 20/s = 50 ms/tick (`content/config/time.yaml:6`). `CombatPhase` = `Idle, Windup, Active,
Recovery, Dodge, Staggered` (`src/World/Runtime/Combat.cs:114-122`); Domain's own doc comment is explicit
that timing is the authority and presentation must scale its clip to it, never the reverse
(`src/Domain/Combat/Combat.cs:48-51`).

| weapon | windup | active | recovery | source |
|---|---|---|---|---|
| Rusted sword (attack_speed 1.43, 0.7 s/swing) | 6 ticks / 0.30 s (40%) | 3 ticks / 0.15 s (20%) | 5 ticks / 0.25 s (40%) | `content/items/weapon/rusted_sword.yaml`; `Combat.cs:179` |
| March spear (attack_speed 1.0) | 8 ticks / 0.4 s | 4 ticks / 0.2 s | 8 ticks / 0.4 s | `content/items/weapon/march_spear.yaml` |
| Unarmed | 5 ticks / 0.25 s | 2 ticks / 0.10 s | 5 ticks / 0.25 s | `Domain/Combat.cs:138` (hardcoded) |
| Hunting bow (draw_time 0.9 s) | 18 ticks / 0.9 s (draw) | 1 tick (instant release) | 6 ticks / 0.3 s | `content/items/weapon/hunting_bow.yaml`; `Domain/Combat.cs:134`; `content/config/damage_constants.yaml` |
| Cast (Brace Ward / Impulse Bolt / Mending Thread) | 0.4 s / 0.6 s / 1.0 s | 1 tick (release) | 0.3 s (all three) | `content/spells/**/*.yaml`; `Magic.cs:22-23` |

Other timings: dodge = 5 ticks (0.25 s) full invulnerability then 5 ticks (0.25 s) recovery, startable only
from `Idle`/`Windup` (`Domain/Combat.cs:121-122`; `World/Runtime/Combat.cs:378`); stagger = 12 ticks (0.6 s)
then a 40-tick (2.0 s) immunity grace, explicitly "never a stun-lock" (`Domain/Combat.cs:115-116, 201`);
block mitigates 70% of physical damage inside a 120-degree frontal arc, no timing window of its own
(`Domain/Combat.cs:117, 119`); there is no parry.

**No combo concept exists in gameplay at all.** `grep -i combo` across `src/` returns nothing. Each weapon
resolves to exactly one `AttackProfile` (`World/Runtime/Combat.cs:410-421`), and `Busy()`
(`Combat.cs:401-407`) refuses a second `AttackCommand` in any phase but `Idle` -- there is no input-buffer
window and no combo-step state to hang a second/third attack pose off of. The already-retargeted
`ext_sword_attack_1`/`ext_sword_attack_2` clip records exist on disk but nothing in `src/**/*.cs` references
them by name. **Consequence for the fix**: giving the player 3+ distinct sword swings does not require a
gameplay combo system -- it can be done entirely in presentation, by having `SkinnedFigure` pick among 3
bound clip IDs (round-robin or random) each time a fresh `Windup` begins, with zero Domain/Application
changes. A true combo chain (each swing gated on a follow-up input timing window) would additionally need a
new Domain-side attack-index/combo-step field, which is out of this audit's scope to design but is worth
flagging as the larger version of the same fix.

Bow has no charge/hold mechanic: `Active` is always exactly 1 tick and release is unconditional
(`Combat.cs:484-497`) -- there is no player-controlled "hold at full draw" phase to animate today.

### 1.4 Concrete causes of "unnatural" / "snags" (summary)

1. **Sword attack is a single hardcoded clip id, no variant** -- SkinnedFigure.cs:220-227. Root cause of
   "one repeated swing."
2. **Standing-with-weapon-drawn has no ready pose** -- `"sword_ready"` is bound but never requested;
   `bow_ready`/`spear_ready` only fire while actively holding the guard input. A weapon-drawn idle player
   shows the plain unarmed idle. Likely root cause of "stance is unnatural." (SkinnedFigure.cs:238-246, 284)
3. **No hysteresis on gait thresholds** -- exact 2.2/4.2 m/s cutoffs with no deadband
   (SkinnedFigure.cs:275-280) can flip walk/run/sprint back and forth while accelerating/decelerating near a
   boundary, each flip re-triggering a fresh 0.2 s blend from a partial pose -- a plausible "snag/stick"
   mechanism.
4. **Gait playback-speed clamp** -- `Mathf.Clamp(speed / pace, 0.6, 1.6)` (SkinnedFigure.cs:267, 281) means
   outside that band stride length is wrong for the travel speed (visible foot-slide), and every authored
   pace for the currently-bound `ext_*_ual` clips is already below the game's canonical table (walk 0.96 vs
   1.4 target, run n/a -- still procedural, sprint 2.92 vs 5.0 target) -- see the per-clip pace table in
   B12_CATALOGUE.md. Binding `run`/`sprint` without correcting for this leaves them permanently in the
   slowed/sped 0.6-1.6x band rather than at 1.0x.
5. **Combat pre-empts crouch** -- an attack always plays the standing clip even while `_crouched` is true
   (SkinnedFigure.cs:220-232 evaluated before 264); only the procedural bend is layered on, producing a
   standing-height swing from a crouched stance.
6. **Run is procedural for a reason that does not apply to it** -- see 1.2. Its own arm flare is already
   the best of the three candidates, so binding external mocap for flare alone will not fix "looks
   unnatural"; the more likely fix is adopting real mocap for its secondary motion (weight shift, spinal
   counter-rotation) that a scripted pose cannot produce, combined with fixing #3/#4 above.
7. **A large amount of already-finished, already-QA'd external motion is simply not wired in** -- see the
   per-state table below. This is not a "snag" cause but is the single highest-leverage fix available: most
   of the remaining player states can move from PROCEDURAL to EXTERNAL MOTION USED with binding-table and
   `SkinnedFigure.cs` branch additions alone, no new sourcing or retargeting.

## 2. Local depot: every pack, every clip

Depot: `F:\Otherreach_External_Assets\animations\`. All five packs' `provenance.json`/`LICENSE.txt` were
read directly; clip names were extracted by parsing each GLB's JSON chunk (bytes 12-16 = chunk length
little-endian, JSON starts at byte 20, `animations[].name`), and by listing FBX filenames for the two
FBX-only packs (binary FBX clip names were not extracted -- no FBX parser available without installing
software; see the rancidmilk note below).

| pack | id | license (verbatim, own LICENSE.txt) | format | rig | clip count |
|---|---|---|---|---|---|
| KayKit Character Animations Free 1.1 | `kaylousberg__kaykit_character_animations` | CC0 1.0 Universal ("free to use in personal, educational and commercial projects... not mandatory" to credit) | GLB + FBX | Rig_Medium: 23 joints, no fingers, T-pose. Rig_Large: scaled variant | 139 across 7 category files (Rig_Medium) |
| Kenney Animated Characters 3 | `kenney__animated_characters_3` | CC0 1.0 Universal ("personal, educational, and commercial purposes... not a requirement" to credit) | FBX | Mixamo-style, partial fingers (index+thumb only), has IK helper bones (`HipsCtrl`, `LeftFootIK`, `LeftFootRollCtrl`) | 3 (`idle`, `jump`, `run`) |
| Quaternius Universal Animation Library (UAL1) Standard | `quaternius__universal_animation_library` | CC0 1.0 Universal (Public Domain Dedication) | GLB + FBX | 65-joint UE-mannequin style, full fingers, T-pose | 43 |
| Quaternius Universal Animation Library 2 (UAL2) Standard | `quaternius__universal_animation_library_2` | CC0 1.0 Universal (Public Domain Dedication) | GLB + FBX | same 65-joint skeleton as UAL1 (compatible retarget target) | 43 |
| RancidMilk Free Character Animations (`Anims_Only_FBX_V1`) | `rancidmilk__free_character_animations` | Custom permissive, chained from CMU: *"Free to use/modify/redistribute so long as you don't sell the animations themselves... may include this data in commercially-sold products, but you may not resell this data directly, even in converted form"* (LICENSE.txt, quoting CMU FAQ) | FBX only | Biovision/Poser-style (`lShldr`/`lForeArm`/`lThigh`/`lButtock`... + mirrored), a fourth, distinct bone-naming convention from all other packs surveyed | 2550 individual clips, named `NN_NN.fbx` (CMU subject_trial numbering, e.g. `01_01`..`104_49`) |

Full clip-name lists for the four GLB-bearing packs (all names extracted and verified this session):

- **KayKit Rig_Medium.glb files**: `General` (15: Death_A/A_Pose, Death_B/B_Pose, Hit_A, Hit_B, Idle_A,
  Idle_B, Interact, PickUp, Spawn_Air, Spawn_Ground, Throw, Use_Item); `MovementBasic` (11: Jump_Full_Long,
  Jump_Full_Short, Jump_Idle, Jump_Land, Jump_Start, Running_A, Running_B, Walking_A/B/C);
  `MovementAdvanced` (13: Crawling, Crouching, Dodge_Backward/Forward/Left/Right, Running_HoldingBow,
  Running_HoldingRifle, Running_Strafe_Left/Right, Sneaking, Walking_Backwards); `CombatMelee` (22:
  Melee_1H_Attack_Chop/Jump_Chop/Slice_Diagonal/Slice_Horizontal/Stab, Melee_2H_Attack_Chop/Slice/Spin/
  Spinning/Stab, Melee_2H_Idle, Melee_Block, Melee_Block_Attack, Melee_Block_Hit, Melee_Blocking,
  Melee_Dualwield_Attack_Chop/Slice/Stab, Melee_Unarmed_Attack_Kick/Punch_A, Melee_Unarmed_Idle);
  `CombatRanged` (20: Ranged_1H/2H_Aiming/Reload/Shoot/Shooting, Ranged_Bow_Aiming_Idle, Ranged_Bow_Draw,
  Ranged_Bow_Draw_Up, Ranged_Bow_Idle, Ranged_Bow_Release, Ranged_Bow_Release_Up, Ranged_Magic_Raise/Shoot/
  Spellcasting/Spellcasting_Long/Summon); `Simulation` (14: Cheering, Lie_Down/Idle/StandUp, Push_Ups,
  Sit_Chair_Down/Idle/StandUp, Sit_Floor_Down/Idle/StandUp, Sit_Ups, Waving); `Special` (15: skeleton
  enemy-set clips, not player-relevant); `Tools` (29: Chop/Chopping, Dig/Digging, Fishing_*, Hammer/
  Hammering, Holding_A/B/C, Lockpick/Lockpicking, Pickaxe/Pickaxing, Saw/Sawing, Work_A/B/C, Working_A/B/C).
- **Kenney**: `idle`, `jump`, `run` (3 total).
- **UAL1 Standard.glb** (43): A_TPose, Crouch_Fwd_Loop, Crouch_Idle_Loop, Dance_Loop, Death01, Driving_Loop,
  Fixing_Kneeling, Hit_Chest, Hit_Head, Idle_Loop, Idle_Talking_Loop, Idle_Torch_Loop, Interact,
  Jog_Fwd_Loop, Jump_Land, Jump_Loop, Jump_Start, PickUp_Table, Pistol_Aim_Down/Neutral/Up, Pistol_Idle_Loop,
  Pistol_Reload, Pistol_Shoot, Punch_Cross, Punch_Jab, Push_Loop, Roll, Sitting_Enter/Exit/Idle_Loop/
  Talking_Loop, Spell_Simple_Enter/Exit/Idle_Loop/Shoot, Sprint_Loop, Swim_Fwd_Loop, Swim_Idle_Loop,
  Sword_Attack, Sword_Idle, Walk_Formal_Loop, Walk_Loop. (A `*_RM.glb` sibling is a root-motion-baked
  duplicate of the same 43.)
- **UAL2 Standard.glb** (43): A_TPose, Chest_Open, ClimbUp_1m, Consume, Farm_Harvest/PlantSeed/Watering,
  Hit_Knockback, Idle_FoldArms_Loop, Idle_Lantern_Loop, Idle_No_Loop, Idle_Rail_Call/Loop,
  Idle_Shield_Break/Loop, Idle_TalkingPhone_Loop, LayToIdle, Melee_Hook, Melee_Hook_Rec, NinjaJump_Idle_Loop/
  Land/Start, OverhandThrow, Shield_Dash, Shield_OneShot, Slide_Exit/Loop/Start, Sword_Block, Sword_Dash,
  Sword_Heavy_Combo, Sword_Regular_A/A_Rec/B/B_Rec/C/Combo, TreeChopping_Loop, Walk_Carry_Loop, Yes,
  Zombie_Idle_Loop, Zombie_Scratch, Zombie_Walk_Fwd_Loop.
- **RancidMilk**: 2550 FBX files named by CMU subject_trial (`01_01`..`104_49`, `93_*`, `94_*`, etc.), no
  per-clip descriptive names available without an FBX parser or a cgspeed/CMU subject-index cross-reference.
  Per the pack's own README (quoted in its `provenance.json`), root motion is baked into every clip and the
  author's own note says clips "require at least a little bit of tweaking/work" before use. The existing
  retarget tool (`_retarget_clip.py`) reads GLB only ("sampled from its own GLB with exact glTF semantics");
  this pack would need FBX-to-GLB conversion and a fourth retarget map (a distinct Biovision/Poser bone
  convention) before the pipeline could touch it at all -- non-trivial effort for an unsorted 2550-clip
  archive with no confirmed sword/spear/bow content (CMU's source categories are general human activity:
  locomotion, sports, dance, interaction -- not weapon combat). **Recommendation: leave as a supplementary
  variety reserve, not a near-term source for the gaps below.**

## 3. Per-state candidates and classification

"Bound" = actually reachable through `SkinnedFigure.Animate()` today, at any `--visual clips=` setting.
"Retargeted" = a QA'd `ext_*` clip record already exists under `assets/animation/clips/` but is not bound.

| state | best candidate (pack : clip) | current status | classification |
|---|---|---|---|
| idle | Quaternius UAL1 : `Idle_Loop` (already shipped as `ext_idle_ual`) | **bound**, `clips=ext` default tier | EXTERNAL MOTION USED |
| idle variant (alt) | Quaternius UAL2 : `Idle_FoldArms_Loop` (retargeted as `ext_idle_alt_ual`, 9.9/14 deg flare, best of all candidates) | retargeted, not bound; `idle_alt` is not a state `SkinnedFigure` recognizes | CUSTOM MOTION REQUIRED (integration: add an alt-idle timer/branch + binding; source is finished) |
| start moving | -- (blend from idle into gait) | procedural crossfade only | PROCEDURAL ONLY -- reason: a transition blend between two already-good clips is normal practice; the defect is threshold hysteresis (1.4 #3), not a missing clip |
| walk | Quaternius UAL1 : `Walk_Loop` (`ext_walk_ual`) | **bound** | EXTERNAL MOTION USED (note: authored pace 0.96 m/s vs the game's 1.4 m/s table -- tune, see Sec.4) |
| run | Quaternius UAL1 : `Jog_Fwd_Loop` (retargeted as `ext_run_ual`) | retargeted, **not bound** -- `run` stays procedural even under `clips=ext` | EXTERNAL MOTION ADAPTED, recommend binding (see 1.2/1.4 #6 for why flare alone won't fix "unnatural") |
| sprint | Quaternius UAL1 : `Sprint_Loop` (`ext_sprint_ual`) | **bound** | EXTERNAL MOTION USED |
| stop | -- (blend back to idle) | procedural crossfade only | PROCEDURAL ONLY -- same reasoning as "start moving" |
| turn left / turn right (in place) | none found in any of the 5 local packs (checked by name, confirmed in B12_CATALOGUE.md) or in this session's web research | no state exists in code at all; player yaw is an instant `LerpAngle`, no clip | CUSTOM MOTION REQUIRED -- no commercially-safe source identified anywhere; needs bespoke keyframing or a procedural additive-yaw-lean scheme |
| strafe left / right | KayKit : `Running_Strafe_Left` / `Running_Strafe_Right` (`ext_strafe_left`/`ext_strafe_right`) | retargeted, not bound; `SkinnedFigure.Pose()` takes only scalar forward speed, no lateral signal | CUSTOM MOTION REQUIRED (integration: `PlayerController` must expose a strafe direction; source exists but carries KayKit's stylised 49-70 deg arm flare with no UAL alternative -- residual quality gap even once wired) |
| walk backward | KayKit : `Walking_Backwards` (`ext_walk_back`) | retargeted, not bound, no state exists | CUSTOM MOTION REQUIRED (same integration gap as strafe) |
| crouch idle | Quaternius UAL1 : `Crouch_Idle_Loop` (`ext_crouch_idle_ual`) | **bound** | EXTERNAL MOTION USED |
| crouch move | Quaternius UAL1 : `Crouch_Fwd_Loop` (`ext_crouch_walk_ual`) | **bound** | EXTERNAL MOTION USED |
| jump (start) | Quaternius UAL1 : `Jump_Start` (`ext_jump_start_ual`) | retargeted, not bound; no `jump_start` state in `SkinnedFigure` -- airborne uses only the procedural `_tuck` bend | CUSTOM MOTION REQUIRED (integration: new state keyed off `_airborne`; source finished) |
| fall | Quaternius UAL1 : `Jump_Loop` (airborne hold, `ext_fall_ual`) | retargeted, not bound, no state exists | CUSTOM MOTION REQUIRED (same as jump start) |
| land | Quaternius UAL1 : `Jump_Land` (`ext_land_ual`, clearest one-shot win: 37/74 deg mean/max vs KayKit's 71/97) | retargeted, not bound, no state exists | CUSTOM MOTION REQUIRED (same as jump start) |
| sword ready | KayKit : `Melee_Blocking` (retargeted as `ext_sword_ready`) | retargeted; bound clip id exists in `people.player.clips.sword_ready` but **is never requested by any code path** (orphaned) | CUSTOM MOTION REQUIRED (wire a real "weapon drawn, not guarding" branch distinct from `sword_block`; likely root cause of "stance is unnatural", see 1.4 #2) |
| sword attack (>= 3 variants) | KayKit : `Melee_1H_Attack_Slice_Horizontal` (`ext_sword_attack_1`), `Melee_1H_Attack_Slice_Diagonal` (`ext_sword_attack_2`), plus untouched `Melee_1H_Attack_Chop` / `Melee_1H_Attack_Stab` for a 3rd/4th; or Quaternius UAL2 : `Sword_Regular_A/B/C` + `Sword_Heavy_Combo` (not yet retargeted to the player rig at all) | 2 of 5+ KayKit variants already retargeted; **none bound**; `SkinnedFigure.cs:223` hardcodes a single clip id | CUSTOM MOTION REQUIRED (root cause of "one repeated swing"; presentation-only round-robin/random pick among 3 bound ids is sufficient, see 1.3) |
| sword recovery | (tail of whichever attack clip plays, scrubbed 70-100%) | scrub-based by convention, consistent with how `ext_sword_attack_1/2` were authored | PROCEDURAL (scrub-driven) -- acceptable methodology; depends on fixing the attack-variant gap above so each variant's own recovery tail is seen |
| sword block | KayKit : `Melee_Block` (`ext_sword_block`) | retargeted, not bound; procedural `sword_block` plays instead | EXTERNAL MOTION ADAPTED, recommend binding |
| hit reaction | KayKit : `Hit_A` (`ext_hit`, light) / `Hit_B` (`ext_hit_heavy`, heavy) | `ext_hit` **bound**; `ext_hit_heavy` retargeted but never requested (`SkinnedFigure.cs` only ever plays `"hit"`) | EXTERNAL MOTION USED for the light hit; CUSTOM MOTION REQUIRED to wire severity-based selection for the heavy variant |
| spear ready | KayKit : `Melee_2H_Idle` (`ext_spear_ready`) | retargeted, not bound | EXTERNAL MOTION ADAPTED, recommend binding |
| spear thrust | KayKit : `Melee_2H_Attack_Stab` (`ext_spear_thrust`) -- a two-handed sword stab repurposed as a spear thrust, not an authored polearm motion | retargeted, not bound | EXTERNAL MOTION ADAPTED (moderate fidelity: no true long-weapon leverage/grip spacing). True spear/polearm mocap remains an open market gap -- see Sec.5 |
| spear recovery | (tail of `ext_spear_thrust`, scrubbed) | scrub-based | PROCEDURAL (scrub-driven), same reasoning as sword recovery |
| bow ready | KayKit : `Ranged_Bow_Idle` (`ext_bow_ready`) | retargeted, not bound | EXTERNAL MOTION ADAPTED, recommend binding |
| bow draw | KayKit : `Ranged_Bow_Draw` (`ext_bow_draw`) | retargeted, not bound | EXTERNAL MOTION ADAPTED, recommend binding |
| bow hold (full draw) | KayKit : `Ranged_Bow_Aiming_Idle` (a genuine held-at-full-draw pose) -- **not yet retargeted at all** | no clip record exists; no gameplay charge/hold phase exists either (bow's `Active` phase is always 1 tick, automatic release, `Combat.cs:484-497`) | PROCEDURAL ONLY FOR A SPECIFIC REASON -- there is no variable-hold gameplay state to visualize today; if a charged-shot mechanic is ever added, retarget `Ranged_Bow_Aiming_Idle` first (CC0, already identified, zero retarget cost) |
| bow release | tail of `ext_bow_draw` today; KayKit also has a **dedicated** `Ranged_Bow_Release` (distinct recoil/relax pose) -- not yet retargeted | no clip record exists for a dedicated release pose | EXTERNAL MOTION AVAILABLE, NOT YET ADAPTED -- recommend retargeting `Ranged_Bow_Release` rather than relying on the draw clip's own tail, since a real release's hand-snap-back form differs from a linear draw scrub |
| cast | KayKit : `Ranged_Magic_Shoot` (`ext_cast`, one-shot) + `Ranged_Magic_Spellcasting` (`ext_cast_channel`, loop) | both retargeted, neither bound | EXTERNAL MOTION ADAPTED, recommend binding |

## 4. Recommendations for transitions (crossfade, sync, foot IK)

1. **Bind what is already finished first.** `run`, `sword_block`, `spear_ready`, `spear_thrust`,
   `bow_ready`, `bow_draw`, `cast`, `cast_channel` are QA'd and sitting unused; add them to
   `people_clips_by_visual_option`'s `clips=ext.player.clips` (and mirror onto `people=production` where a
   distinct production-rig retarget doesn't already exist) before doing any new sourcing work. This is the
   single highest-value, lowest-risk change available.
2. **Add hysteresis to the gait thresholds.** Replace the bare `> 4.2` / `> 2.2` comparisons
   (SkinnedFigure.cs:275-280) with separate enter/exit speeds (e.g. enter run at 2.4, drop to walk only
   below 2.0) so normal speed noise while accelerating/decelerating cannot flip the gait state every frame.
3. **Give the pace-clamped states real target paces**, or re-author the clamp. `ext_walk_ual` (0.96 m/s) and
   `ext_sprint_ual` (2.92 m/s) both sit well off the game's 1.4/5.0 m/s canonical speeds; today the
   `Mathf.Clamp(speed / pace, 0.6, 1.6)` masks this by playing the clip 1.4-1.6x too fast at normal
   movement speed, which reads as a hurried, slightly-wrong-looking gait even before any other fix. Prefer
   binding `Pace` per state from the clip's own measured value (already recorded per clip in
   `art_bindings.json`'s `paces` block) and tightening the clamp range once paces are closer to 1.0x.
4. **Fix the sword-attack variant selection before anything else in combat.** Bind 3 clip ids
   (`sword_attack_1/2/3`, from `ext_sword_attack_1`, `ext_sword_attack_2`, and a third KayKit
   `Melee_1H_Attack_*` clip retargeted the same way), and in `SkinnedFigure.Animate()` pick among them
   (round-robin is simplest and needs no Domain change) each time `CombatPhase` transitions into `Windup`.
   Do the same for spear/bow if/when more than one source clip exists for those weapons.
5. **Give a weapon-drawn idle its own pose.** Add a branch that plays `sword_ready`/`bow_ready`/
   `spear_ready` whenever a weapon is held and the player is not attacking, guarding, or moving fast enough
   to be in a locomotion gait -- not gated behind the `Guarding` input the way it is today. This is the
   direct fix for "stance is unnatural."
6. **Crossfade times**: the existing values (0.1-0.3 s across the file) are reasonable defaults and do not
   need a wholesale change; the two exceptions worth tuning are (a) the attack-entry blend
   (`SkinnedModel.Hold`'s default 0.1 s, SkinnedModel.cs:88) when entering an attack directly from a fast
   gait -- consider a slightly longer blend (0.15-0.2 s) specifically for locomotion-to-combat transitions,
   and (b) removing the crouch/attack conflict (1.4 #5) by authoring or binding a crouched-attack variant
   rather than relying on the procedural bend to paper over a standing-height swing.
7. **Foot IK is already correct and does not need changes.** `BodyModifiers.PlantFeet` is on by default in
   every real quality tier and already fades out while airborne; once jump/fall/land get real clips (item 1
   above), confirm the fade-out timing still matches the new clips' own takeoff/landing contact frames.
8. **Gait phase sync**: none of the newly-bound-recommended locomotion clips carry root motion (confirmed
   zero native root travel in B12_CATALOGUE.md for every KayKit and UAL1/2 locomotion loop used here), so
   the existing `Position = feet`-from-simulation approach continues to work unchanged; no foot-phase
   resync work is needed beyond the pace corrections in item 3.

## 5. Web research: commercially-safe sources for the remaining gaps

Explicitly excluded per project policy (not evaluated further): Mixamo, LAFAN1, Bandai Namco Research
motion dataset, Truebones, Rokoko.

- **Quaternius UAL**: no UAL3/newer release exists as of this research; only UAL1 and UAL2 (both already in
  the local depot). UAL1's most recent devlog update (2026-06-16) only added root motion to existing clips.
  License confirmed on both itch.io pages: *"Free to use in personal, educational and commercial projects.
  (CC0 License)"* -- matches the local `LICENSE.txt` files exactly. UAL2 has real sword-combo content
  (`Sword_Regular_A/B/C` + recoveries, `Sword_Heavy_Combo`) but **no bow and no spear/polearm animation in
  either pack** -- confirmed gap.
- **CMU Graphics Lab Motion Capture Database**: CMU's own stated position (per the FAQ text already quoted
  verbatim in the local rancidmilk `LICENSE.txt`) is free for any use including commercial products, no
  resale of the raw data itself. Known third-party FBX/BVH conversions exist
  (`github.com/una-dinosauria/cmu-mocap`, `github.com/keijiro/CMUMocap`, an academictorrents FBX mirror, a
  Hugging Face dataset mirror) -- **each conversion is a separate derivative work and should have its own
  README/license checked before use**; this was not exhaustively verified per-repo. CMU's source categories
  are general human activity (locomotion, sports, dance, interaction), not weapon combat, so this family is
  a poor fit for the sword/spear/bow gaps regardless of licensing.
- **KayKit (Kay Lousberg)**: beyond the local Character Animations pack, a paid "Character Pack:
  Adventurers" (rigged characters + weapon props) and a separate animation-only page
  (`kaylousberg.itch.io/kaykit-animations`) exist, listing an "Attack-Combo" clip and a "Shoot (2h) Bow"
  clip not present in the locally-downloaded set -- both under the same CC0 terms ("Free for personal and
  commercial use, no attribution required (CC0)"). Worth a follow-up download if the local pack's variants
  prove insufficient. No dedicated spear/polearm clip found on this page either.
- **Kenney.nl**: standard site-wide CC0. A "Modular Characters" pack exists as a sibling to the
  locally-downloaded "Animated Characters 3," but its animation list could not be confirmed in this
  research pass (UNCLEAR) -- Kenney's animation packs generally skew toward simple locomotion, not combat
  combo systems, so this family is not expected to close the sword/spear/bow gaps.
- **OpenGameArt.org**: mostly weapon-prop animations (e.g. "Animated Longbow," a draw-fire *object*
  animation, CC0) rather than full character mocap; also mirrors Quaternius UAL1/2 (same license, not a new
  source). No OGA-native asset was found covering full sword combos, spear mocap, or a character bow
  draw-hold-release cycle.
- **Other CC0/attribution-only finds**: `maxparata.itch.io/cc0-animations` (confirmed CC0, but only 5 clips
  -- Idle/Run/Attack-Sword/Hit/Death -- too sparse to close gaps); `gintoki1234.itch.io/10-bows-and-cross-bows`
  (confirmed CC BY 4.0, but it is an animated *weapon prop*, not a character bow-draw cycle, so it does not
  close the character-animation gap by itself). `mocaponline.itch.io/free-3d-sword-animation-pack` claims
  original (non-Mixamo) mocap but its page did not show explicit license text -- **mark UNCLEAR**, verify
  before use.
- **Reallusion ActorCore**: general Content EULA (`reallusion.com/Content/EULA/EULA.htm`) explicitly permits
  commercial game use, but bars redistributing raw/importable asset files to end users and requires
  retaining embedded copyright/vendor metadata -- **safe as a bake-in source for a shipped game, not safe to
  ship as loose extractable FBX**. A dedicated "Spear Animations Pack" exists in Reallusion's content store
  under this same EULA -- the most concrete **paid** lead found for the spear gap. Free-tier specifics were
  not independently confirmed (UNCLEAR, recommend a direct check before relying on a specific free clip).
- **Synty Studios**: sells a dedicated "ANIMATION | Sword Combat" pack (105 clips, including 3-part Light
  and Heavy combo chains, block/parry/stagger/death) under a standard EULA that permits commercial game
  creation and bars standalone resale of raw assets (`syntystore.com/pages/standard-subscription-licence`)
  -- **safe as a bake-in source, not for raw redistribution**, and the strongest *paid* candidate found for
  closing the sword-combo-variety gap beyond what KayKit/UAL2 already provide locally.
- **Spear/polearm specifically**: no clean CC0 or attribution-only character-animation source was found
  anywhere in this research. The two live leads are both paid, EULA-bound marketplace packs -- Reallusion's
  "Spear Animations Pack" and an Unreal/Fab "Spear and Shield Animation Set" (`fab.com/listings/...`,
  license not independently fetched this session, mark UNCLEAR) -- confirming the local finding that spear
  coverage is thin everywhere and the existing `ext_spear_thrust` (a repurposed two-handed sword stab) is
  the best currently-available option without a purchase.
- **Bow draw/hold/release specifically**: fully covered by the already-downloaded KayKit pack
  (`Ranged_Bow_Idle`, `Ranged_Bow_Draw`, `Ranged_Bow_Aiming_Idle`, `Ranged_Bow_Release`, plus `_Up` variants)
  -- no new source needed; only retargeting/binding work remains (Sec.3).

## 6. Files read for this audit

- `F:\Otherreach_External_Assets\animations\*\provenance.json`, `LICENSE.txt` (all 5 packs)
- `F:\Otherreach_External_Assets\EXTERNAL_ASSET_CATALOG.md`, `RECOMMENDATIONS.md`, `FINAL_REPORT.md`,
  `_research\animation.json`
- `G:\UNNAMED_PHASEB\assets\animation\review\retarget\B12_CATALOGUE.md`
- `G:\UNNAMED_PHASEB\tools\asset_pipeline\_retarget_clip.py` (header/docstring),
  `retarget_maps\{kaykit_rig_medium,quaternius_ual,quaternius_ual_prod}.json`
- `G:\UNNAMED_PHASEB\assets\animation\clips\anim.player.veth_wanderer.*.json` (all `ext_*` and non-`ext_*`
  player clip records)
- `G:\UNNAMED_PHASEB\src\Presentation\Art\{SkinnedFigure.cs, SkinnedModel.cs, ArtBindings.cs,
  AnimationSheet.cs, KitSheet.cs, PostureModifier.cs, BodyModifiers.cs, SkinnedCreature.cs}`,
  `art_bindings.json`, `VisualOptions.cs`
- `G:\UNNAMED_PHASEB\src\Presentation\{Main.cs, Greybox\NpcsView.cs, Player\Figure.cs, Player\Avatar.cs}`
- `G:\UNNAMED_PHASEB\src\World\Runtime\Combat.cs`, `src\Domain\Combat\Combat.cs`, `src\Domain\Items\Items.cs`,
  `src\Domain\Magic\Magic.cs`, `src\Application\PlayerMotion.cs`
- `content\config\{damage_constants.yaml, magic.yaml, time.yaml}`,
  `content\items\weapon\{rusted_sword,march_spear,hunting_bow}.yaml`, `content\spells\**\*.yaml`
