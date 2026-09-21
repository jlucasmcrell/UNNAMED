# Adversarial review — PROGRESSION / SCOPE / ECONOMY (Phase 0 doc set)

Scope: PROGRESSION.md, PROTOTYPE.md, VERTICAL_SLICE.md, ROADMAP.md, GAMEPLAY_LOOPS.md, RISK_REGISTER.md vs PROJECT_CHARTER.md.
Known register risks (RK-01..RK-14) are not restated.

---

## 1. CRITICAL — The XP curve does not sum to the total the document claims, and every downstream pacing number is keyed to it

PROGRESSION.md:61 — `XP required for level n (n ≥ 2): XP(n) = 100 * (n-1)^1.85, rounded to the nearest 25, with a flat +15% band multiplier applied at levels 11–20 and 21–30 ... Total to 50 ≈ 1.9M XP.`

Evaluating the stated formula under its own stated multipliers (levels 11–30 ×1.15, rounded to nearest 25):

| | value |
|---|---|
| Σ 100·(n−1)^1.85, n=2..50 | 2,369,959 |
| ×1.15 band, rounded | 2,448,025 |
| document claim (PROGRESSION.md:61) | ≈1,900,000 |
| error | +28.8% |

PROGRESSION.md:72 then fixes `Target total to level 50: 65–85 hours of mixed play`, which sets the tier rates in PROGRESSION.md:63–69 and is the reference for `AG-6`'s guard: PROGRESSION.md:99 — `` `AG-6` | Rate banding | Designed XP/hour band across activities: 0.6×–1.4× of the tier target rate. ``

At the true 2.45M the mixed-play requirement is ~32.6k XP/hour at 75 h, not the ~25.3k the doc's own total implies. Every tier target rate, the 0.6×–1.4× band that is a **Phase-1 exit criterion** (ROADMAP.md:142 — `Headless tests prove (a) XP per hour stays inside the AG-6 band across four scripted activity profiles`), and the 220 XP prototype quest reward (PROTOTYPE.md:140) are all tuned against a number that is wrong by 29%.

Why it matters: the progression spine is `M2c`, on the critical path before combat (ROADMAP.md:65), and `AG-6` is the only guard preventing one degenerate activity becoming mandatory. Tuning it to a fictional total does not fail loudly — it fails as "leveling feels 30% slow," discovered in playtest after all content is authored.

What I would do instead: make the total to 50 a *stored, derived* value (a test that sums the curve and asserts the published total), and publish tier rates as XP/hour constants rather than deriving them from an hour estimate.

---

## 2. CRITICAL — Three of the ten "orthogonal" axes measure the same underlying quantity: creature-valid repetitions

The claim being tested is PROGRESSION.md:13 — `D-09 sanctions a progression axis only if it answers a different question. This document's core deliverable is the per-axis articulation of that question and the proof that no axis is a restatement of another.`

The actual advance vectors, read together:

- PROGRESSION.md:40 (`AX-SKL`) — `Use-based competence XP per skill, per-action`; PROGRESSION.md:142 — `an action grants skill XP only when its difficulty exceeds skill_ceiling − 15`.
- PROGRESSION.md:41 (`AX-WM`) — `**Use-based**: effective damage dealt with that family under level-band-valid conditions`.
- PROGRESSION.md:44 (`AX-PRF`) — `Craft attempts that produce **new-to-character** outputs`; PROGRESSION.md:250 — `Re-crafting the same item advances nothing after the first success.`
- PROGRESSION.md:42 (`AX-MM`) — study/research/tomes.

So `AX-LVL`, `AX-SKL` and `AX-WM` all advance from *the same event*: doing a valid thing to a valid target. `AX-PRF` advances by the first-time variant. `AX-MM` is the only axis whose advance vector reads a genuinely different input (research actions in safety). The measured quantity differs by valuation only.

