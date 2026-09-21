# PROGRESSION.md — Progression Architecture

**Project:** UNNAMED (working title) — first-person, solo-first, open-world fantasy RPG
**Phase:** 0 — Vision and Architecture (STEP 13)
**Authority:** `PROJECT_CHARTER.md` is the authoritative creative vision. `DECISIONS.md` records settled implementation decisions. Where this document and the charter disagree, the charter wins and this document is wrong. This document is the normative reference for progression; implementation sessions must cite the axis IDs below (`AX-LVL`, `AX-ATTR`, ...) rather than restating the rules.

**Reader assumption:** another AI coding session implementing from documents alone. Nothing here requires engine knowledge. All numeric targets are **initial balance values**, expressed as data constants (D-03), not code literals.

---

## 1. The Spine — D-09 Made Operational

D-09 sanctions a progression axis only if it answers a **different question**. This document's core deliverable is the per-axis articulation of that question and the proof that no axis is a restatement of another.

The five transfers D-09 fixes, verbatim in effect:

| Verb | Axis | What it produces |
|---|---|---|
| grants **breadth** | character level | access to more options, more content bands, more points |
| grants **competence** | skills | reliability at things you can already attempt |
| grants **specialization** | weapon mastery, magic mastery | depth inside a narrow domain |
| grants **production** | professions | the ability to make things others cannot |
| grants **access** | reputation | permission, price, and doors |

**The non-conversion law.** A character cannot convert one axis into another. Concretely, and testably:

1. No axis's advancement function may read another axis's progression currency as a direct input. Gold cannot buy skill, mastery, or level progress. Level cannot be spent for mastery. Reputation cannot be spent for craft tiers.
2. No consumable, service, or item may grant a **permanent** gain on any axis other than equipment. (See §11.3 for the narrow, bounded exception for trainers.)
3. Axes may **gate** each other freely (level gates spell access, attributes gate ability prerequisites, reputation gates trainers, professions gate recipe tiers). Gating is not conversion. A gate changes *when* an axis can advance; conversion would change *how*.
4. Any content or code path proposing to move value from axis X into axis Y is a design bug against D-09 and must be rejected in review.

---

## 2. Axis Inventory — Question, Gate, Advance, Ceiling, Non-Equivalence

| ID | Axis | The question it answers | What it gates | Primary advance vector | Ceiling | Why it is not another axis |
|---|---|---|---|---|---|---|
| `AX-LVL` | Character level | *How much of the world can I survive and reach?* | Content bands, attribute/skill point income, ability tier prerequisites, companion tier caps | XP from mixed sources (§3) | 50 (hard); mastery tiers continue past it (§8) | Level is only breadth. It grants no mastery rank, no profession rank, no reputation, no competence bonus on its own — two level-30 characters differ entirely by their other axes |
| `AX-ATTR` | Attributes | *What shape is this character?* | Derived pools, ability prerequisites, equipment stat minimums, spell potency coefficients | Level-up allocation, capped + racial/quest bonuses | 60 per attribute effective (see §4.3) | Attributes are a *shape*, not a magnitude. They set which options are efficient, not which exist; they do not advance by use |
| `AX-SKL` | Skills | *What can I reliably do?* | Action quality: craft success margin, stealth detectability, dialogue options, harvest yield, persuasion | Use-based competence XP per skill, per-action | 100 per skill, hard, non-decaying above 60 | Skills raise success probability and quality within an already-unlocked activity; they never unlock the activity itself |
| `AX-WM` | Weapon mastery | *How deadly am I with this family of weapons?* | Per-family move set, stagger/crit profile, armor-pierce, combo branches | **Use-based**: effective damage dealt with that family under level-band-valid conditions | Tier 5 per family; 3 *aptitudes* per family held simultaneously (§6) | Mastery is a hands-level skill tied to a *physical weapon family*; it is orthogonal to attributes (a weak character can be a master fencer) and to school mastery (which is a mind-level system) |
| `AX-MM` | Magic mastery | *What do I understand, and what can I hold in mind?* | Spell access per school, school-specific mechanics depth, attunement slots | **Study-based**: research, tomes, teachers, first-hand use of a school's distinct mechanic | Tier 5 per school; 2–4 *attunement* slots total (§7) | Mastery here is *knowledge and cognitive load*, not dexterity. Advancing it requires sources, not repetition; it gates spells, which weapon mastery never does |
| `AX-ABL` | Abilities / talents | *What did I choose to be able to do?* | The active and passive toolkit actually equipped | Discrete points, spent at level-up, faction trainers, and quest rewards | Bounded by total points (≈120 by level 50) vs. ≈400 available nodes | Abilities are *selection*, not power. They express choices already shaped by attributes and masteries; a point spent is a point unavailable elsewhere |
| `AX-PRF` | Professions | *What can I produce, and how well?* | Recipe tiers, quality ceiling, station tiers, settlement structures | Craft attempts that produce **new-to-character** outputs, plus discoveries | Rank 5 per profession; 2 professions at rank 5 max (see §4.3) | Professions produce *things*; skills only improve *odds* on things you can already make. A rank-5 smith with skill 20 makes masterwork rarely; a skill-100 smith with rank 2 cannot make steel at all |
| `AX-REP` | Reputation | *Who will deal with me, and how favourably?* | Prices, trainers, quests, region permission, fast-travel networks, safe passage | Quest outcomes, faction actions, trade volume, crime | −5..+5 standing tiers per faction; 20+ factions tracked, mutually constraining | Reputation grants *permission*, never capability. Exalted standing does not make you a better smith; it lets a master smith agree to teach you |
| `AX-EQP` | Equipment | *How strong am I right now?* | Immediate combat/utility numbers | Find, buy, loot, craft, enchant, socket | Unbounded by level; bounded by attribute/skill minima and acquisition difficulty | Equipment is *immediate and losable*. It can be taken away by death, theft, or decay; no other axis can |
| `AX-CMP` | Companions | *Who grows alongside me?* | Party capability, non-combat roles, story access | Shared experience, personal quests, gear, affinity | 6 active companions out of a larger roster; each level 1–40 | Companions grow *beside* the player, with their own progression rules; they never transfer their growth to the player |

