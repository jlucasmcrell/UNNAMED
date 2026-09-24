# M3e status - Basic Magic

**Date:** 2026-09-23. **Branch:** `claude/phase1`. **State:** implemented and verified; Phase-1 subset only (the owner's ruling: three tiny representative domains with one formula each, Resonance and Strain, no level-10 attunement).

## The ruling, as applied

- **Three formulas of three domains**, the content bible's (`PHASE1_ASHEN_HOLLOW_PLAYABLE_CONTENT_BIBLE.md` §13): Impulse Bolt (Force), Brace Ward (Warding) and Mending Thread (Vital). They take the place of `PROTOTYPE.md`'s Ember bolt, Ward oakskin and Mend salve and prove the same three things: a damage payload, a timed effect and a healing payload, each a data row.
- **No mana.** A working spends Focus, which returns, and adds Strain, which ebbs. Resonance scales its force. Past the character's tolerance the excess is paid in health: a clear risk, never a lock (`MAGIC_SUPERNATURAL_AND_COSMIC_SYSTEMS.md` "Unsafe casting", "No spell-slot limit").
- **No attunement.** There are no slots, no level threshold and no lockout of known formulas (`PROGRESSION_AXIS_RECONCILIATION.md` §4.6). ROADMAP M3e was rewritten to this scope before any code.

## What M3e built

| Layer | What | Where |
|---|---|---|
| Domain | `FormulaDefinition` (domain skill, complexity, Focus and Strain costs, cast time, targeting, a blow or the effects it puts on and lifts), `MagicConstants`, and `MagicRules` as pure functions. Strain rises above one's skill and falls below it (down to half). Fizzle chance applies only above one's skill. Strain past tolerance is paid in health. Resonance sets a working's force | `src/Domain/Magic/Magic.cs` |
| World | `CastCommand`: the working is the player's action, owned with the rest of their combat state. It spends Focus and starts its tell. At release it takes its Strain, then fizzles or takes hold. A projectile goes through the one damage pipeline; a self formula lifts and puts on effects. A wound in the tell breaks it; a dodge abandons it. Focus and Strain recover after a pause. Reading a book is a learning event. Events: `CastStarted`, `CastCompleted`, `CastFizzled`, `CastInterrupted`, `StrainBacklash`, `TechniqueLearned` | `src/World/Runtime/Magic.cs`, `Combat.cs`, `Items.cs`, `Systems.cs` |
| Content | `config.magic`; the domain skills `skill.force`, `skill.warding`, `skill.vital`; the formulas `spell.force.impulse_bolt`, `spell.warding.brace_ward`, `spell.vital.mending_thread`; `effect.braced`; `item.tome.resonance_primer` (renamed from the Ember Primer), on the longhouse shelf (`container.longhouse_shelf`, `loot.longhouse_shelf`). `MagicContent` builds them and lints them (MAG001). 74 definitions | `content/`, `src/Content/MagicContent.cs` |
| Persistence | No schema change. Focus and Strain are the progression record's pools (schema 4), known formulas are its knowledge record (schema 4), and the ward is a player effect (schema 7) | - |
| Presentation | Focus and Strain bars; the Strain bar turns red once Strained. The formulas on keys 4 to 6, in the order they were learned (no formula ID is named in C#). "Casting ..." while a working runs, and the left hand gathers a glow through the tell. A Read button for books. The log says what each working did, fizzled or lost. Resonance on the status line | `src/Presentation` |

## How casting works

- **Knowing.** A formula is known only through a learning event (`PROGRESSION.md` §4.4). Reading the Resonance Primer teaches all three with no cast; the book is used up, and a book with nothing new in it cannot be read. The starting package stays empty, as the bible's starting character carries no magic.
- **Casting.** Keys 4, 5 and 6 cast the known formulas. A working faces where the camera looks, like a swing. Its Focus is spent at once. Its tell is the cast time (bolt 0.6 s, ward 0.4 s, mending 1.0 s), then it is released and the body recovers for 0.3 s.
- **Strain and Focus.** At a Will of 10 the character has 80 Focus, a tolerance of 40 Strain and a Resonance of 20. A novice's bolt costs 10 Focus and 9 Strain, the ward 12 and 11, the mending 14 and 9. Focus returns at 4 a second from 2 s after the last working; Strain ebbs at 3 a second from 3 s after. So about four workings in a row, then a pause.
- **Past tolerance.** Each point a working pushes past tolerance costs 2 health, and the working still takes hold. From three-quarters of tolerance the HUD says Strained.
- **Skill.** Domain skill is competence (`PROGRESSION.md` §7): a working 1 point of complexity above one's skill costs 3% more Strain and fizzles 3% of the time, and below it costs 3% less, down to half. A novice's bolt, ward and mending fizzle 12%, 9% and 15% of the time; at skill 5 none do. A fizzle still spends its Focus and Strain.
- **What teaches.** A working teaches its domain only when it mattered: a bolt that wounded something (difficulty = the creature's level, like a weapon), or a working cast within 8 s of a blow given or taken (difficulty = its complexity). A formula's first success adds the novelty bonus. Casting at nothing, or mending an unhurt body at peace, teaches nothing.
- **Concentration.** A blow that wounds during the tell breaks the working: its Focus is lost and nothing else happens. A dodge abandons it the same way. Bleeding and venom ticks do not break it.
- **The three workings.** The bolt is 9-13 blunt damage along the facing to 20 m. The ward adds 20 armor to every region for 10 s. The mending gives 18 health over 6 s and stops bleeding and venom.

## Reconciliation

- `ROADMAP.md` M3e rewritten to the owner's scope, with the ratified test in place of "500 casts grant zero school mastery".
- `SYSTEMS.md` S-13 and S-06 carry M3e notes. `DATA_MODEL.md` records the formula fields built, `use.grants` as built, the magic-domain skills and `config.magic`. `PROTOTYPE.md` §4.4 records the formulas replacing its three spells, and that burning and oakskin are not built.
- `VERTICAL_SLICE.md`'s school table carried "Mana" as a resource; a note there now reads it as Focus and Strain. The other mana references the execution prompt listed were already reconciled in M2c.

## Exit criteria (`ROADMAP.md` M3e, as rewritten)

| Criterion | Evidence |
|---|---|
| The three formulas cast from content and do different things | `TheImpulseBolt_StrikesAtRange_AndOnlyAWoundTeachesForce`, `BraceWard_HardensTheBody_ForTenSeconds` (four bites braced do less than four bare, and the ward lapses at 10 s), `MendingThread_Mends_AndStopsTheBleed` (the bleed stops at the release; the mending gives 18) |
| Strain accumulates and recovers, and casting past tolerance costs health | `Strain_BuildsWithEachWorking_AndEbbsAtRest_WhileFocusReturns`, `PastTolerance_AWorkingCostsHealth_NotPermission` (38 + 9 against 40: 14 health, Strain held at 40, the ward still holds), `MagicRulesTests` |
| A wound during the tell breaks the cast | `AWoundInTheTell_BreaksTheWorking_AndADodgeAbandonsIt`: a wolf's bite in a mending's tell breaks it (Focus lost, no Strain, no mending); a dodge abandons it |
| Trivial repeated casting grants no domain skill and no formula; study yields a formula without casting | `FiveHundredTrivialWorkings_TeachNoDomainSkill_AndNoFormula`: 500 mendings at peace teach nothing, learn nothing, claim no novelty. `ThePrimer_TeachesItsThreeFormulas_WithoutASingleCast`: reading teaches three, casts nothing, grants no skill. `AWardCastInTheFight_TeachesWarding_AndTheSameCastInPeaceDoesNot` shows the other side |
| Clear feedback | The windowed ui-shots (below) |

Also proven: Resonance, not Might, strengthens the bolt (the same roll, Will +5 hits harder, Might +5 does not); a novice fizzles sometimes and a caster at the formula's complexity never; an unknown formula, a missing formula and too little Focus are refused, as is a second working mid-tell; a save after working keeps Focus, Strain, the ward and the known formulas, with an identical state digest; the MAG001 lint refuses mana costs, a non-magic domain, a projectile without damage, targeting Phase 1 does not build, a book teaching anything but a formula, and a book that is not used up.

## Decisions (flippable)

1. **The numbers** are placeholders with a stated shape (`PROTOTYPE.md` A-5), all in `config.magic` and the formula files: costs, cast times, the Strain and fizzle slopes, the half-Strain floor, 2 health a point of backlash, and 2% force per point of Resonance.
2. **The ward's shape.** Brace Ward is armor on every region. The bible says "protection against intrusion/impact"; a knockdown guard or an absorb pool would need a new effect vocabulary, so it is not built.
3. **The mending's shape.** Mending Thread shares the salve's mending and adds stabilization: it lifts bleeding and venom, the bible's "small recovery/stabilization".
4. **Where the primer is.** It lies on a shelf in the longhouse until the bible's archivist can hand it over (M4). All three formulas come from it.
5. **The hotbar order** is the learning order, and within one book the book's own order (bolt, ward, mending: the bible's keys 4, 5, 6). No formula is named in presentation code (`PROTOTYPE.md` §7's rule against hardcoded spell IDs).
6. **What counts as a working that mattered**: a wounding bolt, or a working cast within 8 s of a blow (M3c's out-of-combat time).
7. **A load starts at rest**: the pause before Focus returns and Strain ebbs is not saved, as with stamina (M3c). The values themselves are saved exactly.
8. **Casting is silent to creatures.** Only a swing is heard (M3d's noise). A working's sound is a later tuning question.

## Verification

- `dotnet test` (from `src/`): **540 passed**, 0 failed. Architecture 14, Content 94, Domain 125, EntityRegistry 23, World 58, Persistence 134, Application 92.
- Content lint: 74 definitions, 0 errors.
- Godot 4.7.2 headless smoke `PASS`: into the longhouse, the book taken from the shelf and read, three formulas known, a ward worked (+11 Strain), a swing that misses, and a quicksave that loads to the identical digest with the same Strain. Five runs on random worlds all pass, one of them after a fizzle and a second working.
- The windowed `--ui-shots` run (ASTRAL) adds `shelf.png` (the Resonance Primer on the shelf), `learned.png` (three formulas learned, keys 4 to 6 on the HUD), `casting.png` (the glow in the tell, "Casting Brace Ward", Focus 68/80, Strain 11/40), `warded.png` (Braced 10 s), `bolt.png` (the bolt hits the boar for 8; Force +38 XP with the first-success novelty) and `mending.png` (a mending in the fight trains Vital) to M3d's pictures. I reviewed them all. This turn also fixed two HUD flaws they showed: bars stretched by the hotbar's width, and identical notices stacking.

## Not done, and why

- **An implement distinction** (the ruling's "if cheap"): the prototype's content has no implement, and adding one needs an item and an equipment rule. Deferred rather than invented.
- Contextual costs (reagents, charges, essence), other targetings (touch, area, beam), channelled workings, cooldowns, reaction chains, custom formulas and a spell editor, Great Works, Otherwhen, and divine systems are all out of Phase 1 scope.
- `PROTOTYPE.md` C10 (the den pack needs all three workings) is a playtest judgement on the whole game, for M6.
- The M3 performance gate still waits on the RAZER window.