The contradiction is stated inside the document. PROGRESSION.md:41 defends `AX-WM` as `hands-level skill`, and PROGRESSION.md:142 defines the skill axis's whole mechanical identity as `Trivial repetition is worthless by construction`. A defence that reads "mastery is a skill" is an admission that skill and mastery answer the same question at different granularity — which is the exact ground on which D-09's author rejected two candidate axes — DECISIONS.md:187 — `It caught two candidate axes during design — a separate "exploration level" and a "crafting level" distinct from professions — both of which were folded into existing axes rather than added.` A distinct "weapon mastery" is a third candidate of that shape that was kept. The same test applied to `AX-SKL` vs `AX-WM` does not clearly pass.

Compounding this, `AX-SKL` is not a peer axis — it is an input to two others: PROGRESSION.md:146 (`A rank-2 alchemist with medicine 90 is an excellent field medic`) and PROGRESSION.md:267 (`A high skill with a low rank cannot exceed the rank's quality ceiling`), plus PROGRESSION.md:199 (research costs `insight` + `research` skill) and PROGRESSION.md:133 (`insight` → `recipe discovery rate`). One currency feeding three axes is the conversion hub the non-conversion law is meant to forbid (PROGRESSION.md:27 — `No axis's advancement function may read another axis's progression currency as a direct input`).

Why it matters: the anti-duplication audit is deferred to telemetry that only reaches a verdict at `M12` (ROADMAP.md:273–276), while ROADMAP.md:364 already prices the failure: `Merging axes late means re-balancing every character and save`. Ten axes × 50 levels × ~11 weapon families × ~6 schools × ~10 professions is a save-format-scale decision, tested last.

What I would do instead: run the correlation test on paper now against the *prototype's* two skills and one mastery track (PROTOTYPE.md:92, PROTOTYPE.md:96) — that matrix is small enough to settle before `M2c` freezes the save schema — and move `G-2` to before the vertical slice.

---

## 3. CRITICAL — The vertical slice is scoped as a product, and the fun verdict is gated behind building it

The slice's own claim, VERTICAL_SLICE.md:8 — `**The slice's job is not to be a game. The slice's job is to answer one question with evidence: is this game actually fun?**`

Then VERTICAL_SLICE.md:2 requires `one 2 000 m × 2 000 m region (4 km²)`, VERTICAL_SLICE.md:31 requires `**400 exterior cells of 100 m × 100 m**` with streaming, and VERTICAL_SLICE.md:108 states the file count: `| Total content files | **~180 YAML** | This is the honest size.`

Measured against the prototype's own cost basis, PROTOTYPE.md:70 — `**10 working days** of implementation effort, not counting the domain scaffolding already implied by D-02/D-05/D-10` — for PROTOTYPE.md:161 `39 definitions total` and PROTOTYPE.md:61 a `200 m × 200 m` area. The slice is ~4.6× the definitions and 100× the area, and adds the systems the prototype explicitly refused: VERTICAL_SLICE.md:119 (`Building (D-08)`), :120 (`Factions & reputation`), :121 (`Professions` — versus PROTOTYPE.md:49 `Two recipes, no profession level`), :122 (`Modifier / quality pipeline`), :123 (Sets / world events / cartography), :124 (tiered AI), :125 (quest graph + debugger).

ROADMAP.md:244–251 places the only go/no-go between "expand" and "fix the loop" **after** all of that: M9 is `GATE — Depends on: everything above`, and ROADMAP.md:363 — `| G-1 | M9 | Is the vertical slice actually fun, and do we expand or fix? | Highest in the project.`

The slice itself concedes the structural objection and schedules the test last. VERTICAL_SLICE.md:316 — `**The slice can only produce its fun feelings with this specific hand-authored content.** ... This is the most dangerous finding of all, and the only way to detect it is the §9.2 protocol run on a *second*, differently-shaped small quest.` That test is E19 (VERTICAL_SLICE.md:383), an exit criterion *after* the ~180 files exist.

Two further conformance gaps against the phase spec it cites (ROADMAP.md:248 — `Target content (Charter PHASE 2, STEP 16)`):

