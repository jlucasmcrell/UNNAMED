# M7 reputation fixture table

Generated from the build by `tests/Application.Tests/ReputationTableTests.cs` (M7 design §5.15). Do not edit by hand: run `UNNAMED_WRITE_REPUTATION=1 dotnet test --filter ReputationTable` from `src/` (refused when `CI=true`).

## 1. Ladder (`config.factions`)

| Tier | Level | Points |
|---|---|---|
| exalted | +5 | 1000 |
| allied | +4 | 700 .. 999 |
| honoured | +3 | 450 .. 699 |
| trusted | +2 | 250 .. 449 |
| accepted | +1 | 100 .. 249 |
| neutral | 0 | -99 .. 99 |
| wary | -1 | -249 .. -100 |
| disliked | -2 | -449 .. -250 |
| despised | -3 | -699 .. -450 |
| outcast | -4 | -999 .. -700 |
| anathema | -5 | -1000 |

## 2. Reactions (content)

| Act | Subject | the Survey | the Waystation | |
|---|---|---|---|---|
| creature_killed | creature.construct.animated_armour | -100 | +100 | **opposite** |
| switch_set | world.foldscar.steadied | +100 | - |  |

## 3. Factions

| Faction | Name | Seat | Members | Relations |
|---|---|---|---|---|
| faction.ashen_hollow.survey | the Survey | location.outpost | npc.ashen_hollow.sel_arien | faction.ashen_hollow.waystation cordial |
| faction.ashen_hollow.waystation | the Waystation | location.outpost | npc.ashen_hollow.kera_voss, npc.ashen_hollow.renn_vale | faction.ashen_hollow.survey cordial |

## 4. Knowledge

- Built: `reported` - a faction learns an act when the character tells one of its members.
- Reserved: `witnessed` (M9).
- Act log capacity: 256; the oldest act goes first.

## 5. Scenarios

A is the Waystation in P rows and the keepers in F rows; B is the Survey or the delvers. A column reads: source, via, identity, Δ, points, tier.

| # | Scenario | Act (seq, kind, subject) | Reports | A | B | Other checks | Proves |
|---|---|---|---|---|---|---|---|
| F1 | wolf killed; tell Kera, then Sel | 1, creature_killed, wolf | A via kera; B via sel | reported, kera, identified, +100, 100, accepted | reported, sel, identified, -100, -100, wary | 2 ReputationChanged | **the same act moves two factions in opposite directions, in a fixture (ROADMAP exit)** |
| F5 | F1, then tell Kera and Sel again | 1, creature_killed, wolf | A, B again | reported, kera, identified, +100, 100, accepted | reported, sel, identified, -100, -100, wary | 0 FactionLearned, 0 ReputationChanged | reports are idempotent |
| F2 | wolf killed; tell nobody | 1, creature_killed, wolf | none | no row, 0, neutral | no row, 0, neutral | knowledge rows: 0; act_done holds: yes | no report, no change |
| F4 | crafted save: act 1 known unidentified by B (witnessed, via sel, Δ 0); then tell Sel | 1, creature_killed, wolf | B via sel | none | before: witnessed, sel, unidentified, 0, 0, neutral. After: reported, sel, identified, -100, -100, wary (upgraded) | 1 FactionLearned with Upgraded; 1 ReputationChanged | the identity seam: nothing moves until identified, then the delta applies once |
| F8 | two wolves killed; tell Kera once | 1, creature_killed, wolf; 2, creature_killed, wolf | A via kera | +100, +100: 200, accepted | no row, 0, neutral | 2 FactionLearned, in Seq order 1 then 2 | distinct acts each apply |
| F10 | F2; save and resume between the act and the report; tell Sel after the resume | 1, creature_killed, wolf | B via sel | no row, 0, neutral | reported, sel, identified, -100, -100, wary | StateDump.Compare: 0 differences against an unsaved twin that did the same | continuity across a save |
| F11 | F1's act with relations A → B opposed, B → A close; tell Kera only | 1, creature_killed, wolf | A via kera | reported, kera, identified, +100, 100, accepted | no row, 0, neutral |  | relations never move standing |
| F12 | wolf killed; a third faction C (faction.fixture.watchers) reacts +100 but has no member; tell Kera and Sel | 1, creature_killed, wolf | A, B | reported, kera, identified, +100, 100, accepted | reported, sel, identified, -100, -100, wary | C: no row, 0, neutral | a faction nobody told never learns |
| F13 | a bristleback boar killed; no faction reacts | none | none | no row, 0, neutral | no row, 0, neutral | 0 ActRecorded; NextActSeq 1 | irrelevant acts are not recorded |
| F14 | capacity 16: wolf 1 killed and told to Kera, then 16 more wolves killed | 2..17 held | A via kera (act 1) | 100, accepted, unchanged by eviction | no row, 0, neutral | after act 17: act 1 gone, knowledge rows 0; NextActSeq 18 | the oldest act goes first; standing is untouched |
| U1 | Domain unit: Learn with identity: unidentified | synthetic | none | row stored, Δ 0, points 0 |  |  | an unidentified row applies no delta |
| P1 | shipped: the armour killed (§5.6.4) | 1, creature_killed, animated | none | no row, 0, neutral | no row, 0, neutral | billets absent from Wares(Kera); a raw BuyCommand for the billet ref: "Kera Voss will not sell you that" | nobody knows |
| P2 | the heart steadied | 2, switch_set, steadied | none | no row, 0, neutral | no row, 0, neutral | acts recorded: 2 (the three stone flags record none) | Tavar is no member: nobody knows |
| P3 | tell Kera armour; buy one billet | 1 | Waystation via kera | reported, kera, identified, +100, 100, accepted | no row, 0, neutral | billets listed, one bought: yes; Kera's relationship events: 0 | report knowledge; the service gate opens; the Survey does not hear |
| P4 | tell Sel tavar_back | 2 | Survey via sel | reported, kera, identified, +100, 100, accepted | reported, sel, identified, +100, 100, accepted | notes offered | the dialogue gate opens |
| P5 | tell Sel armour | 1 | Survey via sel | reported, kera, identified, +100, 100, accepted | reported, sel, identified, -100, 0, neutral | notes not offered; Survey points 0, neutral | **one act: Waystation +100, Survey -100**; the gate closes |
| N1 | checkpoint after P2 | 1, 2 | none | no row, 0, neutral | no row, 0, neutral | knowledge rows: 0; Kera's armour reply offered (P3), Sel's offered (P4) | no psychic factions |
