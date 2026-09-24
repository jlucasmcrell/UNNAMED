# OTHERREACH — Modding, Community Servers, Federation & PvP Seams

**Status:** Owner-approved long-term direction  
**Implementation stage:** Preserve seams now; implementation later  
**D-12 remains in force:** do not build networking/MMO infrastructure during the single-player prototype.

---

# 1. Core Direction

Modding and community servers are first-class long-term goals for both:

- single-player worlds;
- persistent community-hosted worlds.

But:

> **Design for modding now. Build the full mod ecosystem later.**

> **Design for community servers now. Build networking later.**

The current single-player critical path remains the priority.

---

# 2. One Content System

Core content and mods should use the same data-driven pipeline wherever practical.

A mod should pass:

```text
package
  ↓
schema validation
  ↓
DefinitionId validation
  ↓
cross-reference validation
  ↓
dependency validation
  ↓
asset validation
  ↓
compiled content pack
```

Avoid a clean official authoring system plus a separate fragile mod system.

Long-term, internal content tools should become much of the mod SDK.

---

# 3. Mod Capability Tiers

## Content packages — preferred/default

Safe declarative additions using:

- YAML;
- models;
- textures;
- audio;
- animation;
- dialogue;
- regions/content.

Potentially add:

- items;
- creatures;
- spells;
- techniques;
- recipes;
- quests;
- factions;
- NPCs;
- buildings;
- races;
- regions.

## Declarative patches

Explicitly alter existing definitions.

Patches must name targets and fields.

Do not rely on silent filesystem overwrite.

## Sandboxed scripting — future

Potential extension for mechanics not expressible declaratively.

Must be designed with security and determinism in mind.

## Native/unsafe plugins — optional advanced future

Single-player users/server operators may eventually choose to load executable plugins manually.

A community server must **never silently cause arbitrary executable client code to run**.

---

# 4. Package Identity

Every mod/package needs a stable package ID.

Working example:

```text
package_id: jsmith.soulforge
version: 2.3.1
```

The exact manifest schema is future work.

---

# 5. DefinitionId Namespace Reservation

Core Definition IDs remain unchanged.

Reserve a `mod` segment for package-owned definitions while preserving kind-first IDs.

Examples:

```text
item.weapon.mod.jsmith.soulforge.starblade
creature.mod.jsmith.soulforge.void_hound
quest.mod.jsmith.soulforge.introduction
spell.mod.jsmith.soulforge.soul_bolt
```

A package declaring:

```text
jsmith.soulforge
```

may author IDs only under its namespace.

This provides:

- readable type prefix;
- collision avoidance;
- persistent identity;
- migration clarity.

Do **not** build the mod loader now.

Cheap future-proofing allowed now:

- document the reservation;
- ensure current DefinitionId grammar does not reject the pattern.

---

# 6. Core Namespace

The `mod` segment is reserved.

Core game content should not author IDs under:

```text
<kind>.mod.*
```

This prevents future ambiguity.

---

# 7. Deterministic Load Order

No silent “last mod wins.”

Package ordering should eventually be based on:

- explicit dependencies;
- explicit declared ordering where necessary;
- deterministic resolution.

Conflicts should be diagnosable.

If two packages modify the same field incompatibly, the user/server should know.

---

# 8. Patch Model

A patch should explicitly state:

- target DefinitionId;
- operation;
- field/path;
- replacement/add/remove;
- package source.

Do not infer patches from duplicate IDs.

---

# 9. Package Dependencies

A package may declare:

- required core version;
- required packages;
- incompatible packages;
- optional integrations.

Dependency cycles are errors.

---

# 10. Content Profile / Modpack

A world should use an explicit content profile.

Examples:

```text
Vanilla
QoL
Hard Survival
My Modded World
Community Server Pack
```

A save belongs to its content profile.

Installing a mod does not automatically inject it into every existing save.

---

# 11. Lockfile

A world/server should eventually pin exact content identity.

Conceptual lockfile:

```text
core: version/hash
packages:
  - id
  - version
  - hash
```

This gives deterministic reproduction and support diagnostics.

---

# 12. Save Compatibility

Changing/removing gameplay mods is a migration event.

Never load and silently delete unknown persistent state.

Possible outcomes:

- exact package available → load;
- alias/replacement migration → migrate;
- explicit destroy/discard migration → migrate/report;
- orphan preservation → future capability;
- required package missing → refuse with actionable message.

The M2b migration framework is the foundation.

---

# 13. Orphaned Mod State

Future `orphans` support can preserve unknown package-owned records when safe.

Use cases:

- temporarily disabled mod;
- later reinstall;
- world recovery.

Do not implement until a real mod milestone requires it.

---

