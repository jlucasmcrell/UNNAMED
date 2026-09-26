# M7 research notes: factions, reputation, crime, law, knowledge (key: faction_docs)

Reader: M7 designer who will not reread the sources. Canonical source is the read-only snapshot of origin/main at commit e10d2c4. Every `path:line` cites that snapshot unless marked otherwise. **FACT** = stated in a cited source. **INFERENCE** = my reading. Documents that exist only in the `G:/UNNAMED/docs` working tree, and reports in `G:/UNNAMED_HISTORY`, are labelled **(untracked, authority unconfirmed)**.

---

## 0. Executive summary (the design-relevant conclusions)

1. **Reputation is access only.** It never grants capability and never decides who attacks. This is ratified (D-09 amendment, `docs/DECISIONS.md:209`; `docs/PROGRESSION.md:442-452`; `docs/PROGRESSION_AXIS_RECONCILIATION.md:70,183-187,267`; `docs/SYSTEMS.md:309`). Attack decisions come from relationship, legal status, faction relation, war state, identity knowledge and perception (`docs/PROGRESSION.md:444-450`).
2. **Truth is not justice.** The foundational rule is at `docs/CRIME_LAW_REPUTATION_AND_JUSTICE.md:8`: "What happened, what was perceived, what people believe happened, what can be proven, and what the law says about it are different things." The design rule is at `:248`: "The justice system does not know the truth. The simulation does."
3. **Information is local and travels physically.** "Same faction does not imply instant shared awareness" (`docs/STEALTH_DETECTION_AND_THREAT.md:100`). Reports and warrants travel through couriers, caravans and similar channels (`docs/CRIME_LAW_REPUTATION_AND_JUSTICE.md:103-114`). A remote faction cell does not learn of an act "unless communication permits it" (`docs/SOCIAL_INTERACTION_LANGUAGES_AND_KNOWLEDGE.md:639`). The phase-1 execution brief forbids "global faction aggro" (`docs/CLAUDE_PHASE1_EXECUTION_PROMPT.md:552-554`).
4. **Keep many concepts apart.** At least eleven must stay separate (`docs/SOCIAL_INTERACTION_LANGUAGES_AND_KNOWLEDGE.md:548-564`; `docs/MODDING_COMMUNITY_SERVERS_FEDERATION_AND_PVP_SEAMS.md:768-785`). The lists differ from document to document (§5 below).
5. **The M7 scope in ROADMAP** (`docs/ROADMAP.md:282`) covers:
   - faction definitions and the membership graph;
   - per-faction player reputation with directional reactions and **no universal morality meter**;
   - cross-faction attitude relations;
   - crime/bounty records and pardon state;
   - service, dialogue and territory gating.
   The exit criterion "the same act moves two factions in opposite directions in a fixture" is at `:284`, and the proof "a reputation fixture table" at `:285`.
6. **The only concrete faction schema is `DATA_MODEL.md` §4.13** (`docs/DATA_MODEL.md:557-581`). Code has no faction logic: `FactionSchema` is an empty placeholder (`src/Content/SchemaTypeMapper.cs:253-257`), the faction quest types are refused as "factions (M7)" (`src/Domain/Quests/Quests.cs:154-155`), and there is no `content/factions/` directory.
7. **The docs disagree on crime scope.** ROADMAP M7 and SYSTEMS S-27 put crime records, bounties and pardons in Phase 2. PROTOTYPE, INDEX and VERTICAL_SLICE put crime/bounties in Phase 3 or out of the slice. FEATURE_DELIVERY_STAGING puts a crime "proof" in first and witnesses, disguise and evidence in the slice. See contradiction C-1.
8. **Reputation scales and tier names are unreconciled** across PROGRESSION (−5..+5 named standing tiers), DATA_MODEL (−100/0/150/400 example tiers) and VERTICAL_SLICE (5 tiers including "Hostile"). The faction count also differs: "20+ factions tracked" vs 2 vs "2–3". Owner ruling 3's "smallest useful set" is not recorded anywhere I searched. See C-2 and C-3.
9. **A generic Knowledge/Belief/Information record is repeatedly proposed.** Its fields are proposition/fact, source, confidence, timestamp, subject, location, staleness (`docs/SOCIAL_INTERACTION_LANGUAGES_AND_KNOWLEDGE.md:749-765`; `docs/OTHERREACH_MASTER_HANDOFF_2026-09-23_V3.md:203`). The instruction is "Do **not** build a universal framework until its milestone requires it, but avoid incompatible ad-hoc representations" (`docs/SOCIAL_INTERACTION_LANGUAGES_AND_KNOWLEDGE.md:765`).

---

## 1. Source authority and phase status

| Source | Status line | Owning milestone per INDEX | Notes |
|---|---|---|---|
| `docs/CRIME_LAW_REPUTATION_AND_JUSTICE.md` | "Strong working design" (`:3`) | "M7 (factions, reputation); crime is Phase 3" (`docs/INDEX.md:89`) | Design-extension doc: directional until its milestone reconciles it (`docs/INDEX.md:70`) |
| `docs/SOCIAL_INTERACTION_LANGUAGES_AND_KNOWLEDGE.md` | "Owner-approved design direction"; "Phase 1 is structured dialogue (M4) and minimal NPC continuity only. All deeper social systems are future (owner ruling, 2026-09-23)" (`:3-4`) | M4 only; rest future (`docs/INDEX.md:86`) | §31-§40 hostility layers; §45 knowledge architecture |
| `docs/STEALTH_DETECTION_AND_THREAT.md` | "Foundational design direction" | M3d (`docs/INDEX.md:83`) | Perception already implemented for creatures (M3d) |
| `docs/NPC_SIMULATION.md` | "Signature-system candidate" | M4 small population; rest Phase 2+ (`docs/INDEX.md:85`) | |
| `docs/COMPANIONS_HIRELINGS_RELATIONSHIPS_AND_PARTIES.md`, `docs/COMPANION_MATRIX.md` | "Strong working design" | M6; player+3 later (`docs/INDEX.md:88`) | |
| `docs/MODDING_COMMUNITY_SERVERS_FEDERATION_AND_PVP_SEAMS.md` | "Owner-approved long-term direction"; "Preserve seams now" (`:3-5`) | Seams only (`docs/INDEX.md:97`) | §37-§46 hostility and attack legality |
| `docs/SYSTEMS.md` | normative Phase-0 doc | S-27 is Phase 2 (`docs/SYSTEMS.md:426`) | S-26/S-27 carry M4 reconciliation text |
| `docs/DATA_MODEL.md` | normative | §4.13 FactionDefinition | Only concrete faction schema |
| `docs/PROGRESSION.md` | normative since M2c (`docs/PROGRESSION_AXIS_RECONCILIATION.md:5`) | §10 AX-REP | |
| `docs/ROADMAP.md` | governs WHEN | M7 = Phase 2 (`docs/ROADMAP.md:278-286`) | |
| `docs/PROTOTYPE.md` | governs WHAT is in Phase 1 | — | Faction reputation goes to the vertical slice; crime goes to Phase 3 (`:33`, `:40`) |
| `docs/VERTICAL_SLICE.md` | "Specification for implementation" (Phase 2) (`:5`) | — | 2 factions, 5 tiers; crime system a non-goal |
| `docs/FEATURE_DELIVERY_STAGING.md` | "Planning guidance, not a replacement for `ROADMAP.md`" (`:5`) | — | Crime staging layers |

FACT: authority is `PHASE_0_COMPLETE.md` §1 as restated by `docs/IMPLEMENTATION_PRECEDENCE_AND_DESIGN_STATUS.md:9-11`. If `DECISIONS.md` and `ARCHITECTURE.md` disagree, DECISIONS wins (AGENTS.md). Design-extension docs "are directional, NOT normative, until their milestone reconciles them" (`docs/INDEX.md:70`).

FACT: two items are "design-only / do not implement yet", with "Preserve seams only" (`docs/IMPLEMENTATION_PRECEDENCE_AND_DESIGN_STATUS.md:182-201`):
- "full systemic crime/courts" (`:189`);
- "advanced social simulation (anything beyond M4 structured dialogue and minimal continuity)" (`:190`).

---

## 2. Owner rulings: where each is recorded, and which documents conflict

| # | Ruling (given to me as context) | Recorded where (FACT) | Conflicting or at-risk documents |
|---|---|---|---|
| 1 | Godot Navigation is not authoritative. Pathfinding stays deterministic, headless and inside the domain; it responds to building edits and works across cell seams | **Not found** as an explicit ruling in the snapshot docs, the untracked V4 handoff, or the history reports. I grepped "Navigation", "NavigationServer" and "navmesh". Adjacent principles exist: presentation never mutates state (`docs/SYSTEMS.md:21` C-6; D-11); "Add networking to Phase 1 or 2 (D-12), or make Godot presentation authoritative" is forbidden (`docs/OTHERREACH_MASTER_HANDOFF_2026-09-23_V3.md:223`) | `docs/ROADMAP.md:284` makes "the navmesh updates on placement" an M7 exit criterion and asks for a "navmesh path test" (`:285`). `docs/WORLD_ARCHITECTURE.md:435` says "Recast-style, baked per cell, stitched at cell borders". `docs/M3_STATUS.md:72` describes a Godot-side spike navmesh bake ("Recast's region IDs"). INFERENCE: a conflict with ruling 1 only if "navmesh" means the Godot NavigationServer; the docs never say where the navmesh lives |
| 2 | Building v1 is one storey only | **Not found** ("storey", "stairs", "upper floor" absent) | ROADMAP M7 lists "foundations, walls, floors, roofs, doors" (`docs/ROADMAP.md:283`), which is compatible. `docs/WORLD_BUILDING_AND_PROPERTY_DESIGN.md` was not checked for vertical building (not my topic) |
| 3 | Factions: smallest useful set; no global morality meter; many concepts separate; one act can move two factions differently; information is not magically global | Partially recorded. No morality meter: `docs/PROJECT_CHARTER.md:725`, `docs/ROADMAP.md:282`, `docs/CRIME_LAW_REPUTATION_AND_JUSTICE.md:217`. Separation: `docs/SOCIAL_INTERACTION_LANGUAGES_AND_KNOWLEDGE.md:548-564`, `docs/MODDING_COMMUNITY_SERVERS_FEDERATION_AND_PVP_SEAMS.md:768-785`, `docs/OTHERREACH_MASTER_HANDOFF_2026-09-23_V3.md:145`, `G:/UNNAMED/docs/OTHERREACH_MASTER_HANDOFF_2026-09-24_V4.md:1402-1415` (untracked). Same act, two factions: `docs/PROGRESSION.md:436`, `docs/ROADMAP.md:284`. Information not global: `docs/STEALTH_DETECTION_AND_THREAT.md:100`, `docs/SOCIAL_INTERACTION_LANGUAGES_AND_KNOWLEDGE.md:635-641`, `docs/CLAUDE_PHASE1_EXECUTION_PROMPT.md:552-554`. **"Smallest useful set" is not recorded** | `docs/PROGRESSION.md:48` says "20+ factions tracked, mutually constraining" (C-3). `docs/VERTICAL_SLICE.md:169` has backing a faction make the other "hostile" and "puts town guards on you", which risks a reputation-to-hostility coupling (C-5) |
| 4 | Old C10 (the wolf den forcing every tool) is retired | `docs/PROTOTYPE.md:298` ("Revised by owner ruling (2026-09-24)… No single encounter is required to force every combat tool. The March Spear is not weakened"); `docs/M6_STATUS.md:105,141` | `G:/UNNAMED/docs/OTHERREACH_MASTER_HANDOFF_2026-09-24_V4.md:609-625` (untracked) still calls this "not yet formally owner-ratified". That is stale against PROTOTYPE |
| 5 | No core action may require a radial menu | `docs/PHASE1_ASHEN_HOLLOW_PLAYABLE_CONTENT_BIBLE.md:639-670` (formal rule at `:645`); `docs/M6_STATUS.md:56`; V4 `:78-103` (untracked) | `docs/HUD_INPUT_AND_ACTIONS.md:30,36` lists radial menus as an access method, and says "Controller users should retain the same underlying capability through context/radials". INFERENCE: soft tension, because controller parity could lean on radials |
| 6 | No networking in M7; seams only | D-12 (`docs/DECISIONS.md:248,252`); C-10 "No networking code in Phase 0–2" (`docs/SYSTEMS.md:25`); `docs/IMPLEMENTATION_PRECEDENCE_AND_DESIGN_STATUS.md:196-201`; `docs/MODDING_COMMUNITY_SERVERS_FEDERATION_AND_PVP_SEAMS.md:867-882` | None found |
| 7 | Engine-independent C# authority; presentation observes and commands; dotted definition IDs; ULID instance IDs; sparse deltas | `docs/SYSTEMS.md:16-22` (C-1..C-7); `docs/DATA_MODEL.md:98,123`; V4 `:110-131` (untracked) | None found for factions. Note: the ULID prefix list (`docs/DATA_MODEL.md:123`) has no prefix for crime records, evidence, reports or bounties. INFERENCE: M7 needs new prefixes if these become registry entities |

