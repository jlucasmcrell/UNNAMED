# PROGRESSION.md — Progression Architecture

**Project:** Otherreach (codename UNNAMED) — full-body third-person with seamless first-person zoom, solo-first, open-world fantasy RPG
**Phase:** 0 — Vision and Architecture (STEP 13). **Reconciled 2026-09-23** by the M2c progression-axis audit.
- **Merged:** weapon mastery, magic mastery and professions were merged into skills and techniques.
- **Removed:** ability points, level-granted skill points, attunement slots and the mana pool.
- **Rationale and rejected alternatives:** `PROGRESSION_AXIS_RECONCILIATION.md`.

**Authority:** `PROJECT_CHARTER.md` is the authoritative creative vision. `DECISIONS.md` records settled implementation decisions. Where this document and the charter disagree, the charter wins and this document is wrong. This document is the normative reference for progression; implementation sessions must cite the axis IDs below (`AX-LVL`, `AX-ATTR`, ...) rather than restating the rules.

**Reader assumption:** another AI coding session implementing from documents alone. Nothing here requires engine knowledge. All numeric targets are **initial balance values**, expressed as data constants (D-03), not code literals.

---

## 1. The Spine — D-09 Made Operational

D-09 sanctions a progression axis only if it answers a **different question**. This document's core deliverable is the per-axis articulation of that question and the proof that no axis is a restatement of another.

The transfers D-09 fixes:

| Verb | Axis | What it produces |
|---|---|---|
| grants **breadth** | character level | survivability across more of the world, and attribute points |
| grants **shape** | attributes | which options are efficient for this character |
| grants **competence** | skills & disciplines | reliability, efficiency and quality at a kind of activity — weapon, magic, craft, world or social |
| grants **capability** | techniques & formulas | what the character knows how to do |
| grants **access** | reputation | permission, price, and doors |

Specialization is not a separate axis. It is the mastery band inside skills (§4.3). Production is capability plus competence in a crafting discipline (§9).

**The non-conversion law.** A character cannot convert one axis into another. Concretely, and testably:

1. No axis's advancement function may read another axis's progression currency as a direct input. Gold cannot buy level XP, attributes or skill. Level cannot be spent for skill. Reputation cannot be spent for techniques; it can open a teacher's door.
2. No consumable, service or item may grant a **permanent** gain in level, attributes (beyond the bounded exception in §11.3) or skill. Knowledge (`AX-TEC`) enters only through a typed learning event, which may name a teacher or a book as its source (§4.4). The knowledge still needs skill to be worth anything.
3. Axes may **gate** each other freely: skills gate technique prerequisites, attributes gate equipment minima, reputation gates teachers. Gating is not conversion. A gate changes *when* an axis can advance; conversion would change *how*.
4. Any content or code path proposing to move value from axis X into axis Y is a design bug against D-09 and must be rejected in review.

---

## 2. Axis Inventory — Question, Gate, Advance, Ceiling, Non-Equivalence

| ID | Axis | The question it answers | What it gates | Primary advance vector | Ceiling | Why it is not another axis |
|---|---|---|---|---|---|---|
| `AX-LVL` | Character level | *How broadly developed is this character — how much of the world can they survive?* | Survivability across content bands (never permission); companion tier caps | XP from mixed sources (§3) | 50 (hard); Phase 1 caps at 5 | Level is only breadth. A level-up grants attribute points and nothing else — no skill, no technique, no designation, no reputation |
| `AX-ATTR` | Attributes | *What shape is this character?* | Derived pools, equipment minima, technique prerequisites, potency coefficients | Level-up allocation (+1 per level), plus bounded one-time grants | 60 per attribute effective (§4.1) | The **allocation of level**. Its magnitude moves with level by design; the distinct question is the *distribution*, which the player controls. It never advances by use |
| `AX-SKL` | Skills & disciplines | *How reliably, efficiently and well can I do this kind of thing?* | Success, quality, efficiency, Strain cost; technique prerequisites | Use under challenge, per discipline (§4.2) | Common ceiling 60 for every discipline; 100 only for designated masteries (§4.3) | Skills measure practised competence. They never grant a capability the character does not know (§4.4), and no event outside the discipline raises them |
| `AX-TEC` | Techniques & formulas | *What do I know how to do?* | The actions, formulas, craft techniques and recipes available at all | Learning events — teacher, book, quest, study, experiment, discovery/first success, artifact, culture — plus the starting package (§4.4) | Unbounded set; deep techniques narrow as they deepen | Knowledge is a capability set, not a magnitude. Knowing a technique never raises skill; skill never grants a technique |
| `AX-REP` | Reputation | *Who will deal with me, and how favourably?* | Prices, teachers, quests, region permission, fast-travel networks, safe passage | Quest outcomes, faction actions, trade volume, crime | −5..+5 standing tiers per faction; 20+ factions tracked, mutually constraining | Reputation grants *permission*, never capability, and never decides who attacks (§10). Exalted standing does not make you a better smith; it lets a master smith agree to teach you |
| `AX-EQP` | Equipment | *How strong am I right now?* | Immediate combat/utility numbers | Find, buy, loot, craft, enchant, socket | Unbounded by level; bounded by attribute/skill minima and acquisition difficulty | Equipment is *immediate and losable*. It can be taken away by death, theft, or decay; no other axis can |
| `AX-CMP` | Companions | *Who grows alongside me?* | Party capability, non-combat roles, story access | Companions progress on the same character model (level, attributes, skills, techniques); affinity is relationship state | Player + up to 3 active companions/hirelings; the prototype has one | Companions grow *beside* the player; they never transfer their growth to the player |

**Merged or removed axes.** Weapon mastery (`AX-WM`), magic mastery (`AX-MM`) and professions (`AX-PRF`) each measured a competence that is now a skill, and a capability that is now a technique. Abilities/talents (`AX-ABL`) were a point-bought version of `AX-TEC`; the owner ruled that techniques are learned in the world, and the audit found no separate need for the point pool. §13.3 records each.

**Axis count check (anti-duplication audit).** Each row's "Why it is not another axis" cell must be falsifiable: if two axes always move together in playtest telemetry, they are one axis and must be merged (D-09 *Revisit if*). §13.2 records the telemetry that decides this.

