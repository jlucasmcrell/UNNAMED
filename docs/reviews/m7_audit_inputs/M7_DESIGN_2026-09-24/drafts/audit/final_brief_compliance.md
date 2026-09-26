# M7 implementation design - final audit: owner-brief and rulings compliance

Audited file: `G:/UNNAMED_HISTORY/M7_DESIGN_2026-09-24/M7_IMPLEMENTATION_DESIGN.md` (7,522 lines), read in full by line range.
Settled inputs (not re-reported): `drafts/00_SCOPE_RULINGS.md`, `drafts/01_LEAD_RULINGS_ON_AUDITS.md` (L1-L10).
Source checks: the read-only snapshot of `e10d2c4` (`docs/PROTOTYPE.md:298`, `docs/SOCIAL_INTERACTION_LANGUAGES_AND_KNOWLEDGE.md:548-564`, the clock-read scan of `src/Domain`, `src/World`, `src/Application`).

## Verdict

Substantially compliant. Every brief part (scope, A1-A10, B1-B11, C1-C9, D, E, F, G, H, I, J, K) and all 18 final sections are present, and every owner ruling is honoured: no Godot navigation authority (G4, criterion 2), one storey with no walkable elevated surface (section 4.8, criterion 9), two factions with no morality meter, all eleven hostility layers named separately (section 5.11 table; trust, fear and grudge share one row as the M4 relationship dimensions), the same act moving two factions (F1, P5), report-only knowledge (L1), no required radial (section 8.16), no networking, engine-independent authority with dotted IDs, ULID-shaped derived IDs and sparse deltas, C10 not restored (criterion 34), and no M8 work (section 16.10). Every L1-L10 ruling is reflected, except the M7_STATUS half of L2 (finding 10).

