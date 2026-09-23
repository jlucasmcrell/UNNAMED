# OTHERREACH — Progression-Axis Reconciliation

**Project:** Otherreach (codename UNNAMED)
**Date:** 2026-09-23
**Status:** Ratified model for M2c. This is the hard gate that `CLAUDE_PHASE1_EXECUTION_PROMPT.md` §5 and `IMPLEMENTATION_PRECEDENCE_AND_DESIGN_STATUS.md` §3 place before M2c. `PROGRESSION.md` has been rewritten to this model and is the normative reference from now on. This document records *why*.

**Inputs:**

- **Normative:** `PROJECT_CHARTER.md` §4, `DECISIONS.md` D-09, `PROGRESSION.md` (pre-reconciliation), `DATA_MODEL.md`, `SYSTEMS.md`, `ROADMAP.md` M2c, `PROTOTYPE.md`, `PERSISTENCE.md` §5.1.
- **Owner-approved direction:** `SKILLS_AND_DISCIPLINES.md`, `CHARACTER_CREATION_AND_LINEAGE.md`, `CRAFTING_AND_ITEMIZATION.md`, `MAGIC_SUPERNATURAL_AND_COSMIC_SYSTEMS.md`, `SOULS_DEATH_REINCARNATION_AND_LEGACY.md`, `ENDGAME_MASTERY_LEGACY_AND_GREAT_WORKS.md`.
- **Owner rulings of 2026-09-23:** see `IMPLEMENTATION_PRECEDENCE_AND_DESIGN_STATUS.md` §3.
- **Historical evidence, not authority:** `reviews/PROGRESSION_SCOPE_ECONOMY_REVIEW.md` §2.

**Owner gate: not triggered.** Every decision below follows mechanically from D-09, the charter, the owner's rulings, or a principle the owner-approved documents already state. Section 7 lists the one place a subjective choice existed and how the documents settle it.

---

## 1. The Axes as They Stood

`PROGRESSION.md` listed ten axes:

| ID | Axis | Question it claimed | Advance vector |
|---|---|---|---|
| `AX-LVL` | Character level | How much of the world can I survive and reach? | XP from five source kinds; granted +1 attribute, +1 skill point and +3 ability points per level |
| `AX-ATTR` | Attributes | What shape is this character? | Level-up allocation plus bounded one-time grants |
| `AX-SKL` | Skills | What can I reliably do? | Use-based XP with a difficulty gate, over a list containing **no weapon, magic or crafting skill** |
| `AX-WM` | Weapon mastery | How deadly am I with this family? | Use-based: effective damage with the family; tiers 0–5 unlock family moves; 3 Grandmaster aptitudes |
| `AX-MM` | Magic mastery | What do I understand, and what can I hold in mind? | Study-based; tiers gate spells; 2–4 level-gated attunement slots, and unattuned schools cannot be cast |
| `AX-ABL` | Abilities/talents | What did I choose to be able to do? | ≈120 points bought at level-up, spent on ≈400 tree nodes |
| `AX-PRF` | Professions | What can I produce, and how well? | New-to-character outputs; ranks 0–5 cap material tier and quality; 2 professions at rank 5 |
| `AX-REP` | Reputation | Who will deal with me? | Faction actions; tiers from Nemesis(−5) to Exalted(+5) |
| `AX-EQP` | Equipment | How strong am I right now? | Loot, purchase, craft |
| `AX-CMP` | Companions | Who grows alongside me? | Shared XP, personal quests; max 6 active |

---

## 2. What Was Wrong

1. **Weapon competence had two homes.** `SKILLS_AND_DISCIPLINES.md` (owner direction) lists weapon families as skills: sword families, axes, blunt, polearms, daggers, shields, bows, crossbows, thrown, unarmed/grappling. `AX-WM` measured the same competence per family, from the same event (effective use in combat).
   - The planned 2×2 test assumed a "high sword skill" that the old skill list could not express. Once weapon skills exist, "high weapon skill, low weapon mastery" is not reachable by legitimate play, because one event advances both.
   - The historical review said the same thing: `AX-LVL`, `AX-SKL` and `AX-WM` differ by valuation, not input.