#### Independence is tested, not asserted

An adversarial review made the strongest objection to the earlier draft: level, skills and weapon mastery **all advanced from the same event** — a valid action against a valid target — and differed by *valuation* rather than by *input*. The reconciliation accepted it. Weapon mastery was merged into skills, so the question left is whether the surviving neighbors can be driven apart by legitimate play.

For **every retained neighboring pair** (`AX-LVL`×`AX-SKL`, `AX-SKL`×`AX-TEC`, `AX-LVL`×`AX-TEC`, `AX-ATTR`×`AX-SKL`, and discipline×discipline inside `AX-SKL`), an `M2c` domain test builds the 2×2:

- It reaches (high, high), (high, low), (low, high) and (low, low) through the sanctioned advance vectors only.
- It asserts that no vector moves the other axis as a side effect.
- The vectors are real play paths. Discovery and quest XP raise level without touching a skill. Challenging practice raises a skill without awarding level XP. A teacher or a book grants a technique at skill 0.

The tests are pure domain tests: milliseconds, no content, no engine. `AX-REP` joins them at M7 and companions at M6.

**Skill is not a currency hub.** Skill may *gate* techniques and *modify rates* (research speed, discovery chance); it may never be *spent* as another axis's currency.

**Corollary for `G-2` (`M12`):** the fixtures already answer *whether* the axes are separable. `G-2` answers whether the *content* actually exercises the separation, which is the only part that needs playtest data.

---

## 3. `AX-LVL` — Character Level and XP

### 3.1 What level does and does not do

**Level grants:**

- **+1 attribute point per level.** Nothing else, by owner ruling.
- **Survivability across content bands.** Zone entry is never gated by level (§3.5).
- **Companion tier caps** (from M6).

**Level does not grant:** skill points, technique or ability points, mastery designations, reputation, or competence.

### 3.2 Curve and pacing targets

XP required for level *n* (n ≥ 2): `XP(n) = 100 * (n-1)^1.85`, rounded to the nearest 25, with a flat +15% band multiplier applied at levels 11–20 and 21–30 to keep the low tiers brisk for onboarding.

**Total to level 50 = 2,448,025 XP** (2,369,959 before the band multiplier). This figure is the *evaluated* sum of the formula above, not an estimate — an earlier draft of this document stated ≈1.9M, which understated the formula by 28.8% and would have silently mis-tuned every row below. **Any change to the exponent, the rounding step, or the band multiplier requires re-evaluating this total and re-deriving the pacing table in §3.2 and the `AG-6` rate band in §3.4**, because those numbers are all functions of it.

| Tier | Levels | Target hours (mixed play) | Dominant XP sources | Content band unlocked |
|---|---|---|---|---|
| Novice | 1–10 | 4–6 h | discovery, first quests, tutorial-region combat | Band 1 zones (creature levels 1–12) |
| Adept | 11–20 | 8–12 h | quests, dungeons, first crafted techniques, exploration credit | Band 2 (10–24) |
| Veteran | 21–30 | 13–18 h | multi-stage quests, faction work, band-valid combat | Band 3 (22–36) |
| Champion | 31–40 | 16–22 h | artefact quest stages, boss content, high-difficulty crafting, deep exploration | Band 4 (34–46) |
| Legend | 41–50 | 20–28 h | endgame regions, superbosses, faction apex | Band 5 (44–60) |
| Post-cap | 50+ | open-ended | mastery of designated disciplines, deep techniques, Great Works (§8) | All bands |

**Target total to level 50: 65–85 hours of mixed play.**

- Against the correct 2,448,025 XP total, that implies **≈28.8k XP/hour at 85 h and ≈37.7k XP/hour at 65 h**. The mid-point (75 h) is **≈32.6k XP/hour**.
- These are the numbers `AG-6`'s 0.6×–1.4× band must be built around. The per-tier target rates below must sum to roughly 32.6k/hour at the mid-point, not the ~25.3k the earlier (incorrect) 1.9M total implied.
- A combat-only player should reach 50 in roughly 110–130 hours. This gap is intentional and is the primary expression of "killing creatures must not be the only path".

### 3.3 XP source ledger

Every XP source declares a `source_kind`, which the anti-farm system keys on (§3.4).

| Source kind | Share of expected total | Notes |
|---|---|---|
| `discovery` | ~20% | First-time region/cell entry, location discovery, landmark identification, cartography completion, secret found, lore revelation |
| `quest_objective` | ~35% | Largest single source. Weighted by objective *type* (investigation, crafting, construction, diplomacy valued as highly as combat) |
| `combat` | ~30% | Band-valid kills, boss kills |
| `production` | ~8% | **First-time-only** craft of a given definition, first successful use of a new craft technique or recipe, discovery |
| `social` | ~7% | Faction actions, dialogue outcomes, relationship milestones, companion personal quests, trade milestones |

At most 3 of the 5 source kinds are ever required to level. This is enforced as a design constraint: no content band's expected XP total may depend on more than 3 kinds.

### 3.4 Anti-grind policy (invariants, not vibes)

The charter keeps grinding useful and rarely mandatory busywork (Charter §11). These rules must **not** punish legitimate play: hunting a region for hides, practising a weapon family, or farming a faction is sanctioned player intent, and should remain productive on the *axes it actually feeds*.

All windows are measured in **world time or world ticks, never wall-clock time**; nothing in the domain may read the wall clock (`SYSTEMS.md` S-04).

