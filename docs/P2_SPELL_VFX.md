# Playtest 2 spell VFX

Branch `visual/p2-spell-vfx` is based on DS `content/p2-spells` ea6fd978. Presentation only; the formulas, costs, cast time, learning and status effects retain DS's content.

- Cinder Dart: a small amber flame wake with warm embers, a brief fire bloom and falling sparks on impact.
- Shiver Spike: pale ice fragments and cold vapour, impact splinters and a short irregular frost rim at the terrain's actual elevation.
- Holdfast: a brief ivory ground inscription at the caster's feet, with settling grit. It resolves once, rather than glowing for the full 20-second armour effect.
- Hush the Sting: a pale sage bloom at both native hand bones on a completed cast, including a purge that finds no matching ailment.
- Ember Mend: warm amber motes rising around the torso for the actual six-second status effect; emission stops when the effect ends or is removed.
- Clear the Dead Weight: dark, lit flakes and dust released from both native shoulder bones and falling under gravity. Detached world-space particles do not cling to the moving body.

Release effects subscribe to `CastCompleted`; projectile art follows `ShotLoosed` and its resolved path. No resolve tell occurs for an interrupted or fizzled cast. The recipe-only projectile route avoids the old blue sphere fallback. New glyphs have each school's existing polygon and their own physical ink. Violet is reserved for the Other.

All atlases already exist in the VFX pipeline. `P2_SPELL_VFX_SOURCES.json` identifies original atlas hashes and their existing provenance receipts. No generative model was used. The new floor forms use native depth-tested geometry/shaders, with a 1.05-second lifetime. Existing flash-reduction settings still apply.

Validation: public Presentation Release build succeeds, zero errors; MagicContentTests 7/7 pass. Native real-time clips and Claude's verdict are still pending. NOT FINISHED.

