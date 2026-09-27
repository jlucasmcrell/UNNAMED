# OTHERREACH — Phase-1 Ashen Hollow Playable Content Bible

**Status:** Owner-approved Phase-1 content direction  
**Scope:** M3 through M6 playable prototype  
**Prototype footprint:** 200 m × 200 m, four 100 m × 100 m cells  
**Larger future region:** Otherhome Marches  
**Primary camera:** full-body third-person / over-the-shoulder with seamless zoom to first person  
**Purpose:** Define the concrete place, content, encounters, UI, quests, and acceptance route that turn Phase-1 systems into a playable slice of Otherreach.

---

# 1. Foundational Rule

> **Ashen Hollow is not a tutorial corridor. It is a tiny real place whose systems happen to make it suitable for proving the game.**

Phase 1 should prove that Otherreach can already feel like an RPG before the world is large, beautiful, or content-rich.

The player should be able to:

- move through a coherent place;
- explore;
- interact;
- fight;
- acquire and equip gear;
- gather;
- craft;
- use basic magic;
- speak with NPCs;
- complete quests;
- recruit one companion;
- die and return;
- save, quit, relaunch, and resume the same world.

---

# 2. Prototype Geography

Use local coordinates for the 200 m × 200 m prototype.

- **X increases east.**
- **Z increases north.**
- **Y is elevation.**
- Southwest corner = `(0, 0)`
- Northeast corner = `(200, 200)`

| Cell | Bounds | Identity |
|---|---|---|
| A | X 0–100, Z 100–200 | Ashen Hollow Waystation |
| B | X 100–200, Z 100–200 | Charwood Verge |
| C | X 0–100, Z 0–100 | Blackvein Cut |
| D | X 100–200, Z 0–100 | Foldscar Ruin |

```text
NORTH

Z=200
┌──────────────────────────┬──────────────────────────┐
│          CELL A          │          CELL B          │
│     ASHEN HOLLOW         │      CHARWOOD VERGE     │
│                          │                          │
│  Waystation / forge      │  woods / stream         │
│  NPCs / respawn          │  hound / ruined cart    │
│  crafting / storage      │  bow / ash resource     │
│                          │                          │
├──────────────────────────┼──────────────────────────┤ Z=100
│          CELL C          │          CELL D          │
│     BLACKVEIN CUT        │      FOLDSCAR RUIN      │
│                          │                          │
│  quarry / iron           │  reality distortion     │
│  humanoid enemies        │  quiet stones           │
│  boar / heavy enemy      │  spider / Tavar         │
│                          │                          │
└──────────────────────────┴──────────────────────────┘
Z=0
X=0                      X=100                     X=200
```

---

# 3. Macro Elevation

The prototype slopes gently from northwest toward southeast.

| Area | Typical Y |
|---|---:|
| Northwest road ridge | 8–10 m |
| Waystation | 6–8 m |
| Charwood | 4–7 m |
| Blackvein quarry floor | 0–3 m |
| Foldscar basin | 2–4 m |
| Foldscar standing stones | 5–7 m |
| Quarry rim | 8–11 m |

Target approximately 10 m of meaningful elevation variation.

---

# 4. Primary Road

Approximate spline:

```text
(0,180)
  ↓
(30,165)
  ↓
(55,145)     Ashen Hollow
  ↓
(78,125)
  ↓
(105,108)
  ↓
(132,92)
  ↓
(158,70)
  ↓
(200,55)
```

Suggested widths:

- main road: 3–4 m;
- settlement paths: 1.5–2.5 m;
- forest trails: 1–1.5 m;
- quarry work paths: 2–3 m.

---

# 5. Cell A — Ashen Hollow Waystation

**Bounds:** X 0–100, Z 100–200  
**Typical elevation:** Y 6–9 m

The player begins beside the **Ashen Waystone** at approximately `(30,158)`.

From the opening viewpoint the player should notice:

- Kera's smithy smoke;
- the communal building;
- the road through the hollow;
- the quarry ridge;
- only a faint suggestion of Foldscar stone through distant trees.