| ID | Guard | Rule | What it must not do |
|---|---|---|---|
| `AG-1` | Level-band reward | XP multiplier by `(creature_level − player_level)`: `0 → 1.00`, `−1..−5 → 0.60`, `−6..−10 → 0.25`, `−11..−15 → 0.05`, `≤−16 → 0.00`. Above player level: `+1..+5 → 1.15`, `+6..+10 → 1.30`, `>+10 → 1.15` (capped, to discourage suicidal farming) | Must not zero out a *new* creature type a player has never killed, regardless of level |
| `AG-2` | Species novelty | First kill of a species per **world_time day**: full value. Each subsequent kill of the same species that day multiplies XP by `0.9^k` to a floor of 0.10 | Must not affect loot, hides, or skill gain — only `AX-LVL` XP |
| `AG-3` | Spawn-site saturation | Per spawn cluster, after 25 kills inside a rolling window of 30 minutes of simulated play (36,000 world ticks at 20 Hz), XP decays to floor 0.10. The spawner (S-31) doubles the respawn interval until the window empties | Must not change creature behaviour, loot table, or drops |
| `AG-4` | Kill-share neutrality | No rule in `AG-1..AG-3` may reduce any other axis's gain. Skill gain (governed only by its own difficulty gate), material yield, quest/faction objective credit, and hunting/skinning skill progress are unaffected | Prevents "anti-farm" from silently nerfing the sanctioned reasons to grind |
| `AG-5` | Quest sanity | Repeatable quests must use `unique_target` objectives or a hard completion count. Generic `kill N of species S` objectives are non-repeatable once completed | Prevents counter-loop XP |
| `AG-6` | Rate banding | Designed XP/hour band across activities: 0.6×–1.4× of the tier target rate. Any activity measured outside this band for a full tier is re-tuned, not protected | Prevents one degenerate activity becoming mandatory |
| `AG-7` | No conversion loops | No recipe, vendor, or trade route may convert gold → XP, materials → XP beyond the first-time production credit, or salvage → XP | D-09 non-conversion law |
| `AG-8` | Death is the only XP penalty | Death applies **XP debt**, not lost XP: 10% of the current level's XP span is owed. Subsequent XP repays the debt before it counts as progress. Total debt never exceeds one level span, and progress is never reduced, so **a level is never lost** and the debt is always fully repayable by playing. Death never touches skill, knowledge, or reputation | Keeps failure meaningful without invalidating hours of practice, and without a "load an earlier save" reflex |

*Assumption recorded:* the charter leaves the death system open (Charter §23 lists temporary injuries, equipment damage, corpse recovery, and XP debt as candidate options).

- This document assumes the bounded `AG-8` variant — **XP debt, not lost XP** — plus the non-XP consequences owned by `SYSTEMS.md`. Temporary injury and a corpse-recovery trip are named in `VERTICAL_SLICE.md` and are compatible with this.
- **The debt model is the canonical one.** `PROTOTYPE.md` C17 gates Phase 1 on XP-debt arithmetic specifically, and would *fail* an implementation that deducted XP instead.
- It is reversible by a data constant, but any change must be made here first and then propagated to `PROTOTYPE.md` C17 and `VERTICAL_SLICE.md` P8.

**Sanctioned grind, restated.** A player who hunts a valley for six hours should still get:

- hides (hunting/skinning yield);
- weapon-skill progress, for as long as the fights stay challenging;
- faction kill-objective credit;
- rare-drop chances;
- crafting materials.

They will get sharply diminishing **level** XP after the first ~25 kills at one cluster. That is the intended shape: useful, rarely mandatory busywork, and never the only path.

### 3.5 No global level scaling — progression vs. the world

The charter forbids global creature scaling (Charter Pillar 1). Progression must therefore communicate danger *without* scaling content:

1. **Band, not level.** Zones declare a creature-level band. `AG-1` makes under-level content worth little XP and over-level content lethal, so the correct behaviour is to move, not to grind.
2. **Readable danger, three tiers of signal.**
   - (a) *Environmental:* architecture, corpses, weather, ambient audio density, creature silhouette scale.
   - (b) *Mechanical:* a hit taken from an over-band creature produces a distinct screen and audio signature.
   - (c) *Qualitative:* the world-state HUD reports a threat read ("lethal", "severe", "even", "trivial") — never a numeric level over an enemy's head by default.
3. **Damage is band-driven, not HP-sponge.** Over-band creatures kill through damage and mechanics, not inflated health pools (Charter §7 forbids HP sponges). A level-5 player in a level-40 zone dies in 1–2 hits; that is the message.
4. **Level does not unlock zones; it makes them survivable.** Zone entry is physically unrestricted. Knowledge of danger is the gate.
5. **Returning feels earned.** Because the world is static, a level-25 player revisiting a level-40 zone succeeds through attributes, skills, techniques, and equipment gained elsewhere — the felt reward is the *absence of scaling*, exactly as the charter intends.
6. **A level-5 player in a level-40 zone must be able to tell.** Acceptance test: a tester dropped into a band-5 zone at level 5 must self-report "I should not be here yet" within 90 seconds, without UI text saying so.

---

## 4. `AX-ATTR`, `AX-SKL`, `AX-TEC`

### 4.1 Attributes (`AX-ATTR`)

Seven attributes: `might`, `endurance`, `agility`, `precision`, `will`, `insight`, `presence`.

- **This set is canonical** and is the only one any other Phase 0 document may use. `DATA_MODEL.md`'s `CreatureDefinition`/`NPCDefinition` examples and `VERTICAL_SLICE.md`'s content budget both cite it; an earlier draft of each carried a different set (six and five respectively).
- **Changing it later is a save migration, not a rename,** because attributes are persisted per character.
- Each attribute contributes to derived values via data-defined coefficients (D-03):

| Attribute | Primary contributions | Technique prerequisite domain |
|---|---|---|
| `might` | melee power, carry, stagger resistance | two-handed, heavy armour, intimidation |
| `endurance` | Health pool, Stamina pool, Strain tolerance, poison/disease resistance, stamina regen | armour use, shield, sustained techniques |
| `agility` | movement, dodge window, attack recovery, climb | evasion, dual wield, rogue techniques |
| `precision` | ranged accuracy, crit chance, weak-point damage, lockpicking aid | bow, crossbow, marksman, assassin |
| `will` | Focus pool, Resonance, Strain tolerance, fear/charm resistance, concentration under damage | every domain's potency floor, mental resistance |
| `insight` | formula potency ceiling, research speed, technique discovery rate, identify | deep formulas, artifice, enchanting |
| `presence` | persuasion, trade prices, companion affinity gain, follower morale | social techniques, summon command, mercantile |

**Derived values** (S-05; data coefficients in `config.progression`):

