# Playtest 2 spell VFX

Branch `visual/p2-spell-vfx` is based on DS `content/p2-spells` ea6fd978. Presentation only; the formulas, costs, cast time, learning and status effects retain DS's content.

- Cinder Dart: a fine amber streak with a small flame wake and warm embers, a brief fire bloom and falling sparks on impact.
- Shiver Spike: pale ice fragments and cold vapour, impact splinters and a short irregular frost rim at the terrain's actual elevation.
- Holdfast: a brief ivory ground inscription at the caster's feet, with settling grit. It resolves once, rather than glowing for the full 20-second armour effect.
- Hush the Sting: a pale sage bloom at both native hand bones on a completed cast, including a purge that finds no matching ailment.
- Ember Mend: warm amber motes rising around the torso for the actual six-second status effect; emission stops when the effect ends or is removed.
- Clear the Dead Weight: dark, lit flakes and dust released from both native shoulder bones and falling under gravity. Detached world-space particles do not cling to the moving body.

Release effects subscribe to `CastCompleted`; projectile art follows `ShotLoosed` and its resolved path. No resolve tell occurs for an interrupted or fizzled cast. The recipe-only projectile route avoids the old blue sphere fallback. New glyphs have each school's existing polygon and their own physical ink. Violet is reserved for the Other.

All atlases already exist in the VFX pipeline. `P2_SPELL_VFX_SOURCES.json` identifies original atlas hashes and their existing provenance receipts. No generative model was used. The new floor forms use native depth-tested geometry/shaders, with a 1.05-second lifetime. Projectile burst lights retain the existing flash-reduction setting.

Validation: public Presentation Release succeeds, zero errors /23 existing warnings; MagicContentTests7/7 pass. Six normal-cast real-time clips and contact sheets are posted at `C:\Otherreach_External_Assets\PLAYTEST2_SPELL_VFX\20261031\final_review_v1`. `P2_SPELL_VFX_PROOF.json` records the exact runtime commit, per-clip hashes, frame counts, clock checks and scope. Cinder includes the verified warm streak; the other five visuals are unchanged from native07. All7atlases match the pinned release base byte for byte, so no art payload is required.

The native-rig workbench exits0. The older private world host required its watchdog in native07 and aborted after capture in native08; these clips do not claim a clean packaged exit or a performance gate. Nearby low plants were withheld and the HUD hidden for the disclosed presentation fixture. Claude's visual verdict is pending. SOURCE COMPLETE / IN REVIEW, NOT FINISHED.