---

## 3. Foundational principles (verbatim where wording matters)

- Charter §20 (`docs/PROJECT_CHARTER.md:714-727`):
  - actions influence "individuals, settlements, factions, companions, merchants, authorities" (`:718-723`);
  - "Do not reduce morality to one universal "good/evil meter."" (`:725`);
  - "Different people should interpret the same decision differently." (`:727`).
- Charter vision (`docs/PROJECT_CHARTER.md:34`): the player may become "ally or enemy of major factions". Companions and NPCs may have "faction loyalties" and "grudges" (`:112-113`). Quests include "faction conflicts" and "moral decisions" (`:411-412`), and "Not every problem should have an obviously good solution" (`:421`).
- Grinding (`docs/PROJECT_CHARTER.md:442`): "the player needs faction reputation" is a legitimate reason to grind.
- Charter economy (`docs/PROJECT_CHARTER.md:701,708`): value depends partly on "faction"; "Allow trading skills and merchant reputation to matter."
- Charter UI lists "relationships" and "factions" (`docs/PROJECT_CHARTER.md:984-985`). Saves must hold "NPC relationships" and "faction reputation" (`:895-896`). Tests must cover "relationship changes" and "faction reputation" (`:1230-1231`).
- Charter Phase 2 includes "factions" and "reputation" (`docs/PROJECT_CHARTER.md:1154-1155`). Suggested systems include "factions/reputation" and "crime/bounties" (`:1359`).
- Crime doc rules: see `docs/CRIME_LAW_REPUTATION_AND_JUSTICE.md:8,10,248`, quoted in §0.
- Stealth sacred rule (`docs/STEALTH_DETECTION_AND_THREAT.md:8`): "Enemies react to what they can plausibly perceive, infer or communicate — not to what the engine knows happened." Also "No universal encounter telepathy."
- Stealth design rule (`docs/STEALTH_DETECTION_AND_THREAT.md:295`): "Stealth is successful when the character prevents or manipulates information, not when a hidden meter says "invisible.""
- Social core principle (`docs/SOCIAL_INTERACTION_LANGUAGES_AND_KNOWLEDGE.md:7`): "Social skill cannot create a reason for someone to agree where none exists."
- Systemic hostility (`docs/SOCIAL_INTERACTION_LANGUAGES_AND_KNOWLEDGE.md:540`): "Hostility is a relationship/state outcome, not an NPC type."
- Systemic hostility, the single-player seam (`docs/MODDING_COMMUNITY_SERVERS_FEDERATION_AND_PVP_SEAMS.md:762`): "Any sufficiently autonomous person, faction, settlement, institution, or culture can become the player's ally, rival, opponent, or enemy through relationships, law, reputation, ideology, competition, and history." It is "**not** a multiplayer-only feature" (`:764`).
- PvP bridge (`docs/SOCIAL_INTERACTION_LANGUAGES_AND_KNOWLEDGE.md:677`): "Relationship determines attitude. Law/rules determine permissibility. Combat authority determines whether the attempted attack is accepted." Also "Do not assume hostility automatically makes killing legally permitted." (`:679`)
- LLM boundary:
  - the deterministic social system decides; "The model does not decide authoritative persuasion success or relationship state" (`docs/SOCIAL_INTERACTION_LANGUAGES_AND_KNOWLEDGE.md:691`);
  - the AI narrative service may never "change reputation" (`docs/AI_NARRATIVE_SERVICE.md:34`);
  - "The model must not become an omniscient narrator" (`docs/AI_NARRATIVE_SERVICE.md:74`);
  - "Simulation stores relationship truth and memories. Optional AI expresses them; it does not decide them." (`docs/COMPANIONS_HIRELINGS_RELATIONSHIPS_AND_PARTIES.md:223`).
- Maps (`docs/MAPS_CARTOGRAPHY_AND_WORLD_KNOWLEDGE.md:8-14`): "The map represents what the character knows, not what the game database knows." It separates Reality, Knowledge and Interface.
- Things a new session must not do (`docs/OTHERREACH_MASTER_HANDOFF_2026-09-23_V3.md:226,232`): "Make every NPC globally aware of combat, make maps omniscient, or keep one global karma/reputation number". Also "Make prisons real-time waiting … or remote storage magically global."

---

## 4. The conceptual model: what happened, what was perceived, believed, proven, and what the law says

FACT (the crime doc sections, `docs/CRIME_LAW_REPUTATION_AND_JUSTICE.md`), built as a five-layer pipeline:

1. **What happened: objective history.** The simulation "records objective history" (`:10`). The world can record "who acted, who was harmed, where, when, what property changed hands, and what evidence resulted" (`:14`). Self-defense data must be kept: "who initiated aggression, prior threats, witnesses, injuries, and force used" (`:142`).
2. **What was perceived: witnesses.** "Witnesses perceive events, not game-state labels" (`:41`). They may know:
   - rough body/race description, clothing, weapon, face visibility, direction of travel, time, distance, lighting, confidence (`:45-53`).
   - They can be sincerely wrong because of "darkness, fear, distance, intoxication, injury, unfamiliar anatomy, distraction, or elapsed time" (`:55`).
   - They can deliberately lie because of "loyalty, fear, greed, blackmail, revenge, faction pressure, or criminal ties" (`:57`).
3. **What people believe: identity and information records.**
   - The identity ladder: "unknown attacker → body/race description → clothing/equipment → face → name → confirmed legal identity" (`:95`). "Disguise is strongest before identity is firm" (`:97`).
   - Stealth's information record: "hostile suspected?; identity/description; last known position; time observed; weapon seen; confidence; source of information. Information can become stale." (`docs/STEALTH_DETECTION_AND_THREAT.md:146-158`)
   - Social: a lie creates a belief record "proposition/fact; source; confidence; timestamp. The belief can later be disproven." (`docs/SOCIAL_INTERACTION_LANGUAGES_AND_KNOWLEDGE.md:123-130`)
4. **What can be proven: evidence.**
   - Types: blood, bodies, weapons, footprints/tracks, damaged locks, stolen property, magical residue, spell signatures, Otherwake, technological records, documents, testimony, timing/location evidence (`:67-81`).
   - "Evidence should have provenance and confidence, enabling forged, planted, contradictory, destroyed, or weak evidence." (`:83`)
   - Framing (`:85-89`): an NPC "with motive and access can plant goods, forge correspondence, bribe/intimidate witnesses, or exploit the player's criminal associations". "Criminal association raises suspicion rather than automatically proving guilt."
   - Magic feeds it: "Magic can leave residue, patterns, environmental effects, spirit disturbance, Otherwake, or casting signatures. This feeds crime and investigation." (`docs/MAGIC_SUPERNATURAL_AND_COSMIC_SYSTEMS.md:174-178`)
5. **What the law says: jurisdiction and interpretation.**
   - "Jurisdictions then interpret those facts" (`:14`). A killing "might legally become murder, manslaughter, self-defense, lawful bounty action, execution, duel, wartime action, or an unresolved death" (`:16`).
   - "Law belongs to places and institutions" (`:20`).
   - A jurisdiction can regulate ownership/theft, weapons, hunting/poaching, necromancy, summoning and mind magic, grave excavation, Kal ancestral stone, Vaskaal technology, Othergate use, building/trespass, bounty authority, punishment (`:24-35`).
   - "The same act can be legal in one place and severe crime in another." (`:37`)
   - "Jurisdictions may interpret proportionality differently." (`:142`)

The V4 handoff summarises the same model (untracked, authority unconfirmed): "Keep separate: what happened; what was perceived; what is believed; what is proven; what law applies." (`G:/UNNAMED/docs/OTHERREACH_MASTER_HANDOFF_2026-09-24_V4.md:1417-1428`)