- the maxima of the three pools every character has: **Health**, **Stamina** and **Focus**;
- **Resonance**, the capacity to interface with magic (`MAGIC_SUPERNATURAL_AND_COSMIC_SYSTEMS.md`); a species can lack it entirely;
- **Strain tolerance**, the Strain a character can carry before consequences begin.

There is **no mana pool**. Magic spends Focus, accumulates Strain, and may add contextual costs (reagents, charges, blood, environmental energy). Phase 1 makes only Might, Endurance and Will mechanically live (`PROTOTYPE.md`).

**Advancement:**

- 1 point per level (50 points total). A level-up grants nothing else.
- Plus bounded one-time grants: race (at creation), the starting package, rare quests, and at most one permanent attribute item near endgame. The total is capped by §11.3.
- **Attributes never advance by use.**

### 4.2 Skills & disciplines (`AX-SKL`)

A skill measures practised competence at a kind of activity. The disciplines fall into families (after `SKILLS_AND_DISCIPLINES.md`). The initial list is deliberately incomplete — expansion is content, not code.

| Family | Initial disciplines (`skill.<name>`) |
|---|---|
| combat | `one_hand_blade`, `two_hand_blade`, `axe`, `mace_hammer`, `polearm`, `dagger`, `shield`, `bow`, `crossbow`, `thrown`, `staff`, `unarmed`, `tactics` |
| magic | one per domain (`MAGIC_SUPERNATURAL_AND_COSMIC_SYSTEMS.md`): e.g. `force`, `warding`, `vital`, `necromancy`, `conjuration`, `nature`, `transmutation`, `illusion`; traditions are formula families inside a domain |
| crafting | `smithing`, `alchemy`, `leatherworking`, `tailoring`, `woodworking`, `enchanting`, `jewelcrafting`, `artifice`, `masonry`, `arms_maintenance` |
| gathering | `mining`, `herbalism`, `logging`, `hunting`, `skinning`, `fishing`, `farming`, `salvage` |
| world | `athletics`, `stealth`, `survival`, `tracking`, `cartography`, `riding`, `swimming`, `climbing`, `lockpicking`, `pickpocket` |
| social & learned | `persuasion`, `intimidation`, `barter`, `lore`, `medicine`, `music`, `research`, `teaching` |

**Advancement is use under challenge.**

- **The difficulty gate.** An action grants skill XP only when its difficulty exceeds `skill − 15`, so trivial repetition is worthless by construction and needs no punishment logic.
- **Scaling.** The XP scales with how far the difficulty exceeds that gate, and with the outcome. A failure still teaches, at a reduced rate.
- **Novelty.** The first success of a technique, formula or output earns a one-time novelty bonus. This is how experimentation and first successes are rewarded (`CRAFTING_AND_ITEMIZATION.md` §11). Repeating an identical craft stops paying as soon as the character outgrows its difficulty.
- **Effective skill.** It is the base skill plus circumstance modifiers; display always shows the effective value.
- **No other source.** Levels grant no skill points, and no teacher, item or payment raises a skill. A teacher helps by providing *challenging practice* (sparring, supervised work), which is still use.

**Ceilings.** Every discipline can reach the **common ceiling, 60**. Beyond it, only designated masteries advance (§4.3), to **100**.

**Decay.** Below 60, a skill decays at 0.5 points per world_time week of non-use, to a floor of its last band. *Assumption recorded:* decay is bounded and slow; if playtest shows it reads as punishment, set the rate to 0 by data constant. It is not implemented in Phase 1, where it would be unobservable.

**Skill vs. technique disambiguation.** A skill answers "how well do I do a thing I can attempt"; a technique answers "can I attempt it at all" (§4.4). A skilled alchemist who does not know a formula cannot brew it; one who knows it at skill 10 fails often and makes poor salve.

### 4.3 Specialization and the fake-choice guard

Charter §4: *avoid fake choices where every character eventually unlocks everything; build decisions should matter.* The guards:

1. **Mastery designations.**
   - A character may designate a small number of disciplines as **masteries**. The initial data constant is 4, spanning all families, so a battlemage can hold a weapon, a domain and a craft.
   - Only designated disciplines advance past the common ceiling of 60, up to 100, and only they can meet the deepest technique prerequisites.
   - A designation is chosen, never granted by level. Switching one costs a long in-world undertaking, and the incoming discipline must already stand at the ceiling. A dropped mastery keeps its value but stops advancing.
   - This is the old design's specialization rule — weapon aptitudes, school attunement, and "two professions at rank 5" — unified after the merge. It is also `ENDGAME_MASTERY_LEGACY_AND_GREAT_WORKS.md` §4's rule: breadth is open to a novice, and mastery narrows as it deepens.
   - Designations are scheduled with post-cap progression (M12). Phase 1 implements the common ceiling only.
2. **Attribute scarcity.** About 50 points across 7 attributes: a true generalist spreads ~7 per attribute and meets no demanding prerequisite.
3. **Access-gated learning.** Deep techniques need teachers, languages, trust, rare books or discovery. Nobody meets every teacher, and some traditions exclude each other through reputation and culture.

If playtest shows builds feel cramped, raise the designation count rather than lowering skill costs — the constraint is what makes mastery a choice.

### 4.4 Techniques & formulas (`AX-TEC`)

A technique is a known capability:

- a martial move or doctrine (`ability` kind);
- a magic formula (`spell` kind);
- a craft technique or recipe (`recipe` kind).

Together they form a web: each technique names its prerequisites, which are skill thresholds, known techniques, and occasionally attributes. Cross-discipline techniques need more than one discipline (`SKILLS_AND_DISCIPLINES.md` §4).

**Acquisition — the only vector.** Techniques are acquired only through **learning events**, each with a typed source:

- `starting_package`;
- `teacher`;
- `book`;
- `quest`;
- `study` (research);
- `experiment`;
- `discovery` (a first success, or a find in the world);
- `artifact`;
- `culture`.

**Level-ups never grant techniques, and there is no point pool.** Learning is gated by the prerequisites and by access (§4.3, item 3).

**Knowledge is permanent.** A technique is never respecced or lost, except through extraordinary, authored effects. Knowing a technique never changes skill; performing it well depends on skill.