**Axis count check (anti-duplication audit).** Each row's "Why it is not another axis" cell must be falsifiable: if two axes always move together in playtest telemetry, they are one axis and must be merged (D-09 *Revisit if*). §13 records the telemetry that decides this.

#### The three axes most at risk of being one axis, and the cheap test that settles it

An adversarial review made the strongest objection to this document: `AX-LVL`, `AX-SKL` and `AX-WM` **all advance from the same event** — a valid action against a valid target — and differ by *valuation* rather than by *input*. It further observed that `AX-WM`'s own defence ("mastery is a hands-level skill") reads as an admission that skill and mastery are the same question at different granularity, and that `D-09` itself rejected two candidate axes ("exploration level", "crafting level") on exactly this ground while keeping a third of the same shape.

That objection is **accepted as live**. The original plan deferred the verdict to telemetry at `G12`/`M12`, which is too late: `ROADMAP.md` already prices a late merge as "re-balancing every character and save", and ten axes × 50 levels is a save-schema-scale commitment.

**Resolution — settle it on paper and in the prototype, before the save schema is frozen:**

1. **Differentiate by input, not by valuation — and state the claim as a falsifiable prediction.** The axes are distinct only if a player *can* drive them apart. The prototype therefore ships, deliberately, a situation that separates them: a **high-skill, low-mastery** character (a scholar who has read about swords and practised on a training dummy) and a **low-skill, high-mastery** one (a brawler with a single weapon family and no broad competence). If those two cannot be produced by legitimate play, the axes are not orthogonal and `AX-SKL`/`AX-WM` must merge.
2. **The settle-by test is a 2×2 fixture, not a telemetry campaign.** In `M2c`, construct four characters against the XP/skill/mastery API — (high skill, high mastery), (high, low), (low, high), (low, low) — and assert each is reachable through the sanctioned advance vectors without the other axis moving as a side effect. This is a **pure domain-layer unit test**, runs in milliseconds, and needs no content, no engine and no playtest. It is the cheapest form of the `D-09` revisit test and it is scheduled *before* the save schema freezes.
3. **`AX-SKL` must not be a currency hub.** The review also noted that skill feeds `AX-PRF` (quality), `AX-MM` (research) and `AX-EQP` (affix scaling), which is close to the conversion the non-conversion law forbids. The rule that keeps this legal: **skill may *gate* and *modify rates* on other axes; it may never be *spent* as their currency.** A player cannot convert skill points into mastery tiers or profession ranks — only earn those axes by their own vectors, at a rate skill can influence.
4. **If the 2×2 fixture fails, merge immediately and cheaply.** Merging `AX-SKL` into `AX-WM` before `M2c` freezes costs a document edit. The same merge after live saves exist costs every character and every save. That asymmetry is the whole argument for testing it early.

**Corollary for `G-2` (`M12`):** by the time `G-2` runs, the question is no longer *whether* these axes are orthogonal — the 2×2 fixture already answered it — but whether the *content* actually exercises the separation. Those are different questions and only the second one needs playtest data.

---

## 3. `AX-LVL` — Character Level and XP

### 3.1 What level does and does not do

Level grants: +1 attribute point and +1 skill point per level; +3 ability points per level (5 on levels 1, 10, 20, 30, 40, 50); access to content bands; and companion tier caps. Level does **not** grant mastery tiers, profession ranks, reputation, or automatic competence.

### 3.2 Curve and pacing targets

XP required for level *n* (n ≥ 2): `XP(n) = 100 * (n-1)^1.85`, rounded to the nearest 25, with a flat +15% band multiplier applied at levels 11–20 and 21–30 to keep the low tiers brisk for onboarding.

**Total to level 50 = 2,448,025 XP** (2,369,959 before the band multiplier). This figure is the *evaluated* sum of the formula above, not an estimate — an earlier draft of this document stated ≈1.9M, which understated the formula by 28.8% and would have silently mis-tuned every row below. **Any change to the exponent, the rounding step, or the band multiplier requires re-evaluating this total and re-deriving the pacing table in §3.2 and the `AG-6` rate band in §3.4**, because those numbers are all functions of it.

| Tier | Levels | Target hours (mixed play) | Dominant XP sources | Content band unlocked |
|---|---|---|---|---|
| Novice | 1–10 | 4–6 h | discovery, first quests, tutorial-region combat | Band 1 zones (creature levels 1–12) |
| Adept | 11–20 | 8–12 h | quests, dungeons, first profession tier, exploration credit | Band 2 (10–24) |
| Veteran | 21–30 | 13–18 h | multi-stage quests, faction work, mastery-valid combat | Band 3 (22–36) |
| Champion | 31–40 | 16–22 h | artefact quest stages, boss content, high-tier crafting, deep exploration | Band 4 (34–46) |
| Legend | 41–50 | 20–28 h | endgame regions, superbosses, mastery progression, faction apex | Band 5 (44–60) |
| Post-cap | 50+ | open-ended | mastery tiers, profession apex, prestige projects | All bands |