INFERENCE (a designer's reading): the pipeline maps to separate state owners.
- **Ground truth** is an event/record log owned by the acting systems. Combat already publishes `Killed`, `DamageApplied` and similar events (`docs/SYSTEMS.md:164`).
- **Perception** is per-actor records. The creature perception record is already per actor (`docs/SYSTEMS.md:270`).
- **Belief** is per-holder information/belief records with a source and a confidence.
- **Proof** is evidence records with provenance and confidence.
- **Legal status** is per-jurisdiction records such as wanted state, warrant and bounty.
- Keeping these in separate slices is what the architecture requires anyway: "One system, one responsibility… A system changes another system's state only by submitting a command or reacting to an event" (AGENTS.md; `docs/RISK_REGISTER.md:298-316` RK-15).

---

## 5. The concepts that must stay separate: every list, compared

| Source | Items listed |
|---|---|
| Owner ruling 3 (given) | personal relationship / trust / fear / grudge / reputation / legal status / faction relationship / war state / known identity / tactical threat / attack legality |
| `docs/SOCIAL_INTERACTION_LANGUAGES_AND_KNOWLEDGE.md:548-564` (§32) | personal relationship; fear; trust; grudge; reputation/standing; legal status; faction relation; war state; known identity; current tactical threat; attack legality. "They interact but answer different questions." (`:564`) |
| `docs/MODDING_COMMUNITY_SERVERS_FEDERATION_AND_PVP_SEAMS.md:770-781` (§43) | personal relationship; fear/trust/grudge; faction standing; **settlement standing**; legal status; faction relation; war state; identity knowledge; tactical hostility; attack legality |
| `docs/OTHERREACH_MASTER_HANDOFF_2026-09-23_V3.md:145` | relationship; fear/trust/grudge; reputation; legal status; faction relation; war state; identity knowledge; tactical hostility; attack legality |
| V4 `:1406-1415` (untracked) | personal relationship; trust/fear/grudge; reputation; legal status; faction relation; war state; known identity; tactical threat; attack legality |
| `docs/CLAUDE_PHASE1_EXECUTION_PROMPT.md:254-261` | relationship; legal status; faction relation; war state; tactical hostility; attack legality (6 only) |
| `docs/PROGRESSION.md:444-450` (who attacks) | relationship; legal status; faction relation and war state; identity knowledge; **perception**. It does not list attack legality |
| `docs/PROGRESSION_AXIS_RECONCILIATION.md:53,186` | relationship, legal status, faction relation, war state, tactical hostility, attack legality; attack decided "by faction relation, war state, legal status, identity knowledge and perception" |
| `docs/CRIME_LAW_REPUTATION_AND_JUSTICE.md:219-228` (reputation layers) | individual relationship; settlement reputation; faction standing; cultural standing; professional reputation; underworld reputation; criminal notoriety; actual legal status/warrants. Also "Legal acquittal does not guarantee social trust." (`:230`) |
| `docs/SOCIAL_INTERACTION_LANGUAGES_AND_KNOWLEDGE.md:489-503` (§28) | **fame** ("How many people know who you are?") vs **reputation** ("What do they think about you?") |
| `docs/SOCIAL_INTERACTION_LANGUAGES_AND_KNOWLEDGE.md:587-603` (§34, settlement hostility) | legal hostility (guards seek arrest); political hostility (bans/expels/seizes rights); social hostility (refuses service/help); military hostility (enemy combatant). "These states are not interchangeable." |
| `docs/CRIME_LAW_REPUTATION_AND_JUSTICE.md:118-128` (wanted-state layers) | suspicious; person of interest; identified suspect; wanted; fugitive; notorious; infamous. "Do not collapse them into one bounty number." |

Illustrative sentences that encode the separation:
- "A town can dislike the player without its civilians becoming kill-on-sight enemies. A guard can personally like the player while being obligated to arrest them." (`docs/MODDING_COMMUNITY_SERVERS_FEDERATION_AND_PVP_SEAMS.md:783-785`)
- "The same NPC can begin friendly, become suspicious, become an enemy, reconcile, or remain personally friendly while belonging to a hostile faction." (`docs/SOCIAL_INTERACTION_LANGUAGES_AND_KNOWLEDGE.md:542`)
- "Individual members can still hold different personal relationships." (`docs/SOCIAL_INTERACTION_LANGUAGES_AND_KNOWLEDGE.md:619`)
- "A low standing makes a faction refuse service; it does not, by itself, make its members attack on sight." (`docs/PROGRESSION.md:452`)
- "Someone can like the player, trust them, respect their skill, and still despise their necromancy." (`docs/COMPANIONS_HIRELINGS_RELATIONSHIPS_AND_PARTIES.md:52`)
- Culture is not race (`docs/SOCIAL_INTERACTION_LANGUAGES_AND_KNOWLEDGE.md:623-631`): "Avoid `all members of biological race X hate the player`. Race is biology. Culture/community/polity is social. Very large acts can affect broad cultural reputation, but individuals retain agency."

INFERENCE: the union of all lists is about 16 concepts:
- personal relationship (per dimension), trust, fear, grudge;
- faction standing, settlement standing, cultural standing, professional reputation, underworld reputation;
- fame;
- legal status / wanted layer / warrants;
- faction relation (faction↔faction attitude), war state;
- identity knowledge (the ladder);
- tactical threat / awareness;
- attack legality.

Ruling 3 asks for the smallest useful set of factions, not of concepts. The concepts must stay separable in the data model even when few are implemented.

---

## 6. Reputation (`AX-REP`)

### 6.1 The axis (FACT)

- **Question:** "*Who will deal with me, and how favourably?*" (`docs/PROGRESSION.md:430`, `:48`).
- **Verb:** "grants **access** | reputation | permission, price, and doors" (`docs/PROGRESSION.md:27`). D-09 amended: "reputation (access only; never hostility)" (`docs/DECISIONS.md:209`).
- **Model** (`docs/PROGRESSION.md:432-434`): "Per-faction standing on a discrete tier ladder, with per-faction numeric accumulation inside a tier". The ladder is `Anathema(−5), Outcast(−4), Despised(−3), Disliked(−2), Wary(−1), Neutral(0), Accepted(+1), Trusted(+2), Honoured(+3), Allied(+4), Exalted(+5)`.
- **Tier names were chosen deliberately** "so that no tier name implies an attack order" (`docs/PROGRESSION_AXIS_RECONCILIATION.md:185`). The old ladder ran "Nemesis(−5) to Exalted(+5)" and bottomed out at "Hostile" and "Nemesis" (`:31`, `:50-52`).
- **Directional reactions** (`docs/PROGRESSION.md:436`): "The same act may raise standing with one faction and lower another, and *different factions may interpret the same act in opposite directions by data-defined reaction tables*." INFERENCE: this is the only mention of a "reaction table". It is data-defined per faction, keyed by act.
- **Advancement vectors, as currencies** (`docs/PROGRESSION.md:438`): "Quest outcomes; faction-specific actions (turning in bounties, supplying goods, defending assets); trade volume with faction merchants; crime and its consequences (bounties, notoriety, guard response); companion relationships; public world-state outcomes". The reconciliation's shorter list is "Faction actions, quest outcomes, trade" (`docs/PROGRESSION_AXIS_RECONCILIATION.md:70`). PROGRESSION's earlier table says "Quest outcomes, faction actions, trade volume, crime" (`docs/PROGRESSION.md:48`).
- **What it gates** (`docs/PROGRESSION.md:440`): "Teachers (technique and formula access), prices and stock, quest availability, region permission and safe passage, fast-travel networks and caravan routes, settlement-building rights, hireling quality, and titles."
- **What it never grants: capability** (`docs/PROGRESSION.md:442`). Example: reputation "opens the door to a master smith's instruction; the instruction still costs time and materials, and the craft still needs skill."
- **What it never decides: who attacks** (`docs/PROGRESSION.md:444-452`), quoted above.
- **Decay** (`docs/PROGRESSION.md:454`): "Standing drifts toward Neutral at a slow rate (per in-game week) and does not decay past `Honoured` except through hostile action. `Anathema` requires explicit acts and is escapable via atonement content, never via payment alone."
- **Permanence** (`docs/PROGRESSION.md:305`, `:587`): "Reputation: history, and history cannot be undone"; "skills, techniques and reputation are permanent history". This is in tension with decay (C-9).
- **Scale:** "−5..+5 standing tiers per faction; 20+ factions tracked, mutually constraining" (`docs/PROGRESSION.md:48`). See C-3.
- **Faction rank** is not a separate axis: it was "Folded into `AX-REP` (per-faction standing, per-faction numeric accumulation)" (`docs/PROGRESSION.md:574`).
- **Death** "never touches skill, knowledge, or reputation" (`docs/PROGRESSION.md:136`, AG-8).
- **Level-up** grants no reputation (`docs/PROGRESSION.md:44,84`).
- **XP link:** "Faction/relationship milestone" gives `social` XP (`docs/PROGRESSION_AXIS_RECONCILIATION.md:213`). The `social` source kind (~7%) comes from "Faction actions, dialogue outcomes, relationship milestones, companion personal quests, trade milestones" (`docs/PROGRESSION.md:117`). INFERENCE: this is legal. The same event feeds two axes' own currencies; no axis reads another's currency.
- **Sanctioned grind:** "farming a faction is sanctioned player intent" (`docs/PROGRESSION.md:123`). AG-4 keeps "quest/faction objective credit" unaffected by anti-farm guards (`:132`). "faction kill-objective credit" survives (`:148`). Also `docs/GAMEPLAY_LOOPS.md:243`.
- **Legal interactions between axes** (`docs/PROGRESSION.md:540-552`): only five verbs are legal: Gate, Modify rate, Scale effect, Enable hybrid technique, "**Provide access** | reputation opens a teacher | Permission only". "Explicitly illegal: direct currency exchange; conversion of gold or items into level XP, attributes or skill".
- **Owed independence tests:** "`AX-REP` × `AX-LVL` and `AX-REP` × `AX-SKL` at M7" (`docs/PROGRESSION_AXIS_RECONCILIATION.md:246`). The 2×2 method is at `:228` ("Every retained neighboring pair gets a fixture that reaches each cell of its 2×2 through sanctioned vectors").
- **Build identity** includes "reputation profile" (`docs/PROGRESSION.md:503`). The dark-knight build carries a "reputation cost with most factions" (`:516`). INFERENCE: acts such as necromancy are meant to move standing through reaction tables.
- **Starting archetype** includes "(e) a starting faction contact" (`docs/PROGRESSION.md:292`). Origin can affect "local reputation" (`docs/CHARACTER_CREATION_AND_LINEAGE.md:62`).
- **Post-cap "Faction apex"** has the currency "reputation + faction storylines" and yields "titles, region influence, NPC allegiance, fast-travel/authority access" (`docs/PROGRESSION.md:381`).

### 6.2 Gold must not buy reputation (FACT)

- E-7 (`docs/GAMEPLAY_LOOPS.md:238`): "Gold buys **goods and services** … never **rank** — not skill, not reputation, not fast-travel permission… Reputation and access are earned through deeds and faction relationship, never purchase."
- Guard (same row): "a Phase-2 grep-level test: no content definition may grant skill, level XP or a reputation rank in exchange for currency."
- `docs/GAMEPLAY_LOOPS.md:224`: "No buying crafting skill with gold alone; no buying fast travel with level."
- INFERENCE, tension C-10: "trade volume with faction merchants" (`docs/PROGRESSION.md:438`) is a gold-spending path that raises reputation.

### 6.3 Reputation layers beyond faction standing (FACT; directional)

- Crime doc layers (`docs/CRIME_LAW_REPUTATION_AND_JUSTICE.md:219-228`), listed in §5.
- Professional reputation (`docs/SOCIAL_INTERACTION_LANGUAGES_AND_KNOWLEDGE.md:507-518`): communities of smiths, healers, bounty hunters, cartographers, scholars and thieves "can know the player differently". It "can unlock commissions, teachers, invitations, and trust without making the player universally persuasive."
- Fame vs reputation (`:489-503`).
- Regional fame (`docs/ENDGAME_MASTERY_LEGACY_AND_GREAT_WORKS.md:1236-1245`): "Historical/fame propagation remains regional/informational. A character may be: legendary among smiths; unknown in a distant culture; infamous to one faction; revered in one settlement."
- Maker reputation affects item value (`docs/ECONOMY.md` §13, "maker reputation").

---

## 7. Factions: schema, runtime system, slice content

### 7.1 FactionDefinition, `kind: faction` (FACT, `docs/DATA_MODEL.md:557-581`)

Field list (schema comment, `:560-563`):
- `attitude_default: {faction_ref: value in [-1,1]}` (cross-faction attitude);
- `members: [npc.*]`;
- `player_start_reputation`;
- `reputation_tiers: [{tier, min, services: [enum], dialogue_flags: [key]}]`;
- `territory: [region|cell]`;
- `laws: [{offense, response, bounty_base}]`;
- `services_gated: [enum]`;
- `joinable`;
- `join_requirements?: (predicate object)`;
- `enemy_of: [faction.*]` ("hard hostility edges").

Worked example `faction.settlement.stoneford_covenant` (`:564-580`):
- `attitude_default: { faction.wildlife: 0.0, faction.bandit.ash_road_crew: -1.0, faction.scholar.conclave_of_ash: 0.3, faction.undead.barrow_host: -1.0 }`;
- `members: [npc.settlement.miller_hale, npc.settlement.warden_sera]`; `player_start_reputation: 0`;
- tiers: `outsider min -100 [trade]`; `tolerated min 0 [trade, rest]`; `trusted min 150 [trade, rest, craft_station, stable] dialogue_flags [access_mill_ledger]`; `sworn min 400 [+bank] dialogue_flags [access_council]`;
- `territory: [region.stoneford_vale, cell.stoneford_town]`;
- `laws: - { offense: assault, response: guards_hostile, bounty_base: 25 } - { offense: murder, response: guards_lethal_bounty, bounty_base: 200 }`;
- `services_gated: [craft_station, stable, bank]`; `joinable: true`;
- `join_requirements: { reputation: { faction_ref: …stoneford_covenant, min: 150 }, quest_ref: quest.settlement.stoneford_missing_flour }`;
- note: "Starting faction; its tiers are the tutorial for 'reputation buys access, not power' (D-09)."

The NPC service enum is `trade|repair|train|craft_station|rest|stable|bank` (`docs/DATA_MODEL.md:295`). Only `trade` is built in Phase 1 (`src/Domain/Social/Social.cs:17-23`).

Definition vs instance (`docs/DATA_MODEL.md:150`):

| | Contents |
|---|---|
| Definition | "membership graph, attitude defaults, service gates, territory" |
| Instance state | "player reputation value/tier, at-war flags, crime records, bounties" |

Other references to factions in the schemas (FACT):
- `faction_ref` on creatures (`docs/DATA_MODEL.md:275`), NPCs (`:297`) and quests (`:441`, example `:479`).
- Creature/NPC definitions carry "faction" (`docs/DATA_MODEL.md:141-142`).
- Generic hostiles "carry no memory and persist only a death flag" (`:308`).
- Quest objective types (`:461`): `faction_reputation` (`faction_ref`, `min`/`max`, `tier?`) and `faction_state` (`faction_ref`, `state`, `value`).
- Reward kind `reputation`, with the example `{ kind: reputation, faction_ref: faction.scholar.conclave_of_ash, amount: 15 }` (`:468,489`). Also `title` and `access` ("unlock location/service").
- Dialogue closed conditions include `reputation` and `faction_state`. Consequences include `add_reputation`, `know_fact`, `open_service`, `set_price_modifier`, `start_combat` (`:525-529`).
- Loot-table condition `faction_state` (`:589`). Merchant `stock_mode: fixed|tag_filtered|faction_supply` (`:609`).
- Config: "`wait_requires_safe_location: true # cannot fast-forward while in combat or in a hostile cell`" (`:722`). INFERENCE: "hostile cell" is undefined. It must be derived from legal/tactical state, not standing.
- Save sensitivity (`:846`): "faction reputation, tiers, crime records and bounties (threshold changes retroactively reclassify a player)".
- Cross-reference: `faction_ref` → `faction` (`:811`). The mod namespace is reserved as `faction.mod.<ns>.*` (`:823`).

### 7.2 S-27 Factions, Reputation & Offense (FACT, `docs/SYSTEMS.md:302-309`)

- **Responsibility:** "Own faction definitions in play and the player's standing with each, plus the legal/offense state derived from it."
- **Owns:** "Faction membership graph, per-faction player reputation value and tier, cross-faction attitude relations, crime records (offense type, location, witnesses, bounty), and pardon/expiry state."
- **Reads:** S-26 (individual memories), S-24 (faction of each witness), S-22 (player actions), S-28 (dialogue consequences), S-12 (assault/kill events).
- **Persistent:** "Reputation values and tiers, faction-state flags (e.g. at-war, alliance broken), crime records and bounties, pardons."
- **Transient:** "Witness-propagation working set, service-availability cache, guard-alert state per settlement."
- **Commands and events:** `AddReputation(factionId, delta, reason)`, `ReportCrime(offense)`, `Pardon`, `SetFactionState`. It emits `ReputationChanged`, `StandingTierChanged`, `FactionStateChanged`, `BountyPlaced`, `BountyCleared`.
- **Key clause:** "Reputation answers *access* (D-09) — it gates services, dialogue, and territory, and is never converted into character power directly. **It never decides who attacks:** tactical hostility and attack legality are derived from faction relation, war state, legal status, identity knowledge and perception, not from a standing tier (`PROGRESSION.md` §10)."
- `HostileToPlayerChanged` was removed (`docs/PROGRESSION_AXIS_RECONCILIATION.md:52,186`). The execution brief still asks for it to "be reconciled" (`docs/CLAUDE_PHASE1_EXECUTION_PROMPT.md:263`, historical). The IMPLEMENTATION_PRECEDENCE item is at `:94`.
- Phase: S-27 is Phase 2 (`docs/SYSTEMS.md:426`). Deferred beyond the slice: "crime/trial/court systems beyond bounties" (`docs/SYSTEMS.md:427`).

**S-27's neighbours (FACT):**

| System | What it says about factions |
|---|---|
| S-03 World State | Holds "Component blobs per entity (health, position, inventory ref, faction, quest refs)" (`docs/SYSTEMS.md:66`) |
| S-12 Combat | "There is no threat or aggro table: who an actor fights is that actor's own record, derived from what it perceived, inferred or was told, and owned by S-23" (`docs/SYSTEMS.md:160`) |
| S-23 Combat AI | Reads "S-27 (NPC faction and disposition)" (`:266`). Persists "a target an actor still hunts after a cell unload (its own perception record, never a shared table)" (`:267`). "Tier C/D actors **do not** run this system; they run S-27's abstract model (D-06)" (`:269`) |
| S-24 NPCs | Owns "disposition toward factions and toward the player" (`:275`). Persists "disposition" (`:277`). Reads S-27 (faction, reputation) and S-26 (`:276`) |
| S-26 Relationships | See §8 |
| S-28 Dialogue | Reads S-27 reputation conditions (`:315`). Emits commands such as `AddReputation` and never mutates (`:318`) |
| S-29 Quests | Reads S-27 (`:325`). Rewards go through `AddReputation` (`docs/DATA_MODEL.md:468`) |
| S-30 Discovery | Reads "S-27 (faction-gated regions)" (`:335`) |
| S-31 Spawning | Reads "S-27 (faction control of a region)" (`:344`). Persists "altered faction control affecting spawn tables" (`:345`) |
| S-36 Merchants | Price inputs include a "faction and reputation modifier" (`:395`). Reads S-27 (`:396`). Persists "price-modifier world flags (famine, war, boom)" (`:397`) |
| S-38 Fast Travel | Reads "S-27 (faction access)" (`:414`). Unlock methods include "reputation … each is recorded so UI can explain how a node was earned" (`:417`) |

ARCHITECTURE ownership (`docs/ARCHITECTURE.md:207-208`):

| Concept | Owner | Note |
|---|---|---|
| Factions | Faction system | "Static definitions + dynamic standing" |
| Reputation | Faction system | "Derived from standings; not separately stored" |

`IWorldState` holds "faction standings" (`:162`). Domain tests must cover "faction reputation" (`:328`).

### 7.3 Vertical-slice factions (FACT, `docs/VERTICAL_SLICE.md`)

- "Factions | **2** | **The Vessmere Compact** (town authority, order, trade) and **the Ashlings** (outcast reclaimers of the old works). Two opposed factions is the minimum for reputation to be a *choice*. A third would need a third region to be meaningful." (`:94`)
- "Reputation tiers | **5 per faction** | Hostile / Wary / Neutral / Trusted / Sworn. Gates dialogue, prices, and one slice objective." (`:95`)
- §5.6 (`:169`): the Compact wants the seam reopened and the Ashlings expelled. "backing it makes the Ashlings hostile, locks D3, and pleases Saelis". Backing the Ashlings "raises Compact prices, puts town guards on you, and pleases Venn". Five tiers gate "**dialogue, prices, and exactly one slice objective each**". The two factions "have *incommensurable* goals".
- New persistent state for factions and reputation: "Per-faction standing, gate flags" (`:120`).
- Faction staff: `npc.outcast_venn` is the faction contact (`:61`). `npc.captain_rooke` is the "Town authority | Reputation gate, crime/bounty stub, home-defense warning" (`:62`). The town has "1 faction representative" (`:48`) and "1 faction outpost" (`:33`).
- Companion exclusivity: Venn is "faction-locked; mutually exclusive in practice with Saelis's preferred outcome" (`:96`).
- The quest objective `reach_reputation` means "Faction standing ≥/≤ tier" (`:188`). Stage 9 is gated on "Compact ≥ Trusted **or** Ashlings ≥ Trusted" (`:219`). Stage 4 needs "the *silver-bind* recipe from the Ashlings" (`:214`), a "recipe gated behind faction access" (`:230`). Stage 7's ash-glass "requires the Ashlings or a dangerous dive" (`:217`).
- Pillar P6 test: 30 minutes of hunting yields "≥ 40 reputation" from "Band-B cleanups" (`:265`). P8 says "faction choices lock content" (`:267`).
- Exit E9: "Faction choice at epic stage 9 locks and unlocks content as specified, in both directions" (`:363`). E13 requires domain tests for "faction reputation" (`:372`).
- Non-goal: "Crime, bounties, jail, guard-pursuit depth | One authority NPC and reputation exist; the *system* does not" (`:336`).
- ROADMAP M9 target: "2–3 factions with mutually constraining reputation" (`docs/ROADMAP.md:303`).
- Prototype reasoning for deferral: factions need "multiple settlements or at least two opposed groups to be meaningful" (`docs/PROTOTYPE.md:33`).

### 7.4 Setting-level faction material (FACT; content direction, not normative per `docs/INDEX.md:99`)

- **The Stone Question** (`docs/STONE_QUESTION.md:3-8`) is "A major faction conflict and multi-stage questline… **no correct answer.** The player should finish the chain having chosen a side".
  - Options (`:102-107`) include "keep them, and be hunted by both".
  - Aftermath (`:126-131`): "If the quarries continue, Kal PCs meet hostility in Ondrek towns and the reverse"; "The instruments, if kept, become a faction target"; "The Ondrek, if they succeed, become a faction that remembers who helped."
- **Companion matrix, an act-based refusal** (`docs/COMPANION_MATRIX.md:24-25`): "The Kal will not travel with anyone who has worked an Ondrek quarry." "The chain's faction choice is therefore also an access choice, and it is permanent" (`:64-65`). The matrix is keyed by race (`:31-43`). Open question 2 (`:172-174`) asks whether "a companion [can] be earned across a refusal".
- Cosmology: factions disagree on Other-travel (`docs/OTHERREACH_COSMOLOGY.md:200`); "religious factions forbid certain crossings" (`:693`).
- `docs/WORLD_MATERIALS.md:81`: "Any "gathering" of [Kal stone] is a war crime to one people and a necessity to another." This is an example of one act read two ways.
- The Phase-1 content bible's non-goals include "full faction warfare" and "full crime/court simulation" (`docs/PHASE1_ASHEN_HOLLOW_PLAYABLE_CONTENT_BIBLE.md:1082-1083`).

---

## 8. NPC attitudes: relationships, trust, fear, grudge, memory

### 8.1 S-26 Relationships & Memory (FACT, `docs/SYSTEMS.md:292-300`)

- **Responsibility:** "Track what individual NPCs think of the player and of each other, and why."
- **Owns:** "Per-pair relationship values on named dimensions (trust, affection, fear, respect, grudge), the memory log of attributed events (event key, actor, timestamp, weight), and derived disposition tiers."
- **Reads:** S-12 (who the player attacked), S-14/S-36 ("an item given or sold to an NPC is what moves affinity; there is no separate `gift` system"), S-29, S-25, S-28, S-24.
- **Persistent:** "Relationship values and the bounded memory log (cap per pair; eviction is oldest-lowest-weight first and must be deterministic — save-version sensitive)."
- **Interfaces:** `RecordEvent(subjectId, objectId, eventKey, weight, sourceRef)`, `GetRelationship(a, b, dimension)`, `GetDispositionTier(a, b)`. It emits `RelationshipChanged`, `MemoryRecorded`, `DispositionTierChanged`. "There is **no** single good/evil meter (charter §20); every consequence is attributed per observer."
- **M4 as built** (`:300`): the runtime `RelationshipSystem` owns `StateSlice.Relationships`, saved with the player (schema 10). Values per dimension are in [-100, 100], and zero is not stored. A change arrives as an internal command naming its reason, published as `RelationshipChanged(npc, dimension, from, to, event)`. "Phase 1 keeps no memory log and no NPC-to-NPC values; the event names are the attribution until the log arrives."

**Code (FACT):**
- `src/Domain/Social/Social.cs:28-39`: `Relationships.Min = -100`, `Max = 100`, and `Dimensions = ("affection","fear","grudge","respect","trust")`.
- `src/World/Runtime/Social.cs:94`: `internal sealed record ChangeRelationship(string NpcId, string Dimension, int Delta, string EventKey) : InternalCommand`.
- `:59`: `RelationshipChanged(string NpcId, string Dimension, int From, int To, string EventKey, long Tick)`.
- `:174-195`: `RelationshipSystem`.
- Save layout (`docs/PERSISTENCE.md:192`): `npc_id`, `dimension`, `value`, non-zero only.

### 8.2 Other relationship statements (FACT)

- Companion dimensions (`docs/COMPANIONS_HIRELINGS_RELATIONSHIPS_AND_PARTIES.md:40-52`): "Avoid one approval score." The useful dimensions are affection, trust, respect, loyalty, fear, attraction and ideological agreement. Compared with S-26: grudge is missing here, and loyalty, attraction and ideological agreement are missing from S-26 and the code.
- Companion values (`:54-58`): companions react "according to personality/culture, not universal Good/Evil" and "can comply, hesitate, refuse, argue, leave, intervene, or report the player".
- Companion crime interaction (`:205-209`): "A companion who witnesses player crime may lie, stay silent, report, assist, become an accomplice, or leave. Companions can also be arrested."
- Hirelings care about "legality" (`:67`).
- Companion association (`docs/SOCIAL_INTERACTION_LANGUAGES_AND_KNOWLEDGE.md:645-654`): companions may become "accomplices; suspected associates; separately innocent; wanted. They can refuse to enter hostile jurisdictions."
- Relationship memory (`docs/SOCIAL_INTERACTION_LANGUAGES_AND_KNOWLEDGE.md:522-534`): "Important relationship changes should preserve reasons." Examples: "saved my child; lied about my brother; paid fair wages; publicly humiliated me; kept a promise." Also "Internal numeric dimensions may exist, but the attributed memory matters."
- NPC relationship memory (`docs/NPC_SIMULATION.md:176-180`): "NPCs remember meaningful player interactions. The simulation stores the fact. Optional AI later expresses it naturally."
- AI narrative memory examples (`docs/AI_NARRATIVE_SERVICE.md:78-87`): "rescued family member; unpaid debt; witnessed necromancy; prior trade; personal betrayal; last meeting". "Periodically compress old memory into summaries while preserving high-value facts."
- Intimidation (`docs/SOCIAL_INTERACTION_LANGUAGES_AND_KNOWLEDGE.md:182-194`): "creates fear/coercion, not affection". A threat needs "capacity; willingness; leverage; target fear; target alternatives". "A terrified NPC may comply now and seek revenge later."
- Bribery is "negotiation, not a boolean `bribable` flag" (`:198-218`).
- Guards (`docs/CRIME_LAW_REPUTATION_AND_JUSTICE.md:61-63`) have "honor, greed, duty, fear, ambition, prejudice, and loyalty". "One guard may accept a bribe. Another may arrest the player for offering it. Another may accept and later blackmail the player."
- Nemeses (`docs/SOCIAL_INTERACTION_LANGUAGES_AND_KNOWLEDGE.md:568-583`; `docs/MODDING_COMMUNITY_SERVERS_FEDERATION_AND_PVP_SEAMS.md:789-801`): "A significant grievance may create autonomous goals". Examples: ruin reputation, theft, sabotage, framing, bounty, attack, assassination, political opposition, joining enemies. "An enemy can later become an ally if events justify it."
- Social failure (`docs/SOCIAL_INTERACTION_LANGUAGES_AND_KNOWLEDGE.md:734-745`): "Avoid hidden one-roll permanent catastrophes". Failure should create "refusal; suspicion; counteroffer; relationship damage".
- Surrender (`docs/COMBAT_DAMAGE_ARMOR_AND_DEATH.md:481-485`): "Enemy personality, faction and reputation influence outcome." `docs/CRIME_LAW_REPUTATION_AND_JUSTICE.md:169`: the reaction to surrender "depends on personality, faction, reputation, and orders".
- Slice companions: "Trust and Respect (−100..100)" (`docs/VERTICAL_SLICE.md:165`).
- Relationship init as definition data: `relationships_init: npc.settlement.warden_sera: { trust: 0.4, respect: 0.2 }` (`docs/DATA_MODEL.md:302-303`). This is refused in Phase 1 (`src/Content/SocialContent.cs:85-89`). Initial relationships are definition data; opinions after events are instance state (`docs/DATA_MODEL.md:162`).

---

## 9. Crime, law and justice: full inventory (FACT, `docs/CRIME_LAW_REPUTATION_AND_JUSTICE.md` unless noted)

- **Crime families** (`:130-138`):

| Family | Crimes |
|---|---|
| Property | theft, burglary, trespass, vandalism, fencing, fraud |
| Violence | assault, attempted murder, manslaughter, murder |
| Public order | unlawful dueling, restricted weapons, disorder |
| Economic | smuggling, counterfeiting, contract fraud |
| Cultural/religious | grave desecration, forbidden ritual, ancestral-stone violation |
| Political | treason, sabotage, espionage |
| Travel/technology | illegal gate use, border violations, restricted technology |

  Soul crimes are added in `docs/SOULS_DEATH_REINCARNATION_AND_LEGACY.md:245`: "forced binding, memory stripping, fragmentation, forced reincarnation, possession, and Soul destruction".
- **Information travel** (`:103-114`): "Reports and warrants propagate through real communication: couriers; pigeons; caravans; magical messages; Vaskaal communication; Othergate networks. Remote places do not learn about crimes instantly unless infrastructure permits it."
- **Identity and disguise** (`:91-101`): the identity ladder, as in §4. Useful changes include "clothing, cloak, armor, mask/helmet, hairstyle, facial hair, cosmetics, or conspicuous equipment". "Laying low can work through leaving the jurisdiction, changing appearance, avoiding known associates, and reducing exposure."
  - Stealth recognition (`docs/STEALTH_DETECTION_AND_THREAT.md:171-183`) depends on "face seen; clothing; race/body silhouette; voice; distinctive equipment; reputation; previous encounters". "Changing clothing helps only when identity was not firmly established."
- **Wanted-state layers** (`:116-128`), as in §5.
- **Bounty hunting** (`:144-159`): "can be a licensed profession". Contracts: capture alive; capture preferred/lethal authorized; dead or alive; confirmed kill; recover property; repossession; locate fugitive; escort prisoner. "Proof can vary by jurisdiction and technology."
- **Repossession** (`:161-163`) recovers leased equipment, wagons, secured trade goods, stolen Vaskaal gear or bonded artifacts. It "creates legal/economic stories without requiring murder."
- **Nonlethal capture and surrender** (`:165-169`): tools include nets, mancatchers, restraints, bolas, clubs, grappling, sedatives, binding magic and stun technology. "NPCs and players can surrender."
- **Arrest** (`:171-177`): "demand surrender → player choice → restraint/processing". "Choices include surrender, negotiate, bribe, flee, or resist. Resistance adds consequences."
- **Sentencing** (`:179-193`):
  - penalties: warning, restitution, fine, confiscation, license loss, service/labor, imprisonment, exile, execution;
  - severity depends on "offense, intent, victim, prior record, cooperation, restitution, jurisdiction, magistrate traits, and corruption".
- **Prison** (`:195-213`): "Prison hurts because **world time passes without the character**."
  - Consequences: unpaid rent/eviction, closed business, expired contracts, neglected crops, companion decisions, attacks on remote property, market changes, seasons/weather, NPC adventurer progression.
  - Presentation: "Use a short illustrated/rendered montage with dates, letters, world-news snippets, and release." Prison "can also create contacts, enemies, rumors, underworld relationships, and teachers."
  - The V3 handoff forbids "real-time waiting" prisons (`docs/OTHERREACH_MASTER_HANDOFF_2026-09-23_V3.md:232`).
- **Criminal organizations** (`:232-236`) include fences, burglars, smugglers, forgers, safe houses, informants, corrupt officials, contraband markets and criminal contracts. "Criminal groups can enforce their own rules."
- **NPC crime** (`:238-240`): "NPCs can steal, smuggle, assault, flee, become wanted, be framed, serve prison time, or collect bounties."
- **Player settlements, later** (`:242-244`) may set policies for "magic, weapons, extradition, property, bounty hunting, punishment, taxation, and guard funding".
- **Jurisdiction on property** (`docs/WORLD_BUILDING_AND_PROPERTY_DESIGN.md:66-91`):
  - types: none, tribal, village, town, city, faction, religious, disputed;
  - it determines "whether building is permitted; taxes/rent; required permits; prohibited crafts; weapon restrictions; necromancy/magic law; guard protection; theft/trespass rules; available services";
  - "Different cultures should have genuinely different laws";
  - property states (`:52-64`): `Unclaimed, WildernessClaim, Leased, Owned, FactionGranted, SettlementControlled, PlayerSettlement`, "design concepts, not present schema commitments";
  - wilderness has "no automatic guard response" (`:107`);
  - base attacks are "reducible through defenses, reputation and location choice" (`:112`);
  - settlements may track "reputation" and "jurisdiction" (`:200-202`).
- **Staging** (`docs/FEATURE_DELIVERY_STAGING.md:49-77`):

| Layer | Contents |
|---|---|
| **Proof** | ownership; theft; assault/murder; witness line of sight; local guard response; surrender; fine/bounty |
| **Vertical slice** | witness descriptions; disguise; evidence; self-defense; local warrants; bounty-hunter contract; prison time skip |
| **Expansion** | corruption; framing; criminal associations; multiple jurisdictions; thieves' guild; extradition; settlement law; Soul crimes |

  Companion "crime testimony/accomplice behavior" is Expansion (`:101`). "faction alarm networks" is Combat/stealth Expansion (`:46`). "faction takeovers" is Quests Expansion (`:123`).

---

## 10. Information, rumor and knowledge propagation

**Stealth (FACT, `docs/STEALTH_DETECTION_AND_THREAT.md`):**
- Awareness states: "Unaware → Suspicious → Investigating → Alerted → Searching → Uneasy → Unaware" (`:69`). "Alerted does **not** mean omniscient" (`:71`).
- Last known position (`:73-82`): the actor knows "last seen location; direction; elapsed time; possibly description. It does not receive live player coordinates."
- Pulling (`:84-96`): "If Enemy A notices the player but Enemy B has no plausible sensory or communication path, B remains unaware." B may join when "A shouts; B sees pursuit; combat becomes audible; alarm is triggered; faction communication reaches them."
- Social behaviour is not psychic aggro (`:98-111`): "Same faction does not imply instant shared awareness." A fortified base is hard "because it deliberately has overlapping sightlines; patrols; whistles; bells; magical alarms; radios. A loose bandit camp may be much easier to isolate."
- Communication channels (`:49-55`): speech/shouting, horns, bells, magical alarm, radio/technology, hive/chemical signals.
- Bodies (`:131-144`): "A silent kill does not alert unseen NPCs." A patrol that finds a body "may inspect; raise alarm; search; change routes; lock entrances; communicate description."
- Information records (`:146-158`) and propagation (`:160-169`): "When Guard A tells Guard B "archer near north wall," B learns that report — not the player's exact current position. This same system later supports: crime witnesses; rumors; faction alerts; disguises."
- Behaviour varies by faction among other factors (`:267-275`).

**Social (FACT, `docs/SOCIAL_INTERACTION_LANGUAGES_AND_KNOWLEDGE.md`):**
- Deception creates beliefs (`:106-130`). The target evaluates "plausibility; prior knowledge; source trust; evidence; verification; behavior". Lies have continuity (`:134-145`).
- NPCs can lie (`:165-178`). "High player insight can reveal contradictions/clues rather than displaying a universal `LIE DETECTED` banner."
- Rumors (`:466-486`): they carry "source; confidence; distortion; spread; age". Worked distortion: "three travelers killed by a wyvern" → "a dragon destroyed a caravan" → "something from the Other ate thirty people".
- NPC-to-NPC influence (`:449-462`): NPCs "persuade; recruit; proselytize; argue; spread rumors; change affiliations. Most offscreen interaction can be abstracted. Tier-A presentation materializes it when the player is present."
- Information propagation (`:635-641`): "Hostility/reputation should spread through information. A remote faction cell does not instantly know about an act unless communication permits it. Disguise and identity knowledge therefore remain relevant."
- Knowledge/Belief architecture (`:749-765`): "proposition/fact; source; confidence; timestamp; subject; location; staleness/expiry. The simulation can know truth while characters hold beliefs." The warning against building a universal framework early is quoted in §0.
- Credentials (`:255-270`): citizenship, guild membership, title, religious office, professional license, academic credential, faction rank, settlement founder status. "Standing is contextual. A scholarly title may mean nothing to bandits."
- Clothing communicates affiliation (`:274-288`): "Do not reduce this to `formal clothes +12 speech`."
- Secret communication (`:356-367`): thieves' cant, guild notation, military signs.

**Elsewhere (FACT):**
- MODDING §45 (`docs/MODDING_COMMUNITY_SERVERS_FEDERATION_AND_PVP_SEAMS.md:805-816`): "A crime or political act does not instantly make every remote faction member omniscient. This preserves: disguise; rumors; jurisdiction; delayed news."
- Endgame history (`docs/ENDGAME_MASTERY_LEGACY_AND_GREAT_WORKS.md:606-624`): public belief carries "source; confidence; distortion; agenda; time elapsed", and "should eventually use the same generalized Knowledge/Belief architecture proposed elsewhere for: crime; rumors; maps; social systems; divination."
- V3 "Systems to design once, deliberately, before any is built ad hoc" (`docs/OTHERREACH_MASTER_HANDOFF_2026-09-23_V3.md:201-206`):
  - "a **Knowledge/Belief/Information** record … used by maps, markers, stealth, witnesses, crime, rumors, NPC memory, deception, divination and news";
  - "**Contracts**, used by bounties, commissions, hirelings…";
  - "**Relationships**, used by companions, witnesses, guards, romance, teaching, crime and social systems".
- Maps: knowledge sources include "rumors" and "faction intelligence" (`docs/MAPS_CARTOGRAPHY_AND_WORLD_KNOWLEDGE.md:38,40`). Accuracy bands are exact/approximate/reported/outdated/disputed (`:59-69`). "Default should avoid unexplained omniscience" (`:150`).
- Diegetic notification channels include "faction intelligence" (`docs/QUESTS_DUNGEONS_WORLD_EVENTS_AND_REPOPULATION.md:52-61`).
- `FactDefinition` kind `fact.` in `facts/` needs "Text key, source, propagation/known-by rules; the `known_fact` command target" (`docs/DATA_MODEL.md:67`). S-22 persists the "journal/known-facts set" (`docs/SYSTEMS.md:257`). Dialogue reads it (`:315`). The dialogue example shows a trust-gated rumour: `conditions: [{ kind: relationship, …, dimension: trust, min: 0.1 }]`, `consequences: [{ command: know_fact, fact_ref: fact.rumour.silver_vein_north_road }]` (`docs/DATA_MODEL.md:542-545`). The `know_fact` objective and condition are refused until "knowledge facts" arrive (`src/Domain/Quests/Quests.cs:157`).
- AI dialogue gets "only facts the NPC is allowed to know: personality; relationships; witnessed events; cultural knowledge; local rumors" (`docs/AI_NARRATIVE_SERVICE.md:64-74`).
- Phase-1 perception already implements "no shared awareness" (FACT, code). `src/World/Runtime/Creatures.cs:121-129`: creatures "act on what they perceive, infer or are told through a call… There is no shared awareness: a packmate out of earshot of a howl knows nothing". At `:308-309`: "A call is its own kind's: a howl brings wolves, not whatever else is in earshot."

INFERENCE: every source keeps the same propagation rule. Knowledge moves only along modelled channels, each with latency and a range: sight, sound, a call, speech, a courier or caravan, a magical message. What arrives is the report, not the truth, and it carries a source, a confidence and an age. No document gives M7 numbers for speed, range or decay of information.

---

## 11. Hostility, tactical threat, attack legality, and the PvP seam

**MODDING doc (FACT, `docs/MODDING_COMMUNITY_SERVERS_FEDERATION_AND_PVP_SEAMS.md`):**
- §37 (`:687-695`): "Combat must not hard-code that another player is always an invalid target. Attack legality is a ruleset decision." "Do not implement PvP during the current single-player roadmap."
- §38 (`:699-710`): possible modes are disabled, consensual duels, consensual flagging, designated zones, faction/war, unrestricted. These are "examples, not settled rules".
- §39 (`:714-726`): "No separate "PvP combat engine.""
- §40 (`:730-741`): "Physical ability to attack does not imply legal permission." Examples: licensed duel, war enemy, murderer in town, outlaw region. "The crime/law system can make PvP much richer than red-name targeting."
- §46 (`:820-839`):

  ```text
  CanAttemptAttack(attacker, target, context)
  ```

  Rules may consider "world type; server rules; duel consent; faction war; legal restrictions; target type". "Do not scatter `if target.IsPlayer return false` throughout damage code. This is a **seam**, not a mandate to implement PvP now."
- §48 (`:867-882`): "What may happen now … avoid hard-coded player-target invalidity". Not now: "no WorldId/ServerId added merely for future possibility".

**Other statements (FACT):**
- IMPLEMENTATION_PRECEDENCE §8 (`docs/IMPLEMENTATION_PRECEDENCE_AND_DESIGN_STATUS.md:219-225`): "Do not hard-code "player targets are always invalid" deep in combat. Attack legality should eventually be a policy seam."
- Faction hostility effects (`docs/SOCIAL_INTERACTION_LANGUAGES_AND_KNOWLEDGE.md:607-619`): hostile factions may "deny services; close territory; issue bounties; sabotage; raid; propagandize; form alliances".
- Property consequences (`:658-669`): enemies target "licenses; bank access; rented property; caravans; businesses; player settlements".
- Enemy self-preservation (`docs/STEALTH_DETECTION_AND_THREAT.md:252-265`): alerted enemies may hide, retreat, call help, flank or flee.
- Hostile settlements (`docs/QUESTS_DUNGEONS_WORLD_EVENTS_AND_REPOPULATION.md:224-228`) still have food, trade, housing and families. "Conquest should create economic/population consequences."
- Phase-1 faction hostility (M3d ruling, `docs/ROADMAP.md:226`): "Faction hostility is "every creature against the player, indifferent to each other"".

**Code (FACT):**
- Tactical hostility is derived from perception. `src/World/Runtime/Creatures.cs:36` has `public bool Hostile => Mind == CreatureMind.Engaged;`, and perception targets the player's body (`:294-296`).
- A search for `CanAttemptAttack` / `IsPlayer` found nothing in `src`. INFERENCE: no attack-legality seam exists yet, and nothing hard-codes player invalidity either. Hostility is implicit ("every creature against the player").

---

## 12. Reputation-gated services, dialogue, territory and prices

FACT, by gated surface:

| Surface | Sources |
|---|---|
| Services | FactionDefinition `reputation_tiers[].services` and `services_gated` (`docs/DATA_MODEL.md:561-577`); S-27 "service-availability cache" (`docs/SYSTEMS.md:308`); social hostility "refuses service/help" (`docs/SOCIAL_INTERACTION_LANGUAGES_AND_KNOWLEDGE.md:597-598`) |
| Dialogue | `reputation_tiers[].dialogue_flags` (`docs/DATA_MODEL.md:561,571-572`); dialogue conditions `reputation`, `faction_state` (`:525-526`); S-28 reads S-27 (`docs/SYSTEMS.md:315`) |
| Territory and region permission | `territory: [region\|cell]` (`docs/DATA_MODEL.md:561`); S-27 gates "territory" (`docs/SYSTEMS.md:309`); S-30 "faction-gated regions" (`:335`); AX-REP gates "region permission and safe passage" (`docs/PROGRESSION.md:440`); "close territory" (`docs/SOCIAL_INTERACTION_LANGUAGES_AND_KNOWLEDGE.md:612`) |
| Prices and stock | S-36 "faction and reputation modifier" (`docs/SYSTEMS.md:395`); merchant `stock_mode: faction_supply` (`docs/DATA_MODEL.md:609`); dialogue `set_price_modifier` (`:529`); ECONOMY regional pricing responds to "faction law/tax" (`docs/ECONOMY.md:105`); slice "raises Compact prices" (`docs/VERTICAL_SLICE.md:169`); E-1 arbitrage guard: stock and prices respond to "faction standing" (`docs/GAMEPLAY_LOOPS.md:232`) |
| Travel | S-38 reads "faction access" and has a reputation unlock method (`docs/SYSTEMS.md:414,417`); RETURN loop inputs "faction standing and permissions" (`docs/GAMEPLAY_LOOPS.md:164`); "access can be lost (faction standing collapses…)" (`:167`); slice RETURN "faction-gated access" (`:285`) |
| Teachers and techniques | "reputation gates teachers" (`docs/PROGRESSION.md:35`); teachers are "faction/NPC gated: cost plus relationship" (`:349`); "some traditions exclude each other through reputation and culture" (`:242`) |
| Quests | `faction_reputation` / `faction_state` objectives (`docs/DATA_MODEL.md:461`); `reach_reputation` (`docs/VERTICAL_SLICE.md:188`); `join_requirements` predicate (`docs/DATA_MODEL.md:563,579`) |
| Loot | table-level condition `faction_state` (`docs/DATA_MODEL.md:589`) |
| Spawns | "altered faction control affecting spawn tables" (`docs/SYSTEMS.md:345`); repopulation via "faction expansion" (`docs/QUESTS_DUNGEONS_WORLD_EVENTS_AND_REPOPULATION.md:104`) |
| Building | settlement-building rights (`docs/PROGRESSION.md:440`); `FactionGranted` property (`docs/WORLD_BUILDING_AND_PROPERTY_DESIGN.md:60`); receiving property "from a quest/faction" (`:121`) |
| Recruitment | RECRUIT inputs "reputation and faction access" (`docs/GAMEPLAY_LOOPS.md:140`); "hireling quality" (`docs/PROGRESSION.md:440`) |
| Barter | an actor can compare "reputation/relationship" among money, materials, equipment and services (`docs/ECONOMY.md:82-92`). INFERENCE: tension with non-conversion (C-10) |
| Loop drains and outputs | FIGHT drains "faction standing with the killed party's allies" (`docs/GAMEPLAY_LOOPS.md:92`); QUEST drains "reputation cost with the faction the player acted against" (`:154`); FIGHT outputs "a faction's response to the kill" (`:90`) |

---

## 13. Consolidated data shapes proposed by the documents

| Shape | Fields (as documented) | Source |
|---|---|---|
| FactionDefinition | `attitude_default{faction_ref→[-1,1]}`, `members[npc]`, `player_start_reputation`, `reputation_tiers[{tier,min,services[],dialogue_flags[]}]`, `territory[region\|cell]`, `laws[{offense,response,bounty_base}]`, `services_gated[]`, `joinable`, `join_requirements?`, `enemy_of[faction]` | `docs/DATA_MODEL.md:560-563` |
| Faction instance state | player reputation value/tier, at-war flags, crime records, bounties | `docs/DATA_MODEL.md:150` |
| S-27 persistent | reputation values and tiers; faction-state flags (at-war, alliance broken); crime records and bounties; pardons | `docs/SYSTEMS.md:307` |
| Crime record | offense type, location, witnesses, bounty; pardon/expiry state | `docs/SYSTEMS.md:305` |
| Reputation event log | "Reputation values plus their bounded event log persist; no oscillation across reload" (T-21, P2) | `docs/PERSISTENCE.md:595` |
| Relationship | per pair × dimension {trust, affection, fear, respect, grudge}; built as [-100,100] ints | `docs/SYSTEMS.md:295,300`; `src/Domain/Social/Social.cs:28-39` |
| Relationship memory | event key, actor, timestamp, weight; bounded per pair; evict oldest-lowest-weight deterministically | `docs/SYSTEMS.md:295,297` |
| `RecordEvent` | `(subjectId, objectId, eventKey, weight, sourceRef)` | `docs/SYSTEMS.md:299` |
| Witness knowledge | body/race description, clothing, weapon, face visibility, direction of travel, time, distance, lighting, confidence | `docs/CRIME_LAW_REPUTATION_AND_JUSTICE.md:45-53` |
| Information record | hostile suspected?, identity/description, last known position, time observed, weapon seen, confidence, source; staleness | `docs/STEALTH_DETECTION_AND_THREAT.md:148-158` |
| Belief record | proposition/fact, source, confidence, timestamp | `docs/SOCIAL_INTERACTION_LANGUAGES_AND_KNOWLEDGE.md:125-128` |
| Generic knowledge record | proposition/fact, source, confidence, timestamp, subject, location, staleness/expiry | `docs/SOCIAL_INTERACTION_LANGUAGES_AND_KNOWLEDGE.md:753-761` |
| Rumor | source, confidence, distortion, spread, age | `docs/SOCIAL_INTERACTION_LANGUAGES_AND_KNOWLEDGE.md:468-474` |
| Public/historical belief | source, confidence, distortion, agenda, time elapsed | `docs/ENDGAME_MASTERY_LEGACY_AND_GREAT_WORKS.md:610-616` |
| Evidence | type (13 listed); provenance; confidence; states forged/planted/contradictory/destroyed/weak | `docs/CRIME_LAW_REPUTATION_AND_JUSTICE.md:67-83` |
| Identity ladder | unknown → body/race → clothing/equipment → face → name → confirmed legal identity | `:95` |
| Wanted state | suspicious, person of interest, identified suspect, wanted, fugitive, notorious, infamous | `:118-126` |
| Awareness | Unaware, Suspicious, Investigating, Alerted, Searching, Uneasy | `docs/STEALTH_DETECTION_AND_THREAT.md:69` |
| Self-defense record | initiator, prior threats, witnesses, injuries, force used | `docs/CRIME_LAW_REPUTATION_AND_JUSTICE.md:142` |
| Bounty contract types | 8 listed | `:150-157` |
| Sentencing | 9 penalties; severity inputs (9) | `:181-193` |
| Settlement hostility | legal / political / social / military | `docs/SOCIAL_INTERACTION_LANGUAGES_AND_KNOWLEDGE.md:591-601` |
| Jurisdiction types | none/tribal/village/town/city/faction/religious/disputed | `docs/WORLD_BUILDING_AND_PROPERTY_DESIGN.md:70-77` |
| FactDefinition | text key, source, propagation/known-by rules | `docs/DATA_MODEL.md:67` |
| Attack-legality seam | `CanAttemptAttack(attacker, target, context)` with inputs world type, server rules, duel consent, faction war, legal restrictions, target type | `docs/MODDING_COMMUNITY_SERVERS_FEDERATION_AND_PVP_SEAMS.md:825-835` |
| NPC adventurer state | includes "faction standing; relationships; known locations/maps" | `docs/NPC_SIMULATION.md:33-48` |
| Companion identity | includes "factions; fears; … moral limits" | `docs/COMPANIONS_HIRELINGS_RELATIONSHIPS_AND_PARTIES.md:20-34` |

**Persistence placement (FACT).**
- `player.msgpack` is "fully serialized: character, inventory, equipment, skills, quests, reputation" (`docs/PERSISTENCE.md:111`). It includes "faction reputation and crime records, relationship values and the memory log" (`:188`).
- Content definitions, factions included, are not saved; only IDs are (`:80`).
- "Derived caches (encumbrance, faction power, settlement wealth)" are recomputed once after migration (`:86`).
- The current schema is 13 (posture) (`:198`). INFERENCE: M7 state is schema 14+, with a historical fixture per `tests/Persistence.Tests/Fixtures/README.md` (AGENTS.md).
- T-20: "Relationship deltas persist and remain attributable to their source events" (P2, `:594`).
- Open problem: if respawn later depends on "faction control", the dependency must be enumerated first (`:278`).

---

## 14. Simulation tiers and faction state

- WORLD_ARCHITECTURE §7.1 (`docs/WORLD_ARCHITECTURE.md:277-288`): abstract tiers B/C may **never** advance "Dialogue, relationship decisions, faction-defining acts" (`:284`) or "Combat resolution of any kind" (`:279`). "abstract tiers never produce a state that Tier A cannot legally instantiate" (`:288`).
- §9 (`:383-395`):
  - "Faction control, regional tension | **Yes** | C | Bounded event log, no simulation of individual faction members" (`:390`);
  - named NPCs at C/D: "relationships/quest flags persist. They do *not* gain skills, items or wealth through abstract play in Phase 2" (`:385`);
  - generic NPCs: "Existence + schedule band + settlement membership" (`:386`).
- Persistent interiors run at Tier C including "occupant factions" (`:370`).
- Promotion must not place an actor "inside a player building without permission" (`:297`).
- NPC_SIMULATION Tier D is "Large-step analytical advancement: income; residence; long travel; profession activity; **reputation**; goal changes" (`docs/NPC_SIMULATION.md:86-93`). SYSTEMS assumption 2 defines "Tier D = stored state, no ticking" (`docs/SYSTEMS.md:449`). See C-8.
- Offscreen NPC-to-NPC influence "can be abstracted" (`docs/SOCIAL_INTERACTION_LANGUAGES_AND_KNOWLEDGE.md:460`).
- NPC goal planning "should be deterministic/systemic where practical, not LLM-controlled" (`docs/NPC_SIMULATION.md:56`).
- S-23: Tier C/D actors "run S-27's abstract model (D-06)" (`docs/SYSTEMS.md:269`). INFERENCE: S-27's "abstract model" is not defined anywhere.
- Phase-1 status: every NPC is simulated in full; no tiers (`docs/SYSTEMS.md:280`; `docs/IMPLEMENTATION_PRECEDENCE_AND_DESIGN_STATUS.md:146`).

---

## 15. Phase 2 (M7) vs later

| Item | Phase per docs | Citation |
|---|---|---|
| Faction definitions, membership graph, per-faction reputation with directional reactions, cross-faction attitudes, gating of services/dialogue/territory | **M7 / Phase 2** | `docs/ROADMAP.md:282`; `docs/SYSTEMS.md:426` |
| "the same act moves two factions in opposite directions in a fixture"; a reputation fixture table | M7 exit/proof | `docs/ROADMAP.md:284-285` |
| `AX-REP` × `AX-LVL` and `AX-REP` × `AX-SKL` independence tests | M7 | `docs/PROGRESSION_AXIS_RECONCILIATION.md:246` |
| Crime/bounty records and pardon state (S-27) | M7 per ROADMAP; **Phase 3 per PROTOTYPE/INDEX**; non-goal in slice | `docs/ROADMAP.md:282` vs `docs/PROTOTYPE.md:40`, `docs/INDEX.md:89`, `docs/VERTICAL_SLICE.md:336` |
| S-26 relationship system (memory log, NPC↔NPC) | Phase 2 | `docs/SYSTEMS.md:426` |
| Faction quest objective types `faction_reputation`, `faction_state` | M7 (code says "factions (M7)") | `src/Domain/Quests/Quests.cs:154-155` |
| Quest-level `faction_ref` | refused in Phase 1 | `docs/DATA_MODEL.md:517`; `src/Content/QuestContent.cs:29`; `tests/Content.Tests/QuestContentTests.cs:118-122` |
| Knowledge facts (`know_fact`) | "knowledge facts" (unscheduled) | `src/Domain/Quests/Quests.cs:157`; `docs/INDEX.md:76` ("the knowledge model later") |
| Persuasion, rumour, languages, beliefs, hostility layers | future | `docs/IMPLEMENTATION_PRECEDENCE_AND_DESIGN_STATUS.md:147`; `docs/ROADMAP.md:252`; `docs/M4_STATUS.md:76` |
| Full systemic crime/courts | design-only | `docs/IMPLEMENTATION_PRECEDENCE_AND_DESIGN_STATUS.md:189`; `docs/SYSTEMS.md:427` |
| Settlement simulation; prices by faction | M10 | `docs/ROADMAP.md:312-313` |
| Faction apex | post-cap (M12+) | `docs/PROGRESSION.md:381` |
| PvP, networking | not scheduled; seams only | `docs/MODDING_COMMUNITY_SERVERS_FEDERATION_AND_PVP_SEAMS.md:695`; D-12 |
| Faction alarm networks, faction takeovers | Expansion layer | `docs/FEATURE_DELIVERY_STAGING.md:46,123` |
| Tier-transition simulation | "first milestone that actually introduces simulation tiers" | `docs/ROADMAP.md:249-250` |

The M7 gate (untracked V4, authority unconfirmed) reads: "Do **not** begin M7 until Phase-1 consolidation, performance proof, and required playtest work are finished." (`G:/UNNAMED/docs/OTHERREACH_MASTER_HANDOFF_2026-09-24_V4.md:20`). Also `:527` "M7 remains unauthorized", and `:1894`. The early feel test "is **not an M7 entry blocker**" (`docs/ROADMAP.md:274`).

---

## 16. Code state at e10d2c4 (FACT)

- No faction logic, state or save field exists:
  - `src/Content/SchemaTypeMapper.cs:250-257`: `FactionSchema : ContentEnvelope` with "Faction-specific properties would be added here";
  - `src/Content/SchemaResolution.cs:158-163` registers kind `faction`, directory `factions`, prefix `faction`;
  - `src/Content/ContentChecks.cs:34` maps `faction_ref` → `faction`;
  - there is no `content/factions/` directory.
- `NpcDefinition(Id, Name, Role, Services, MerchantId, DialogueId)` has no faction field (`src/Domain/Social/Social.cs:10`). NPC content refuses `schedule_ref, anchors, combat_profile, relationships_init, name_pool` (`src/Content/SocialContent.cs:85-89`). NPC instance IDs derive from definitions and "nothing about them needs saving" (`docs/SYSTEMS.md:280`).
- Relationship system: see §8.1.
- Quest content refuses `faction_ref` ("faction_ref is not built in Phase 1", `tests/Content.Tests/QuestContentTests.cs:122`). A nonexistent faction reference raises an XREF error (`:121`).
- Dialogue builds the `visited, world_state, has_item, relationship, skill, level, quest_state, companion_present` conditions (`docs/DATA_MODEL.md:555`). `reputation` / `faction_state` / `add_reputation` are not built.
- Creature awareness and call propagation are local, as in §10. Tactical hostility is a creature's `Engaged` mind (`src/World/Runtime/Creatures.cs:36`).

---

## 17. Contradictions (doc vs doc, doc vs code, doc vs owner ruling)

- **C-1 Crime scope and phase.**
  - `docs/ROADMAP.md:282` puts "crime/bounty records and pardon state (`S-27`)" in M7. `docs/SYSTEMS.md:305-307` has S-27 (Phase 2) own and persist crime records, bounties and pardons. `docs/SYSTEMS.md:427` defers only "crime/trial/court systems beyond bounties".
  - Against that: `docs/PROTOTYPE.md:40` says "Crime, bounties, guards reacting to the player → Phase 3". `docs/INDEX.md:89` says "crime is Phase 3". `docs/VERTICAL_SLICE.md:336` makes "Crime, bounties, jail, guard-pursuit depth" a slice non-goal ("the *system* does not" exist), but keeps a "crime/bounty stub" (`:62`).
  - `docs/FEATURE_DELIVERY_STAGING.md:51-67` puts a crime Proof layer first and "witness descriptions; disguise; evidence; self-defense; local warrants; bounty-hunter contract; prison time skip" in the vertical slice. `docs/IMPLEMENTATION_PRECEDENCE_AND_DESIGN_STATUS.md:189` makes full systemic crime/courts design-only.
- **C-2 Reputation scale and tier names.**
  - `docs/PROGRESSION.md:432-434`: 11 named tiers −5..+5 with numeric accumulation inside a tier. The "no attack-order names" rule is at `docs/PROGRESSION_AXIS_RECONCILIATION.md:185`.
  - `docs/DATA_MODEL.md:568-572`: tiers `outsider/tolerated/trusted/sworn` at mins −100/0/150/400. The quest reward "amount: 15" is at `:489`.
  - `docs/VERTICAL_SLICE.md:95`: 5 tiers "Hostile / Wary / Neutral / Trusted / Sworn". "Hostile" breaks the ratified naming rule. "≥ 40 reputation" is at `:265`.
- **C-3 Faction count.**
  - "20+ factions tracked, mutually constraining" (`docs/PROGRESSION.md:48`);
  - vs "**2** … A third would need a third region to be meaningful" (`docs/VERTICAL_SLICE.md:94`);
  - vs "2–3 factions with mutually constraining reputation" (`docs/ROADMAP.md:303`);
  - vs owner ruling 3 "smallest useful set" (not recorded in the repo).
- **C-4 Relationship value scale.**
  - Built and specified at [-100,100] ints: `docs/SYSTEMS.md:300`; `src/Domain/Social/Social.cs:28-39`; `docs/VERTICAL_SLICE.md:165`.
  - DATA_MODEL examples use fractions: `relationships_init … { trust: 0.4, respect: 0.2 }` (`docs/DATA_MODEL.md:303`), a dialogue `min: 0.1` (`:544`) and a quest `fail_if … trust, max: -0.5` (`:484`).
  - FactionDefinition `attitude_default` is in [-1,1] (`:560`), a third scale beside reputation's.
- **C-5 Standing-driven hostility in slice text.**
  - `docs/VERTICAL_SLICE.md:169` has "backing it makes the Ashlings hostile" and "puts town guards on you".
  - FactionDefinition `laws.response: guards_hostile | guards_lethal_bounty` (`docs/DATA_MODEL.md:575-576`) and `enemy_of` "(hard hostility edges)" (`:563`) turn a faction-level law constant straight into hostility.
  - Against: S-27 "never decides who attacks" (`docs/SYSTEMS.md:309`) and `docs/PROGRESSION.md:444-452`.
  - INFERENCE: this is compatible only if backing a faction sets faction relation, war state or legal status, never a standing tier. `guards_hostile` also skips the witness → report → legal-status pipeline.
- **C-6 One bounty number vs layered wanted states.** `docs/CRIME_LAW_REPUTATION_AND_JUSTICE.md:118-128` says "Do not collapse them into one bounty number". S-27's crime record has a single `bounty` (`docs/SYSTEMS.md:305`), FactionDefinition has `bounty_base` per offense (`docs/DATA_MODEL.md:562`), and events are `BountyPlaced` / `BountyCleared` (`docs/SYSTEMS.md:309`).
- **C-7 Law belongs to places vs law on the faction.** "Law belongs to places and institutions" (`docs/CRIME_LAW_REPUTATION_AND_JUSTICE.md:20`). Jurisdiction types include `none`, `disputed`, `religious` (`docs/WORLD_BUILDING_AND_PROPERTY_DESIGN.md:70-77`). DATA_MODEL attaches `laws` and `territory` to a FactionDefinition (`docs/DATA_MODEL.md:561-562`), which cannot express no-law or disputed places, or several institutions over one place.
- **C-8 Tier D meaning and offscreen reputation.**
  - `docs/NPC_SIMULATION.md:86-93`: Tier D advances "reputation; goal changes".
  - `docs/SYSTEMS.md:449`: Tier D = "stored state, no ticking".
  - `docs/WORLD_ARCHITECTURE.md:284`: abstract tiers may never advance "relationship decisions, faction-defining acts".
  - `docs/SYSTEMS.md:269`: C/D actors "run S-27's abstract model", which is undefined.
- **C-9 Reputation decay vs permanence.** Decay "drifts toward Neutral … per in-game week" (`docs/PROGRESSION.md:454`). Against it: "Reputation: history, and history cannot be undone" (`:305`) and "reputation [is] permanent history" (`:587`). INFERENCE: the permanence lines are about respec, but they read as absolute.
- **C-10 Gold → reputation paths.** "trade volume with faction merchants" is an advancement vector (`docs/PROGRESSION.md:438,48`). Barter lets an actor compare "reputation/relationship" (`docs/ECONOMY.md:92`). Against both: E-7, "Reputation and access are earned through deeds and faction relationship, never purchase", plus the grep test (`docs/GAMEPLAY_LOOPS.md:238`), and "Anathema … never via payment alone" (`docs/PROGRESSION.md:454`).
- **C-11 Is reputation stored or derived?** `docs/ARCHITECTURE.md:208` says "Reputation — Derived from standings; not separately stored". S-27 persists "Reputation values and tiers" (`docs/SYSTEMS.md:307`). `docs/PERSISTENCE.md:595` persists "Reputation values plus their bounded event log". `docs/DATA_MODEL.md:846` warns that "threshold changes retroactively reclassify a player". INFERENCE: storing the tier as well as the value creates a derived cache; PERSISTENCE says caches are recomputed, never migrated (`docs/PERSISTENCE.md:86`).
- **C-12 Two owners of disposition.** S-24 owns and persists "disposition toward factions and toward the player" (`docs/SYSTEMS.md:275,277`). S-26 owns "derived disposition tiers" (`:295`). S-23 reads "S-27 (NPC faction and disposition)" (`:266`). This breaks "one system, one responsibility".
- **C-13 Witness propagation is transient but travel takes time.** S-27's "Witness-propagation working set" and "guard-alert state per settlement" are **transient** (`docs/SYSTEMS.md:308`). The crime doc makes reports and warrants travel physically over time (`docs/CRIME_LAW_REPUTATION_AND_JUSTICE.md:103-114`). INFERENCE: in-flight reports must persist, or a save/load would drop or instantly deliver them. That would break T-01's full-equality rule (`docs/PERSISTENCE.md:575`) and the replay contract.
- **C-14 Relationship dimension sets differ.** S-26/code has trust, affection, fear, respect, grudge. Companions doc (`docs/COMPANIONS_HIRELINGS_RELATIONSHIPS_AND_PARTIES.md:42-50`) has affection, trust, respect, loyalty, fear, attraction, ideological agreement (no grudge). The slice has Trust and Respect only (`docs/VERTICAL_SLICE.md:165`).
- **C-15 The memory log is specified but not built.** S-26 specifies a bounded memory log (`docs/SYSTEMS.md:295-297`), and T-20 requires deltas "attributable to their source events" (`docs/PERSISTENCE.md:594`). M4 built "no memory log; the event names are the attribution" (`docs/SYSTEMS.md:300`), and the save holds values only (`docs/PERSISTENCE.md:192`).
- **C-16 `faction_id` field: doc vs code.** `docs/PROTOTYPE.md:110,264,395` (A-2): "`faction_id` field exists on NPCs and is saved … in the save schema from day one". Code: `NpcDefinition` has no faction (`src/Domain/Social/Social.cs:10`), no "faction" appears in `src/Persistence` or `src/World`, and NPCs are not saved at all (`docs/SYSTEMS.md:280`). INFERENCE: M7 needs an NPC-state migration after all.
- **C-17 Hostility-separation lists differ.** Compare `docs/CLAUDE_PHASE1_EXECUTION_PROMPT.md:254-261` (6 items), `docs/PROGRESSION.md:444-450` (5 deciders, including perception, excluding attack legality), `docs/SOCIAL_INTERACTION_LANGUAGES_AND_KNOWLEDGE.md:548-564` (11) and `docs/MODDING_COMMUNITY_SERVERS_FEDERATION_AND_PVP_SEAMS.md:770-781` (10, adds settlement standing).
- **C-18 Race-keyed hostility vs "culture, not race".** `docs/SOCIAL_INTERACTION_LANGUAGES_AND_KNOWLEDGE.md:625` says "Avoid `all members of biological race X hate the player`". Against it: `docs/STONE_QUESTION.md:127` ("Kal PCs meet hostility in Ondrek towns") and the race-keyed travel matrix (`docs/COMPANION_MATRIX.md:31-43`, e.g. "The Mor will not travel with the living", `:24`). INFERENCE: this can be reconciled as a culture/polity attitude with individual exceptions ("no" = persuadable, `:47`), but the data is keyed by race.
- **C-19 Navmesh wording vs owner ruling 1** (see §2). This is a risk only if the M7 navmesh is Godot's: `docs/ROADMAP.md:284-285`; `docs/WORLD_ARCHITECTURE.md:414,435`; `docs/M3_STATUS.md:72`.
- **C-20 C10 status in the untracked V4** ("not yet formally owner-ratified", `G:/UNNAMED/docs/OTHERREACH_MASTER_HANDOFF_2026-09-24_V4.md:615`) vs the ratified revision (`docs/PROTOTYPE.md:298`, `docs/M6_STATUS.md:141`).
- **C-21 Radial as a controller path.** `docs/HUD_INPUT_AND_ACTIONS.md:36` vs the bible rule (`docs/PHASE1_ASHEN_HOLLOW_PLAYABLE_CONTENT_BIBLE.md:645`). This is soft.
- **C-22 Stale `HostileToPlayerChanged` instruction.** `docs/CLAUDE_PHASE1_EXECUTION_PROMPT.md:263` and `docs/IMPLEMENTATION_PRECEDENCE_AND_DESIGN_STATUS.md:94` still describe S-27 as emitting it. SYSTEMS no longer does (`docs/SYSTEMS.md:309`), and the reconciliation records the removal (`docs/PROGRESSION_AXIS_RECONCILIATION.md:186`). Historical only.

---

## 18. What a designer can take as settled vs open (INFERENCE, grounded in the above)

**Settled** (ratified or normative, with no contrary normative text):
- reputation is per-faction, access-only, never a capability or attack decider;
- no global morality meter;
- an act may move factions in opposite directions through data-defined reactions;
- dialogue, quests, rewards and loot reference factions only through closed vocabularies (commands, never direct mutation);
- awareness and information are local and perception/communication-driven;
- LLMs never decide reputation;
- no networking, but no hard-coded player-target invalidity;
- reputation and crime records live in the player save section and are save-version sensitive.

**Unsettled, needing a design decision before the M7 schema:**
1. The number of factions and their identities for M7. The docs give 2 (slice), 2–3 (M9) and 20+ (PROGRESSION); ruling 3 says "smallest useful".
2. The tier ladder and numeric scale, and the rename of VERTICAL_SLICE's "Hostile" tier.
3. Whether any crime (records, witness line of sight, local guard response, fine/bounty) is in M7 (C-1). If it is, how to avoid "one bounty number" (C-6) and where law lives: on a faction, or on a place/jurisdiction record (C-7).
4. Where in-flight information (reports, alerts, rumors) lives and whether it persists (C-13). Whether M7 introduces the generic knowledge record or a narrower report record that stays compatible with it (`docs/SOCIAL_INTERACTION_LANGUAGES_AND_KNOWLEDGE.md:765`).
5. The ownership split between S-24 disposition, S-26 relationships and S-27 faction relation/standing (C-12). The relationship scale (C-4). Whether the memory log arrives with M7 (C-15, T-20).
6. The shape of the attack-legality seam and of faction relation / war state, as separate state from standing (C-5, `docs/MODDING_COMMUNITY_SERVERS_FEDERATION_AND_PVP_SEAMS.md:825`).
7. Reputation decay (C-9) and trade-volume reputation (C-10).
8. How an NPC carries faction membership. FactionDefinition lists `members` (`docs/DATA_MODEL.md:566`) and NPCs carry `faction_ref` (`:297`): two sources of membership truth (INFERENCE). The PROTOTYPE `faction_id` slot does not exist (C-16).

---

## 19. Files read

**Fully:**
- `docs/CRIME_LAW_REPUTATION_AND_JUSTICE.md`, `docs/NPC_SIMULATION.md`, `docs/STEALTH_DETECTION_AND_THREAT.md`;
- `docs/COMPANIONS_HIRELINGS_RELATIONSHIPS_AND_PARTIES.md`, `docs/COMPANION_MATRIX.md`, `docs/SOCIAL_INTERACTION_LANGUAGES_AND_KNOWLEDGE.md`;
- `docs/QUESTS_DUNGEONS_WORLD_EVENTS_AND_REPOPULATION.md`, `docs/ECONOMY.md`;
- `docs/SYSTEMS.md` (§1, S-03, S-04, S-12, S-21..S-38, §3-§4, assumptions);
- the MODDING doc's PvP sections §37-§49;
- PROGRESSION §1-§3.4, §5, §8, §10, §12-§14.

**By heading/grep and range:**
- `docs/DATA_MODEL.md`, `docs/PROJECT_CHARTER.md`, `docs/PROGRESSION_AXIS_RECONCILIATION.md`, `docs/GAMEPLAY_LOOPS.md`, `docs/VERTICAL_SLICE.md`;
- `docs/RISK_REGISTER.md` (no faction-, reputation- or knowledge-specific risk exists; RK-04 lists "faction state" as a quest shape, `:114`; RK-07 and RK-15 are relevant to tiers and cross-slice writes);
- `docs/ROADMAP.md`, `docs/PROTOTYPE.md`, `docs/INDEX.md`, `docs/IMPLEMENTATION_PRECEDENCE_AND_DESIGN_STATUS.md`, `docs/CLAUDE_PHASE1_EXECUTION_PROMPT.md`, `docs/OTHERREACH_MASTER_HANDOFF_2026-09-23_V3.md`;
- `docs/DECISIONS.md`, `docs/ARCHITECTURE.md`, `docs/PERSISTENCE.md`, `docs/WORLD_ARCHITECTURE.md`, `docs/FEATURE_DELIVERY_STAGING.md`, `docs/WORLD_BUILDING_AND_PROPERTY_DESIGN.md`;
- `docs/AI_NARRATIVE_SERVICE.md`, `docs/MAPS_CARTOGRAPHY_AND_WORLD_KNOWLEDGE.md`, `docs/ENDGAME_MASTERY_LEGACY_AND_GREAT_WORKS.md`, `docs/STONE_QUESTION.md`, the bible §20/§36, `docs/HUD_INPUT_AND_ACTIONS.md`, `docs/M6_STATUS.md`;
- the code files cited above.

**Untracked, authority unconfirmed:**
- `G:/UNNAMED/docs/OTHERREACH_MASTER_HANDOFF_2026-09-24_V4.md` (§0, §2-§4, §18, §40, §50-§53);
- `G:/UNNAMED/docs/PRE_CLAUDE_RECONCILIATION_REVIEW.md` §8 ("Reputation Is Not Hostility", `:188-204`);
- `G:/UNNAMED_HISTORY/OVERNIGHT_REPORT_2026-09-24.md` (grep only: "No M7 work has begun", `:141`).

The other untracked Phase-1 docs (HUD spec, quest lock, playtest plan, integration matrix, difficulty draft, FOR DEEPSEEK) contain no faction, reputation or crime text (grep returned nothing).