**The charter's "talent/ability trees"** are this web. Only the acquisition currency changed, from points to learning.

---

## 5. Classes: Starting Archetypes Only — the Call

**Decision (`AX-ARCH-1`).** Classes exist as **starting archetypes only**. There is no class field on the character after creation, no class-locked content, no class-locked techniques.

**Justification.** The charter permits this ("Classes may function as starting archetypes rather than permanent restrictions if that produces a better system", Charter §4) and requires "unusual combinations" and hybrids.

- **A permanent class duplicates other axes.** It would answer the same question as `AX-ATTR` + `AX-SKL` + `AX-TEC` — a D-09 violation by construction.
- **It would lock builds.** The 22 charter builds would become 22 lockers rather than 22 emergent outcomes.
- **An archetype answers a different question:** "what is my opening hand?" It solves the real problem — a new player at level 1 lacks enough information to build meaningfully — without constraining the endgame.

**What an archetype is.** A creation-time package of:

- (a) an attribute bias of 4 points, pre-allocated;
- (b) 2 disciplines starting at 15;
- (c) a small starting technique package;
- (d) starting equipment;
- (e) a starting faction contact;
- (f) one unique opening dialogue/scene.

It grants nothing that cannot be reached by any other character. Phase 1 has no archetype choice (`PROTOTYPE.md` A-3). It uses one default starting package defined in `config.progression`.

**Initial archetype set** (content, ~6, expandable in Phase 3): `warrior`, `mage`, `rogue`, `ranger`, `healer/priest`, `artisan`. Races are selected separately and contribute a small, non-dominant modifier package (Charter §5: not cosmetic, not superior).

**Retraining.**

- **Attributes:** allocation is respecable through in-world trainers, at escalating cost and time (the first respec is cheap, later ones expensive).
- **Skills:** practised competence; nothing to respec.
- **Techniques:** knowledge, not respecced.
- **Mastery designations:** switchable at a cost (§4.3).
- **Reputation:** history, and history cannot be undone.

This keeps builds consequential while staying forgiving enough not to force save reloading (Charter §22–23).

---

## 6. Weapon-Family Skills and Martial Techniques (was `AX-WM`)

**Question:** *How deadly am I with this family of weapons?* It is answered by two axes working together: the weapon-family **skill** (how reliably and efficiently) and the known **martial techniques** (what moves are available).

**Competence — the skill.** A weapon skill advances from *effective* contribution with that family, which is the combat meaning of "challenge":

- **It counts:** damage dealt to band-valid targets, successful blocks and parries with a family-appropriate defence, and successful technique use.
- **It does not count:** swinging at nothing; attacking targets below the difficulty gate; kills where the family dealt less than 15% of the damage.

Competence scales consistency, stagger, crit profile and stamina efficiency through data coefficients. It is never a flat +damage ladder.

| Skill band | Name | Typical technique gates (examples, content-defined) |
|---|---|---|
| 0–14 | Untrained | base attacks only; −10% damage |
| 15–29 | Familiar | first family technique (e.g. a guard-break) |
| 30–44 | Proficient | combo branches |
| 45–59 | Adept | the full basic technique set; armour-piercing heavy attacks |
| 60 | Expert — the common ceiling | signature techniques from a teacher |
| 61–100 | Master → Grandmaster (**designated masteries only**) | capstone techniques and family passives (shield block-reflect, bow charge-pierce); deep doctrine chains (`ENDGAME_MASTERY_LEGACY_AND_GREAT_WORKS.md` §4) |

**Capability — the techniques.** Martial techniques are learned from teachers, manuals, quests, and practice-discovery: a technique can be discovered by first successful execution once its prerequisites are met. A technique needs its skill threshold to be *learned*; performing it well needs more.

**Weapon familiarity (separate, intentional).**

- **What it is:** individual rare or legendary weapons track a per-instance `familiarity` 0–5, advanced by using *that item*. Familiarity unlocks that weapon's unique properties (a named sword's special attack).
- **Why it is not another axis:** it is a per-instance property of `AX-EQP`, owned by the item, and it is lost if the item is lost.
- **Why it exists:** so rare items discovered at level 15 remain interesting at level 25 (Charter §8).

---

## 7. Magic — Domain Skills and Formulas (was `AX-MM`)

