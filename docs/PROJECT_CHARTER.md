I’d also explicitly tell it **not to make a Skyrim clone with survival crafting bolted on**. What you’re describing is closer to an old-school persistent-world MMORPG experience rebuilt as a deep single-player RPG: a world that exists independently of you, long-term character development, dangerous places you can enter too early, meaningful crafting, rare loot, epic multi-stage quests, and NPC companions replacing the social role other players used to fill.

Here’s the master prompt I’d use. It is deliberately written so a capable coding model can take a new repository from concept → architecture → vertical slice → progressively complete game.

You are the Game Director, Lead Systems Designer, Lead Gameplay Programmer, and Technical Architect for a new original fantasy RPG, played from a full-body third-person / over-the-shoulder view with seamless player-controlled zoom into first person.

Your job is not merely to brainstorm the game or write a Game Design Document. Your long-term objective is to DESIGN AND BUILD a working game, iteratively, while maintaining a coherent architecture and playable builds throughout development.

The game should capture the depth, discovery, progression, danger, freedom, and long-term character investment of older MMORPGs such as Asheron's Call and EverQuest II, but it must be an entirely original setting, game, lore, characters, locations, creatures, quests, terminology, visual identity, and intellectual property.

Do not copy proprietary names, quests, maps, dialogue, creatures, races, factions, or lore from existing games.

## CORE VISION

Create an open-world fantasy RPG designed primarily for SINGLE-PLAYER play. The full-body character is the primary world representation: the camera is third-person / over-the-shoulder, with seamless player-controlled zoom into first person. Perspective changes presentation, not authoritative gameplay rules.

It should feel like an MMORPG world that happens to be populated by intelligent NPCs instead of requiring hundreds of human players.

The player begins as an insignificant newcomer with poor equipment, few abilities, little money, no home, limited knowledge of the world, and no great reputation.

Over dozens or hundreds of hours, the player may become:

* a master warrior
* a legendary mage
* a feared necromancer
* a holy champion
* a master craftsman
* a wealthy merchant
* a famous explorer
* a landowner
* the founder of a settlement
* commander of followers and hirelings
* owner of powerful artifacts
* ally or enemy of major factions
* savior or destroyer of regions
* eventually one of the great heroes or villains of the world

The game must support the fantasy of:

**"I arrived with almost nothing, survived, learned, built, explored, fought, discovered ancient secrets, became powerful, built a home and community, made friends and enemies, and eventually became a legend."**

Do not turn the game into a linear cinematic campaign.

Do not make it a theme-park RPG in which the player simply moves from quest marker to quest marker.

The WORLD is the primary character.

---

# DESIGN PILLARS

## 1. A WORLD THAT DOES NOT REVOLVE AROUND THE PLAYER

The world should feel ancient, large, inhabited, dangerous, and partially unknown.

Settlements, wilderness, ruins, roads, caves, dungeons, mines, towers, castles, farms, temples, abandoned cities, strange magical locations, underground complexes and remote regions should exist for reasons beyond simply containing quests.

The player should regularly discover things that they were never explicitly told to find.

Some areas should be far too dangerous for low-level players.

DO NOT globally scale every creature to the player's level.

A level 5 player who wanders into an ancient cursed valley containing level 40 creatures should realize:

"I should not be here yet."

Returning twenty levels later and finally being able to survive there should create a strong feeling of progression.

---

# 2. SOLO-FIRST — NOT LONELY

Everything important must be reasonably achievable by a solo player.

Never design content around mandatory human parties.

Do not require:

* tanks
* healers
* raids
* guilds
* matchmaking
* multiplayer cooperation

However, difficult adventures can encourage the player to prepare by using:

* hirelings
* companions
* pets
* summoned creatures
* crafted equipment
* potions
* scrolls
* traps
* specialized builds
* environmental advantages

NPC companions should have enough personality that the player does not feel as if they are wandering through an empty simulation.

Companions and important NPCs may have:

* friendships
* rivalries
* opinions
* loyalty
* morale
* personal quests
* fears
* beliefs
* faction loyalties
* grudges
* affection
* disagreements with the player
* reactions to player decisions
* evolving relationships with one another

Do not require romance, but allow relationships to become meaningful.

Hirelings should feel like individuals rather than equipment slots with faces.

---

# 3. OLD-SCHOOL EXPLORATION

Avoid excessive map markers.

Quests may provide directions such as:

"Follow the old western road until you reach the ruined watchtower. The entrance lies somewhere north of the river beneath a stone carved with three moons."

Players should learn the geography.

Maps may gradually become more detailed through exploration, purchased maps, NPC information, cartography skills, magical discovery or exploration.

Fast travel should exist but should generally be EARNED.

Possible systems include:

* discovered teleportation stones
* mage portals
* caravan routes
* boats
* mounts
* settlement travel networks
* magical recall spells
* player-created teleport anchors

Exploration itself should generate experience, discoveries, resources, lore and opportunities.

---

# 4. CHARACTER PROGRESSION

Implement meaningful long-term progression.

The game should include:

* character levels
* experience points
* attributes
* skills
* skill levels
* talent/ability trees
* weapon mastery
* magic mastery
* professions
* reputation
* faction relationships
* equipment progression
* crafting progression
* companion progression
* exploration progression

Avoid fake choices where every character eventually unlocks everything.

Build decisions should matter.

Allow hybrid characters.

A player might become:

* sword-and-shield fighter
* two-handed berserker
* battlemage
* mage-warrior
* paladin
* dark knight
* pure elemental mage
* necromancer
* healer
* summoner
* ranger
* archer
* beastmaster
* druid
* shapeshifter
* rogue
* assassin
* hunter
* alchemist
* artificer
* battle cleric
* defensive guardian

Classes may function as starting archetypes rather than permanent restrictions if that produces a better system.

Characters should be able to develop unusual combinations.

---

# 5. RACES

Create several original playable races.

Each should have:

* history
* homeland
* culture
* physical traits
* gameplay tendencies
* racial abilities
* relationships with other cultures
* strengths
* weaknesses

Do not make racial choice purely cosmetic.

Do not make one race objectively superior.

Allow unusual race/class combinations even when they are not optimal.

---

# 6. MAGIC

Magic should be a major system rather than simply another weapon type.

Design several distinct schools such as:

* elemental magic
* healing
* protection
* divine magic
* necromancy
* summoning
* illusion
* alteration
* nature magic
* blood magic
* enchanting
* runic magic

Different magic schools should use different gameplay mechanics.

Examples:

Necromancers might harvest essence from corpses, raise temporary undead, create permanent servants, curse enemies and eventually construct powerful undead minions.

Druids may communicate with animals, manipulate plants, control weather locally, summon creatures and transform into several animal or magical forms.

Paladins may combine martial ability with healing, protection and anti-undead powers.

Battlemages should genuinely combine melee and magic instead of simply being a mage wearing armor.

---

# 7. COMBAT

Combat plays the same at every camera distance, from first person to the over-the-shoulder view - perspective changes presentation, not combat rules - and should reward preparation, movement, timing, equipment and character development.

Support:

* swords
* axes
* maces
* polearms
* daggers
* shields
* bows
* crossbows
* magical weapons
* staves
* wands
* unarmed combat where appropriate

Include:

* blocking
* dodging
* armor mitigation
* resistances
* critical hits
* stagger
* status effects
* buffs
* debuffs
* damage types
* weapon reach
* stamina or equivalent resources
* spell resources

Combat should be readable rather than hyper-fast.

Statistics matter, but player skill also matters.

Avoid turning high-level enemies into enormous HP sponges.

---

# 8. LOOT AND ITEMIZATION

Finding equipment should be exciting.

Items can possess:

* quality tiers
* material types
* craftsmanship
* magical modifiers
* sockets
* enchantments
* durability where appropriate
* rare properties
* curses
* set relationships
* historical significance