**Target total to level 50: 65–85 hours of mixed play.** Against the correct 2,448,025 XP total, that implies **≈28.8k XP/hour at 85 h and ≈37.7k XP/hour at 65 h**; the mid-point (75 h) is **≈32.6k XP/hour**. These are the numbers `AG-6`'s 0.6×–1.4× band must be built around, and the per-tier target rates below must sum to roughly 32.6k/hour at the mid-point rather than the ~25.3k the earlier (incorrect) 1.9M total implied. A combat-only player should reach 50 in roughly 110–130 hours; this gap is intentional and is the primary expression of "killing creatures must not be the only path".

### 3.3 XP source ledger

Every XP source declares a `source_kind`, which the anti-farm system keys on (§3.4).

| Source kind | Share of expected total | Notes |
|---|---|---|
| `discovery` | ~20% | First-time region/cell entry, location discovery, landmark identification, cartography completion, secret found, lore revelation |
| `quest_objective` | ~35% | Largest single source. Weighted by objective *type* (investigation, crafting, construction, diplomacy valued as highly as combat) |
| `combat` | ~30% | Band-valid kills, boss kills |
| `production` | ~8% | **First-time-only** craft of a given definition, first successful use of a new recipe, profession discovery |
| `social` | ~7% | Faction actions, dialogue outcomes, relationship milestones, companion personal quests, trade milestones |

Only a maximum of 3 of 5 source kinds are ever required to level. This is enforced as a design constraint: no content band's expected XP total may depend on more than 3 kinds.

### 3.4 Anti-grind policy (invariants, not vibes)

The charter keeps grinding useful and rarely mandatory busywork (Charter §11). These rules must **not** punish legitimate play: hunting a region for hides, leveling a weapon family, or farming a faction is sanctioned player intent and should remain productive on the *axes it actually feeds*.

| ID | Guard | Rule | What it must not do |
|---|---|---|---|
| `AG-1` | Level-band reward | XP multiplier by `(creature_level − player_level)`: `+4..0 → 1.00`, `−1..−5 → 0.60`, `−6..−10 → 0.25`, `−11..−15 → 0.05`, `≤−16 → 0.00`. Above player level: `+1..+5 → 1.15`, `+6..+10 → 1.30`, `>+10 → 1.15` (capped, to discourage suicidal farming) | Must not zero out a *new* creature type a player has never killed, regardless of level |
| `AG-2` | Species novelty | First kill of a species per real day: full value. Each subsequent kill of the same species that day multiplies XP by `0.9^k` to a floor of 0.10 | Must not affect loot, hides, or mastery gain — only `AX-LVL` XP |
| `AG-3` | Spawn-site saturation | Per spawn-cluster, after 25 kills inside a rolling 30-minute window, XP decays to floor 0.10 and respawn interval lengthens ×2 until the window empties | Must not change creature behaviour, loot table, or drops |
| `AG-4` | Kill-share neutrality | No rule in `AG-1..AG-3` may reduce any other axis's gain. Mastery/aptitude gain, profession material yield, faction kill-objective credit, and Skinning/Hunting profession progress are unaffected | Prevents "anti-farm" from silently nerfing the sanctioned reasons to grind |
| `AG-5` | Quest sanity | Repeatable quests must use `unique_target` objectives or a hard completion count. Generic `kill N of species S` objectives are non-repeatable once completed | Prevents counter-loop XP |
| `AG-6` | Rate banding | Designed XP/hour band across activities: 0.6×–1.4× of the tier target rate. Any activity measured outside this band for a full tier is re-tuned, not protected | Prevents one degenerate activity becoming mandatory |
| `AG-7` | No conversion loops | No recipe, vendor, or trade route may convert gold → XP, materials → XP beyond the first-time production credit, or salvage → XP | D-09 non-conversion law |
| `AG-8` | Death is the only XP penalty | Death applies **XP debt**, not lost XP: a bounded fraction of the **current level's** progress is owed and discharged out of subsequent XP earnings (default: 10%). The debt never drains progress below the floor of the current level, so **a level is never lost**, and it is always fully repayable by playing. Death never touches mastery, profession, reputation, or skill progress | Keeps failure meaningful without invalidating hours of mastery work, and without a "load an earlier save" reflex |

*Assumption recorded:* the charter leaves the death system open (Charter §23 lists temporary injuries, equipment damage, corpse recovery, and XP debt as candidate options). This document assumes the bounded `AG-8` variant — **XP debt, not lost XP** — plus the non-XP consequences owned by `SYSTEMS.md` (temporary injury and a corpse-recovery trip are named in `VERTICAL_SLICE.md` and are compatible with this). The **debt model is the canonical one**: `PROTOTYPE.md` C17 gates Phase 1 on XP-debt arithmetic specifically, and would *fail* an implementation that deducted XP instead. Reversible by a data constant, but any change must be made here first and then propagated to `PROTOTYPE.md` C17 and `VERTICAL_SLICE.md` P8.

**Sanctioned grind, restated.** A player who hunts a valley for six hours should still get: hides (Hunting/Skinning yield), weapon mastery tiers, faction kill-objective credit, rare-drop chances, and profession materials. They will get sharply diminishing **level** XP after the first ~25 kills at one cluster. That is the intended shape: useful, rarely mandatory busywork, and never the only path.