# 14. Cosmetic / Presentation Mods

Presentation-only mods can have more permissive rules.

Examples:

- HUD;
- fonts;
- textures;
- audio;
- accessibility overlays.

Multiplayer servers may restrict local mods that expose hidden information.

---

# 15. Total Conversions

The architecture should not intentionally prevent major conversions.

A future package set may replace:

- world;
- races;
- factions;
- quests;
- items;
- art;
- rules.

This is a long-term goal, not a 1.0 promise.

---

# 16. Mod Distribution Is Separate From Mod Format

Do not design the format around one storefront.

Possible providers later:

- Steam Workshop;
- Nexus;
- direct download;
- server repository;
- other platforms.

The package format remains independent.

---

# 17. Licensing Metadata

Package manifests should eventually support:

- author;
- version;
- homepage/source;
- license;
- redistribution policy;
- dependencies.

A server cannot assume it may legally redistribute every required mod.

---

# 18. Mod SDK / Tooling

Long-term internal tools can become:

```text
otherreach mod:init
otherreach mod:validate
otherreach mod:pack
otherreach mod:test
```

Potential documentation:

- schemas;
- IDs;
- assets;
- sockets;
- animations;
- migrations;
- dependencies.

---

# 19. Community Server Model

A community server is:

> **Someone else's persistent Otherreach world.**

Conceptually:

```text
OtherreachServer
  ├── World
  ├── Save
  ├── Ruleset
  ├── Content profile
  ├── Mods
  ├── NPC simulation
  └── Connected players
```

Do not implement this before the networking milestone.

---

# 20. Server Authority

Multiplayer uses:

```text
client requests
server validates
server mutates authoritative world
server sends result
```

Reuse the existing command/domain architecture.

Do not create separate multiplayer combat/inventory rules.

---

# 21. Community Server Freedom

Servers may eventually choose:

- vanilla co-op;
- roleplay;
- survival;
- custom progression;
- faction warfare;
- custom regions;
- heavily modded worlds.

The official game does not need to balance every community world against one centralized economy.

---

# 22. Server Manifest

A server should eventually advertise:

- server/game version;
- world/ruleset;
- required content profile;
- modpack hash;
- PvP policy;
- transfer policy;
- player count;
- server age.

The client can determine compatibility before connecting.

---

# 23. Safe Server Package Download

Eventually:

```text
select server
  ↓
detect missing safe packages
  ↓
download
  ↓
verify hash/signature
  ↓
connect
```

Automatic delivery applies to safe data/assets.

Never silently execute arbitrary client DLLs/scripts with native privileges.

---

# 24. Server Upgrade Workflow

Persistent server updates should use:

```text
stage
  ↓
migration dry-run
  ↓
backup
  ↓
validate
  ↓
commit
```

M2b tooling should be reused rather than bypassed.

---

# 25. Administration

Future headless server tools may include:

- backup/restore;
- whitelist/password;
- bans;
- operator roles;
- modpack management;
- migration reports;
- scheduled restart;
- audit logs.

Administrative cheats, if supported, should be explicit and auditable.

---

# 26. World-Owned Characters

Default:

> **A character belongs to a world.**

A single-player character does not automatically join a server.

A community server owns authoritative character state for that world.

This prevents trivial single-player save editing from contaminating server economies.

---

# 27. World-Owned Souls

Default:

> **A Soul belongs to a world.**

Single-player World A has its own Soul history.

Community Server B has another.

Explicit future transfer rules may change this.

---

# 28. Character Transfer

Transfers are policy-controlled.

Possible server policies:

- no transfer;
- new character only;
- Soul-only import;
- character import, no items;
- cluster-only transfer;
- trusted full transfer.

Destination server validates.

---

# 29. Transfer Package

Potential future transfer record:

- identity;
- Soul state;
- character state;
- skills/progression;
- allowed items;
- definition references;
- source realm;
- signatures.

Unknown destination content is never guessed.

---

# 30. Trusted Server Clusters

Servers controlled by one operator may share:

- content;
- identity;
- transfer policy.

This is simpler than open federation and should come first if transfers are ever implemented.

---

# 31. Federation

Far-future option:

> **Independent persistent worlds can selectively exchange people/state through Othergates.**

In fiction and UI, entering an Othergate can represent a realm transfer.

Underneath:

1. source produces signed transfer;
2. destination validates;
3. client changes server;
4. destination instantiates accepted state.

---

# 32. Federation Trust Is Granular

A destination may trust another realm for:

- character identity;
- Soul identity;
- progression;
- achievements;

but **not**:

- inventory;
- currency;
- custom items.

Trust is not binary.

---

# 33. Economy / Item Trust

A server with:

```text
Sword of +999999
```

cannot inject it into a trusted realm unless the destination explicitly accepts that definition/ruleset.

Possible transfer contracts:

- no inventory;
- shared approved content only;
- signed namespaces only;
- map unsupported items;
- reject transfer.

Destination authority wins.

---

# 34. Server / World Identity

Future networking will need explicit world/server identity concepts.

Examples:

- WorldId;
- ServerId;
- RealmId.

Do **not** add them to M2/M2c solely for speculation.

Design them when multiplayer becomes scheduled.

---

# 35. Server Discovery

Future possibilities:

- LAN discovery;
- direct host/IP;
- friends;
- community browser;
- favorites;
- recent servers.

Advertise rules clearly.

---

# 36. Hosting

Long-term target:

- Windows headless;
- Linux headless;
- Docker where practical.

Users may host on:

- PC;
- home server;
- VPS;
- provider.

This is enabled by the engine-independent authoritative domain but not implemented now.

---

# 37. PvP — Preserve the Seam, Design Later

PvP is intentionally not deeply designed yet.

The architecture should make one commitment:

> **Combat must not hard-code that another player is always an invalid target. Attack legality is a ruleset decision.**

Do not implement PvP during the current single-player roadmap.

---

# 38. Future PvP Policies

Possible server modes later:

- disabled;
- consensual duels;
- consensual flagging;
- designated zones;
- faction/war;
- unrestricted.

These are examples, not settled rules.

---

# 39. Shared Combat System

Future PvP should reuse:

- armor;
- wounds;
- shields;
- magic;
- stealth;
- projectiles;
- death states.

No separate “PvP combat engine.”

---

# 40. Law vs PvP

Physical ability to attack does not imply legal permission.

Examples:

- licensed duel;
- war enemy;
- murderer in town;
- outlaw region.

The crime/law system can make PvP much richer than red-name targeting.

---

# 41. Griefing / Safety

If unrestricted PvP is ever supported, later design must address:

- spawn safety;
- logout/combat rules;
- newbie protection where appropriate;
- loot/death policy;
- building damage;
- harassment/admin tooling.

Do not solve these now.

---

# 42. Systemic Hostility — Core Single-Player Seam

> **Any sufficiently autonomous person, faction, settlement, institution, or culture can become the player's ally, rival, opponent, or enemy through relationships, law, reputation, ideology, competition, and history.**

This is **not** a multiplayer-only feature.

---

# 43. Hostility Is Not One Number

Keep conceptually distinct:

- personal relationship;
- fear/trust/grudge;
- faction standing;
- settlement standing;
- legal status;
- faction relation;
- war state;
- identity knowledge;
- tactical hostility;
- attack legality.

A town can dislike the player without its civilians becoming kill-on-sight enemies.

A guard can personally like the player while being obligated to arrest them.

---

# 44. PvNPC / Nemesis Behavior

A personally hostile NPC may develop goals:

- bounty;
- sabotage;
- framing;
- theft;
- political opposition;
- assassination;
- alliance with enemies.

This can create organic recurring antagonists.

---

# 45. Information-Based Hostility

Hostility/reputation should propagate through believable communication.

A crime or political act does not instantly make every remote faction member omniscient.

This preserves:

- disguise;
- rumors;
- jurisdiction;
- delayed news.

---

# 46. Attack-Legality Seam

Future authoritative combat should conceptually ask:

```text
CanAttemptAttack(attacker, target, context)
```

Rules may consider:

- world type;
- server rules;
- duel consent;
- faction war;
- legal restrictions;
- target type.

Do not scatter `if target.IsPlayer return false` throughout damage code.

This is a **seam**, not a mandate to implement PvP now.

---

# 47. Multiplayer Development Order

If multiplayer becomes scheduled, preferred progression:

```text
single-player authority
  ↓
LAN/co-op seam experiment
  ↓
host/join
  ↓
community dedicated server
  ↓
persistent community worlds
  ↓
trusted transfer clusters
  ↓
optional federation
```

No jump directly to MMO-scale infrastructure.

---

# 48. Current Implementation Rule

D-12 remains authoritative:

- no networking code now;
- no dedicated server now;
- no speculative concurrent persistence;
- no WorldId/ServerId added merely for future possibility.

What may happen now:

- reserve mod DefinitionId namespace;
- keep data-driven systems;
- keep commands authoritative;
- avoid hard-coded player-target invalidity;
- avoid assumptions that make safe content packaging impossible.

---

# 49. Foundational Rule

> **Otherreach should be a great single-player game even if multiplayer never ships, a moddable game even if no official server ecosystem exists, and an architecture capable of growing into community worlds without having secretly built an MMO during the prototype.**