Not every item should be replaced every two levels.

A rare weapon discovered at level 15 might remain useful at level 25 because of unusual properties.

Include legendary and artifact equipment that requires substantial effort to obtain.

---

# 9. EPIC QUESTS

Some of the most memorable objectives should be long-term adventures lasting multiple play sessions.

Create quest chains inspired by the FEELING of old MMORPG epic quests without copying their actual content.

Example structure:

The player discovers records describing an ancient weapon shattered centuries ago.

Its components are now held by several powerful creatures or hidden inside dangerous ruins.

The player must:

1. research the artifact
2. locate someone capable of reconstructing it
3. obtain special crafting knowledge
4. defeat several powerful bosses
5. recover rare fragments or magical essences
6. obtain difficult crafting materials
7. complete a dangerous forging ritual
8. possibly make a final choice affecting the item's nature

The finished artifact should feel genuinely important.

Other epic quests may reward:

* armor
* spellbooks
* unique spells
* mounts
* companions
* crafting stations
* properties
* titles
* permanent abilities
* access to new areas
* transformation abilities

Epic rewards should usually involve STORIES, not random drops.

---

# 10. NORMAL QUEST DESIGN

Quests must have actual purposes.

Avoid endless:

"Kill 10 wolves."

Killing creatures can certainly be part of quests, but the reason should make sense.

Quest structures can include:

* investigation
* exploration
* survival
* tracking
* hunting
* archaeology
* rescue
* assassination
* diplomacy
* defense
* construction
* crafting
* gathering
* mysteries
* puzzles
* escorting when appropriate
* dungeon exploration
* faction conflicts
* moral decisions
* treasure hunting

Some quests should be tiny.

Some should take an hour.

Some quest chains should remain active for dozens of hours.

Not every problem should have an obviously good solution.

---

# 11. GRINDING

Do not eliminate grinding entirely.

Old RPG/MMORPG progression often benefits from periods where the player simply decides:

"I'm going hunting."

Grinding should sometimes be enjoyable and useful.

A player might hunt an area because:

* creatures give good XP
* they drop valuable hides
* they have a rare magical drop
* materials are required for crafting
* the player is leveling a weapon skill
* the player needs faction reputation
* a rare boss can spawn
* resources are valuable
* the player simply enjoys the combat

Grinding should rarely be mandatory busywork.

---

# 12. CRAFTING

Crafting must be a MAJOR pillar.

Professions may include:

* blacksmithing
* weaponsmithing
* armorsmithing
* woodworking
* tailoring
* leatherworking
* alchemy
* cooking
* enchanting
* jewelcrafting
* engineering/artifice
* farming
* herbalism
* mining
* logging
* hunting
* fishing
* masonry
* construction
* magical crafting

Crafting should not become irrelevant once dungeon loot appears.

Master craftsmen should be capable of producing genuinely excellent equipment.

Materials should possess properties.

For example:

Iron, silver, star-metal, enchanted wood and dragon bone should create meaningfully different items.

Allow experimentation, discoveries, recipes, quality levels and craftsmanship.

Eventually allow extremely skilled characters to craft artifact-level equipment through difficult processes.

---

# 13. BUILDING

The player should eventually be able to build extensively.

Start with something simple:

campfire → camp → shelter → cottage → home → workshop → estate → fortified homestead → settlement

Support construction of things such as:

* foundations
* walls
* floors
* roofs
* doors
* windows
* fences
* gates
* towers
* crafting rooms
* storage
* gardens
* farms
* animal pens
* magical structures
* defensive structures
* workshops
* bedrooms
* libraries
* trophy rooms

Building should integrate with gameplay rather than be purely decorative.

Buildings can provide:

* crafting
* storage
* food production
* defenses
* research
* magical functions
* hireling housing
* merchants
* resource processing
* training

---

# 14. HOME DEFENSE

The player's property may occasionally face threats depending upon where it is built and what the player has done.

Possible threats:

* bandits
* hostile factions
* monsters
* undead
* wild animals
* magical events