### 3.5 No global level scaling — progression vs. the world

The charter forbids global creature scaling (Charter Pillar 1). Progression must therefore communicate danger *without* scaling content:

1. **Band, not level.** Zones declare a creature-level band. `AG-1` makes under-level content worth little XP and over-level content lethal, so the correct behaviour is to move, not to grind.
2. **Readable danger, three tiers of signal.** (a) *Environmental*: architecture, corpses, weather, ambient audio density, creature silhouette scale. (b) *Mechanical*: a hit taken from an over-band creature produces a distinct screen and audio signature, and the world-state HUD reports a qualitative threat read ("lethal", "severe", "even", "trivial") — never a numeric level over an enemy's head by default.
3. **Damage is band-driven, not HP-sponge.** Over-band creatures kill through damage and mechanics, not inflated health pools (Charter §7 forbids HP sponges). A level-5 player in a level-40 zone dies in 1–2 hits; that is the message.
4. **Level does not unlock zones; it makes them survivable.** Zone entry is physically unrestricted. Knowledge of danger is the gate.
5. **Returning feels earned.** Because the world is static, a level-25 player revisiting a level-40 zone succeeds through attributes, abilities, equipment, and mastery gained elsewhere — the felt reward is the *absence of scaling*, exactly as the charter intends.
6. **A level-5 player in a level-40 zone must be able to tell.** Acceptance test: a tester dropped into a band-5 zone at level 5 must self-report "I should not be here yet" within 90 seconds, without UI text saying so.

---

## 4. `AX-ATTR`, `AX-SKL`, `AX-ABL`

### 4.1 Attributes (`AX-ATTR`)

Seven attributes: `might`, `endurance`, `agility`, `precision`, `will`, `insight`, `presence`. **This set is canonical and is the only one any other Phase 0 document may use** — `DATA_MODEL.md`'s `CreatureDefinition`/`NPCDefinition` examples and `VERTICAL_SLICE.md`'s content budget both cite it, and an earlier draft of each carried a different set (six and five respectively). Because attributes are persisted per character, adopting a different set later is a save migration, not a rename. Each contributes to derived pools via data-defined coefficients (D-03):

| Attribute | Primary contributions | Ability prerequisite domain |
|---|---|---|
| `might` | melee power, carry, stagger resistance | two-handed, heavy armour, intimidation |
| `endurance` | health pool, stamina pool, poison/disease resistance, stamina regen | armour use, shield, sustained abilities |
| `agility` | movement, dodge window, attack recovery, climb | evasion, dual wield, rogue abilities |
| `precision` | ranged accuracy, crit chance, weak-point damage, lockpicking aid | bow, crossbow, marksman, assassin |
| `will` | spell resource pool, fear/charm resistance, concentration under damage | all schools' potency floor, mental resistance |
| `insight` | spell potency ceiling, research speed, recipe discovery rate, identify | school depth prerequisites, artifice, enchanting |
| `presence` | persuasion, trade prices, companion affinity gain, follower morale | social abilities, summon command, mercantile |

Advancement: 1 point per level (50 points total), plus bounded one-time bonuses from race (creation), rare quests, and at most one permanent attribute item near endgame. **Attributes never advance by use.**

### 4.2 Skills (`AX-SKL`)

Skill list (initial, deliberately small — expansion is content, not code): `athletics, stealth, lockpicking, pickpocket, persuasion, intimidation, barter, survival, tracking, cartography, lore, medicine, music, riding, swimming, arms-maintenance, enchanting-theory, research`.

Advancement is **use-based competence XP with a difficulty gate**: an action grants skill XP only when its difficulty exceeds `skill_ceiling − 15`. Trivial repetition is worthless by construction, with no punishment logic required. Effective skill = base skill + circumstance modifiers; display always shows the effective value.

Ceiling: 100 per skill. Above 60, skill does not decay. Below 60, decay at 0.5 points/week of non-use to a floor of the last tier band. *Assumption recorded:* decay is bounded and slow; if playtest shows it reads as punishment, set the decay rate to 0 by data constant (D-09 is unaffected).

Skill vs. profession disambiguation test: a skill answers "how well do I do a thing I can attempt"; a profession answers "may I attempt to make this thing at all, and what quality ceiling applies". A rank-2 alchemist with `medicine` 90 is an excellent field medic who cannot brew a tier-3 elixir.

### 4.3 Ability points and the fake-choice guard

Total points by level 50 ≈ 120; available nodes ≈ 400. Total attribute points ≈ 50 against a 7-attribute space; a true generalist spreads ~7 points per attribute and is competent at nothing that requires prerequisites. Profession hard cap: **2 professions at rank 5**; additional professions cap at rank 3. Weapon families at tier 5 and school tiers at 5 share a soft cap through aptitude/attunement limits (§6, §7), not through point scarcity.

Charter requirement: *avoid fake choices where every character eventually unlocks everything.* Enforcement: the total node count must always exceed maximum obtainable points by ≥3×; a content-validation rule fails the build if spendable points ≥ 40% of available node cost. That validator is listed on the critical path in `ROADMAP.md` (M1, M7).

---

## 5. Classes: Starting Archetypes Only — the Call

**Decision (`AX-ARCH-1`).** Classes exist as **starting archetypes only**. There is no class field on the character after creation, no class-locked content, no class-locked abilities.