No critical or high findings. Four medium findings are internal contradictions on items the brief requires (the v14 fixture's knowledge rows, the T2 CI threshold, the E0 document list, the permitted Phase-1 test edits). Nine low findings are partial answers or minor mismatches.

Not verifiable: the brief's "thirteen named" Part I risks. The brief text is not in the design folder (the same limit `audit_impl.md` recorded), so the thirteen cannot be matched one by one. Section 15's 38 rows (R-A1..A9, R-B1..B7, R-C1..C3, R-X1..X19) cover every plausible candidate (navigation complexity, nondeterminism, seams, rebuild cost, save growth, persistence loss, stuck NPCs, companion regression, psychic knowledge, morality meter, scope creep, assets, UI input).

## Findings (most severe first)

### 1. MEDIUM - The v14 fixture's knowledge rows are described two ways (Part F fixtures; L1 seams)

- Line 2394 (section 5.3.3): "The v14 fixture (section 7) stores one `witnessed` and one `unidentified` row so both keys round-trip; M7 code never writes either."
- Line 4025 (section 7.12, normative for the fixture): both rows are "`identified`, `reported`" (delvers via `npc.fixture.smith`, keepers via `npc.fixture.warden`).
- Line 2750 (section 5.12): "two `reported`/`identified` knowledge rows, with act 2 known by no faction". Line 3581 (section 7 decisions): "M7 writes only `reported`/`identified` and stores no other witness field."
- Consequence: an E2 implementer building `M2.Probe`'s v14 player from section 5.3.3 changes the F-E2 M7 block, `expected.json` and the alias counts at line 4051; STOP S9 fires.
- Fix: replace line 2394's sentence with: "The v14 fixture stores two `reported`/`identified` rows (section 7.12). Decode accepts `witnessed` and `unidentified` (section 7.5), and fixture row F4's crafted `unidentified`/`witnessed` row exercises them; M7 code never writes either."

### 2. MEDIUM - T2's CI threshold is 6 ms in section 14 and 12 ms in sections 11 and 12 (Part J instrumentation)

- Line 5977 (criterion 33): "T2 is green at CI < 12 ms mean." Line 6301 (section 12.8): "CI mean < 12 ms; ASTRAL evidence ≤ 2 ms".
- Line 6916 (section 14.12.3, T2 step 5): "**CI:** mean < 6 ms." Line 6840 (budget sheet): ASTRAL "mean ≤ 2 ms", CI "mean < 6 ms". Line 7035: "(CI mean < 6 ms)". Line 6053 (the rule): "timings are asserted only at 3× the ASTRAL target".
- Fix: at lines 5977 and 6301, replace "< 12 ms" with "< 6 ms (3× the 2 ms ASTRAL target)".

### 3. MEDIUM - Section 1.4 names a different E0 document set from E0 itself (brief: the first slice writes the rulings)

- Line 132 (section 1.4 item 1): "WORLD_ARCHITECTURE, RISK_REGISTER, PERSISTENCE, SYSTEMS, DATA_MODEL, PROTOTYPE A-2, HUD_INPUT and IMPL_PRECEDENCE amended where they conflict."
- Lines 4995-5009 (the E0 table) contain no DATA_MODEL row but do contain GAMEPLAY_LOOPS and the content bible. Line 5042 (E0 non-goals): "No as-built text: DATA_MODEL shapes ... land with their slices." Line 5035 (checklist item 9) requires the marker in "exactly" 11 documents, which exclude DATA_MODEL and include GAMEPLAY_LOOPS and the content bible.
- Consequence: an implementer who follows section 1.4 and marks DATA_MODEL fails checklist item 9, and E0's STOP fires.
- Fix: line 132 becomes "WORLD_ARCHITECTURE, RISK_REGISTER, PERSISTENCE §2, SYSTEMS, GAMEPLAY_LOOPS, the Ashen Hollow content bible, PROTOTYPE A-2, HUD_INPUT and IMPL_PRECEDENCE amended where they conflict; DATA_MODEL changes land as built in E2, E3 and E10."

### 4. MEDIUM - Section 2.18's "only permitted" test-edit list is shorter than section 12.10's (Part E tests; STOP S1)

- Line 477 (section 2.18): "Existing tests named elsewhere as edited are the only permitted Phase-1 test edits: `LoadAll_Loads_Yaml_Files` ..., and the `DialogueRulesTests` fake." The list has no `HisState_RoundTripsThroughASave_FieldByField_AndGoesOnTheSame`, no `TheProbesContentMirror_IsTheFixtureContentPack` and no `KnownDirectories_Is_Closed_Set`.
- Lines 6321-6332 (section 12.10) permit all three (`HisState_…`: "the route assertion (E4)"; `TheProbesContentMirror_…`: "E2; the latter again on a 0.2.10 bump"). Line 1238 (section 3.20.4) also changes `HisState_…` by name. Line 4961 (STOP S1) is keyed to section 12.10.
- Fix: replace line 477's enumeration with "the edits section 12.10 lists by name (the only Phase-1 test edits; anything else is STOP S1)", or add the three missing tests to it.

### 5. LOW - Section 8.9 cites the wrong assign refusal (Part H assign UI)

- Line 4372: "Asking is in person. Focus uses talk reach, which is assign refusal 5."
- Line 1794 (section 4.15): "4. the player's body is not within `TalkReachMm` (1950) of the NPC's body". Line 1795: "5. the piece is not an intact station".
- Fix: at line 4372, "assign refusal 5" becomes "assign refusal 4".

### 6. LOW - Three different `--playthrough-verify` field estimates from E3 onwards (Part F evidence)

- Line 3942 (section 7.9): "From E3: + 37 for the ledger ... = 464, plus about 3-9 ...: **about 470**." Line 4113 (F-E9): "427, then about 470". Line 4144 (section 7.17): "`--playthrough-verify` at about 470".
- Line 6554 (section 13.8): "E3-E4: about 500 (+37 for the ledger, about 28 for Kera's materialised wares record, the bought billet, ...)". Line 5257 (E3's runtime acceptance): "about 500 fields (§13.8)".
- Section 7.9 leaves out Kera's wares record, which the `m7_tell_kera` billet purchase materialises. Section 7.9 also requires that "a gap from the estimate is explained".
- Fix: line 3942 becomes "From E3: +37 for the ledger, about 28 for Kera's materialised wares record and the bought billet, and a few dialogue-memory and relationship rows: **about 500**". At lines 4113 and 4144, "about 470" becomes "about 500".

### 7. LOW - T4 is missing from two optional-instrument lists (Part J instrumentation)

- Line 5704 (E10 OPTIONAL): "T3, T5, T7 and T8 as separate tests". Line 6349 (section 12.11) lists T3, T5, T7 and T8, with no T4.
- Line 6969 (section 14.13): "T3, T4, T5 and T7 as separate tests". Line 7038 names "T4 `AnEditThatReplansBothMovers_StaysWithinTheTickBudget`". Line 7286 (section 16.8): "T3, T4, T5, T7 and T8". Line 6918 is "the T4 fold".
- Fix: add T4 `AnEditThatReplansBothMovers_StaysWithinTheTickBudget` to line 5704's list and to section 12.11's row at line 6349.

### 8. LOW - Part H "cancel" is never specified

- The brief's UI list is enter/exit, select, rotate, place, cancel, remove and invalid feedback. Line 4204 (the key list) and the section 8.2 table (4230-4241) cover every item except cancel. Line 4253 only says Esc "**leaves build mode**". The word "cancel" does not appear anywhere in the document (the GH draft's "cancel, as expected" was dropped).
- Fix: add to section 8.3 (after line 4253), and as an F1 row in section 8.15: "Cancel: Esc (or B) leaves build mode, discarding the ghost and any armed take-down. A placement is one immediate command, so nothing is queued to cancel. Changing the target, or waiting 2 s, disarms a take-down without leaving build mode."

### 9. LOW - E10 is missing the brief's per-slice fields (Part E)

- The brief requires each slice to state purpose, code areas, new data, commands, events, content, persistence, presentation, tests, runtime acceptance, non-goals and STOP. E0 (line 5017) and E7 (line 5511) state "None" explicitly. E10 (lines 5684-5738) has no new-data, commands, events, content, persistence or presentation line.
- Fix: after E10's OPTIONAL list (line 5705), add: "**New authoritative data, commands, events, content, persistence.** None. **Presentation.** `FrameStats` gains `sim_ms`, `ticks` and `save_ms` (section 14.12.2); nothing else."

### 10. LOW - L2's M7_STATUS obligation for pad and roof health is not carried into E10

- `01_LEAD_RULINGS_ON_AUDITS.md:29`: "Pads and roofs: health is stored but no M7 rule reaches it. Say so explicitly (lint allows `health_max` on them; §4 and M7_STATUS state it)."
- Section 4 says it (lines 1323, 1418, 1702). E10's M7_STATUS contents (lines 5708-5714) and the residue list at section 5.19 (lines 2990-2999) do not.
- Fix: add to line 5711's M7_STATUS bullets: "the stored health of pads and roofs, which no M7 rule reaches (L2)".

### 11. LOW - `TraversalClass.NonBlocking` is an M9-only value built in M7 ("no M8+ work")

- Line 1471: "`TraversalClass.NonBlocking` exists for M9 and is never emitted in M7". Line 3260: "`TraversalClass { Solid, Door, NonBlocking }`". Line 1030: "`NonBlocking` → none".
- Against: the "no M8+ work" ruling. L9 also trimmed comparable speculative abstractions, and line 7099 (R-X1) requires "a scope ledger in `M7_STATUS` mapping every built type to a ROADMAP phrase or a scope row", which this value cannot meet.
- Fix: `TraversalClass { Solid, Door }` at lines 1471 and 3260. Delete "`NonBlocking` → none" at line 1030, and delete line 1471's M9 sentence. The milestone that first emits a non-blocking part adds the value (it is not persisted, so no migration is needed).

### 12. LOW - Q2's latest decision point is stated three ways (Part K)

- Line 7100 (R-X2): "Latest decision points: Q2 before E0".
- Line 7335 (Q2): "before E0 writes the ROADMAP reconciliation". Line 7480 (section 18.6): "before E0 commit 2 (the ROADMAP text)". Line 4882: "Q2 (before commit 2)".
- Fix: at line 7100, "Q2 before E0" becomes "Q2 before E0 commit 2".

### 13. LOW - Part A3's resolution trade-off omits wall size

- The brief asks for the grid resolution trade-off from body, door, wall, movement and cell sizes. Section 3.3 (lines 563-579) derives the choice from body radius (C1), door width (C2), cell, terrain and module tiling (C3) and movement (C4). Wall thickness appears only in passing at line 546 ("1.6 m doorways in 0.4 m walls").
- Fix: add after line 570: "**C5, walls.** Wall thickness sets no lower bound on `s`. `FitAt` inflates every footprint by `Rp`, and C1 holds for any convex footprint. A 0.4 m wall or jamb therefore removes a 1.2 m band of nodes (4-5 rows at s = 250), and no walkable lattice edge crosses it."

## Brief checklist (item → where answered)

| Brief item | Where | Status |
|---|---|---|
| Authoritative scope first: REQUIRED / OPTIONAL IF CHEAP / DEFERRED, explicit conflicts | section 1.3 (20 conflict rows), 1.4, 1.5, 1.6 | answered (finding 3 on the E0 list) |
| A1 consumers | 3.1 | answered |
| A2 representation A/B/C/D across the eleven criteria | 3.2 table | answered |
| A3 resolution trade-off | 3.3 | partial (finding 13) |
| A4 authority table | 3.4 | answered |
| A5 update sequence, rebuild scope | 3.5 | answered |
| A6 seams (duplicated nodes, borders, coordinates, order) | 3.6 | answered |
| A7 costs, neighbour order, ties, diagonals, inflation, unreachable, triggers | 3.7.1-3.7.6 | answered |
| A8 local movement | 3.8 | answered |
| A9 future seams (schedules, settlements, doors, bridges, roads, creature sizes) | 3.19 | answered |
| A10 the ten test cases | 3.20 | answered |
| B1-B11 | 4.3, 4.4, 4.5, 4.6, 4.7, 4.8, 4.9, 4.10, 4.19/7, 4.13, 4.22 | answered |
| C1-C9 (two factions fully profiled; the third considered in 5.6.2) | 5.1, 5.2, 5.3, 5.6.3, 5.6.1, 5.10, 5.11, 5.12, 5.16 | answered (finding 1 on 5.3.3) |
| D couplings (G1-G5 are the five named), dependency graph, reuse | 6.4, 6.3, 6.5 | answered |
| E slices with the twelve fields | 10.6-10.16 | partial (finding 9) |
| F schema fields, defaults, derived vs serialised, step, fixtures, evidence | 7.2, 7.3, 7.4, 7.12, 7.15 | answered (findings 1 and 6) |
| G three asset lists, greybox kit, faction visuals | 9.2, 9.3, 9.5 | answered |
| H enter/exit, select, rotate, place, cancel, remove, invalid feedback; faction screen; no radial | 8.2-8.7, 8.13, 8.16 | partial (findings 5 and 8) |
| I risks | 15 | answered; the thirteen names cannot be verified (see the verdict) |
| J navigation memory, planning frequency, rebuild scope, piece ceilings, faction complexity, instrumentation | 14.2, 14.3, 14.4, 14.5, 14.7, 14.12-14.13 | answered (findings 2 and 7) |
| K at most five questions in the six-field format | 17 (Q1-Q5) | answered (finding 12) |
| The 18 final sections | lines 18-35 and the headings | answered |
| Rulings: Godot navigation, one storey, factions, radial, networking, identity and deltas, C10, no M8+ | criteria 2, 9, 22-24, 28, 31, 19/29/30, 34; section 16.10 | honoured (finding 11) |
| Lead rulings L1-L10 | throughout | honoured, except L2's M7_STATUS clause (finding 10) |