- ROADMAP.md:248 requires `several skill trees`; VERTICAL_SLICE.md:85 defines `Skills | **8** | ... each levelling by use` and VERTICAL_SLICE.md:87 defines `Ability/talent choices | **12 nodes** | 3 tiers × 4 options`. Eight use-levelled skills and one 12-node pool are not skill trees.
- ROADMAP.md:248 requires `weapon ... mastery` (via GAMEPLAY_LOOPS.md:278 — `Several skill trees; multiple builds; weapon and magic mastery; reputation; factions`); PROGRESSION.md:41 defines the 11-family mastery axis, and VERTICAL_SLICE.md never mentions it outside a parenthetical: VERTICAL_SLICE.md:80 — `one shared-skill pair (sword vs axe) so mastery differentiation is testable`.

Why it matters: this is RK-10 territory, but the register's mitigation is "discipline" (RISK_REGISTER.md:202) rather than a cheaper test. A 4 km² slice that must be finished before fun can be measured converts an architectural question into a content-production commitment.

What I would do instead: split the fun test from the slice. Run the §9.2 protocol on the *prototype's * 40-minute hollow with a second differently-shaped quest bolted on (PROTOTYPE.md:371 already budgets a recorded playthrough), and keep E19's "second quest" test as a precondition for M9 rather than an exit criterion of it.

---

## 4. MAJOR — `AG-1` zeroes the XP from exactly the content the world's danger design tells the player to return to

PROGRESSION.md:94 — `` `AG-1` | Level-band reward | XP multiplier by (creature_level − player_level): +4..0 → 1.00, −1..−5 → 0.60, −6..−10 → 0.25, −11..−15 → 0.05, ≤−16 → 0.00. ``

PROGRESSION.md:115 — `**Returning feels earned.** Because the world is static, a level-25 player revisiting a level-40 zone succeeds through attributes, abilities, equipment, and mastery gained elsewhere — the felt reward is the *absence of scaling*, exactly as the charter intends.`

PROGRESSION.md:63 defines Band 1 as `creature levels 1–12` and Band 5 as `(44–60)`. A level-50 character exploring Band 1–2 gets 0.00–0.05× XP for everything in a fifth of the world. The design mandates the player move outward (PROGRESSION.md:111 — `so the correct behaviour is to move, not to grind`), then prices return trips at zero.

The mitigation is real but late: PROGRESSION.md:83 gives `production` only `~8%` of XP and makes it `**First-time-only**`; PROGRESSION.md:84 gives `social` `~7%`; GAMEPLAY_LOOPS.md:266 offers local escalation via `world events (charter §18) that push dangerous forces into familiar ground`. So the intended non-combat return loop is bounded at ~15% of XP and one event stream, while the world's stated pillar (`a world that was already happening when you arrived`, VERTICAL_SLICE.md:12) is about revisiting places.

Also note the asymmetry: PROGRESSION.md:94 caps above-level rewards at 1.15×/1.30× but floors below-level at 0.00 — so the safe strategy is always to farm at-or-above band, which is the opposite of the "hunting a valley" session PROGRESSION.md:105 protects.

Why it matters: the anti-farm floor and the no-scaling pillar were designed separately and now disagree about what old regions are for.

What I would do instead: make the low-band floor nonzero for *novel content within* an old region (a new creature type, a new node, a new site) rather than for the region, so returning has a reason that is not XP-per-kill.

---

## 5. MAJOR — "Rare items stay useful for 10 levels" is asserted twice with two incompatible mechanisms, and the slices' own loot rule breaks the non-conversion law

Three statements that cannot all hold:

- PROGRESSION.md:295 — `(c) Rare items hold value across ~10 levels via unique properties and per-instance familiarity (§6), **not via raw numbers**.`
- PROGRESSION.md:46 (`AX-EQP`) — `Unbounded by level; bounded by attribute/skill minima and acquisition difficulty`; PROGRESSION.md:291 — `Requirements are expressed as **attribute/skill/family minima**, not level minima`.
- VERTICAL_SLICE.md:155 — `**Modifier budget, not item replacement.** Rare items carry 2–4 affixes; the slice's affix pool includes two that scale with a *skill level* rather than an item level, so an early rare can grow with the character.`

An affix that scales with a skill level makes equipment power climb with `AX-SKL`. That is `AX-SKL` read directly into `AX-EQP` output, which is what PROGRESSION.md:27 forbids (`No axis's advancement function may read another axis's progression currency as a direct input`), and it contradicts PROGRESSION.md:295's own "not via raw numbers" on the same page as the claim it is meant to support.