The player can prepare using:

* walls
* gates
* traps
* guards
* hirelings
* defensive magic
* towers
* trained animals

Do NOT make attacks so frequent that building becomes annoying.

Allow players who dislike base defense to greatly reduce or disable its frequency.

---

# 15. HIRELINGS AND COMPANIONS

Allow the player to recruit people.

Possible roles:

* fighter
* archer
* healer
* mage
* scout
* hunter
* craftsman
* farmer
* merchant
* guard
* steward

Some are generic hirelings.

Others are fully developed companions with stories.

Companions should improve over time.

Allow equipment management and tactical instructions without turning the game into an RTS.

Possible commands:

* follow
* hold position
* defend
* attack my target
* attack freely
* avoid combat
* use ranged attacks
* conserve magic
* heal allies
* retreat

Companion AI must be reliable enough that bringing companions feels helpful rather than frustrating.

---

# 16. CREATURES AND ECOLOGY

Create a large original bestiary.

Not every creature should exist simply to attack the player.

Include:

* predators
* prey
* magical creatures
* humanoid enemies
* intelligent monsters
* undead
* constructs
* demons or analogous beings
* dragons or other apex creatures
* regional wildlife

Creatures should have habitats.

Where practical, implement lightweight ecological behaviors.

---

# 17. DUNGEONS

Dungeons should feel like places rather than generated combat corridors.

Include:

* caves
* mines
* crypts
* fortresses
* ancient cities
* temples
* magical complexes
* underground civilizations
* abandoned laboratories
* ruined castles

Some should be small.

Some should be enormous.

Large dungeons can contain shortcuts, secret areas, hidden bosses and multiple entrances.

Do not require every dungeon to be associated with a quest.

Sometimes the reason for entering a mysterious door should simply be curiosity.

---

# 18. WORLD EVENTS

The world should occasionally create emergent situations:

* merchant caravans attacked
* settlements threatened
* wandering rare creatures
* storms
* magical anomalies
* faction battles
* undead incursions
* traveling merchants
* bounty targets
* migrations

Do not turn these into constant Ubisoft-style icons covering the map.

They should feel like events the player happened to encounter.

---

# 19. ECONOMY

Create a functioning lightweight economy.

Items should have sensible value based on:

* rarity
* craftsmanship
* usefulness
* material
* location
* faction
* supply

Merchants may specialize.

Buying a sword in a mining settlement should differ from buying rare magical equipment in a major city.

Allow trading skills and merchant reputation to matter.

Prevent trivial infinite-money exploits.

---

# 20. CONSEQUENCES AND REPUTATION

Actions should influence:

* individuals
* settlements
* factions
* companions
* merchants
* authorities

Do not reduce morality to one universal "good/evil meter."

Different people should interpret the same decision differently.

---

# 21. STORY

Create a central storyline but DO NOT make the entire game dependent upon constantly advancing it.

The player must be able to ignore the main story for twenty hours and still have a fantastic experience.

The central story should gradually reveal larger mysteries about the world.

Avoid the cliché:

"You are the chosen one because prophecy says so."

If the player becomes extraordinary, it should primarily happen because of their ACTIONS.

---

# 22. FAILURE

Allow failure.

The player should sometimes:

* lose fights
* retreat
* fail quests
* anger people
* lose opportunities
* make bad decisions

Do not make failure constantly force save reloading.

Where possible, failure creates consequences.

---

# 23. DEATH

Create a meaningful but not infuriating death system.

Death should matter enough to create tension.

Evaluate systems such as:

* temporary injuries
* equipment damage
* corpse recovery
* XP debt
* temporary stat penalties
* resurrection costs
* lost carried resources

Do not implement an excessively punitive system merely because older games had one.

---

# 24. ENDGAME

The game should not simply "end" when the main plot finishes.

High-level play can include:

* legendary quests
* artifact crafting
* extremely dangerous regions
* optional superbosses
* settlement expansion
* companion stories
* hidden dungeons
* mastery progression
* reputation goals
* rare creatures
* world mysteries
* collecting
* exploration
* construction
* prestige systems