## Key placements

- Ashen Waystone: `(27,158)`
- Kera's smithy: `(58,142)`
- Communal lodge/steward building: `(44,128)`
- Sel's survey area: `(72,122)`
- Well: `(49,151)`
- Storage chest: `(38,137)`
- Charwood exit: `(88,145)`
- Blackvein exit: `(65,108)`
- Foldscar trail branch: near `(90,115)`

Approximate developed settlement footprint:

```text
X 30–85
Z 115–170
```

The settlement should feel like a frontier stop, not a village or city.

---

# 6. Cell B — Charwood Verge

**Bounds:** X 100–200, Z 100–200  
**Typical elevation:** Y 4–7 m

Open woodland with alternating tree clusters, brush, fallen timber, clearings, and shallow drainage.

Typical sight distance should often be 20–40 m.

## Key placements

- Main trail entry: `(102,142)`
- Ash Ember Hound encounter: `(128,150)`
- Damaged merchant cart / hunting bow: `(157,162)`
- Ash Haft resource: `(180,138)`
- Optional Woundmoss: `(151,128)`

Stream path:

```text
(190,195)
→ (170,165)
→ (150,132)
→ (132,108)
→ Foldscar drainage
```

At approximately `(175,120)`, frame the first strong view of the Foldscar through the trees.

No cinematic is required.

---

# 7. Cell C — Blackvein Cut

**Bounds:** X 0–100, Z 0–100

A shallow abandoned quarry / iron working.

Quarry rim: Y 7–10 m  
Quarry floor: Y 1–3 m

## Key placements

- Quarry entrance: `(63,98)`
- Upper overlook: `(54,74)`
- Bone Walker patrol: `(48,58)`
- Iron vein: `(28,45)`
- Animated Armour: `(65,34)`
- Bristleback Boar territory: `(18,72)`
- Blocked shaft: `(76,22)`

The player should be able to get ore without being forced to defeat every enemy.

The blocked shaft communicates that the world extends beyond the prototype; it is not a Phase-1 dungeon.

---

# 8. Cell D — Foldscar Ruin

**Bounds:** X 100–200, Z 0–100  
**Typical elevation:** Y 2–7 m

The Foldscar is the first place where the prototype visibly stops being ordinary frontier fantasy.

Keep the weirdness restrained:

- slight image doubling;
- delayed shadow;
- displaced audio;
- inconsistent reflections.

## Key placements

- Primary approach: `(120,93)`
- Secondary Charwood approach: `(164,101)`
- Central Foldscar: `(153,48)`
- North Quiet Stone: `(150,78)`
- Southwest Quiet Stone: `(122,38)`
- Southeast Quiet Stone: `(181,31)`
- Cave Hunting Spider territory: `(172,48)`
- Tavar recovery position: `(145,42)`

Safer route around the spider:

```text
(160,65)
→ (187,65)
→ (194,45)
→ (181,31)
```

The non-kill quest must actually be completable without killing the spider.

---

# 9. Phase-1 NPC Roster

Names are working names until cultural naming is finalized.

## Renn Vale — Veth
**Role:** waystation steward / practical local authority  
**Purpose:** orientation, structured dialogue, world-state acknowledgment.

## Kera Voss — Kal
**Role:** smith  
**Purpose:** first crafting quest, equipment interaction, resource-to-object loop.

## Sel Arien — Siann
**Role:** archivist / survey scholar  
**Purpose:** Foldscar framing, basic magic access, uncertain knowledge rather than omniscient exposition.

## Tavar Orr — Orenth
**Role:** guide / first companion  
**Purpose:** non-kill quest outcome, first recruitable companion, Other-related perspective.

Phase-1 companion commands:

- Follow;
- Wait.

No radial menu is required.

---

# 10. Phase-1 Enemy Roster

Phase 1 should ultimately contain **five distinct creature/enemy archetypes**, not one creature duplicated under five behavioral labels.