The vertical slice also narrows the pillar to a single hand-authored exception rather than a mechanism. VERTICAL_SLICE.md:157 — `Name, don't enumerate: the slice hand-authors **one** memorable pre-epic item — item.weapon.silvered_ash_hatchet, a level-11 axe with a bleed affix found in D2 — as the worked example of "useful past its level". The other ~44 bases are ordinary.` The charter's requirement (PROJECT_CHARTER.md:331 — `Not every item should be replaced every two levels. A rare weapon discovered at level 15 might remain useful at level 25 because of unusual properties`) is answered for one item out of ~45, in a slice whose level cap is 22 (VERTICAL_SLICE.md:37) — so the 15→25 test the charter actually describes cannot occur anywhere in the slice.

Why it matters: `P7` (VERTICAL_SLICE.md:266) claims this is tested (`% of equipped items with crafted_by == player at level 20`), but the *anti-obsolescence* half is structurally untestable at a level cap of 22.

What I would do instead: pick the item-power model first (level-independent property effects vs. skill-scaling affixes), state it once in PROGRESSION.md §11.1, and give the slice one deliberate "found at 11, used at 21" test item with the affix disabled to prove the properties alone carry it.

---

## 6. MAJOR — The world has no stated currency faucet, and the four economy invariants all regulate rates, never outstanding money

GAMEPLAY_LOOPS.md:17 — `**Drains** — where value leaves the loop. **A loop with no drain is an exploit**, so every loop here has at least one.`

The FIGHT loop's outputs (GAMEPLAY_LOOPS.md:90) are `XP; dropped and salvageable equipment; harvestable materials from corpses ...; faction and reputation consequences; **knowledge**`. GATHER's (GAMEPLAY_LOOPS.md:110) are materials and node state. No loop lists coin as an output. Currency appears only as a *reward* in content (PROTOTYPE.md:140 — `220 XP (levels a fresh character from 1 → 2), 40 coin`) and as a merchant-side concept (SYSTEMS.md:364 — `gold_reserve: [min,max] at restock`).

Every guard in GAMEPLAY_LOOPS.md §13 then regulates throughput, not stock: E-1 `Arbitrage is limited by **carry capacity, travel cost, and merchant liquidity**`; E-2 `vendors buy limited quantity per restock`; E-3 the recipe-vs-input invariant; E-6 `capped by **staffing, land, inputs, and upkeep**`. `E-7` forbids buying rank with gold, which is the one rule that matters here — and it is enforced by a content review checklist plus a grep (GAMEPLAY_LOOPS.md:238), not by any bound on the gold supply.

Why it matters: with a defined faucet and no sink, the classic late-game failure (money becomes meaningless; every merchant decision collapses) is invisible to the guards. It is also the precondition for the only *harmful* version of E-7: E-7 as written forbids gold→rank, so wealth can only buy equipment — which is `AX-EQP`, `the only axis that gives immediate power` (PROGRESSION.md:291). Unbounded gold therefore *is* unbounded power, laundered through the one axis the law exempts.

What I would do instead: state the faucet (which loop mints coin, and whether loot tables carry currency) and add one bounded sink per phase — repair and trainer costs exist, but nothing in the docs removes money permanently once earned.

---

## 7. MAJOR — Death penalty is specified three different ways across three documents

- PROGRESSION.md:101 — `` `AG-8` | Death is the only XP penalty | Death may cost a bounded fraction of the **current level's** progress (default: 10%, floored so a level is never lost). ``
- PROTOTYPE.md:175 — `death event: −10 % XP debt (not lost XP), respawn at outpost, 60 s of effect.weakened`
- PROTOTYPE.md:103 — `This document assumes the bounded AG-8 variant plus the non-XP consequences owned by SYSTEMS.md. Reversible by a data constant; if the owner prefers corpse recovery or XP debt, only AG-8 changes.`
- VERTICAL_SLICE.md:267 — `Death applies XP debt + a temporary injury + a corpse-recovery trip (no permanent item loss)`