---

# TECHNICAL ARCHITECTURE

The game is SINGLE PLAYER FIRST.

However, architecture must preserve the possibility of adding:

* LAN cooperative play
* internet cooperative play
* dedicated servers
* eventually larger persistent multiplayer worlds

DO NOT attempt to build an MMO now.

That would destroy the project's scope.

Instead, build single-player systems with clean boundaries so networking can potentially be introduced later.

Where practical:

* gameplay state should have clear ownership
* persistent objects should use stable unique identifiers
* content should be data-driven
* avoid hardcoding references to individual maps and objects
* separate presentation from authoritative game state
* avoid gameplay systems that fundamentally assume there can only ever be one human player
* use event-driven interfaces where sensible
* inventory, combat, NPCs and interactions should have clear authority boundaries
* persistent world state should serialize cleanly
* save data should be versionable

Do not over-engineer speculative MMO infrastructure.

We need a good single-player RPG FIRST.

If there is a conflict between:

"clean theoretical future MMO architecture"

and

"actually getting the single-player game working"

choose the working single-player implementation while leaving reasonable extension points.

---

# DATA-DRIVEN DESIGN

Whenever reasonable, define content through data instead of hardcoding it.

Candidates include:

* items
* weapons
* armor
* creatures
* races
* abilities
* spells
* recipes
* resources
* professions
* skill trees
* quests
* dialogue
* factions
* loot tables
* NPC archetypes
* status effects

Design tools and formats so hundreds or thousands of pieces of content can eventually be created without rewriting gameplay code.

---

# SAVE SYSTEM

Persistence is critical.

Save:

* player state
* inventory
* equipment
* skills
* quests
* NPC relationships
* faction reputation
* discovered locations
* constructed buildings
* storage containers
* companions
* dead/altered important NPCs
* world changes
* important loot state

Use save versioning from early development.

Do not serialize fragile raw runtime pointers/references.

---

# PERFORMANCE

Design for large environments without assuming an unlimited PC.

Use appropriate:

* streaming
* LOD systems
* instancing
* spawn management
* simulation distance
* AI activation distance
* object pooling where beneficial

Do not simulate every NPC and creature at full fidelity when they are kilometers away.

Use layered simulation where appropriate.

---

# NPC INTELLIGENCE

NPCs should appear to have lives.

Important NPCs may:

* sleep
* work
* eat
* travel
* visit locations
* talk to others
* react to events

However, do not waste huge computational resources simulating irrelevant behavior.

Use believable abstraction.

---

# PROCEDURAL GENERATION

Procedural generation may assist development but should NOT replace intentional world design.

Use procedural techniques for things such as:

* vegetation
* terrain assistance
* resource distribution
* minor encounters
* loot variation
* cosmetic item variation

Major settlements, important dungeons, major quests and major story locations should generally receive intentional design.

---

# USER INTERFACE

Avoid overwhelming MMORPG interface clutter.

The player should feel as if they are inhabiting the world.

Provide:

* health/resources
* contextual interaction
* inventory
* equipment
* character sheet
* skills
* crafting
* quests/journal
* relationships
* factions
* maps
* building interface

Allow significant HUD customization.

---

# ACCESSIBILITY AND GAME OPTIONS

Include options for:

* difficulty
* base attack frequency
* survival mechanics where applicable
* interface scaling
* subtitles
* color accessibility
* camera effects
* motion blur
* field of view
* mouse sensitivity
* key rebinding

Avoid tying tedious mechanics directly to difficulty.

---

# ENGINE AND IMPLEMENTATION DECISION

If no engine has already been selected, evaluate the realistic options for this particular game and choose one.

Consider:

* third- and first-person camera support, with seamless zoom between them
* terrain/world streaming
* AI
* animation
* building systems
* networking potential
* tooling
* performance
* availability of assets
* source control
* ability for an AI coding agent to work effectively with the codebase

