# WORLD_BUILDING_AND_PROPERTY_DESIGN.md

**Project:** UNNAMED  
**Status:** Design extension following Phase 0  
**Purpose:** Define the intended relationship among authored world geography, procedural world dressing, seamless traversal, player property, settlement jurisdiction, and player-founded settlements.

This document does not replace the architectural decisions in `DECISIONS.md`, `WORLD_ARCHITECTURE.md`, `PERSISTENCE.md`, or `PROTOTYPE.md`.

Where this document conflicts with those sources, the existing authoritative Phase 0 documents govern until an explicit architectural decision is amended.

---

# 1. Core World Philosophy

The game world should feel **designed rather than generated**, while still using procedural systems aggressively to make production practical.

The intended model is:

> **Hand-designed geography + procedurally assisted detail + handcrafted important locations + seamless streaming + consequential player construction.**

Procedural generation is a development multiplier, not the world's author.

The player should eventually remember places because of their geography:

- the valley below the ruined tower
- the road beside the black lake
- the ridge where dangerous creatures begin appearing
- the forest containing the hidden shrine
- the town at the river crossing
- the mountain pass leading toward a region that was once too dangerous
- the player's own homestead visible from the road

The world must remain geographically understandable and memorable.

---

# 2. Three Layers of World Construction

## 2.1 Authored Macro Geography

The large-scale shape of the world is intentionally designed.

This includes:

- mountains
- valleys
- rivers
- lakes
- coastlines
- major forests
- roads
- passes
- bridges
- settlement locations
- ruins
- dungeon entrances
- towers
- castles
- temples
- major caves
- dangerous regions
- major resource regions
- important vistas
- quest-critical geography
- major travel routes
- faction territories

These elements determine how players understand and navigate the world.

They should therefore not be arbitrarily regenerated between games.

The terrain should answer questions such as:

- Why was this town built here?
- Why does this road exist?
- Why is this valley dangerous?
- Why was this fortress placed on this hill?
- Why is mining important to this settlement?
- Why would anyone choose to live here?

---

# 2.2 Procedural Micro Geography

Procedural generation fills and varies the spaces between authored landmarks.

Good candidates include:

- trees
- shrubs
- grass
- flowers
- rocks
- fallen logs
- ground clutter
- debris
- mushrooms
- minor resource nodes
- wildlife distribution
- minor encounters
- small camps
- environmental decals
- erosion detail
- small terrain variation
- cosmetic object variation
- ambient creature populations

These systems should use deterministic regional/cell seeds so that an unchanged world can regenerate identically for persistence.

Procedural generation should reduce authoring workload without making exploration feel random.

A designer should be able to override procedural output whenever a location needs intentional composition.

---

# 2.3 Handcrafted Important Spaces

Major locations should be intentionally built.

Examples:

- settlements
- important ruins
- temples
- fortresses
- story-critical caves
- major dungeons
- boss arenas
- ancient cities
- underground complexes
- faction headquarters
- unique magical locations
- major quest locations

Procedural dressing may still populate these areas with:

- rubble
- props
- vegetation
- clutter
- corpses
- books
- furniture variations
- lighting variation

But the location's structure, pacing, secrets, routes, and encounters remain authored.

---

# 3. Regions Instead of One Giant Map

The world should be developed region by region.

A region is an authored geographic unit containing enough content to feel like a place rather than a biome tile.

A typical major region may contain:

- one primary settlement
- wilderness
- smaller inhabited locations
- several dungeons
- resource areas
- dangerous zones
- secrets
- faction activity
- roaming encounters
- player-buildable territory
- at least one memorable long-term objective

Additional regions may be added without redesigning the world model.

The player should experience them as pieces of one continuous world.

---

# 4. Seamless Travel and Loading

The preferred player experience is:

> **No noticeable loading screens during ordinary exploration.**

The implementation does not require every location to exist in one enormous simultaneously loaded scene.

It requires the player to perceive continuity.

## 4.1 Exterior Travel

Walking or riding through:

- wilderness
- roads
- towns
- farms
- ruins
- player settlements

should not display loading screens.

World cells stream asynchronously around the player.

---

# 4.2 Settlements

Entering a settlement should normally be completely seamless.

There should be no arbitrary transition such as:

> Enter Vessmere?

followed by a loading screen.

The road should simply become the town road.

NPC density, structures, audio, services, and simulation fidelity increase naturally as the player approaches.

---

# 4.3 Interiors

Small interiors should ideally stream seamlessly.

Examples:

- houses
- shops
- taverns
- workshops
- towers
- small caves

Large interiors may technically exist in separate spaces while still hiding the transition.

Useful streaming masks include:

- doors
- narrow cave passages
- staircases
- mine shafts
- elevators
- gates
- ladders
- tunnels
- magical thresholds
- descending passages

The transition gives the engine time to unload exterior detail and prepare the interior.

The player experiences movement through the world rather than a loading screen.

---

# 4.4 Hard Loading Screens

Visible loading screens should be reserved primarily for:

- loading a saved game
- starting a new game
- catastrophic recovery
- very long-distance instantaneous travel where an environmental transition cannot reasonably conceal streaming