**Justification.** The charter permits this ("Classes may function as starting archetypes rather than permanent restrictions if that produces a better system", Charter §4) and requires "unusual combinations" and hybrids. A permanent class would answer the same question as `AX-ATTR` + `AX-ABL` + masteries — a D-09 violation by construction — and would make the 22 charter builds into 22 lockers rather than 22 emergent outcomes. An archetype answers a genuinely different question: **"what is my opening hand?"** It solves the real problem (a new player at level 1 lacks enough information to build meaningfully) without constraining the endgame.

**What an archetype is:** a creation-time package of (a) an attribute bias of 4 points pre-allocated, (b) 2 skills starting at 15, (c) 3 ability nodes pre-selected, (d) starting equipment, (e) a starting faction contact, (f) one unique opening dialogue/scene. It grants nothing that cannot be reached by any other character.

Initial archetype set (content, ~6, expandable in Phase 3): `warrior`, `mage`, `rogue`, `ranger`, `healer/priest`, `artisan`. Races are selected separately and contribute a small, non-dominant modifier package (Charter §5: not cosmetic, not superior).

**Retraining.** All attribute, ability, and skill-point allocations are respecable through in-world trainers at escalating cost and time (first respec cheap, later ones expensive). Mastery tiers, profession ranks, and reputation are **not** respecable — they are history, and history cannot be undone. This is the mechanism that makes builds consequential while keeping the system forgiving enough not to force save reloading (Charter §22–23).

---

## 6. `AX-WM` — Weapon Mastery (use-based, per family)

**Question:** *How deadly am I with this family of weapons?*

**Mechanic.** Weapon mastery is **use-based**, measured in mastery XP earned from *effective* contribution with that weapon family: damage dealt to band-valid targets, successful blocks/parries with a family-appropriate defence, and successful use of family moves. Mastery XP is **not** granted for swinging at nothing, for attacking trivial targets beyond a floor rate, or for kills where the family dealt <15% of the damage.

**Families (initial 11):** `one_hand_blade, two_hand_blade, axe, mace_hammer, polearm, dagger, shield, bow, crossbow, staff, unarmed`.

**Tiers (0–5) per family.** Tier 5 is the ceiling and is deliberately long (≈40–60 h of focused use in that family across the campaign).

| Tier | Name | Unlocks |
|---|---|---|
| 0 | Untrained | base attacks; −10% damage; no family moves |
| 1 | Familiar | 1 family move; +5% stagger consistency |
| 2 | Proficient | 2–3 moves, one combo branch; +5% crit profile |
| 3 | Adept | full basic move set; armour-pierce on heavy attacks |
| 4 | Master | signature move; reduced stamina cost; +10% vs. staggered |
| 5 | Grandmaster | capstone move; family passive (e.g. shield: block-reflect; bow: charge-pierce) |

**Aptitudes — the horizontal endgame layer (§8).** Only **3 families** can hold a Grandmaster *aptitude* at once. Aptitude is an explicit, chosen designation: it grants the tier-5 family passive and enables `AX-WM` post-cap **insight ranks** (`I..V`) that add narrow, non-numeric effects (new move variants, recovery frames, special interactions). Switching an aptitude costs a long in-world ritual and the new family must already be tier 4. Mastery *tiers* never decay; **aptitudes** are the constrained, chosen resource.

**Weapon familiarity (separate, intentional).** Individual rare/legendary weapons track a per-instance `familiarity` 0–5, advanced by using *that item*. Familiarity unlocks that weapon's unique properties (a named sword's special attack). This is not a fourth mastery system: it is a per-instance property of `AX-EQP`, owned by the item, and it is lost if the item is lost. It exists so rare items discovered at level 15 remain interesting at level 25 (Charter §8).

---

## 7. `AX-MM` — Magic Mastery (study-based, per school)

**Question:** *What do I understand, and what can I hold in mind?*

**Mechanic.** Magic mastery is **study-based**, not repetition-based. It advances through: (a) **research** at a library/study station (time + materials + `insight` + `research` skill), (b) **tomes** (consumed; one-time tier credit), (c) **teachers** (faction/NPC gated, cost + relationship), (d) **first successful use of a school's distinctive mechanic** (one-time credits per mechanic, not per cast), and (e) **discovery** in the world (a runic tablet, a dead archmage's notes).

Repetitive casting grants school mastery **nothing**. This is the deliberate structural difference from `AX-WM`: weapon mastery is earned with the hands in danger; school mastery is earned with the mind in safety, and so it costs *time in town and access to sources* rather than combat risk.

**Schools (initial 6):** `elemental, healing, protection, necromancy, summoning, nature`. Expansion to illusion, alteration, blood, runic, divine, and enchanting in Phase 3.

**Tiers (0–5) per school.** A tier unlocks that school's spell *family*, and every school has a distinct mechanic that its depth extends:

| School | Distinct mechanic | Depth reveals |
|---|---|---|
| `elemental` | elemental reaction chains (wet→shock, chilled→shatter) | multi-element detonation, environmental catalysing |
| `healing` | delayed healing over time vs. burst; wound-type matching | resurrection-adjacent effects, group restore |
| `protection` | ward layering with damage-type absorption budgets | reflected damage, group wards, structure warding |
| `necromancy` | essence harvested from corpses as a spendable resource | permanent servants, curses, construct minions |
| `summoning` | binding contracts, per-summon upkeep and command limits | persistent summons, multi-summon, bound elites |
| `nature` | localised weather/terrain manipulation, animal rapport | shapeshifting forms, weather control, wild growth |