Temporary placeholder geometry may be reused while systems are being built, but the data model and M3d/M6 acceptance must support five separate archetypes.

| Enemy | Role | Primary lesson |
|---|---|---|
| Ash Ember Hound | fast predator | movement, timing, pursuit |
| Bone Walker Husk | basic humanoid | readable melee/reach |
| Animated Armour | armored heavy | armor/weak-point logic |
| Bristleback Boar | charging brute | lateral movement/terrain |
| Cave Hunting Spider | ambush/nonhuman | perception/avoidance |

Initial placements:

| Enemy | Position |
|---|---|
| Ash Ember Hound | `(128,150)` |
| Bone Walker Husk | `(48,58)` |
| Animated Armour | `(65,34)` |
| Bristleback Boar | `(18,72)` |
| Cave Hunting Spider | `(172,48)` |

Home territories are acceptable for prototype stability, but should read fictionally rather than as MMO aggro leashes.

---

# 11. Phase-1 Weapon Families

Freeze three prototype weapon families:

1. one-handed sword;
2. bow;
3. spear/polearm.

Acquisition flow:

```text
START
  ↓
Sword already owned
  ↓
EXPLORE
  ↓
Bow found at merchant cart
  ↓
GATHER + CRAFT
  ↓
Spear crafted by player
```

---

# 12. Phase-1 Crafting

First profession proof:

**blacksmithing**

Only two recipes are required.

## Iron Billet

Input:
- Raw Iron Ore

Output:
- Iron Billet

## March Spear

Inputs:
- Iron Billet
- Ash Haft

Output:
- March Spear

This is a simplified ancestor of the future compositional crafting system.

---

# 13. Phase-1 Magic

These are three small formulas from different domains, not a final school system.

Magic uses **Resonance / Strain**, not universal mana.

## Impulse Bolt
**Domain:** Force  
Short ranged kinetic projection.

## Brace Ward
**Domain:** Warding  
Brief local protection against intrusion/impact.

## Mending Thread
**Domain:** Vital  
Small recovery/stabilization effect.

---

# 14. Starting Character

Phase 1 may use a fixed Veth acceptance character rather than final character creation.

Working package name:

**Wayfarer**

Starting equipment:

- simple arming sword;
- prototype Veth-compatible outfit;
- torch;
- one simple restorative if needed;
- tiny amount of currency.

The player does not begin with the bow or spear.

---

# 15. Quest 1 — Iron Under Ash

**Quest giver:** Kera Voss

```text
Speak to Kera
      ↓
Reach Blackvein Cut
      ↓
Obtain Raw Iron Ore
      ↓
Return to Ashen Hollow
      ↓
Produce Iron Billet
      ↓
Craft March Spear
      ↓
Show Kera the finished weapon
```

There is no objective to kill a fixed number of enemies.

If the player gets the ore without fighting, that is valid.

Reward:

- keep the spear;
- modest progression;
- modest money/material;
- Kera's recognition.

---

# 16. Quest 2 — The Three Quiet Stones

**Quest giver:** Sel Arien

```text
Learn what happened to Tavar
        ↓
Reach Foldscar
        ↓
ANY ORDER:
  Align north marker
  Align southwest marker
  Align southeast marker
        ↓
All three aligned
        ↓
Stabilize central Foldscar
        ↓
Tavar becomes reachable
        ↓
Speak to Tavar / Sel
```

There is no mandatory combat objective.

The spider controls the convenient route, not the objective itself.

---

# 17. First Companion — Tavar Orr

Phase-1 functionality:

- recruit;
- follow;
- wait;
- catch up;
- fight;
- downed/death behavior required by prototype;
- save/load continuity.

No Phase-1:

- affinity ladder;
- romance;
- full tactical menu;
- personal quest;
- offscreen life simulation.

---

# 18. Death / Respawn

Phase-1 terminal flow:

```text
Downed
  ↓
Dying
  ↓
Dead
  ↓
Return at Ashen Waystone
```

Respawn location:

`(30,158)`

Phase-1:

- no inventory loss;
- world state remains persistent;
- quest progress remains valid;
- death does not reload the entire world to the previous save.

Use simple fiction:

> **You return at the Ashen Waystone.**

Do not prematurely canonize full Soul/resurrection metaphysics.

---

# 19. Phase-1 HUD

The current M3 debug HUD may remain while systems are under active development.

Before M6 acceptance, provide a functional but intentionally plain player HUD.

Final visual styling is deferred until gameplay survives playtesting.

## Core player information

Support:

- Health;
- Stamina;
- Focus;
- Strain;
- current/equipped weapon;
- active item;
- contextual interaction prompt;
- optional compass/heading;
- tracked objective;
- companion state when recruited.

Avoid an MMO-style permanent wall of statistics.

## Enemy information

Do not default to exact numeric enemy health in release-facing UI.

Prefer qualitative condition where useful:

- Healthy;
- Wounded;
- Critical;
- Downed.

Exact numeric health may remain in development/debug mode.

## Hotbar

Recommended prototype convenience layer:

8 slots.

Example:

```text
1 Sword
2 Bow
3 Spear
4 Impulse Bolt
5 Brace Ward
6 Mending Thread
7 Restorative
8 Torch
```

The hotbar is a shortcut, not the only way to access capability.

## Companion HUD

Tavar needs only:

- name;
- relevant health/downed state;
- Follow / Wait state.

No affinity meter.

## Quest tracker

Optional/minimal.

Example:

```text
IRON UNDER ASH
Obtain iron from Blackvein Cut
```

The Journal remains the authoritative full quest record.

## Status effects

Show only when relevant.

Examples:

- Bleeding;
- Wounded;
- Strained;
- Burning;
- Weakened.

## Death UI

Support clear:

- Downed;
- Dying;
- Dead / cause of death;
- return/respawn message.

Detailed combat telemetry can remain a developer option.

---

# 20. UI Rule — No Mandatory Radial Menus

> **Radial menus are not the default interaction model in Otherreach.**

Formal rule:

> **A radial menu may exist as an optional convenience layer, but no core action, spell, weapon, companion command, building function, emote, or interaction may require a radial menu.**

Primary interfaces:

- direct keybinds;
- hotbars;
- contextual prompts;
- tabs;
- searchable/categorized lists;
- conventional menus.

Any optional radial should use:

- large forgiving slices;
- strong snap;
- text labels plus icons;
- center dead-zone protection;
- configurable sensitivity;
- remembered last selection;
- optional explicit confirmation;
- no mandatory release-to-select;
- no nested radial chains where avoidable;
- keyboard/controller parity;
- optional pause or slow-time in single-player.

No capability disappears if radial menus are disabled.

---

# 21. Basic Phase-1 Menus

By M6, provide functional developer-quality versions of:

- Inventory / Equipment;
- Character;
- Journal;
- Known Magic / Techniques;
- Companion, after Tavar is recruited.

Do not spend Phase 1 on ornate final UI art.

Preserve room for HUD modes:

- Full;
- Minimal;
- Dynamic / Auto-hide;
- Off.

---

# 22. Map / Compass

Prefer a simple compass/heading cue over an omniscient minimap.

A rough discovered map may exist.

The tiny prototype should be learnable spatially.

---

# 23. Interaction Set

## Cell A
- door;
- chest;
- NPCs;
- forge;
- anvil;
- optional well.

## Cell B
- damaged cart;
- bow pickup;
- ash resource;
- optional Woundmoss.

## Cell C
- iron node;
- loot;
- quarry equipment.

## Cell D
- three Quiet Stones;
- central Foldscar;
- Tavar.

---

# 24. Streaming / Cell Boundaries

Avoid critical content directly on seams.

Recommended soft exclusion zones:

```text
X = 95–105
Z = 95–105
```

Roads and trails should cross seams deliberately.

Acceptance movement should naturally exercise:

```text
A → B
B/A → C
C → A
A → D
D → A
```

---

# 25. Sightline Rule

> **The player should usually have one orientation landmark, but should never see the whole prototype at once.**

