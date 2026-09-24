# OTHERREACH — NPC Simulation & Autonomous Adventurers

**Status:** Signature-system candidate  
**M2 impact:** None

## 1. Core principle

> **NPCs are participants in the world, not decorations placed for the player.**

The player is one adventurer among other adventurers.

## 2. NPC simulation classes

Not every NPC needs the same complexity.

### Ambient NPC
Presentation/schedule flavor with minimal durable state.

### Resident NPC
Home, work, money, relationships, schedule, basic inventory.

### Economic Agent
Production, consumption, shop/business behavior, contracts.

### Adventurer Agent
Goals, combat, travel, quests, parties, equipment, progression.

### Major Character
Everything relevant above plus authored story/personality/relationship depth.

## 3. Autonomous adventurer state

An adventurer may possess:

- lineage/culture/background;
- level/skills/masteries;
- equipment/inventory;
- money;
- profession knowledge;
- home/room/camp;
- faction standing;
- relationships;
- known locations/maps;
- discovered travel nodes;
- ambitions;
- current goals;
- party role preferences;
- risk tolerance.

## 4. Goal planning

Example:

**Need better bow → need money → find bounty → buy arrows → travel → hunt → collect reward → commission bow**

Planning should be deterministic/systemic where practical, not LLM-controlled.

## 5. Simulation tiers

Use existing A/B/C/D simulation philosophy.

### Tier A
Full local simulation:
- movement;
- combat;
- pathfinding;
- animation;
- interactions.

### Tier B
Reduced simulation:
- coarse movement;
- scheduled activities;
- lower-frequency decisions;
- simplified combat.

### Tier C
Event resolution:
- travel jobs;
- hunting sessions;
- crafting jobs;
- trade;
- injuries;
- resource consumption.

### Tier D
Large-step analytical advancement:
- income;
- residence;
- long travel;
- profession activity;
- reputation;
- goal changes.

Promotion to higher fidelity must reconcile into a legal physical state.

## 6. Quests and contracts

NPC adventurers can:

- accept systemic contracts;
- post their own contracts;
- abandon jobs;
- fail jobs;
- complete bounties;
- commission gear.

Major authored player quests should not be casually consumed by autonomous actors unless explicitly designed as shared world events.

## 7. Parties

Adventurers evaluate difficult objectives and recruit roles such as:

- frontline;
- healer;
- ranged;
- arcane;
- lock/trap;
- tracker.

They may invite the player because they genuinely need that capability.

## 8. Failure

NPC adventurers may:

- flee;
- become injured;
- lose gear;
- become trapped;
- be captured;
- fail contracts;
- die where permitted.

Failure can produce emergent rescue/recovery content.

## 9. Progression

NPCs can improve over time through legitimate system actions.

A novice encountered early may later:

- own better equipment;
- gain skills;
- change profession;
- acquire property;
- retire.

## 10. Retirement and settlement

An adventurer may eventually:

- buy a home;
- open a shop;
- become a trainer;
- found a homestead;
- join a settlement.

This allows recognizable world history.

## 11. Crafting and commissions

NPCs use the same item/content grammar.

A smith lacking material can create a procurement contract. An adventurer may order an unusual weapon, creating economic demand.

## 12. Boss interaction

Classify major encounters:

- **personal/epic:** protected from generic NPC completion;
- **repeatable world boss:** NPC parties may participate;
- **persistent threat:** NPCs genuinely can defeat it;
- **catastrophic/story boss:** controlled by authored state transitions.

## 13. Relationship memory

NPCs remember meaningful player interactions.

The simulation stores the fact. Optional AI later expresses it naturally.

## 14. Performance rule

Never run full physics/pathfinding/combat for thousands of distant NPCs.

Autonomy is not the same thing as full real-time simulation.