**Attunement — the cognitive-load resource.** This is magic mastery's ceiling mechanism, analogous to (but mechanically distinct from) weapon aptitudes:

- An attuned school may be cast at all. Unattuned schools may be *read* and *researched* but not cast.
- **Attunement slots: 2 at character level 10, 3 at level 30, 4 at level 50.** Slot count is granular and level-gated, so `AX-LVL` *gates* magic breadth while `AX-MM` supplies depth — a gate, not a conversion.
- Switching attunement requires a rest at a study station (or an expensive scroll). The cost is friction, not lockout.
- **School potency coefficient** derives from `wil` + `insight` + school tier. Attributes set the ceiling; mastery sets the access; neither substitutes for the other.

**Spell preparation.** Schools that prepare spells (protection, summoning, necromancy) have a prepared list bound at rest; the list size grows with school tier. Overworld casting draws from the prepared list. This keeps the moment-to-moment loop about *choice under constraint* rather than a spell menu.

**Battlemage support (Charter §6: "genuinely combine melee and magic").** The `spellblade` ability line requires a weapon family at tier ≥2 *and* a school at tier ≥2 and spends ability points on hybrid nodes whose effects scale on *both* axes. Neither axis can produce the battlemage alone; the hybrid lives in `AX-ABL`, which is exactly where a cross-axis choice belongs.

---

## 8. Post-Cap / Endgame Mastery Progression (Charter §24)

Level 50 is not the end. Post-cap progression is deliberately **horizontal and specific**, never a resumption of vertical power:

| Post-cap track | Currency | Ceiling | What it changes |
|---|---|---|---|
| Weapon **insight ranks** | aptitudes (max 3 active) + use | `I..V` per family | Move variants, timings, interactions — not raw damage multiplication |
| School **deep study** | research projects, rare components, site pilgrimages | `I..V` per school | New spell variants, school capstones, ritual access |
| **Profession mastery projects** | materials + time + discoveries | 1 masterwork project per profession | Artefact-tier crafting capability; named items with authored properties |
| **Faction apex** | reputation + faction storylines | per faction | Titles, region influence, NPC allegiance, fast-travel/authority unlocks |
| **Companion stories** | affinity + personal quests | per companion | Companion capstone ability, ending states |
| **Prestige projects** | everything above, combined | settlement / estate scale | Player-chosen world works: settlement founding, landmark construction, region restoration |

Design constraint: post-cap tracks must **not** produce a strictly larger damage number than a well-built level-50 character. Their value is *new options, access, and authored outcomes* — this keeps the endgame from invalidating the pre-cap curve and keeps `AX-LVL` from being re-opened as a stealth power axis.

---

## 9. `AX-PRF` — Professions

**Question:** *What can I produce, and how good can it be?*

**Mechanic.** Profession advancement requires producing outputs **new to that character**: a first-time success at a recipe of the next tier, an experiment that yields a new discovery, or a masterwork attempt at the current ceiling. Re-crafting the same item advances nothing after the first success. This makes profession progress a function of *exploration of the recipe space* (which is gated by materials, knowledge, and stations), not of bulk production.

**Tiers 0–5 per profession.** Rank caps the *material tier* and the *quality ceiling*:

| Rank | Material tier | Quality ceiling | Gated by |
|---|---|---|---|
| 0 | — | — | — |
| 1 | common | fine | basic station |
| 2 | uncommon | superior | station upgrade, 1 discovery |
| 3 | rare | exceptional | knowledge source (teacher/tome/ruin), faction access |
| 4 | very rare | masterwork | rare component access, master project |
| 5 | legendary | masterwork + authored properties | artefact project completed |

**Initial professions (content, ~10 at Phase 2):** `blacksmithing, weaponsmithing, armorsmithing, alchemy, leatherworking, tailoring, woodworking, enchanting, jewelcrafting, artifice.` Gathering professions (`mining, herbalism, logging, hunting/skinning, fishing, farming, masonry`) are **skills plus resource nodes**, not professions, except `masonry` which gates construction pieces.

**Crafting stays relevant (Charter §12).** Crafted gear is the only source of: chosen material-property combinations, socketed bases, masterwork quality, and constructed structures. Dungeon loot supplies unique authored properties and set relationships. Neither category fully substitutes for the other — enforced as a content rule: no dungeon drop may reproduce a masterwork-quality crafted base, and no craft may reproduce a unique authored effect.

**Interaction with skills.** `arms-maintenance`, `research`, `lore`, `medicine` improve success margins and discovery rates. A high skill with a low rank cannot exceed the rank's quality ceiling; a high rank with a low skill fails more often. Both matter, and neither is a restatement of the other.

---

## 10. `AX-REP` — Reputation

**Question:** *Who will deal with me, and how favourably?*

**Model.** Per-faction standing on a discrete tier ladder — `Nemesis(−5), Hostile(−4), Despised(−3), Disliked(−2), Wary(−1), Neutral(0), Accepted(+1), Trusted(+2), Honoured(+3), Allied(+4), Exalted(+5)` — with per-faction numeric accumulation inside a tier. No universal good/evil meter (Charter §20): the same act may raise standing with one faction and lower another, and *different factions may interpret the same act in opposite directions by data-defined reaction tables*.

**Advancement vectors.** Quest outcomes; faction-specific actions (turning in bounties, supplying goods, defending assets); trade volume with faction merchants; crime and its consequences (bounties, notoriety, guard response); companion relationships; public world-state outcomes (Charter §20).