Use terrain folds, vegetation, quarry walls, roofs, and rock shelves.

The Foldscar should become progressively more visible rather than dominating the opening view.

---

# 26. Camera-Test Locations

Deliberately exercise camera behavior in:

- wide Charwood clearing;
- narrow smithy/fence gap;
- small communal-building interior;
- quarry switchback;
- sparse Foldscar approach.

Test:

- third-person collision;
- zoom;
- first-person transition;
- obstruction;
- indoor compression.

---

# 27. Environment Tone

First half:

- wood;
- iron;
- mud;
- smoke;
- cloth;
- forest;
- quarry stone.

Second half:

- Foldscar misregistration;
- Orenth displacement;
- Resonance;
- impossible alignment.

> **If everything is strange, the Other stops feeling Other.**

---

# 28. Lighting / Weather

Default acceptance:

- daytime;
- late morning / early afternoon;
- clear or lightly overcast.

Night and severe weather are separate future tests.

---

# 29. First Ten Minutes

**0:00** — Spawn beside Ashen Waystone.

**0:30** — Interact with settlement door/chest.

**1:00** — Speak with Renn/Kera.

**2:00** — Receive *Iron Under Ash*.

**3:00** — Leave toward Charwood.

**4:00** — First hound encounter.

**5:00** — Find merchant cart and bow.

**6:00** — Reach Blackvein.

**7:00** — First Bone Walker combat.

**8:00** — Mine iron.

**9:00** — Begin return.

**10:00** — Player has experienced movement, exploration, combat, resource acquisition, and equipment.

---

# 30. Full 30-Minute Acceptance Route

```text
Spawn at Ashen Waystone
↓
Talk to NPCs
↓
Accept Iron Under Ash
↓
Explore Charwood
↓
Fight or avoid hound
↓
Find bow
↓
Reach quarry
↓
Fight or avoid Bone Walker
↓
Mine iron
↓
Return to waystation
↓
Refine billet
↓
Craft spear
↓
Complete first quest
↓
Speak to Sel
↓
Gain access to three magic proofs
↓
Begin The Three Quiet Stones
↓
Enter Foldscar
↓
Align three markers in any order
↓
Avoid or fight spider
↓
Stabilize Foldscar
↓
Recover Tavar
↓
Recruit companion
↓
Return to waystation
↓
Save
↓
Quit application
↓
Relaunch
↓
Load
↓
Verify persistent state
```

---

# 31. Exact Acceptance Path

```text
START
Ashen Waystone
(30,158)

→ Kera
(58,142)

→ east exit
(88,145)

→ hound area
(128,150)

→ cart / bow
(157,162)

→ return southwest
(100,115)

→ quarry entrance
(63,98)

→ Bone Walker
(48,58)

→ iron node
(28,45)

→ return north
(58,142)

→ forge/anvil

→ Foldscar path
(105,108)

→ north Quiet Stone
(150,78)

→ southwest Quiet Stone
(122,38)

→ southeast Quiet Stone
(181,31)

→ central Foldscar
(153,48)

→ Tavar
(145,42)

→ return settlement
(55,140)

→ save / quit / reload
```

This is an acceptance path, not a player railroad.

---

# 32. Required Player-Choice Tolerance

The prototype should tolerate:

- quarry visit before accepting Kera's quest;
- Foldscar visit before receiving Sel's quest;
- bow pickup before quest progression;
- obtaining ore without combat;
- ignoring the boar;
- killing or avoiding the spider;
- alternate routes.

Quest logic should not assume a single sequence unless fiction requires it.

---

# 33. Persistence Acceptance State

Construct a save that touches many systems:

- progression changed;
- bow acquired;
- spear crafted;
- ore consumed;
- container opened;
- creature state diverged;
- Quiet Stones aligned;
- Tavar recruited;
- player Health non-default;
- player Strain non-default;
- selected weapon changed;
- player away from spawn.

Then:

```text
save
→ quit application completely
→ relaunch
→ load
→ field-by-field compare authoritative state
```