2. **Crafting competence had two homes.** `SKILLS_AND_DISCIPLINES.md`'s crafting skills (smithing, woodworking, leather, textiles, alchemy, artificing, mechanisms, enchanting, construction) duplicate the `AX-PRF` professions almost one for one.
3. **Magic competence and magic knowledge were fused and locked.**
   - `AX-MM` tiers gated which spells existed for the character.
   - Attunement slots made known schools uncastable.
   - The owner-approved magic model says the opposite: known capability stays accessible; preparation, implements, complexity and Strain change speed, stability and cost, not existence (`MAGIC_SUPERNATURAL_AND_COSMIC_SYSTEMS.md` "No spell-slot limit", "Unsafe casting").
   - `PROGRESSION.md`, `DATA_MODEL.md`, `SYSTEMS.md` and `ROADMAP.md` still assumed a mana pool.
4. **Skills had two currencies.** Level-ups granted skill points, while skills also advanced by use. Nothing said what a skill point bought.
5. **Techniques were bought, not learned.** `AX-ABL` sold ≈400 nodes for level-up points. The owner ruled that techniques are learned in the world and level-ups do not buy them.
6. **Reputation doubled as hostility.**
   - The standing ladder bottomed out at "Hostile" and "Nemesis".
   - `SYSTEMS.md` S-27 emitted `HostileToPlayerChanged` from reputation.
   - `CRIME_LAW_REPUTATION_AND_JUSTICE.md` and the owner rulings keep relationship, legal status, faction relation, war state, tactical hostility and attack legality as separate things.
7. **Party size and endgame vocabulary were stale.**
   - `AX-CMP` and `PERSISTENCE.md` said six active companions; the owner-approved party is player + up to 3.
   - The post-cap track was called "prestige projects", with "paragon" in `SYSTEMS.md`. `ENDGAME_MASTERY_LEGACY_AND_GREAT_WORKS.md` rejects prestige ladders in favour of finite mastery, Great Works and legacy.

---

## 3. The Ratified Axes

Seven axes survive: four are implemented in Phase 1 and three later.

| ID | Axis | The question | Currency / advance vector | Classification | Phase 1? |
|---|---|---|---|---|---|
| `AX-LVL` | Character level | How broadly developed is this character — how much of the world can they survive? | Level XP from five source kinds: `discovery`, `quest_objective`, `combat`, `production` (first-time only) and `social`. The anti-farm guards `AG-1`..`AG-8` apply | True axis | Yes (cap 5) |
| `AX-ATTR` | Attributes | What shape is this character? | Attribute points: **+1 per level-up**, the only thing a level-up grants (owner ruling), plus bounded one-time grants (starting package, race, rare quest, trainer; ≤8 % of the total) | The **allocation of `AX-LVL`**, not an independently advancing axis. Its claim to a distinct question is about distribution, not magnitude. It never advances by use | Yes |
| `AX-SKL` | Skills & disciplines | How reliably, efficiently and well can I do this kind of thing? | Skill XP per discipline from **use under challenge**: a use grants XP only when its difficulty exceeds `skill − 15` (the difficulty gate). A first success earns a novelty bonus. There are no level-granted points | True axis. Now **includes** weapon families (was `AX-WM`), magic domains (the competence half of `AX-MM`) and crafting disciplines (the competence half of `AX-PRF`), alongside gathering, world and social skills | Yes |
| `AX-TEC` | Techniques & formulas | What do I know how to do? | **Learning events:** teacher, book, quest, study/research, experimentation, discovery/first success, artifact, cultural training — plus the starting package. Never level-ups, points, or a purchase of power | A **capability set** (knowledge), not a magnitude. It replaces `AX-ABL`'s node graph and absorbs formula knowledge (the knowledge half of `AX-MM`) and craft techniques/recipes (the permission half of `AX-PRF`) | Yes (formulas, recipes) |
| `AX-REP` | Reputation | Who will deal with me, and how favourably? | Faction actions, quest outcomes, trade | Access/standing only. **Never** tactical hostility or attack legality | No (M7) |
| `AX-EQP` | Equipment | How strong am I right now? | Loot, purchase, craft | Immediate and losable. Per-instance familiarity on rare items is **item state**, not a character axis | M3b |
| `AX-CMP` | Companions | Who grows alongside me? | Companions use the same character model (level, attributes, skills, techniques) beside the player; affinity is relationship state | True axis, non-transferable | M6 (one companion) |

**Post-cap (M12 and later)** is not an axis. It is where `AX-SKL` and `AX-TEC` deepen:

- mastery of designated disciplines (§4.3);
- deep technique chains that get narrower as they get deeper (`ENDGAME_MASTERY_LEGACY_AND_GREAT_WORKS.md` §4);
- personal techniques;
- Great Works, institutions and legacy.