**Gates.** Trainers (ability/school/attribute prerequisites), prices and stock, quest availability, region permission and safe passage, fast-travel networks and caravan routes, settlement-building rights, hireling quality, and titles.

**What it never grants:** capability. Reputation opens the door to a master smith's instruction; the instruction still costs time, materials, and profession advancement. This is the cleanest possible illustration of the non-conversion law.

**Decay.** Standing drifts toward Neutral at a slow rate (per in-game week) and does not decay past `Honoured` except through hostile action. `Nemesis` requires explicit acts and is escapable via atonement content, never via payment alone.

---

## 11. `AX-EQP`, `AX-CMP`

### 11.1 Equipment (`AX-EQP`)

The only axis that gives immediate power and the only one that can be lost. Sources: loot, purchase, craft, enchant, socket, augment, artefact quests. Requirements are expressed as **attribute/skill/family minima**, not level minima, so an unusually built character can wield something surprising — an intentional route to "unusual combinations".

Rarity ladder: `common, fine, superior, exceptional, masterwork, unique, legendary, artifact`. Durability exists only where thematically justified (Charter §8: "where appropriate"). Enchantment and socketing are `enchanting`-profession products, not a separate axis.

**Anti-degeneracy rules.** (a) Global item power per tier is banded so no single drop trivialises a band. (b) No item grants permanent progression on another axis; the narrow exception is *trainer items* — one-use tomes/teachers that must still be bought with access and time (§11.3). (c) Rare items hold value across ~10 levels via unique properties and per-instance familiarity (§6), not via raw numbers.

### 11.2 Companions (`AX-CMP`)

**Question:** *Who grows alongside me?* Companions have their own level (1–40, capped by player level tier and affinity), their own equipment, their own ability set, their own affinity ladder (Charter §2). They advance through shared XP, personal quests, gifts, combat cooperation, and disagreements resolved.

**Non-conversion invariant:** companion growth never transfers to the player. A companion's power is *party* power, and the player can lose it by offending, dismissing, or losing the companion permanently (Charter §22 permits failure without forcing reload). Max 6 active companions out of a larger roster.

### 11.3 The single bounded exception

`AX-ABL`, `AX-MM`, and `AX-ATTR` may each receive a **small, bounded, one-time** permanent gain from a **trainer or quest reward** that costs access (`AX-REP`), time, and materials. This is a **gate, not a conversion**: the currency paid is not another axis's progress value, the gain is finite and non-repeatable, and the total contribution of all such gains is capped (≤8% of that axis's total obtainable progress). Any proposal to raise that cap is a D-09 review item.

---

## 12. Build Expression Without a Class System

**Build identity** is the tuple: dominant attributes + aptitude families (≤3) + attuned schools (≤4) + slotted abilities + profession pair + reputation profile + equipment theme + companion roster. No single element defines a build; that is why hybrids are emergent rather than authored.

| Charter build | Attributes | Aptitudes | Attunement | Professions | Notes |
|---|---|---|---|---|---|
| sword-and-shield fighter | high `end`/`might` | `one_hand_blade`, `shield` | — | smithing | block-reflect capstone; shield archetype opening |
| two-handed berserker | `might`/`end` | `two_hand_blade`, `axe` | — | — | stamina-fuelled cleave; low `will` |
| battlemage | `might`/`wil`/`insight` | one melee family | `elemental` | enchanting | requires `spellblade` hybrid nodes in `AX-ABL` |
| mage-warrior | `wil`/`insight`/`end` | one melee family | `protection` | — | defensive casting, ward-while-fighting |
| paladin | `might`/`will`/`presence` | `mace_hammer`, `shield` | `healing` or `protection` | — | anti-undead via protection depth |
| dark knight | `might`/`will` | `two_hand_blade` | `necromancy` | — | essence-fuelled, reputation cost with most factions |
| elemental mage | `insight`/`wil` | `staff` | `elemental` (+1 flex) | — | reaction-chain specialist |
| necromancer | `insight`/`will` | any | `necromancy`, `summoning` | alchemy | servant/construct pipeline |
| healer | `will`/`insight`/`presence` | — | `healing`, `protection` | alchemy | wound-type matching |
| summoner | `will`/`insight` | any | `summoning` (+contracts) | — | upkeep-limited binding |
| ranger | `precision`/`agility`/`survival`-skill | `bow` | `nature` | leatherworking | tracking + terrain |
| archer | `precision`/`agility` | `bow`, `crossbow` | — | woodworking | pure marksman capstones |
| beastmaster | `presence`/`agility` | `bow` or `unarmed` | `nature` | hunting | animal rapport + taming |
| druid | `insight`/`will`/`end` | `staff` | `nature` (+`healing`) | herbalism | forms gate on nature depth |
| shapeshifter | `end`/`agility`/`insight` | `unarmed` | `nature` (deep) | — | forms are abilities unlocked by nature insight ranks |
| rogue | `agility`/`precision`/`presence` | `dagger` | — | — | `stealth`/`lockpicking` skills |
| assassin | `precision`/`agility` | `dagger` | optional `illusion` (P3) | alchemy | crit/weak-point stacking |
| hunter | `precision`/`survival` | `bow` | — | hunting/skinning | tracking, rare-spawn knowledge |
| alchemist | `insight`/`presence` | — | — | alchemy (R5) | discovery-driven; consumable economy |
| artificer | `insight`/`precision` | `crossbow` | `protection` | artifice, enchanting | construct/socket specialist |
| battle cleric | `presence`/`will`/`end` | `mace_hammer` | `healing` | — | group restore in solo via companions |
| defensive guardian | `end`/`presence` | `shield`, `polearm` | `protection` | masonry | ward + fortification synergy |