Do not accept merely:

> "The save opened and looked okay."

---

# 34. Performance Target

Phase-1 practical gate:

> **1080p / sustained 60 FPS on RAZER's RTX 4070 Ti in a clean performance window.**

Record:

- CPU frame time;
- GPU frame time;
- 1% lows where practical;
- RAM;
- VRAM;
- visible streaming hitches.

The 2×2 km greybox remains measurement evidence rather than the formal D-01 revisit gate.

Failure of the simple Phase-1 prototype to sustain 60 FPS after reasonable optimization is an owner-review stop.

---

# 35. M6 Acceptance Requirements

Before Phase 2:

- movement works;
- third-person ↔ first-person works;
- four cells traverse/stream;
- interactions work;
- inventory/equipment works;
- sword/bow/spear work;
- combat works;
- death/respawn works;
- five distinct creature/enemy archetypes exist as data definitions;
- three basic magic formulas work with Resonance/Strain;
- gather → craft loop works;
- structured NPC dialogue works;
- both quests work;
- one non-kill objective is genuinely completable without combat;
- Tavar can be recruited;
- save/reload field comparison passes;
- recorded acceptance run exists;
- 10-minute playthrough exists;
- 1080p/60 FPS evidence exists;
- status/README documentation is current;
- later, 3–5 blind testers complete the early-feel check before Phase 2.

---

# 36. Explicit Non-Goals

Do not expand Ashen Hollow to include:

- mounts;
- player settlement building (M7 reconciliation (2026-09-24): superseded for M7 by one content-defined build area at the crossing, owner Q4 approved 2026-09-25; this remains a Phase-1 prototype boundary);
- romance;
- full faction warfare;
- full crime/court simulation;
- full survival;
- seasons;
- full custom spell construction;
- Great Works;
- Soul reincarnation;
- community servers;
- networking;
- PvP;
- Othergate travel;
- simulation-tier switching;
- full NPC schedules;
- major dungeon;
- boss fight;
- sprawling dialogue trees.

---

# 37. Working Content ID Placeholders

Final IDs must follow the canonical DefinitionId grammar and actual content schema.

```text
location.ashen_hollow.waystation
location.ashen_hollow.charwood_verge
location.ashen_hollow.blackvein_cut
location.ashen_hollow.foldscar_ruin

npc.ashen_hollow.renn_vale
npc.ashen_hollow.kera_voss
npc.ashen_hollow.sel_arien
npc.ashen_hollow.tavar_orr

creature.ashen_hollow.ash_ember_hound
creature.ashen_hollow.bone_walker_husk
creature.ashen_hollow.animated_armour
creature.ashen_hollow.bristleback_boar
creature.ashen_hollow.cave_hunting_spider

quest.ashen_hollow.iron_under_ash
quest.ashen_hollow.three_quiet_stones

item.weapon.ashen_hollow.arming_sword
item.weapon.ashen_hollow.hunting_bow
item.weapon.ashen_hollow.march_spear

resource.ashen_hollow.raw_iron_ore
resource.ashen_hollow.ash_haft
resource.ashen_hollow.woundmoss

formula.force.impulse_bolt
formula.warding.brace_ward
formula.vital.mending_thread
```

These are design placeholders, not permission to bypass content validation.

---

# 38. Implementation Coordination

## Claude
Owns gameplay/domain/application implementation, runtime presentation integration, milestone status, tests, and proof.

## DeepSeek / asset pipeline
Owns player assets, animation, weapons, enemies, environment kit, materials, VFX, and asset validation.

## This document
Defines the common content target both tracks converge on.

Placeholder geometry is acceptable while production assets mature.

Gameplay architecture must not depend on a temporary mesh.

---

# 39. Final Design Principle

> **Ashen Hollow should feel small because it is a frontier waystation, not because it is a test map.**

The player should leave Phase 1 with two impressions:

1. **The ordinary world is coherent enough to live in.**
2. **Something beyond ordinary reality is waiting just outside what they understand.**