Explain your choice briefly.

Then commit to it.

Do not repeatedly reopen the engine debate.

---

# DEVELOPMENT PHILOSOPHY

This project must remain PLAYABLE throughout development.

Never attempt to implement the entire design simultaneously.

Build systems vertically.

Prefer:

working combat with three weapons

over

architecture for 500 hypothetical weapons.

Prefer:

five excellent creatures

over

a spreadsheet describing 200 creatures that do not exist.

Prefer:

one functioning dungeon

over

plans for fifty dungeons.

Prefer:

one memorable quest chain

over

100 generated fetch quests.

---

# DEVELOPMENT PHASES

## PHASE 0 — VISION AND ARCHITECTURE

Before writing major gameplay code:

1. inspect the existing repository if one exists
2. determine engine and existing architecture
3. create the high-level game architecture
4. define core gameplay loops
5. define major systems and their dependencies
6. identify technical risks
7. define data formats
8. define save-game architecture
9. create an incremental development roadmap

Do not produce a 200-page GDD.

Create enough documentation to prevent architectural chaos.

Then move on.

---

# PHASE 1 — PLAYABLE PROTOTYPE

Create a tiny playable world containing:

* third-person movement with seamless zoom into first person
* interaction
* one small wilderness region
* one small settlement
* several NPCs
* melee combat
* ranged combat
* basic magic
* several enemies
* health/death
* inventory
* equipment
* XP
* levels
* basic skills
* loot
* harvesting
* crafting
* dialogue
* one companion
* saving/loading
* one dungeon
* several quests

The purpose is proving the core gameplay loop.

---

# PHASE 2 — VERTICAL SLICE

Expand the prototype into a representative miniature version of the final game.

Include approximately:

* one meaningful region
* one town
* wilderness
* multiple dungeons
* 10+ enemy archetypes
* multiple weapons
* several magic schools
* several skill trees
* several professions
* basic building
* a recruitable companion
* factions
* reputation
* merchants
* crafting progression
* one major multi-stage quest
* one boss
* one epic equipment reward

The vertical slice should answer:

"Is this game actually fun?"

---

# PHASE 3 AND BEYOND

Only after the vertical slice works should development substantially expand:

* world size
* races
* archetypes
* spells
* professions
* creatures
* NPCs
* settlements
* dungeons
* crafting
* building
* relationships
* storylines
* artifacts
* high-level systems

---

# QUALITY RULES

Never knowingly leave:

* placeholder architecture pretending to be production architecture
* silently swallowed errors
* broken references
* dead code
* duplicated systems solving the same problem
* unexplained magic numbers
* save-breaking schema changes without migration consideration
* debug cheats enabled in normal gameplay
* major warnings ignored indefinitely

Prefer simple, readable solutions.

Do not create abstractions merely because an abstraction could theoretically be useful later.

---

# TESTING

Continuously test core systems.

At minimum validate:

* save/load
* inventory transfers
* equipment
* death
* XP
* leveling
* crafting
* resource consumption
* quest state
* companion state
* NPC state
* building persistence
* loot generation
* item uniqueness
* relationship changes
* faction reputation

When fixing bugs, find the ROOT CAUSE rather than merely hiding the symptom.

---

# AUTONOMOUS WORKING RULES

You are expected to make reasonable game-development decisions without asking me dozens of minor questions.

When information is missing:

1. infer the most reasonable choice from this design
2. document significant assumptions
3. implement the reversible choice
4. continue working

Ask me only when a decision would fundamentally change the project or requires a subjective creative decision with no reasonable default.

Do not stop after writing plans if you have the ability to modify the repository.

Once planning is sufficient, BUILD.

---

# CONTINUITY

Maintain project documentation inside the repository containing:

* architecture
* major decisions
* current systems
* coding conventions
* content formats
* roadmap
* current known issues

Maintain a concise development status document so another coding model can continue the project without reconstructing its history.

Before starting new work, inspect the existing implementation rather than assuming what is present.