Note the deliberate **cross-axis requirements**: no row is expressible with a single axis, and several (battlemage, shapeshifter, artificer) are *impossible* without a specific pairing. This is the design's proof that the axes are not duplicating each other.

---

## 13. Interaction Rules, Telemetry, and Rejected Axes

### 13.1 Sanctioned interaction verbs (closed set)

Only five interactions between axes are legal. Any code or content that creates a sixth is a design review item:

| Verb | Example | Why legal |
|---|---|---|
| **Gate** | level/attribute/insight prerequisites | Changes *when*, not *how much* |
| **Modify rate** | `insight` speeds research | Multiplier on the same currency |
| **Scale effect** | spell potency from `wil`+`insight`+school tier | Composes within one axis's output |
| **Enable hybrid node** | `spellblade` needs family ≥2 and school ≥2 | Creates an option, spends `AX-ABL` points |
| **Provide access** | reputation trainer unlocks a school tier | Permission only |

Explicitly illegal: direct currency exchange, conversion of gold/items into axis progress, and any "respec" that moves progress between axes (attribute/ability/skill respec moves *points within* the axis, never across).

### 13.2 Duplication telemetry (the D-09 revisit trigger)

Instrumentation is a Phase-1 deliverable, not a later one. Required signals:

- Per-axis advancement events with timestamps and source kind (`AX-*`, `source_kind`), written to a non-shipped telemetry log.
- Correlation matrix of axis advancement rates over a play session; alert if any pair exceeds `r > 0.8` across ≥10 hours of varied play with no design justification on record.
- Profiling counters: XP/hour band compliance (`AG-6`) per activity type.
- Acceptance criteria for `AX-LVL` vs. `AX-PRF`: a player who spends 8 hours crafting must gain levels *and* profession ranks, and the level gain must be strictly smaller than the profession gain (breadth vs. production separation).

### 13.3 Rejected axes

| Rejected candidate | Why it failed the different-question test | Where it was folded |
|---|---|---|
| **Exploration level** (charter §4 lists "exploration progression") | It answered "how much of the world have I seen", which is already answered by `discovery` XP feeding `AX-LVL`, plus the `cartography` skill, plus discovery-credit world state. As its own axis it would have duplicated `AX-LVL` while adding a second number that moves when you walk | Folds into `AX-LVL` (`discovery` source kind, ~20% of XP), `AX-SKL.cartography`, and `WORLD_ARCHITECTURE.md` discovery-credit state that gates fast travel and map detail |
| **Crafting level** (distinct from professions) | It answered "how much have I crafted", a pure volume counter. Volume-based advancement is the definition of grind-as-busywork and duplicated `AX-PRF` | Folds into `AX-PRF` (rank = material tier + quality ceiling) and `AX-SKL` (`arms-maintenance`, `research`, `medicine`, `lore`) |
| **Faction rank as a separate axis from reputation** | Same question as `AX-REP` at a coarser granularity | Folded into `AX-REP` (per-faction standing, per-faction numeric accumulation) |
| **Weapon "level" separate from weapon mastery** | Same question at a coarser granularity | Folded into `AX-WM` tiers 0–5 plus per-instance familiarity |
| **Prestige/reincarnation** | Would convert accumulated progress on all axes into a new currency, violating the non-conversion law, and would invalidate the no-scaling world by resetting the power curve | Not adopted. Post-cap progression is horizontal instead (§8) |
| **Companion "bond level" as its own axis** | Same question as companion progression within `AX-CMP` | Folded into `AX-CMP` (affinity ladder + personal progression) |
| **A global "power score"** | Rejected in D-09 itself; destroys build legibility and enables the "every character is the same" failure | Not adopted. Any UI that displays a single aggregate power number is a design bug |

---

## 14. Assumptions Recorded

1. **Attribute semantics.** `insight` is used as the intellect/arcana attribute; one of the seven was renamed away from a generic "intelligence" precisely so no axis reads as a universal power stat.
2. **Level cap 50, attribute cap 60 effective, skill cap 100, masteries tier 0–5, professions rank 0–5, companion level 1–40.** All are data constants. Changing the cap is a balance pass, not an architecture change.
3. **Death penalty** uses the bounded `AG-8` variant — XP debt rather than deducted XP (see §3.4), which `PROTOTYPE.md` C17 asserts explicitly. The charter leaves this open; this is an inference, documented, and reversible.
4. **Skill decay below 60** is a bounded 0.5/week. If playtest reads it as punishment, set to zero; nothing else in this document depends on it.
5. **Respec policy:** attributes/abilities/skills respecable at escalating cost; mastery/professions/reputation are permanent history.
6. **No aggregate power score is ever displayed.** Threat reads are qualitative.
7. **Two of the charter's §4 progression bullets are intentionally served by other documents:** "faction relationships" is owned by `SYSTEMS.md` (relationship state) and contributes to `AX-REP`; "crafting progression" is `AX-PRF`; "exploration progression" is folded per §13.3.
8. **Aptitude and attunement counts (3 / 2–4)** are the primary specialization constraint. If playtest shows builds feel cramped, raise slot counts rather than lowering mastery costs — the constraint is what makes masteries a *choice*.