There are no prestige or paragon ladders.

---

## 4. Keep / Merge / Remove, Axis by Axis

For each axis: the question, the currency, whether legitimate play can move it independently, its classification, and whether something else already covers it (`CLAUDE_PHASE1_EXECUTION_PROMPT.md` §5.1).

### 4.1 Character level (`AX-LVL`) — **keep**

- **Question:** breadth of development and survivability.
- **Currency:** level XP. The source-kind ledger and `AG-1..AG-8` are unchanged, with two corrections:
  - `AG-2` counts per **world_time day** instead of per real day, because nothing in the domain may read wall-clock time (`SYSTEMS.md` S-04);
  - `AG-3`'s window is measured in world ticks.
- **Level-up grants attribute points only.**
  - The +1 skill point is removed: skills have one currency.
  - The +3 ability points are removed: there is no point pool.
  - Designation slots do not grow with level. If they did, level would be a hidden technique currency.
- **Independent?** Yes. Level advances from discovery, quests and social outcomes without touching any skill. Skills advance from practice without awarding level XP, because practice and sparring are not XP source kinds.

### 4.2 Attributes (`AX-ATTR`) — **keep, reclassified**

- **Question:** the shape of the character.
- **Currency:** level-up points, plus bounded one-time grants.
- **Reclassified as the allocation of level.** The total magnitude always moves with level; that is by design, not duplication. D-09's "always move together" test applies to *distribution*, which the player controls.
- **Derived values are data coefficients:** Health, Stamina and Focus maxima, Resonance, and Strain tolerance. There is no mana. In Phase 1 only Might, Endurance and Will are mechanically live (`PROTOTYPE.md`).
- **Respec:** only attribute allocation can be respecced (through trainers, at escalating cost). Skills are practised competence and techniques are knowledge; neither is an allocation, so neither is respecced.

### 4.3 Skills & disciplines (`AX-SKL`) — **keep, absorbs weapon, magic-competence and crafting-competence axes**

- **Question:** reliability, efficiency and quality at a kind of activity.
- **Families:** combat (weapon families, shields, unarmed, tactics); magic (domains and traditions); crafting; gathering; world (athletics, survival, riding, …); social.
- **Currency:** skill XP from use under challenge.
  - **The difficulty gate** makes trivial repetition worthless by construction.
  - **The novelty bonus** rewards the first success of a new technique or output (`CRAFTING_AND_ITEMIZATION.md` §11).
  - **Repetition:** identical crafting stops paying once the character outgrows its difficulty. That is the "rapidly diminish" requirement, and it needs no separate punishment logic.
- **Ceilings — how "avoid fake choices where every character eventually unlocks everything" (charter §4) is kept:**
  - Every discipline can rise to a common ceiling (initial data constant: 60).
  - Beyond it, up to 100, only disciplines the character has **designated as masteries** can advance. The designation count is a small data constant; switching costs time and effort.
  - This is the old design's own specialization mechanism (weapon aptitudes, school attunement, "2 professions at rank 5"; `PROGRESSION.md` §14.8: "the constraint is what makes masteries a choice"), unified into one rule after the merge.
  - It is also `ENDGAME_MASTERY_LEGACY_AND_GREAT_WORKS.md` §4's rule: breadth is open to a novice, and mastery narrows as it deepens.
  - Phase 1 implements the common ceiling only. Designations are scheduled with post-cap progression (M12): no Phase-1 character can approach the ceiling at level cap 5, and building the mechanism now would be a system outside `PROTOTYPE.md`'s list (`RK-10`).
- **Decay:** bounded decay below 60 remains a data-constant design option (default 0.5/week of world time). It is not implemented in Phase 1: across a prototype session it is unobservable.
- **Independent?** Yes, from level (§4.1), from techniques (§4.4) and from attributes (they never move by use). Each discipline also moves independently of every other discipline.

### 4.4 Techniques & formulas (`AX-TEC`) — **new name for what `AX-ABL` held, acquired by learning**

- **Question:** what the character knows how to do: martial techniques, magic formulas, craft techniques and recipes.
- **Currency:** learning events, whose source is typed as one of:
  - `starting_package`;
  - `teacher`;
  - `book`;
  - `quest`;
  - `study` (research);
  - `experiment`;
  - `discovery` (first success);
  - `artifact`;
  - `culture`.
