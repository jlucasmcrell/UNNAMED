# Phase B Remediation - Owner Checkpoint

Date 2026-09-26. Branch `claude/phase-b-visual-overhaul` (worktree `G:/UNNAMED_PHASEB`). **Not merged; not a "final report".**
Stopped here for the owner's review, as the remediation brief directs. Every result is reported by the gates of
[ACCEPTANCE_CONTRACT.md](ACCEPTANCE_CONTRACT.md): G1 exists, G2 loads, G3 bound, G4 selected by normal play, G5 displayed correctly
in normal play (video), G6 accepted by the owner or blind QA. G6 is always the owner's; blind reviews are advisory.

"Showcase" videos below are real-time captures of the game's own selectors driven by scripted input (Movie Maker at 60 fps with the
20 Hz simulation, so frames and ticks interleave as in play) with a frame-by-frame state log (`--state-log`). They are normal
gameplay code paths, not offline sheets; a hand-played session by the owner is still the final word.

## 1. Baseline

Rejected build: code commit `22af661`, report commit `dd5283c`, tag `phaseB-owner-review-rejected-baseline` (-> `dd5283c`). The Codex
forensic audit of that build (`G:/UNNAMED_HISTORY/CODEX_PHASEB_FORENSIC_AUDIT_2026-09-26/`) is the BEFORE evidence and is untouched.

## 2. Commits in this remediation

`2dd223c` character standard, facial bakeoff, acceptance contract; `9569e4f` grass blades, Mending recipe, selector work (WIP);
this checkpoint's commit (locomotion/sword bindings, gait sampling, fold echo, HUD candidate style, docs). No history rewritten.

## 3. Acceptance contract

Frozen first: [ACCEPTANCE_CONTRACT.md](ACCEPTANCE_CONTRACT.md) - six gates, clay-first geometry, required gameplay video list.

## 4. The Codex register, answered