Even these transitions should favor presentation appropriate to the world over generic progress bars where practical.

---

# 5. Player Building Philosophy

Player construction should eventually be possible across much of the world.

The default question should not be:

> Is this a designated construction plot?

The better question is:

> What are the consequences of building here?

Building location should be a meaningful gameplay decision.

---

# 6. Wilderness Construction

Outside controlled settlements and restricted locations, players should generally be able to establish property wherever terrain and gameplay constraints permit.

Restrictions should mostly represent real reasons:

- impossible slope
- water
- road obstruction
- dungeon entrance
- critical quest location
- protected landmark
- hostile faction structure
- collision/navigation impossibility
- another owned property
- jurisdictional prohibition

Avoid invisible arbitrary building zones across wilderness.

---

# 7. Location Creates Consequences

A wilderness home has advantages and disadvantages determined by its location.

## Deep Forest

Possible advantages:

- abundant timber
- hunting
- herbs
- privacy
- no property taxes

Possible disadvantages:

- wildlife
- monsters
- bandits
- poor merchant access
- no guard response
- long supply routes

---

## Trade Road

Possible advantages:

- merchants
- visitors
- transport
- easier recruitment
- economic opportunities

Possible disadvantages:

- thieves
- bandits
- less privacy
- territorial attention
- higher likelihood of travelers becoming involved in local events

---

## Remote Mountain Area

Possible advantages:

- rare ores
- defensive terrain
- isolation
- magical resources
- unique vistas

Possible disadvantages:

- extreme travel distance
- harsh weather
- dangerous creatures
- difficult logistics
- expensive construction

---

## Dangerous Ancient Site

Possible advantages:

- rare resources
- magical effects
- artifact-related discoveries
- strategic location

Possible disadvantages:

- undead
- magical anomalies
- hostile factions
- world events
- repeated threats

The game's systems should make location meaningful without requiring every possible consequence to be scripted individually.

---

# 8. Settlement Property

Civilized settlements offer a different bargain.

Property inside or near a settlement may involve:

- buying land
- renting property
- leasing land from a faction
- receiving land as a quest reward
- receiving property through reputation
- inheriting property
- receiving a military or guild grant

In exchange for reduced freedom, the player receives civilization.

---

# 9. Settlement Protection

Property under settlement jurisdiction may benefit from:

- guards
- walls
- patrols
- nearby healers
- merchants
- crafting services
- fire response
- repair services
- easier recruitment
- fast travel
- local storage or banking
- safer roads

A threat attacking the player's home inside a defended settlement should encounter the settlement's defenses as well.

Security is one of the things the player is buying.

---

# 10. Settlement Obligations

Civilized property may create obligations.

Examples:

- rent
- property tax
- faction allegiance
- reputation requirements
- construction limits
- local laws
- livestock restrictions
- prohibited magical practices
- business licenses
- service obligations
- defense obligations during emergencies

These should produce interesting choices rather than become constant administrative chores.

Failure to pay taxes should not immediately delete the player's home.

Consequences should escalate logically.

For example:

late payment  
→ warning  
→ debt  
→ reduced services  
→ legal dispute  
→ possible seizure after prolonged refusal

---

# 11. Building Regulations as Worldbuilding

Different societies may permit different structures.

A conventional human settlement may prohibit:

- corpse piles
- necromantic laboratories
- demon shrines
- hazardous alchemy
- giant defensive walls inside town
- animal pens in commercial districts

Another culture might view entirely different things as unacceptable.

Building regulations therefore become another expression of culture.

Players seeking total freedom can build outside jurisdiction.

---

# 12. Property Status

A general property model should eventually support states such as:

### Unclaimed

No recognized ownership.

### Wilderness Claim

Player-created claim outside organized jurisdiction.

### Leased

Player controls property while paying a recurring obligation.

### Owned

Player legally owns property.

### Faction Granted

Property remains dependent upon faction relationship or service.

### Settlement Controlled

The property lies under laws and protection of a settlement.

### Player Settlement

The player has effectively created a new recognized settlement.

These are gameplay states rather than merely UI labels.

---

# 13. Jurisdiction

A location may exist within a political or social jurisdiction.

Possible jurisdiction types:

- none
- tribal territory
- village
- town
- city
- kingdom
- faction territory
- religious territory
- disputed territory

Jurisdiction may affect:

- construction permission
- law enforcement
- taxes
- guard response
- merchant access
- recruitment
- crime
- faction reaction
- allowable structures

The same physical building could therefore have different consequences depending on where it stands.

---

# 14. Player-Founded Settlements

The game should support a long-term progression from personal shelter to community.

The ideal progression is emergent:

campfire  
→ camp  
→ shelter  
→ cabin  
→ homestead  
→ workshop  
→ estate  
→ fortified homestead  
→ hamlet  
→ village  
→ settlement

The game should not require a single scripted:

> Found Settlement

button to make this fantasy work.

Instead, settlement status should become possible when gameplay conditions emerge.

Examples:

- enough housing
- permanent residents
- food production
- storage
- economic activity
- defenses
- services
- infrastructure