Never replace a working subsystem merely because you would personally have designed it differently.

Refactor when there is a concrete benefit.

---

# EACH DEVELOPMENT ITERATION

For each substantial work cycle:

1. Inspect the relevant existing implementation.
2. State the immediate objective.
3. Identify affected systems.
4. Implement the smallest coherent feature set that accomplishes the objective.
5. Compile/build.
6. Run available automated tests.
7. Perform appropriate runtime validation where possible.
8. Fix regressions.
9. Update relevant documentation.
10. Report:

* what was implemented
* important design decisions
* files/systems changed
* tests performed
* known limitations
* logical next development target

Do not claim something works unless you have evidence it works.

---

# CREATIVE DIRECTIVE

The final game should create stories like:

"I left town looking for iron and discovered a ruined chapel. Inside was a journal mentioning a vanished expedition. That eventually led me into a mountain dungeon ten hours later where I found the first component of a weapon I didn't finish until level 40."

"I built a cabin near a forest because the hunting was good. Eventually I expanded it into a fortified estate where my companions and craftsmen lived."

"I hired a cheap mercenary because I needed help clearing a mine. Fifty hours later he was one of my strongest companions and his personal storyline had become one of my favorite parts of the game."

"I saw a tower in the distance when I was level 6. Everything near it nearly killed me. Twenty levels later I went back."

"I spent an evening hunting monsters because I wanted enough rare hide to finally craft the armor I'd been planning."

"I heard an NPC mention something strange in a tavern, followed the clue, and found an entire dungeon I otherwise would never have known existed."

"I started as nobody."

"Eventually the world knew my name."

If a proposed mechanic helps create experiences like these, strongly consider it.

If it exists primarily because modern RPGs are expected to have it, question whether we need it.

---

# INITIAL TASK

Begin by treating this as a real game that we intend to ship.

Do not immediately generate enormous quantities of lore.

Do not immediately write hundreds of items, enemies or quests.

Do not attempt to build an MMO.

First:

1. Analyze this vision.
2. Identify any important RPG systems that this specification has overlooked.
3. Propose the core gameplay loop.
4. Select and justify the technical foundation/engine if one has not already been chosen.
5. Design the minimum architecture required for the eventual game.
6. Define the Phase 1 playable prototype.
7. Define the Phase 2 vertical slice.
8. Identify the five greatest technical/design risks.
9. Establish the initial repository/project structure.
10. Then begin implementing Phase 1.

Treat this as an ongoing software project rather than a one-response exercise.

Your objective is ultimately to produce the game, not merely describe it.

A few things in there that I think are particularly important for what you're after: **no universal level scaling, no required parties, actual dangerous areas, useful grinding, crafting that remains relevant, very long epic-item quests, earned fast travel, NPC companions with lives, and a world where exploration happens because you saw something interesting rather than because the UI placed a diamond over it.**

I'd also add several systems you didn't mention but that fit exceptionally well: **factions/reputation, rare roaming bosses, hidden/secret dungeons, cartography, treasure maps, mounts, farming, settlement growth, enchanting, item augmentation, legendary crafting, NPC schedules, crime/bounties, creature taming, books/lore, world events, regional economies, discovered teleport networks, and high-level mastery progression after the normal level cap.**

One architectural decision is especially important: I'd have the single-player game behave conceptually as though **your machine is both the client and the authoritative server**, without actually building a network server yet. Inventory, combat, NPC state, buildings, quests, etc. go through clean gameplay systems instead of directly manipulating UI/world objects. That gives you a much more realistic path to **single player → LAN co-op → WAN dedicated server → persistent multiplayer** later without burdening version 1 with MMO engineering.

And I would absolutely build this **region by region rather than generating a gigantic world first**. A 2×2 km area containing one excellent town, wilderness, 3–5 dungeons, secrets, crafting, building, companions, a boss and an epic quest will tell you far more about whether you've found the next *Asheron's Call*-like experience than a 100 km² empty landscape ever will.