**Question:** *What can I work, and how well?* It is answered by domain **skills** (competence) and known **formulas** (capability). Resources are Health/Stamina/**Focus**, **Resonance**, and **Strain** (§4.1) — never mana.

**Formulas are knowledge.** A formula is learned only from a learning source:

- study and research at a library or study station (time, materials, `insight` and the `research` skill);
- tomes;
- teachers (faction/NPC gated: cost plus relationship);
- the first successful working of a new effect;
- discovery in the world (a runic tablet, a dead archmage's notes).

Repetition never teaches a formula. This keeps the old design's deliberate asymmetry where it matters: what a mage *knows* is earned with the mind, in safety, through access to sources.

**Domain skill is competence.** Casting a formula whose complexity exceeds `skill − 15` grants that domain's skill XP; trivial repeated casting grants nothing. Higher domain skill means less Strain, more stability and faster casting for the same formula — "mastery includes efficiency" (`MAGIC_SUPERNATURAL_AND_COSMIC_SYSTEMS.md`).

**Domains.** The domain set is `MAGIC_SUPERNATURAL_AND_COSMIC_SYSTEMS.md`'s core domains. Each domain is one magic discipline. A tradition is a culture's family of formulas inside a domain. Phase 1 has three tiny representative domains/traditions with one formula each (M3e, owner ruling).

**No slots and no lockout.**

- **Known formulas stay castable.** A formula above the caster's skill still works, at higher Strain and failure risk — clear risk, never an arbitrary lock ("Unsafe casting").
- **Preparation.** Favourites and prepared formulas are faster and cheaper to call up. Preparation changes speed and convenience, not whether a formula exists.
- **Implements** change precision, stability, throughput and delivery.

**Potency.** It is scaled by `will` + `insight` + the domain skill. Attributes set the ceiling and skill sets the efficiency; neither substitutes for the other.

**Battlemage support** (Charter §6: "genuinely combine melee and magic"). Spellblade techniques require a weapon skill *and* a domain skill threshold, and scale on both. Neither discipline alone reaches them. The hybrid lives in `AX-TEC`, which is exactly where a cross-discipline capability belongs.

---

## 8. Post-Cap and Endgame Mastery (Charter §24)

Level 50 is not the end. Post-cap progression is deliberately **horizontal and specific**, never a resumption of vertical power, and it ends in meaningful endpoints rather than a treadmill (`ENDGAME_MASTERY_LEGACY_AND_GREAT_WORKS.md` §3, §35):

| Post-cap track | Currency | What it changes |
|---|---|---|
| **Mastery of designated disciplines** | challenging use (§4.2–4.3) | skills 60 → 100 in the chosen disciplines; access to the deepest techniques |
| **Deep technique chains and personal techniques** | teachers, research, experimentation, invention | narrower, deeper doctrine (§4 of the endgame document); player-created techniques that become knowledge in the world |
| **Formula refinement** | research projects, rare components, site pilgrimages | efficiency, Strain reduction, variants, ritual access |
| **Artifact-level crafting** | materials + time + discoveries | named items with authored properties (endgame document §24) |
| **Faction apex** | reputation + faction storylines | titles, region influence, NPC allegiance, fast-travel/authority access |
| **Companion stories** | affinity + personal quests | companion capstones, ending states |
| **Great Works, institutions and legacy** | everything above, combined | settlement founding, landmark construction, region restoration, schools, lasting institutions (endgame document §7–§14) |

**Design constraint.** Post-cap tracks must **not** produce a strictly larger damage number than a well-built level-50 character.

- **What they add instead:** new options, access, and authored outcomes.
- **Why:** this keeps the endgame from invalidating the pre-cap curve, and keeps `AX-LVL` from being re-opened as a stealth power axis.
- **No ladder:** there is no prestige or paragon ladder (endgame document §3, §39).

---

## 9. Crafting — Discipline Skills and Craft Techniques (was `AX-PRF`)

**Question:** *What can I produce, and how good can it be?* It is answered by two axes working together:

- **Capability** is the known craft techniques and recipes (`AX-TEC`): "may I attempt to make this at all".
- **Competence** is the crafting skill (`AX-SKL`): success, quality and waste.
- Both are bounded by materials, stations, tools and **complexity**. Complexity gates failure risk, the quality ceiling, and the tools, stations and help required (`CRAFTING_AND_ITEMIZATION.md` §10).

**What advances.**

- **Level:** first-time production grants `production` XP (§3.3).
- **Skill:** challenging crafting grants crafting-skill XP, plus a novelty bonus for a first success. Re-crafting an item the character has outgrown advances nothing.
- **Capability:** comes from learning — teachers, manuals, experimentation, discovery, disassembly and research.

This makes crafting progress a function of *exploration of the technique space*, not of bulk production: nobody becomes a master by making a thousand daggers.

**No ranks.** Profession ranks are gone:

- The old rank ladder's material tiers and quality ceilings are expressed as technique requirements (steel-working is a technique) and as complexity against skill.
- "Two professions at rank 5" is now the mastery-designation rule (§4.3).

**Disciplines.** `CRAFTING_AND_ITEMIZATION.md` §7 lists the candidates; §4.2 lists the initial crafting skills.

- **Gathering** is skills plus resource nodes.
- **Masonry** is a crafting skill that gates construction pieces.
- **Phase 1** has two recipes and one gather→craft loop, with no rank ladder (M3f, owner ruling).

**Crafting stays relevant (Charter §12).**

- **Crafted gear is the only source of:** chosen material-property combinations, socketed bases, masterwork quality, and constructed structures.
- **Dungeon loot supplies:** unique authored properties and set relationships.
- **Neither substitutes for the other**, enforced as a content rule: no dungeon drop may reproduce a masterwork-quality crafted base, and no craft may reproduce a unique authored effect.

---

## 10. `AX-REP` — Reputation

**Question:** *Who will deal with me, and how favourably?*

**Model.** Per-faction standing on a discrete tier ladder, with per-faction numeric accumulation inside a tier:

`Anathema(−5), Outcast(−4), Despised(−3), Disliked(−2), Wary(−1), Neutral(0), Accepted(+1), Trusted(+2), Honoured(+3), Allied(+4), Exalted(+5)`

There is no universal good/evil meter (Charter §20). The same act may raise standing with one faction and lower another, and *different factions may interpret the same act in opposite directions by data-defined reaction tables*.

**Advancement vectors.** Quest outcomes; faction-specific actions (turning in bounties, supplying goods, defending assets); trade volume with faction merchants; crime and its consequences (bounties, notoriety, guard response); companion relationships; public world-state outcomes (Charter §20).

**Gates.** Teachers (technique and formula access), prices and stock, quest availability, region permission and safe passage, fast-travel networks and caravan routes, settlement-building rights, hireling quality, and titles.

**What it never grants: capability.** Reputation opens the door to a master smith's instruction; the instruction still costs time and materials, and the craft still needs skill. This is the cleanest possible illustration of the non-conversion law.

**What it never decides: who attacks.** Standing is access, not an enemy-state variable. Whether an actor becomes hostile is decided separately by:

- relationship;
- legal status;
- faction relation and war state;
- identity knowledge;
- perception (`STEALTH_DETECTION_AND_THREAT.md`).

That is the systemic-hostility seam (`CRIME_LAW_REPUTATION_AND_JUSTICE.md`, `SOCIAL_INTERACTION_LANGUAGES_AND_KNOWLEDGE.md`). A low standing makes a faction refuse service; it does not, by itself, make its members attack on sight.

**Decay.** Standing drifts toward Neutral at a slow rate (per in-game week) and does not decay past `Honoured` except through hostile action. `Anathema` requires explicit acts and is escapable via atonement content, never via payment alone.

---


**As built (M7).** The ladder's numbers live in `config.factions`: eleven tiers on points [-1000, 1000], neutral from -99 to 99, accepted from 100; an ordinary reaction stops at -999, so anathema (-1000) is unreachable until atonement content exists. The tier is derived from the points each time it is read and never stored, so a ladder change reclassifies saved points without a migration. Decay is deferred. "Trade volume" in this section means goods supplied, never coin: no act kind is a trade, so coin never buys standing (E-7). Standing and the personal relationship stay separate layers (ruling 3): a relationship moves by an NPC's own events, standing by what a faction learns of the character's acts, and no code connects them.

## 11. `AX-EQP`, `AX-CMP`

### 11.1 Equipment (`AX-EQP`)

**What it is.** The only axis that gives immediate power, and the only one that can be lost.

- **Sources:** loot, purchase, craft, enchant, socket, augment, artefact quests.
- **Requirements** are expressed as **attribute and skill minima**, not level minima, so an unusually built character can wield something surprising — an intentional route to "unusual combinations".
- **Rarity ladder:** `common, fine, superior, exceptional, masterwork, unique, legendary, artifact`.
- **Durability** exists only where thematically justified (Charter §8: "where appropriate").
- **Enchantment and socketing** are products of the `enchanting` discipline, not a separate axis.

**Anti-degeneracy rules.**

- (a) Global item power per tier is banded so no single drop trivialises a band.
- (b) No item grants permanent progression on another axis. The narrow exceptions are knowledge sources — a book is a learning event (§4.4) — and the bounded attribute grants of §11.3.
- (c) Rare items hold value across ~10 levels via unique properties and per-instance familiarity (§6), not via raw numbers.

### 11.2 Companions (`AX-CMP`)

**Question:** *Who grows alongside me?*

- **Their own progression.** Companions have their own level (1–40, capped by player level tier and affinity), their own equipment, their own skills and techniques, and their own affinity ladder (Charter §2).
- **Advancement.** They advance through shared XP, their own practice, personal quests, gifts, combat cooperation, and disagreements resolved.
- **Non-conversion invariant.** Companion growth never transfers to the player. A companion's power is *party* power, and the player can lose it by offending, dismissing, or losing the companion permanently (Charter §22 permits failure without forcing reload).
- **Party size.** The normal active party is **player + up to 3** companions/hirelings (`COMPANIONS_HIRELINGS_RELATIONSHIPS_AND_PARTIES.md`). The prototype has exactly one companion, with no affinity ladder (M6).

### 11.3 The single bounded exception

`AX-ATTR` may receive a **small, bounded, one-time** permanent gain from a **trainer or quest reward**.

- **What it costs:** access (`AX-REP`), time, and materials.
- **Why it is a gate, not a conversion:** the currency paid is not another axis's progress value, the gain is finite and non-repeatable, and the total of all such gains is capped at ≤8% of the attribute axis's total obtainable points.
- **Other axes:** skills have no such exception; techniques do not need one, because learning is their normal vector.
- **Changing the cap:** any proposal to raise it is a D-09 review item.

---

## 12. Build Expression Without a Class System

**Build identity** is the tuple of:

- dominant attributes;
- core disciplines, and which of them are designated masteries (≤4);
- signature techniques and formulas;
- reputation profile;
- equipment theme;
- companion roster.

No single element defines a build; that is why hybrids are emergent rather than authored.

| Charter build | Attributes | Core disciplines (weapon / magic / craft) | Signature techniques | Notes |
|---|---|---|---|---|
| sword-and-shield fighter | high `end`/`might` | `one_hand_blade`, `shield` / — / `smithing` | block-reflect capstone | shield archetype opening |
| two-handed berserker | `might`/`end` | `two_hand_blade`, `axe` / — / — | stamina-fuelled cleave | low `will` |
| battlemage | `might`/`will`/`insight` | one weapon / `force` / `enchanting` | spellblade techniques (weapon + `force`) | the hybrid needs both skill thresholds |
| mage-warrior | `will`/`insight`/`end` | one weapon / `warding` / — | ward-while-fighting | defensive casting |
| paladin | `might`/`will`/`presence` | `mace_hammer`, `shield` / `vital` or `warding` / — | anti-undead warding | |
| dark knight | `might`/`will` | `two_hand_blade` / `necromancy` / — | essence-fuelled strikes | reputation cost with most factions |
| elemental mage | `insight`/`will` | `staff` / `force` (+1 domain) / — | reaction-chain formulas | |
| necromancer | `insight`/`will` | any / `necromancy`, `conjuration` / `alchemy` | servant and construct formulas | |
| healer | `will`/`insight`/`presence` | — / `vital`, `warding` / `alchemy` | wound-type matching | |
| summoner | `will`/`insight` | any / `conjuration` / — | binding contracts | upkeep-limited |
| ranger | `precision`/`agility` | `bow` / `nature` / `leatherworking` | tracking + terrain | `survival`, `tracking` skills |
| archer | `precision`/`agility` | `bow`, `crossbow` / — / `woodworking` | marksman capstones | |
| beastmaster | `presence`/`agility` | `bow` or `unarmed` / `nature` / — | animal rapport + taming | `hunting` skill |
| druid | `insight`/`will`/`end` | `staff` / `nature` (+`vital`) / — | forms gated on `nature` depth | `herbalism` skill |
| shapeshifter | `end`/`agility`/`insight` | `unarmed` / `nature` (deep) / — | shapeshift forms | forms are deep `nature` techniques |
| rogue | `agility`/`precision`/`presence` | `dagger` / — / — | | `stealth`, `lockpicking` skills |
| assassin | `precision`/`agility` | `dagger` / optional `illusion` (P3) / `alchemy` | crit/weak-point techniques | |
| hunter | `precision` | `bow` / — / — | rare-spawn knowledge | `hunting`, `skinning`, `tracking` skills |
| alchemist | `insight`/`presence` | — / — / `alchemy` (mastery) | discovery-driven formulae | consumable economy |
| artificer | `insight`/`precision` | `crossbow` / `warding` / `artifice`, `enchanting` | construct/socket techniques | |
| battle cleric | `presence`/`will`/`end` | `mace_hammer` / `vital` / — | group restore | solo via companions |
| defensive guardian | `end`/`presence` | `shield`, `polearm` / `warding` / `masonry` | ward + fortification synergy | |

Note the deliberate **cross-axis requirements**: no row is expressible with a single axis, and several (battlemage, shapeshifter, artificer) are *impossible* without a specific pairing of disciplines and techniques. This is the design's proof that the axes are not duplicating each other.

---

## 13. Interaction Rules, Telemetry, and Rejected Axes

### 13.1 Sanctioned interaction verbs (closed set)

Only five interactions between axes are legal. Any code or content that creates a sixth is a design review item:

| Verb | Example | Why legal |
|---|---|---|
| **Gate** | skill and attribute prerequisites on a technique | Changes *when*, not *how much* |
| **Modify rate** | `insight` speeds research | Multiplier on the same currency |
| **Scale effect** | formula potency from `will`+`insight`+domain skill | Composes within one axis's output |
| **Enable hybrid technique** | spellblade techniques need a weapon skill and a domain skill threshold | Creates an option; the technique must still be learned |
| **Provide access** | reputation opens a teacher | Permission only |

**Explicitly illegal:** direct currency exchange; conversion of gold or items into level XP, attributes or skill; and any respec that moves progress between axes. An attribute respec moves points *within* attributes; nothing else is respecced.

### 13.2 Duplication telemetry (the D-09 revisit trigger)

Instrumentation is a Phase-1 deliverable, not a later one. Required signals:

- Per-axis advancement events with timestamps and source kind (`AX-*`, `source_kind`), written to a non-shipped telemetry log.
- Correlation matrix of axis advancement rates over a play session; alert if any pair exceeds `r > 0.8` across ≥10 hours of varied play with no design justification on record.
- Profiling counters: XP/hour band compliance (`AG-6`) per activity type.
- Acceptance criteria for `AX-LVL` vs. crafting: a player who spends 8 hours crafting must gain levels *and* crafting-skill progress, and new techniques through discovery. The level gain must be strictly smaller than the crafting-skill gain, each measured as a fraction of its axis's range (breadth vs. competence separation).

### 13.3 Rejected axes

| Rejected candidate | Why it failed the different-question test | Where it was folded |
|---|---|---|
| **Exploration level** (charter §4 lists "exploration progression") | It answered "how much of the world have I seen", which is already answered by `discovery` XP feeding `AX-LVL`, plus the `cartography` skill, plus discovery-credit world state. As its own axis it would have duplicated `AX-LVL` while adding a second number that moves when you walk | Folds into `AX-LVL` (`discovery` source kind, ~20% of XP), the `cartography` skill, and `WORLD_ARCHITECTURE.md` discovery-credit state that gates fast travel and map detail |
| **Crafting level** | It answered "how much have I crafted", a pure volume counter. Volume-based advancement is the definition of grind-as-busywork | Folds into crafting skills (challenge + novelty) and craft techniques |
| **Weapon mastery beside weapon-family skills** (`AX-WM`, reconciled 2026-09-23) | Same competence at a different granularity, advanced by the same event; the 2×2 could not separate it from a weapon skill by legitimate play | Weapon-family skills (§6); family moves become techniques; aptitudes become mastery designations |
| **Magic mastery as a separate study axis with attunement slots** (`AX-MM`) | It fused competence and knowledge, and the attunement lockout contradicted the owner-approved magic model | Domain skills and formulas (§7); slots removed |
| **Ability/talent points** (`AX-ABL`) | Owner ruling: techniques are learned in the world, not bought at level-up; the audit found no separate need for a point pool | `AX-TEC` (§4.4); the fake-choice guard is §4.3 |
| **Profession ranks** (`AX-PRF`) | A rank was technique knowledge plus skill under another name | Craft techniques plus crafting skills (§9) |
| **Level-granted skill points** | A second currency for the skill axis | Removed; skills advance only by use under challenge |
| **Faction rank as a separate axis from reputation** | Same question as `AX-REP` at a coarser granularity | Folded into `AX-REP` (per-faction standing, per-faction numeric accumulation) |
| **Prestige/reincarnation** | Would convert accumulated progress on all axes into a new currency, violating the non-conversion law, and would invalidate the no-scaling world by resetting the power curve | Not adopted. Post-cap progression is horizontal instead (§8); Soul continuity is its own future design (`SOULS_DEATH_REINCARNATION_AND_LEGACY.md`) |
| **Companion "bond level" as its own axis** | Same question as companion progression within `AX-CMP` | Folded into `AX-CMP` (affinity ladder + personal progression) |
| **A global "power score"** | Rejected in D-09 itself; destroys build legibility and enables the "every character is the same" failure | Not adopted. Any UI that displays a single aggregate power number is a design bug |

---

## 14. Assumptions Recorded

1. **Attribute semantics.** `insight` is used as the intellect/arcana attribute; one of the seven was renamed away from a generic "intelligence" precisely so no axis reads as a universal power stat.
2. **Caps are data constants.** Level cap 50 (Phase 1: 5), attribute cap 60 effective, skills 0–100 with a common ceiling of 60 and designated mastery to 100, companion level 1–40. Changing a cap is a balance pass, not an architecture change.
3. **Death penalty** uses the bounded `AG-8` variant — XP debt rather than deducted XP (see §3.4), which `PROTOTYPE.md` C17 asserts explicitly. The charter leaves this open; this is an inference, documented, and reversible.
4. **Skill decay below 60** is a bounded 0.5 per world_time week. If playtest reads it as punishment, set it to zero; nothing else in this document depends on it.
5. **Respec policy:** attribute allocation is respecable at escalating cost; skills, techniques and reputation are permanent history; designations switch at a cost.
6. **No aggregate power score is ever displayed.** Threat reads are qualitative.
7. **Charter §4 bullets served by other structures.**
   - "Faction relationships" is owned by `SYSTEMS.md` (relationship state) and contributes to `AX-REP`.
   - "Crafting progression" is crafting skills plus craft techniques.
   - "Weapon mastery" and "magic mastery" are weapon-family and domain skills with their techniques, deepened by mastery designations.
   - "Talent/ability trees" are the technique web.
   - "Exploration progression" is folded per §13.3.
8. **The mastery-designation count (4)** is the primary specialization constraint. If playtest shows builds feel cramped, raise the count rather than lowering skill costs — the constraint is what makes mastery a choice.