| ID | Finding | Status | Evidence (gate) |
|---|---|---|---|
| H01 | Character neck/collar geometry | **SUPERSEDED BY NEW PIPELINE - not yet in game** | The MPFB-based standard has a clean, continuous neck by construction (clay: `characters/face_wrap_candidate/`, `characters/standard/`); blind reviewers still rate its faces weaker than the rejected ones (section 9). The in-game characters are unchanged. |
| H02 | Animated Armour shattered | **NOT FIXED** | A TRELLIS.2 replacement from the existing concept hung the local ComfyUI in mesh processing and wrote no mesh (section 20). |
| H03 | Locomotion state coverage | **PARTIAL** | Run now selects CMU mocap (`run_cmu3517`), sprint `sprint_cmu12707`, jump start / fall / land are selected (G4: `animation/rem1_locomotion_states.csv`; G5 video `animation/rem1_locomotion.mp4`). Blind review (13) preferred the baseline clip overall, citing the companion crowding the camera, crouch-walk clipping on a slope and a floaty jump. |
| H04 | Gait sampling / rates | **PARTIAL** | NPC speed now comes from the simulation's per-tick steps and the drawn position is interpolated (`Greybox/GaitSpeed.cs`); the player's tick-wrap no longer substitutes the intended gait speed. Tavar's state log over 45 s of following: 22 state changes, no run-idle-idle-run flicker (every state held 20+ frames). Rate range widened to 0.6-2.2 with measured paces; the walk still plays at ~2x on the rejected player's short-legged rig. |
| H05 | Sword state / variety | **PARTIAL** | G4: `sword_ready` at rest, attacks cycle through four distinct clips (UAL1 Sword_Attack, UAL2 Regular A/B/C) with measured phase marks, block held, return to ready and to running (`animation/rem1_sword_states.csv`). G5 video `animation/rem1_sword.mp4`. Blind review (14) preferred it to the baseline ("better momentum and follow-through"), still prototype: foot slide, stiff off-hand. |
| H06 | Mending Thread | **PARTIAL** | Mending no longer uses the camera-facing quad (the audit's mechanism): it is recipe particles on the body (ring emission, orbit, local coordinates). Captured in normal play at 3.6 m, 1.8 m and a steep -0.75 pitch while active (`vfx/mending_views_rem1.jpg`, `vfx/rem1_spells.mp4`, camera per frame in `vfx/rem1_spells_states.csv`): no top/bottom cut. Its look is modest; the Brace Ward is still a flat billboard ring. |
| H07 | Old crossed-card grass | **FIXED (Medium/High/Ultra)** | The procedural card grass is now `plants=classic` only; four generated geometry blade clumps replace it (`_procgen_grass_blades.py`). Stills `vegetation/near_ground_before_after.jpg`; both blind reviewers prefer it (15). Low tier keeps classic by design. Chunk-level plant LOD (8 m) unchanged. |
| H08 | Acceptance-gate weakness | **PARTIAL** | The six-gate contract and the per-frame state log exist and are used in this report; not yet wired into the automated package gate. |
| M01 | Terrain composition | NOT FIXED | Not reached this checkpoint (the grass change alters near composition only). |
| M02 | Trees | NOT FIXED | Not reached; plan: re-prepare Poly Haven `fir_tree_01` with a real LOD chain (VEGETATION_SOURCE_AUDIT.md). |
| M03 | Tavar presentation | **PARTIAL** | While the fold's barrier stands, Tavar shows the content bible's "slight image doubling" and "delayed shadow" (`Greybox/FoldEcho.cs`); it thins away over 2 s when the heart is steadied. Keyed only to the barrier state; quest logic untouched. Stills `vfx/tavar_fold_double_*.jpg`. The release moment is not yet filmed. |
| M04 | HUD | **PARTIAL - PHASE-B VISUAL PROOF / CANDIDATE STYLE** | Styling only, under `hud=production` (section 21). Not the FE-2 HUD: every element and every piece of information of the current HUD is still shown, in the same screen region and at the same time. Both blind reviewers prefer it (18: Gemini 4/10 vs 2/10, still "prototype"; Pegasus 9 vs 8). `hud/`. |
| M05 | Inventory UX | NOT CHANGED (coordinated) | [../M7_INPUT_COORDINATION.md](../M7_INPUT_COORDINATION.md): request and acceptance handed to M7, the input owner. |

## 5. 3D pipelines researched

[pipeline_research/3D_PIPELINE_RESEARCH.md](pipeline_research/3D_PIPELINE_RESEARCH.md) (TRELLIS.2, Pixal3D, ComfyUI-3D-Pack,
InstantMesh, CRM, TripoSR, SF3D, Hunyuan (benchmark only), MPFB), [HUMAN_BASE_AND_FACIAL.md](pipeline_research/HUMAN_BASE_AND_FACIAL.md),
[FACIAL_EXTERNAL_RESEARCH.md](pipeline_research/FACIAL_EXTERNAL_RESEARCH.md), [FACIAL_LOCAL_AUDIT.md](pipeline_research/FACIAL_LOCAL_AUDIT.md).

## 6. Pipelines benchmarked (the player, same references)

A current production (rejected); B TRELLIS.2; C Pixal3D multi-view; D MPFB hybrid (clean topology + transferred identity); then the
facial bakeoff's MPFB parametric, MPFB + identity texture and the custom head wrap (reviews 01-12).

## 7. License status

All adopted assets and tools are CC0, CC-BY (attribution recorded per garment/hair descriptor), MIT/Apache, or the MakeHuman CC0
system assets (official FAQ copies in `F:/Otherreach_External_3D/mpfb/LICENSE_EVIDENCE/`). CMU mocap via rancidmilk: commercial use
allowed, raw data not resold. Nothing purchased; no trial started. Tables: [FACIAL_PIPELINE_BAKEOFF.md](FACIAL_PIPELINE_BAKEOFF.md)
section 10.

## 8. Winner and why

For **topology, rig, face channels, components, garments and reuse**: the MPFB standard (`charstd`) - three faces from configuration
alone, one performance on all of them, the same 89 named channels in Godot. For **identity**: no winner. The free routes stay
prototype/borderline in blind review; the candidates closest to the concept inherit the rejected heads. This is the brief's STOP
condition "no tested 3D pipeline can materially improve the player" for likeness - reported, not worked around (section 27).

## 9. Player (Checkpoint A)

Not replaced in game. Clean anatomy, neck and hands achieved offline; likeness not. Facial motion path proven offline and in a
Godot proof scene (blink, jaw, visemes, expressions, eye bones). Details: [CHARACTER_FACE_PIPELINE_BAKEOFF.md](CHARACTER_FACE_PIPELINE_BAKEOFF.md).

## 10. Faces and facial motion

[FACIAL_PIPELINE_BAKEOFF.md](FACIAL_PIPELINE_BAKEOFF.md) - the 25-item bakeoff; recommendation HYBRID (open standard + an identity
escalation that needs the owner).

## 11. Visible equipment (Checkpoint B) and the character standard

[CHARACTER_ASSET_STANDARD.md](CHARACTER_ASSET_STANDARD.md). Offline: base outfit (tank, trousers, boots) and an alternate torso item
(hide vest) on the same body and rig, swapped by listing garments, stress-posed (`characters/standard/archetype_*_outfit.jpg`).
Second garment: ~15 minutes, no new code. **Not in game**: equipping `item.armor.hide_vest` does not yet change the player (it needs
the standard player in game, which waits on the identity decision). No schema or save change is needed for it
(presentation map + the authoritative chest slot, as planned).

## 12. Vegetation (Checkpoint C)

H07 above. Performance of the blades (5090, measured earlier this remediation): +1.0 ms in the meadow, +0.5 ms in the Charwood.

## 13. Tree (Checkpoint D)

Not done (M02).

## 14. Ground and horizon

Unchanged from Phase B (preserved per the audit).

## 15. Running (Checkpoint E)

H03/H04 above. Clip paces measured by foot contact on this rig (`charprod2/clip_pace.py`): UAL walk 0.79, CMU run 2.37, CMU sprint
3.47, UAL crouch walk 0.59 m/s against gameplay's 1.6 / 3.2 / 5.12 / 1.6 m/s; gameplay speeds unchanged.

## 16. Sword combat (Checkpoint F)

H05 above. Grip/contact not re-tuned for the new clips.

## 17. Spear, bow, magic

Spear thrust, bow and cast remain the generated clips (G4 selected, e.g. `spear_thrust` in `animation/rem1_locomotion_states.csv`).
Next candidate for spear: KayKit two-hand stab, A/B on the production rig. Tavar still attacks with a sword clip while holding the
spear (not changed).

## 18. Mending Thread (Checkpoint G)

H06 above.

## 19. Tavar / Three Quiet Stones (Checkpoint H)

M03 above.

## 20. Animated Armour (Checkpoint I)

H02: the rejected mesh is shattered in its unrigged ready stage (audit). A TRELLIS.2 generation from the existing concept
(`assets/concepts/creature_animated_armour.png`) was run on the local ComfyUI: sampling finished, then the server hung in the
mesh-processing stage of the 13.2 M-face raw mesh (no CPU or GPU activity, HTTP unresponsive for 25 minutes; the server was
restarted) and no mesh was written. The next attempt needs a lower sparse-structure resolution or a decimation before remeshing.
Rigging (weights transferred from the old skin by proximity to keep its 20-bone clips), clay review and a gameplay capture remain.
**H02 NOT FIXED.**

## 21. HUD (Checkpoint J) - PHASE-B VISUAL PROOF / CANDIDATE STYLE

**This is a candidate visual style, not the final FE-2 HUD implementation.** Scope follows the owner's HUD clarification:
Phase B owns look (type, spacing, panels, bars, icons, opacity, hierarchy); the Player Journey design owns the information
architecture (which information is always shown, contextual or menu-only; HUD tiers and modes; the notification queue; the
hotbar), and FE-2 implements it. The design package was read as reference only (`09_UI_HUD_INFORMATION_ARCHITECTURE.md`,
`01` section 6.8, `13`); the style follows its stated direction - restrained and legible, dark translucent panels, clear
sans-serif type, a thin warm rule line, no parchment, neon or ornamental frames.

What the style does (visual option `hud=production`, on in every Phase B tier; `hud=classic` is the Phase-A HUD, unchanged):

- the status text sits in a dark translucent panel with a thin warm rule; its first line (name, level, XP) is the title, its XP
  also drawn as a thin bar, its other two lines smaller and muted - the same text, word for word;
- the four pools are slim framed bars of one width, the health bar tallest, each with its current value at its end;
- the formula icons are framed slots with their key, in their classic place above the pools; the formula names, costs and the
  casting line are kept, a size down;
- the interaction prompt and the combat log sit on soft dark gradient bands (no frame), the log's band only as tall as its lines;
  the log keeps all seven lines at a constant 85 % opacity (nothing fades or is dropped);
- the tracker, toasts, target bar and death panel take the same colours and panel.

Behaviour: none changed. The HUD receives the same calls from `Main.cs` as the classic HUD (an earlier draft's extra name-plate
call was removed); `Hud.SetStatus` lays out the same string it always received. The only logic in the style is presentational: the
prompt hides its band when it has no words. Code: `Ui/Hud.cs` (`Production`, `Panel`, `Band`, `Frame`).

Withdrawn from the earlier draft (review 16) because they changed what is shown rather than how: hiding the status text, dropping
the formula text, fading the log, a bottom-centre hotbar. The captures also showed that hotbar colliding with the dialogue panel.

Evidence (scripted `--ui-shots` run, 1920x1080, the same moments in both styles), `hud/`:

| Moment | Before (classic) | After (candidate style) |
|---|---|---|
| Exploration | `before_exploration.jpg` | `after_exploration.jpg` |
| Combat (target, log, pools in use) | `before_combat.jpg` | `after_combat.jpg` |
| Interaction prompt (after a kill, casting) | `before_interaction.jpg` | `after_interaction.jpg` |
| Tracked objective (with toasts, in a conversation) | `before_objective.jpg` | `after_objective.jpg` |
| Notifications (learned formulas, XP) | `before_notification.jpg` | `after_notification.jpg` |

Blind reviews: 17 (before the prompt/log bands) and 18 (final) - both reviewers rank the candidate style first both times.
Gemini (18): HUD professionalism 4/10 vs 2/10, both "prototype"; it credits the slimmer bars with values and the backed status
panel, and wants a bespoke type pair and grounded floating text. Its strongest request - take armour and carrying weight off the
permanent HUD - is information architecture and is left to FE-2. Pegasus (18): 9 vs 8, shallow as usual. One Gemini claim (the
classic status text "cut off" at the left) is wrong: the text is complete, only low in contrast against the sky.

Not done here (FE-2 or later): tiers/modes, a notification queue, a hotbar, text scale at 200 %, a sourced typeface (the
engine's default sans-serif is kept; a font would be a new licensed asset), icon redraws.

## 22. Independent reviews (Checkpoint K)

Raw Gemini (gemini-3.8-flash) and Pegasus 1.5 outputs with prompts, inputs and private keys: `external_reviews/00-18`. Pegasus's
answers are consistently shallow (rubric-like praise, rarely a named defect) - kept, weighted low.

| Review | Subject | Gemini | Pegasus |
|---|---|---|---|
| 01/02 | player pipelines (studio sheets) | rejected production best | mixed |
| 03-12 | faces, motion, performance reuse | rejected / wrap best on likeness; standard faces prototype-borderline | similar ranking, shallow |
| 13 | locomotion video | baseline preferred (camera crowding, crouch clip, jump) | - |
| 14 | sword video | remediation preferred | - |
| 15 | near-ground grass | blades preferred | blades preferred |
| 16 | HUD (withdrawn draft - changed the information shown) | draft preferred (5 vs 3) | draft preferred |
| 17 | HUD candidate style, before bands | candidate preferred (4.5 vs 2.5), prototype | candidate preferred (8 vs 7) |
| 18 | HUD candidate style, final | candidate preferred (4 vs 2), prototype | candidate preferred (9 vs 8) |

## 23. Unresolved defects

Character likeness (all routes); the rejected characters still in game (H01); Animated Armour (H02); walk cadence ~2x and
crouch-walk clipping; jump lacks anticipation; companion following crowds the third-person camera; foot slide in sword recovery;
spear/bow/cast clips generated; Brace Ward flat ring; Tavar's release not filmed; trees (M02); terrain composition (M01); plant LOD
chunk granularity; the HUD candidate style is still prototype-grade in review (type, icons, and the information architecture FE-2 owns).

## 24. Performance

Not re-measured on the RAZER. Changes with cost: blade grass (above); the fold echo draws two extra copies of Tavar while he is
held; the HUD style and gait sampling are negligible.

## 25. M7 coordination

[../M7_INPUT_COORDINATION.md](../M7_INPUT_COORDINATION.md). M7 untouched; its worktree not read.

## 26. Assets moved from downloaded to used

[../ASSET_UTILIZATION_REVIEW.md](../ASSET_UTILIZATION_REVIEW.md) - including the EXTERNAL ASSET TIME SAVINGS table.

## 27. Owner decisions and STOP conditions

- **STOP condition met (likeness)**: "no tested 3D pipeline can materially improve the player" - true for identity; false for
  anatomy, neck, hands, facial motion and modular clothing. Decision: accept the MPFB standard with its current likeness, or approve
  an identity escalation.
- **Escalation needing your yes**: the KeenTools FaceBuilder 15-day trial (it requires accepting their license agreement on your
  behalf); or artist sculpt time on MPFB topology for hero faces.
- **Reviewer disagreement**: blind review preferred the baseline locomotion clip; I do not claim locomotion is accepted.
- Not needed: schema/save changes, M7 contract changes, lore changes.
