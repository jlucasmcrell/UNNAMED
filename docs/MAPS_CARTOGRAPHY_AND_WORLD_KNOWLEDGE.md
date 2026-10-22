# OTHERREACH — Maps, Cartography & World Knowledge

**Status:** Signature-system candidate — §13 records what is built, and what this document still asks for  
**M2 impact:** None

## 1. Foundational rule

> **The map represents what the character knows, not what the game database knows.**

Separate:

1. **Reality** — what exists.
2. **Knowledge** — what the character believes/knows.
3. **Interface** — how the UI displays that knowledge.

## 2. No automatic omniscient map

Do not automatically reveal every:

- quest giver;
- rare resource;
- enemy;
- cave;
- treasure;
- secret;
- objective.

## 3. Knowledge sources

Map information can come from:

- exploration;
- purchased maps;
- cartographers;
- NPC directions;
- found fragments;
- journals/books;
- rumors;
- treasure maps;
- faction intelligence;
- hunting/tracking;
- surveying;
- divination;
- technology.

## 4. Map products

Examples:

- regional road map;
- herbalist's map;
- mining survey;
- military map;
- monster-hunter notes;
- treasure map;
- ancient ruin map;
- gate/Otherway chart.

## 5. Accuracy and uncertainty

A map marker can represent confidence rather than exact coordinates.

Examples:

- exact;
- approximate;
- reported;
- outdated;
- disputed.

An old map may show:

- collapsed bridge;
- abandoned road;
- vanished settlement;
- changed river.

## 6. Quest markers

Quest markers are allowed **when the player has acquired information precise enough to justify them**.

Example:

NPC says “north of the split stone” → journal clue, no exact marker.

Player buys military survey → ruins become marked.

The quest did not change; knowledge did.

## 7. Cartography skill

A cartographer can:

- survey;
- triangulate;
- improve accuracy;
- copy maps;
- combine fragments;
- annotate;
- sell information;
- chart travel nodes.

NPC adventurers may buy player-produced maps.

## 8. Tracking layers

Tracking information should often be temporary:

- tracks;
- blood trail;
- recent direction;
- estimated age;
- disturbed vegetation;
- scent trail.

Do not permanently mark “wolf here” because the player found one track.

## 9. Gathering knowledge

Skills may infer:

- favorable soil;
- ore-bearing geology;
- animal territory;
- plant habitat;
- weather patterns.

This can add zones/notes rather than exact magical pins.

## 10. Pets and tools

Functional animals or equipment may improve discovery:

- tracking hound;
- truffle pig;
- scouting bird;
- magical lens;
- Vaskaal sensor;
- survey equipment.

## 11. Shared knowledge

Later decide whether:

- companions share discoveries automatically;
- maps can be traded;
- characters on the same account share maps;
- community servers allow player-map markets.

Default should avoid unexplained omniscience.

## 12. Design principle

> Exploration becomes richer when information itself is loot.

## 13. What is built today (spec 41, 2026-10-22)

This section is the honest ledger: each principle above, what the code does now, and what it does not yet do. Everything marked **built** is
under test; everything marked **not built** is still an intention, not a behaviour.

| § | Principle | State |
|---|---|---|
| 1 | The map shows knowledge, not the database | **Built.** `KnownMap` holds only places the character has discovered and roads walked; the map panel draws that and nothing else. |
| 2 | No omniscient map | **Built.** A place appears by being walked into (`LocationDiscovered`); the map never reveals an unvisited site. |
| 3 | Knowledge sources: journals/books, NPC directions | **Partly built.** Lore entries are found by walking into a place, reading a book or inscription in the world, asking a person, finishing a job, or studying a beast (spec 41). Purchased maps, cartographers, fragments, rumours, faction intelligence and divination are **not built**. |
| 4 | Map products (herbalist's map, mining survey, hunter's notes …) | **Not built.** The lore entries are the closest thing: a Survey record writes up what the Survey measured. |
| 5 | Accuracy and uncertainty (exact / approximate / reported / outdated / disputed) | **Partly built.** The map draws discovered ground only; markers carry no confidence grade. Disagreement is carried by the *lore* (pages in different voices that contradict each other) rather than by the marker. |
| 6 | Quest markers only when the information justifies them | **Built.** A tracked quest shows its own marker once its objective names a place; a quest that says "north of the split stone" gives a journal clue and no pin. |
| 7 | Cartography as a skill | **Not built.** No surveying, triangulation, copying, combining or selling of maps exists. |
| 8 | Temporary tracking layers | **Not built.** No tracks, blood trails or estimated ages are drawn, and the awareness rows show *bodies and what they have noticed*, never a suspicion the character has not observed. |
| 9 | Gathering knowledge infers zones | **Not built.** |
| 10 | Pets and tools improve discovery | **Not built.** |
| 11 | Shared knowledge across characters, companions, accounts | **Not built**, and the document's own default applies: no unexplained omniscience. |
| 12 | Information is loot | **Built in miniature.** 85 entries and 190 pages of lore are found and kept; a page can be the reward for a job (quest epilogues), for a person's trust (a telling), or for surviving a beast long enough to study it (the bestiary). |

**Map notes (built).** Where the journal holds something about a place, the map says so under the pin — the entry's title and how much of it is
known. More than one entry can name the same ground, and the pin says both rather than letting one win by accident.
