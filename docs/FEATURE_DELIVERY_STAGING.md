# OTHERREACH — Feature Delivery Staging

**Purpose:** Preserve the large vision without requiring every advanced system in the first playable build.

**Status:** Planning guidance, not a replacement for `ROADMAP.md`  
**M2 impact:** None

## Principle

Each ambitious feature should grow through layers:

1. **Proof** — narrow mechanic works.
2. **Playable system** — enough breadth for a vertical slice.
3. **Systemic integration** — NPCs/economy/world consume it.
4. **Long-horizon simulation** — persistent consequences.
5. **Advanced/cosmic/multiplayer expansion** — only after the core game proves itself.

Current foundation remains:

**M2 → M2b → M2c → engine validation spike → M3+**

## Combat & stealth

### Proof
- melee attack/block/dodge;
- one ranged weapon;
- health/stamina;
- basic armor;
- vision/sound detection;
- last-known position;
- no global aggro.

### Vertical slice
- multiple weapon families;
- armor coverage/gaps;
- weak-point kills;
- wounds/downed;
- surrender;
- pulling/kiting;
- corpses/evidence.

### Expansion
- grappling;
- scent/track perception;
- advanced morale;
- faction alarm networks;
- magical/technical detection.

## Crime & justice

### Proof
- ownership;
- theft;
- assault/murder;
- witness line of sight;
- local guard response;
- surrender;
- fine/bounty.

### Vertical slice
- witness descriptions;
- disguise;
- evidence;
- self-defense;
- local warrants;
- bounty-hunter contract;
- prison time skip.

### Expansion
- corruption;
- framing;
- criminal associations;
- multiple jurisdictions;
- thieves' guild;
- extradition;
- settlement law;
- Soul crimes.

## Companions

### Proof
- one companion;
- follow/hold/attack/retreat;
- equipment;
- basic relationship state;
- downed/rescue.

### Vertical slice
- player + 3;
- formations;
- standing tactics;
- personal goals;
- equipment ownership;
- simple offscreen life.

### Expansion
- autonomous parties;
- relationships/romance;
- retirement;
- teaching;
- crime testimony/accomplice behavior.

## Quests/dungeons/events

### Proof
- authored quests;
- optional timer;
- one systemic contract type;
- one dungeon clear state;
- one repopulation transition.

### Vertical slice
- NPC quest completion;
- LFG board;
- changing dungeon occupants;
- role-replacement boss;
- resolved-opportunity history;
- local event director.

### Expansion
- town destruction/rebuilding;
- competitive/shared opportunities;
- faction takeovers;
- regional events.

### Late
- continental/cosmic events;
- persistent epics;
- multiplayer shared-event rules.

## Magic

### Proof
- Resonance/Strain;
- 1–2 domains;
- known formulas;
- wand/staff difference;
- healing linked to injury.

### Vertical slice
- several domains;
- tuning known spells;
- runes/enchanting;
- rituals;
- countermagic;
- magical residue.

### Expansion
- full spell grammar/editor;
- player-named formulas;
- teaching;
- divine ritual;
- summoning;
- spatial/Othercraft.

### Late
- Great Works;
- Otherwhen;
- cosmic/astromantic practice.

## Souls

### Proof
- Soul identity exists internally;
- Soul Sheet placeholder;
- one incarnation;
- resonance signature;
- no inherited power yet.

### Vertical slice
- attunements;
- echoes;
- scars;
- divine/Mor recognition hooks;
- soul-bound artifact hook.

### Expansion
- reincarnation;
- Soul Familiarity;
- incarnation history;
- cross-life recognition.

### Late
- Soulcraft;
- intentional Soul modification;
- complex covenants;
- rare Soul damage;
- extraordinary multi-incarnation/Otherwhen exceptions.

## Economy/NPC simulation

### Proof
- local shop;
- basic supply/demand;
- one route;
- simple NPC work.

### Vertical slice
- autonomous adventurers;
- contracts;
- crafting commissions;
- local market response;
- home/property progression.

### Expansion
- settlement economic chains;
- shortages;
- NPC property/investment;
- regional trade/history.

## Crafting

### Proof
- modular interfaces;
- several materials;
- repair/reforge;
- a few hybrid items.

### Vertical slice
- broad component grammar;
- cross-profession craft;
- quality dimensions;
- salvage;
- commissions.

### Expansion
- advanced mechanisms;
- custom enchantment integration;
- alien/cultural technologies;
- provenance/famous makers.

## Maps/knowledge

### Proof
- map fog/knowledge;
- purchased map reveals info;
- journal directions;
- simple tracking.

### Vertical slice
- confidence/outdated maps;
- cartography;
- resource/hunting overlays.

### Expansion
- player-created/sold maps;
- faction intelligence;
- divination;
- shared knowledge rules.

## Property/settlement

### Proof
- one home;
- wilderness camp;
- storage.

### Vertical slice
- ownership/rent;
- modular building;
- tax/jurisdiction;
- workers.

### Expansion
- homestead → hamlet;
- NPC migration;
- services;
- trade.

### Late
- governance policy;
- destruction/rebuilding;
- gate hubs;
- server politics.

## AI narrative

### Proof
- no runtime LLM dependency;
- structured/template dialogue;
- provider interface only.

### Vertical slice
- optional direct dialogue;
- commission prose;
- memory summaries;
- caching.

### Expansion
- story proposals;
- books/letters/history;
- companion contextual dialogue.

The game remains playable with AI disabled.

## Multiplayer

### Stage A
Single-player authoritative local world.

### Stage B
LAN/host-join experiment after single-player loop is solid.

### Stage C
Community dedicated servers.

### Stage D
Official persistent realms.

### Stage E
Optional federation / server-to-server Othergates.

MMO-scale infrastructure is not a prerequisite for any earlier stage.

## Internal release labels

Classify features as:

- **Prototype**
- **Vertical Slice**
- **1.0 Candidate**
- **Post-1.0**
- **Experimental/Future**

Do not market every future idea as a launch promise.

## Priority heuristic

Prefer work that:

1. improves the current playable loop;
2. validates a high-risk assumption;
3. unlocks several later systems;
4. reuses existing content;
5. can be tested objectively.

## Core rule

> **Otherreach should grow by deepening working systems, not by shipping dozens of half-built ones.**
