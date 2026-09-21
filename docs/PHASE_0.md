You are beginning Phase 0 of the RPG defined in PROJECT_CHARTER.md.

PROJECT_CHARTER.md is the authoritative creative vision for this project. Follow it closely. Do not substantially alter its design pillars simply because another design would be easier to implement.

For this task you are acting primarily as:

* Principal Game Systems Architect
* Technical Director
* Lead RPG Systems Designer
* Senior Gameplay Engineer

You are NOT yet the implementation agent.

Your purpose in this phase is to design the minimum durable foundation upon which the game can actually be built.

## IMPORTANT

Do NOT begin writing the game.

Do NOT generate large quantities of lore.

Do NOT generate hundreds of weapons, spells, recipes, monsters, NPCs, quests or locations.

Do NOT design speculative MMO infrastructure.

Do NOT produce an enormous traditional Game Design Document.

We are trying to prevent expensive architectural mistakes before implementation begins.

Think deeply before committing to major decisions.

---

# PRIMARY OBJECTIVE

Turn the vision in PROJECT_CHARTER.md into a technically realistic architecture for a large, first-person, solo-first RPG featuring:

* a large persistent world
* exploration
* combat
* magic
* skills and character progression
* classes/archetypes
* races
* crafting
* gathering
* loot
* quests
* factions
* NPCs
* companions and hirelings
* relationships
* home construction
* settlement development
* base defense
* persistent world changes
* dungeons
* bosses
* epic multi-stage artifact quests
* eventual optional LAN/WAN cooperative expansion

The game must be practical for a small development effort assisted heavily by AI.

---

# STEP 1 — CHALLENGE THE SPECIFICATION

Read the complete PROJECT_CHARTER.md.

Identify:

1. important RPG systems that are missing
2. systems that overlap unnecessarily
3. systems that could interact in particularly interesting ways
4. features that create disproportionate implementation risk
5. design choices that could prevent future multiplayer
6. design choices likely to cause save-game/persistence problems
7. features likely to produce unmanageable content-production requirements
8. anything that contradicts another part of the vision

Do not remove features merely because they are difficult.

Distinguish between:

* genuinely dangerous architectural decisions
* features that are merely expensive
* features that can safely be deferred

---

# STEP 2 — DEFINE THE CORE GAMEPLAY LOOPS

Define the major interconnected loops.

At minimum examine:

EXPLORE
→ discover locations/resources/NPCs/threats

FIGHT
→ gain experience/resources/equipment/knowledge

PROGRESS
→ levels/skills/abilities/build specialization

GATHER
→ obtain resources

CRAFT
→ create equipment/tools/building components

BUILD
→ improve home/workshops/settlement

RECRUIT
→ companions/hirelings/craftspeople

QUEST
→ discover stories/objectives/artifacts/world changes

RETURN
→ use accomplishments to unlock deeper exploration

The loops must reinforce one another rather than become isolated minigames.

Explain where rewards enter and leave each loop.

Identify exploitable circular economies or progression shortcuts.

---

# STEP 3 — ENGINE DECISION

Evaluate the realistic game-engine choices for THIS project.

Consider at least:

* Unreal Engine
* Unity
* Godot

Evaluate them specifically for:

* first-person combat
* large-world streaming
* terrain
* foliage
* AI/navigation
* animation
* NPC populations
* building systems
* save persistence
* data-driven content
* procedural assistance
* graphics
* tooling
* asset ecosystem
* future networking
* source control
* automated testing
* AI-assisted development
* ability for coding agents to modify and validate the project

Choose ONE.

Explain the decision.

Once selected, treat it as the project's engine unless substantial new evidence later requires reconsideration.

---

# STEP 4 — SYSTEM ARCHITECTURE

Design the major runtime systems and their responsibilities.

At minimum address:

Player
Character stats
Attributes
Skills
Abilities
Combat
Damage
Magic
Status effects
Inventory
Equipment
Items
Loot
Crafting
Resources
Building
World persistence
World streaming
NPCs
AI
Companions
Relationships
Quests
Dialogue
Factions
Reputation
Spawning
Dungeons
Bosses
Merchants
Economy
Time/weather
Exploration/discovery
Fast travel
Save/load

For every major system specify:

* responsibility
* authoritative state
* important dependencies
* persistent data
* runtime/transient data
* interfaces/events exposed to other systems

Prefer loose coupling.

Avoid creating a god object or universal GameManager.

---

# STEP 5 — STATE OWNERSHIP

This is extremely important.

Define who owns authoritative gameplay state.

The game is single player NOW.

However, avoid designs where adding multiplayer later requires rewriting every gameplay system.

Define sensible authority boundaries for:

* characters
* combat
* inventories
* containers
* NPCs
* creatures
* loot
* quests
* buildings
* resources
* world events

Do NOT implement a network server.

Simply keep gameplay state and presentation sufficiently separate that a server-authoritative model could potentially replace local authority later.

---

# STEP 6 — IDENTITY SYSTEM

Design stable identifiers for persistent entities.

We will eventually have:

* items
* item instances
* NPC definitions
* NPC instances
* creatures
* locations
* quests
* quest states
* buildings
* player-built objects
* containers
* companions
* factions
* recipes
* spells
* skills

Distinguish between:

DEFINITION IDS

and

RUNTIME/PERSISTENT INSTANCE IDS.

Do not depend upon fragile runtime memory references for persistent state.

---

# STEP 7 — DATA-DRIVEN CONTENT

Define how content should be represented.

Design schemas/concepts for:

* ItemDefinition
* WeaponDefinition
* ArmorDefinition
* CreatureDefinition
* NPCDefinition
* SpellDefinition
* AbilityDefinition
* StatusEffectDefinition
* RecipeDefinition
* ResourceDefinition
* QuestDefinition
* DialogueDefinition
* FactionDefinition
* LootTableDefinition

Do NOT populate these with hundreds of examples.

One or two examples are sufficient to validate each design.

Explain which data belongs in content definitions versus runtime state.

---

# STEP 8 — SAVE ARCHITECTURE

Design persistence BEFORE large gameplay systems exist.

The save system must eventually support:

* player state
* inventory
* equipment
* skills
* quests
* faction state
* relationships
* companions
* buildings
* containers
* harvested resources
* important killed NPCs
* changed world objects
* discovered locations
* world events
* settlement state

Address:

* save versioning
* migrations
* corruption recovery
* autosaves
* manual saves
* stable IDs
* incremental world state
* large save sizes
* deleted/changed content definitions between game versions

Do not save the entire runtime world blindly.

---

# STEP 9 — WORLD ARCHITECTURE

Design a world structure capable of eventually becoming large without requiring the entire world to remain simulated.

Define:

* regions
* cells/chunks
* loaded world
* simulated world
* abstract/offline simulation
* spawn populations
* resource respawning
* NPC schedules
* persistent exceptions
* dungeons/interiors
* player buildings

Explain what continues to exist when the player is far away versus what merely stores state.

---

# STEP 10 — NPC ARCHITECTURE

We want the world to feel populated without simulating thousands of full AI brains continuously.

Design tiers such as:

* full nearby simulation
* simplified regional simulation
* abstract distant simulation

Important NPCs must retain identity, relationships and history.

Generic NPCs should be much cheaper.

Companions require substantially richer state.

---

# STEP 11 — QUEST ARCHITECTURE

The quest system must support far more than simple kill counters.

It should eventually support:

* exploration
* dialogue
* item acquisition
* crafting
* construction
* combat
* bosses
* faction state
* relationships
* puzzles
* hidden objectives
* branching
* failure
* timed conditions
* world-state conditions
* long-running artifact quests

Design a flexible objective/state model without attempting to build a visual programming language.

Avoid hardcoding individual quests into gameplay code.

---

# STEP 12 — BUILDING ARCHITECTURE

Design the conceptual building system.

Account for:

* placement
* snapping where appropriate
* foundations
* structural pieces
* crafting stations
* storage
* ownership
* damage
* repair
* defenses
* NPC assignment
* persistence
* possible attacks

Do not design a structural-engineering simulator unless justified.

---

# STEP 13 — PROGRESSION ARCHITECTURE

Design the relationship among:

* character level
* XP
* attributes
* skills
* weapon mastery
* magic mastery
* abilities
* professions
* reputation
* equipment
* companions

Avoid making five progression systems that all represent the same thing.

Preserve meaningful specialization and hybrid builds.

---

# STEP 14 — CONTENT SCALE STRATEGY

Assume the eventual game could contain:

* thousands of items
* hundreds of creatures
* hundreds of quests
* many regions
* many NPCs
* many recipes

Explain how content will scale without requiring custom gameplay code for each asset.

Identify which content should remain intentionally handcrafted.

---

# STEP 15 — DEFINE THE FIRST PLAYABLE PROTOTYPE

The first prototype must be SMALL.

Define the absolute minimum content required to prove:

* movement
* exploration
* combat
* an enemy
* loot
* inventory
* equipment
* XP
* skill progression
* basic magic
* NPC interaction
* dialogue
* one quest
* gathering
* one crafting system
* one companion
* saving/loading

Do not confuse prototype with vertical slice.

---

# STEP 16 — DEFINE THE VERTICAL SLICE

Then define a miniature version of the actual game.

Target something approximately like:

ONE region
ONE meaningful settlement
several wilderness areas
2–4 dungeons
multiple enemy families
several character builds
multiple crafting professions
building
companions
factions
a major boss
one substantial multi-stage quest
one memorable epic reward

The slice should prove that the complete gameplay loop is enjoyable.

---

# STEP 17 — DEPENDENCY ORDER

Produce a dependency graph or ordered implementation sequence.

For example, do NOT build crafting before items exist.

Do NOT build epic quests before the quest framework exists.

Do NOT build settlement simulation before NPC persistence works.

Identify the critical path.

---

# STEP 18 — RISK REGISTER

Identify the ten greatest risks.

For each provide:

* risk
* why it matters
* likelihood
* impact
* earliest inexpensive validation
* mitigation

Pay particular attention to:

* world persistence
* AI scale
* building persistence
* save compatibility
* quest complexity
* content scale
* companion AI
* networking assumptions
* performance
* project scope

---

# DELIVERABLES

Produce these documents:

1. ARCHITECTURE.md
2. GAMEPLAY_LOOPS.md
3. SYSTEMS.md
4. DATA_MODEL.md
5. PERSISTENCE.md
6. WORLD_ARCHITECTURE.md
7. PROGRESSION.md
8. PROTOTYPE.md
9. VERTICAL_SLICE.md
10. ROADMAP.md
11. RISK_REGISTER.md
12. DECISIONS.md

If you do not have filesystem access, output each document clearly separated so they can be saved individually.

DECISIONS.md is especially important.

For every major irreversible or expensive architectural choice record:

* decision
* alternatives considered
* reason selected
* consequences
* conditions that would justify revisiting it

---

# FINAL REVIEW

After drafting the architecture, perform a second-pass adversarial review.

Ask yourself:

"If this game grows for three years, which decisions made today are most likely to cause us to regret them?"

Look specifically for:

* unnecessary abstraction
* premature MMO architecture
* tight coupling
* fragile persistence
* systems that cannot scale
* duplicated concepts
* god objects
* content hardcoding
* systems that require rewriting to support building or companions
* assumptions that make cooperative networking impossible
* systems that are far more ambitious than the game actually needs

Correct material problems before finalizing the documents.

Do not begin implementation.

End with a concise section titled:

READY FOR IMPLEMENTATION

containing:

* chosen engine
* core architectural pattern
* first implementation milestone
* first 10 development tasks in dependency order
* unresolved decisions that genuinely require the project owner's input

If no owner decision is actually required, say so rather than manufacturing questions.