`AG-8` says **fraction of the current level's progress**, floored so a level cannot be lost — a loss model. PROTOTYPE.md:175 says **XP debt** — a future-earnings multiplier model. PROTOTYPE.md:103 treats "corpse recovery or XP debt" as an either/or *not* assumed, while PROTOTYPE.md:175 already implements debt. VERTICAL_SLICE.md:267 adds corpse recovery *and* says `(no permanent item loss)` while making recovery a trip.

These are mechanically distinct systems, and they are gated by a Phase-1 exit criterion: PROTOTYPE.md:292 — `C17 | die at least once and respawn with the stated penalty applied exactly once (XP debt arithmetic visible in the character sheet). | Penalty applied twice, or not at all, or **lost XP instead of XP debt**.` C17 fails the implementation if it does what `AG-8` describes.

Why it matters: PROTOTYPE.md:7 states the reader is `another AI coding session implementing from documents alone`; this session cannot determine which death penalty to build, and the test that gates the prototype contradicts one of the two candidate answers.

What I would do instead: name one model in PROGRESSION.md, and have PROTOTYPE.md's C17 reference it by ID rather than restate it.

---

## 8. MINOR — Two documents assert 10 working days for work the same paragraph declares unbounded

PROTOTYPE.md:70 — `| Total build-to-playable target | **10 working days** of implementation effort, not counting the domain scaffolding already implied by D-02/D-05/D-10. |`

The excluded set is the majority of the prototype's own critical path: PROTOTYPE.md:346–354 lists P1–P5 as `Entity Registry (D-10) + ULID assignment + WorldStateStore`, content loader + schema validator, command bus + event bus + stub view, sparse-delta save/load with corruption recovery, plus Godot presentation (P6) and death/XP/skills/mastery (P8). PROTOTYPE.md:229 requires the save/load test to handle `a corruption-truncated save rejected cleanly with the backup loaded`, and PERSISTENCE.md:414 (T-14) requires `Kill between every step of §7.1`. None of those are scaffolding by any normal reading, yet all are excluded from the 10 days.

Why it matters: the only duration estimate in the whole doc set is the one that cannot be checked. RK-10's mitigation is discipline, so an unmeasurable estimate is the exact input that discipline needs.

What I would do instead: state the estimate as *P5 requires N sessions* and drop the total-days framing, or include the scaffolding and state the larger number.

---

## Risks MISSING from RISK_REGISTER.md (progression / scope specific)

Checked against RK-01..RK-14 and the "deliberately accepting" and "not promoted" tables at RISK_REGISTER.md:280–328.

1. **No currency-supply bound (missing).** Every economic guard regulates rate; nothing models total gold. Register has no economics risk at all beyond `RK-08`'s authoring throughput.
2. **Authoring-consistency drift is a different risk from authoring throughput (missing).** RK-08 measures whether 1000 items get written. Nothing measures whether item 700 contradicts the material-property table, the band-power table, or an E-3 invariant that was only true at item 12. The proposed content validators (ROADMAP.md:114) check schema, ID existence, duplicates, and ID immutability — not numeric or semantic consistency. The vertical slice's ~180 files are exactly where this starts.
3. **No orthogonal-axis merge protocol (partially covered, under-priced).** RK-10 and the M12 gate acknowledge the merge, but no risk entry prices *how* an axis is retired once saves contain per-axis state. D-09's revisit trigger is the decision; the migration is the risk.
4. **High-level content exhaustion in a permanently static world (missing).** GAMEPLAY_LOOPS.md:266 names the one-way ratchet and proposes events and rare spawns as guards, but no register entry covers the end state: a level-50 player has depleted every authored Band 4–5 spawn cluster under AG-3 saturation (PROGRESSION.md:96) and the world has no mechanism to regenerate hostility. This is the direct consequence of Pillar 1 and it is unbounded by construction.
5. **Save-growth budget failure without a fallback (missing).** `E12` (VERTICAL_SLICE.md:371) commits to `Save file ≤ 2 MB after a 6-hour playthrough`; PERSISTENCE.md:424 makes it a CI regression. RK-06 covers super-linear growth with piece count, but no entry covers the case where a 6-hour *normal* save (every wolf corpse, node, discovery flag, and container delta) exceeds budget and the only remedies are a format change or content cuts.