- **Rules:**
  - Knowledge is gated by prerequisites: skill thresholds, known prerequisite techniques, and access through teachers, language and trust.
  - Knowing a technique never raises skill. A known technique performed with low skill fails more often or produces lower quality.
  - Knowledge is permanent history. It is not respecced.
- **Charter "talent/ability trees":** served by the technique web, a graph of prerequisites; `SKILLS_AND_DISCIPLINES.md` §1 prefers "discipline webs". Only the acquisition currency changed, from points to learning. Cross-discipline techniques carry the hybrid builds: the old `spellblade` line becomes techniques requiring both a weapon skill and a magic-domain skill.
- **The generic ability-point pool is removed.** The audit found no separate need for it. Its jobs are done elsewhere:
  - scarcity of choice → attribute scarcity, use-based skill growth and mastery designations;
  - hybrid gating → cross-discipline prerequisites;
  - the fake-choice guard → designations plus access-gated learning.
- **Independent?** Yes. From a teacher, a technique can be learned at skill 0. By practice, skill can reach any level without a technique being granted.

### 4.5 Weapon mastery (`AX-WM`) — **merged**

- **Competence** (the tiers' damage, stagger and crit consistency) becomes the weapon-family skill.
- **Family moves** (tier unlocks, signature and capstone moves) become techniques, learned from teachers and through practice-discovery.
- **Aptitudes** become mastery designations.
- **Per-instance familiarity** stays as item state under `AX-EQP`.
- The old 2×2 fixture is replaced by the independence tests in §6.

### 4.6 Magic mastery (`AX-MM`) — **merged**

- **Domain competence** (reliability, efficiency, less Strain for the same effect) becomes a magic-domain skill advanced by challenging casting.
- **Formula knowledge** becomes techniques: learned from study, tomes, teachers, discovery and first successful use. This preserves the old "study-based" identity where it matters: what you *know* still comes from study, not repetition.
- **Attunement slots and the lockout of known schools are removed.** Preparation and favourites affect speed and convenience; complexity, implements and Strain affect cost and stability.
- ROADMAP M3e's named test ("500 casts grant zero school mastery; one research session grants credit") becomes:
  - trivial repeated casting grants no domain skill (difficulty gate) and no formula;
  - research can yield a formula without casting.
  - M3e finalises the wording.

### 4.7 Abilities/talents (`AX-ABL`) — **removed as a point-bought axis**

Its content, the technique graph, is `AX-TEC`. See §4.4.

### 4.8 Professions (`AX-PRF`) — **merged**

- **"May I make this at all?"** becomes knowing the craft technique or recipe (`AX-TEC`).
- **"How well?"** becomes the crafting skill (`AX-SKL`), bounded by materials, stations, tools and complexity (`CRAFTING_AND_ITEMIZATION.md` §10).
- **Ranks are removed.** "Two professions at rank 5" becomes mastery designations.
- **The rank-vs-skill disambiguation survives as technique-vs-skill:**
  - a skilled alchemist who does not know a formula cannot brew it;
  - one who knows it at low skill fails often.
  - ROADMAP M3f's test is reworded that way at M3f.
- **Phase 1:** two recipes and one gather→craft loop, with no rank ladder (owner ruling).

### 4.9 Reputation (`AX-REP`) — **keep, access only**

- **Tier names:** the ladder is renamed to standing terms, `Anathema(−5)`, `Outcast(−4)`, `Despised(−3)` … `Exalted(+5)`, so that no tier name implies an attack order.
- **Hostility events:** `SYSTEMS.md` S-27 no longer emits `HostileToPlayerChanged`. Whether an actor attacks is decided by faction relation, war state, legal status, identity knowledge and perception (`STEALTH_DETECTION_AND_THREAT.md`); that is the future systemic-hostility seam.
- **Scheduling:** not implemented before M7 (`PROTOTYPE.md`: a `faction_id` slot only).

### 4.10 Equipment (`AX-EQP`) and equipment familiarity — **keep**

- Familiarity is per-instance item state, lost with the item. It is not a character axis.
- Requirements are attribute and skill minima, not level (`PROGRESSION.md` §11.1). `DATA_MODEL.md`'s item example still shows a `level` requirement; M3b reconciles it.

### 4.11 Companions (`AX-CMP`) — **keep**

- Companions use the same character model as the player.
- The normal active party is player + up to 3. The max-six rule is removed.
- The prototype has exactly one companion (M6), with no affinity ladder in Phase 1 (owner ruling).

---

## 5. Advancement Vectors (the complete table)

| Event | `AX-LVL` | `AX-ATTR` | `AX-SKL` | `AX-TEC` |
|---|---|---|---|---|
| First entry to a cell/location; discovery | `discovery` XP | — | — | — |
| Quest objective completed | `quest_objective` XP | — | — | Possibly (a quest reward can teach) |
| Band-valid kill | `combat` XP (`AG-1..AG-3`) | — | Weapon/magic skill XP for the uses in the fight, if challenging | — |
| Challenging practice, sparring or training; repeated challenging craft | — | — | Skill XP | — |
| First successful craft of a definition | `production` XP (first time only) | — | Skill XP + novelty bonus | — |
| Experiment or discovery of a new technique | — | — | Novelty bonus if performed | Technique (`experiment`/`discovery`) |
| Teacher, book, study, artifact, culture | — | — | — | Technique |
| Faction/relationship milestone | `social` XP | — | — | — |
| Level-up | — | +1 attribute point | — | — |
| Death | XP **debt** (`AG-8`); progress is never reduced | — | — | — |

**The non-conversion law is restated for the new axes:**

- No axis's advancement reads another axis's currency.
- Gold and items cannot advance level XP, attributes (beyond the bounded one-time grants) or skill XP.
- Knowledge enters only through a typed learning event. That event may name a teacher or a book as its source, but it never changes level, attributes or skill. Paying a teacher buys access to knowledge; the knowledge still needs skill to be worth anything.
- Gates (prerequisites), rate modifiers, effect scaling and access remain the only legal interactions (`PROGRESSION.md` §13.1).

---

## 6. Independence Tests

Every retained neighboring pair gets a fixture that reaches each cell of its 2×2 through sanctioned vectors, with the other axis unchanged as a side effect. These run in M2c as pure domain tests.

| Pair | High A / low B | Low A / high B | Vector used |
|---|---|---|---|
| `AX-LVL` × `AX-SKL` | Level from discovery and quest objectives; a given skill stays at 0 | A skill from challenging practice; level stays 1 | Level XP events vs. skill-use events |
| `AX-SKL` × `AX-TEC` | A skill from practice; a technique stays unknown | A technique from a teacher; the skill stays at 0 | Skill-use events vs. learning events |
| `AX-LVL` × `AX-TEC` | Level from discovery; nothing is known | A technique from a book; level stays 1 | Level XP events vs. learning events |
| `AX-ATTR` × `AX-SKL` | Attributes allocated from level-ups; skills stay at 0 | A skill from practice; attributes unchanged | Allocation vs. skill-use events |
| `AX-SKL` × `AX-SKL` | Practising one discipline | Leaves every other discipline unchanged | Per-discipline ledgers |

**Structural tests** (M2c):

- a level-up grants exactly the configured attribute points and nothing else;
- no public API accepts gold, an item stack, or another axis's currency as the input to level XP, attributes or skill XP;
- learning a technique changes only the knowledge record.

**Owed at the milestone that implements the axis:**

- `AX-REP` × `AX-LVL` and `AX-REP` × `AX-SKL` at M7;
- the companion non-transfer test at M6;
- `AX-EQP` → nothing at M3b (items cannot advance another axis; the M2c typed API already forbids the call).

---

## 7. Rejected Alternatives

| Alternative | Why rejected |
|---|---|
| Keep `AX-WM` beside weapon-family skills | Same question at a different granularity (D-09), advanced by the same event; the 2×2 cannot separate them by legitimate play |
| Keep weapon competence only in `AX-WM` and leave the skill list non-combat | Contradicts owner direction (`SKILLS_AND_DISCIPLINES.md` lists weapon families as skills) and still leaves magic and crafting unreconciled |
| Keep `AX-MM` as a study-only axis that gates spell existence | Contradicts the owner-approved magic model (known capability stays accessible); "study" survives as the source of formula knowledge |
| Keep attunement slots | Owner direction: no hard slot system without justification; preparation changes convenience, not existence |
| Keep the ability-point pool | Owner ruling; the audit found no separate need (§4.4) |
| Keep profession ranks | A rank is technique knowledge plus skill under another name |
| Level-granted skill points alongside use | Two currencies for one axis; owner ruling |
| **A total skill cap** (the sum of all skills bounded; raise one, lower another) | The subjective alternative to designations. Settled by the owner-approved documents rather than taste: `ENDGAME_MASTERY_LEGACY_AND_GREAT_WORKS.md` §4 says breadth stays open to a novice while mastery narrows as it deepens, which a zero-sum cap contradicts; and the existing design already chose limited designations as its specialization constraint (`PROGRESSION.md` §14.8) |
| No specialization constraint at all | Violates charter §4: "Avoid fake choices where every character eventually unlocks everything. Build decisions should matter." |
| Designation slots that grow with level | Would make level a hidden technique currency, against the level-ups-go-to-attributes ruling |
| A universal mana pool | Owner-approved design: Health/Stamina/Focus plus Resonance/Strain and contextual costs |
| Reputation as the enemy-state variable | Owner ruling; hostility is decided by separate relation, legal and tactical state |
| Prestige or paragon ladders after the cap | `ENDGAME_MASTERY_LEGACY_AND_GREAT_WORKS.md` §3 and §39 |

---

## 8. Schema Implications (M2c — `schema_version` 4)

The player section gains a progression record. Everything is fully serialized (`PERSISTENCE.md` §5.1). Derived values are never stored.

| Field | Content |
|---|---|
| `level`, `level_progress_xp`, `xp_debt` | Level, progress within it, and the `AG-8` debt. Level and progress are stored separately so a later balance change to the curve never silently changes an existing level |
| `lifetime_xp_by_source` | Five totals, one per source kind, for the character sheet and telemetry checks |
| `attribute_allocation`, `unspent_attribute_points`, `attribute_grants` | Points spent per attribute (the base comes from `config.progression`), unspent points, and each one-time grant with its source (for the ≤8 % cap) |
| `skills` | Per discipline: level and progress XP |
| `known` | Per technique, formula or recipe ID: the learning source kind, the source reference and the tick |
| `pools` | Current Health, Stamina, Focus and Strain; absent means full. Maxima are derived |
| `guards` | `AG-2` per-species counts for the current world day, and `AG-3` per-cluster kill ticks inside the window |

**Migration 3 → 4:** a character that predates progression becomes level 1 with no XP, no debt, nothing allocated, no skills, nothing known, full pools and empty guards. The step needs no content. Designations, reputation and companion progression add fields at their own milestones, through the same harness.

**Content:**

- **New kind:** `skill`, with IDs `skill.<name>` and a `family` field.
- **`ability` redefined as a technique:** no tree, tier or point cost; prerequisites are skills and known techniques.
- **`spell` redefined as a formula:** costs are Focus and Strain plus optional contextual costs, and its magic domain is a skill.
- **Config:** `config.progression` joins the existing `config.time`, `config.xp_curve` and `config.level_cap`.

---

## 9. Documents Reconciled with This Decision

| Document | Change |
|---|---|
| `PROGRESSION.md` | Rewritten to the §3 model: axis table, level-up grants, skills and ceilings, techniques, merged weapon/magic/crafting sections, reputation tiers, party size, post-cap vocabulary, independence tests, rejected axes |
| `DECISIONS.md` D-09 | Sanctioned axes amended to the §3 set, with the merge recorded |
| `DATA_MODEL.md` | `skill` kind added; `ability` as technique; `spell` costs without mana; creature pools; §3.1 spell row |
| `SYSTEMS.md` | S-05, S-06 (no mana; Focus/Strain), S-08 (attribute points only), S-09 (skills and disciplines), S-10 (techniques and formulas), S-13 (reads Focus/Strain), S-17 (skills and techniques, no ranks), S-27 (access only), the post-cap entry |
| `ROADMAP.md` | M2c work and exit criteria rewritten; mana wording at M3c/M3e; the post-cap line in M12 |
| `PROTOTYPE.md` | The mastery track becomes weapon skill `skill.one_hand_blade`; skills 3; the spell resource bar becomes Health/Stamina/Focus + Strain; the character sheet shows known techniques |
| `PERSISTENCE.md` | Player-section contents; companion roster (player + 3) |
| `GAMEPLAY_LOOPS.md` | `PROGRESS` and `CRAFT` loop inputs/outputs |
| `SKILLS_AND_DISCIPLINES.md` | Status header: reconciled |
| `RISK_REGISTER.md` | Implementation task 9 ("one ability" becomes "one technique") |

**Left for their owning milestones:**

- M3e finalises magic content and the named casting test;
- M3f finalises crafting content and the technique-vs-skill test;
- M3c reconciles `SYSTEMS.md` S-12's aggro table;
- M3b reconciles item level requirements;
- the Phase-2 wording in `VERTICAL_SLICE.md` is reconciled at M7.