At some threshold, the world begins treating the site as a settlement.

---

# 15. Settlement Population

NPCs may be attracted to the player's property because of what exists there.

Examples:

A forge may attract:

- smiths
- miners
- merchants

A farm may attract:

- laborers
- cooks
- livestock traders

A fortified location may attract:

- guards
- refugees
- mercenaries

A magical academy may attract:

- scholars
- mages
- apprentices

Population therefore grows from capability rather than an abstract population meter alone.

---

# 16. Player Choice of Lifestyle

The property system should support genuinely different ways of living.

## Civilized Property

Safe, convenient, regulated.

## Wilderness Homestead

Independent, resource-rich, dangerous.

## Frontier Fort

Strategic, defensible, expensive.

## Crafting Estate

Optimized for production and storage.

## Merchant Compound

Located near transportation and trade.

## Magical Retreat

Remote and designed around research.

## Necromancer Stronghold

Socially unacceptable but mechanically powerful.

## Player Settlement

High investment with economic, defensive, and social consequences.

No one location type should be universally superior.

---

# 17. Base Defense

Threats should depend in part upon location and player action.

Possible causes:

- nearby monster populations
- hostile factions
- player reputation
- valuable resources
- world events
- dangerous magical experiments
- strategic geography

A wilderness home may rely entirely upon:

- player defenses
- companions
- guards hired by the player
- traps
- walls
- magical defenses

A settlement home may benefit from:

- town walls
- guard patrols
- local militia
- nearby allies

Base attacks should remain relatively rare and meaningful.

Players who dislike this gameplay must retain an option to greatly reduce or disable property attacks.

---

# 18. Buildings Must Remain World Objects

A player building is not merely decoration.

It can affect:

- navigation
- storage
- crafting
- population
- security
- economy
- NPC assignment
- quests
- travel
- reputation
- territorial control

Placed buildings must therefore remain persistent world-state entities.

They must survive:

- leaving the area
- unloading the cell
- saving
- quitting
- loading
- world simulation
- later world updates

---

# 19. Streaming and Player Buildings

Player-created structures create an additional streaming requirement:

The engine cannot assume the authored world is the only world geometry that matters.

A streamed cell must reconstruct:

authored baseline  
+ procedural baseline  
+ persistent world changes  
+ player construction

before it becomes authoritative.

Player structures should remain logical records while distant.

Their full:

- mesh
- collision
- navigation
- interaction components

are instantiated only when necessary.

---

# 20. Navigation

NPCs and companions must be able to navigate player structures reliably.

This is one of the primary reasons construction remains socket/snap-based rather than unrestricted physics building.

Important cases include:

- entering player houses
- using doors
- walking between floors
- reaching crafting stations
- sleeping in assigned beds
- defending walls
- moving through gates
- accessing storage
- evacuating during attacks

A beautiful construction system that NPCs cannot navigate would undermine several core game systems simultaneously.

---

# 21. Procedural Generation Must Never Erase Authorship

The procedural system is subordinate to world design.

The final test should always be:

> Can a player describe where they are without saying only which biome they are in?

Good:

> I'm north of Vessmere, past the old watchtower, near the ravine where the road forks toward the barrows.

Bad:

> I'm in forest chunk 37.

The player should learn the world.

---

# 22. Long-Term Goal

The property/world system should eventually support stories such as:

> I found a quiet place beside a river and built a cabin.

> The hunting was good, so I stayed.

> I built a forge because the mountains nearby had iron.

> A smith moved in.

> Then a farmer.

> Bandits started attacking the trade road, so I built a wall and hired guards.

> Merchants began stopping there.

> What started as my cabin became a village.

And also:

> I bought a townhouse because I wanted somewhere safe to store my equipment near the market.

Both experiences should be legitimate.

---

# 23. Explicit Non-Goals

Do not turn construction into:

- Minecraft-style terrain destruction
- voxel world editing
- structural engineering simulation
- physics collapse simulation
- unrestricted clipping/free-placement chaos
- constant settlement micromanagement

The goal is a persistent RPG world with meaningful property ownership, not a pure construction simulator.

---

# 24. Implementation Timing

This document should NOT change M1, M1b, M2, or the early prototype.

These concepts become implementation-relevant primarily when development reaches:

- building
- property ownership
- factions/reputation
- economy
- NPC settlement behavior
- world persistence
- advanced world streaming

Before Building v1 begins, this design should be converted into explicit system requirements and architectural decisions where necessary.

---

# 25. Future Companion Design: Travel

Travel should receive its own design document before advanced world traversal systems are implemented.

Topics requiring decisions include:

- walking
- sprinting
- mounts
- carts/caravans
- boats
- magical movement
- flight
- gliding
- teleportation
- portals
- recalls
- wormholes / space-time traversal
- discovery requirements
- travel costs
- danger during travel
- transporting companions
- transporting cargo
- travel while encumbered
- race-specific traversal
- culture-specific traversal technology/magic
- faction-controlled transportation
- player-created travel infrastructure

The core requirement should remain:

> Faster travel is something the player understands, discovers, earns, builds, purchases, learns, or controls — not simply a UI button that removes the world.