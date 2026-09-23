# OTHERREACH — Design Extension Pack

**Repository codename:** UNNAMED  
**Game title:** **Otherreach**  
**Status:** Design capture / post-Phase-0 extension  
**Implementation warning:** These documents capture decisions and strong working directions from design discussions. They do **not** amend current M2 identity/persistence contracts while M2 is in progress. Reconcile them into the normative Phase 0 documents only after M2 is complete and audited.

## Core design principles captured here

1. **The world does not revolve around the player.**
2. **Simulation truth, character knowledge, and UI presentation are separate things.**
3. **The UI exposes capability; it should not arbitrarily limit capability.**
4. **Level affects competence and access, but should not replace physical logic.**
5. **NPCs are participants in the world, not decorations placed for the player.**
6. **Crafting favors compatible combinations and meaningful tradeoffs over arbitrary item-class prohibitions.**
7. **Race determines biology and starting context more than destiny. Cultural knowledge is learnable.**
8. **Stealth depends on plausible perception and information propagation, not global aggro telepathy.**
9. **Fast travel, maps, logistics, and knowledge are systems to earn and build, not omniscient conveniences.**
10. **AI may generate language and proposals; deterministic game systems remain authoritative.**
11. **Otherreach remains single-player-first while preserving a clean path toward LAN, community servers, and potentially larger multiplayer later.**

## Documents in this pack

| Document | Purpose |
|---|---|
| `OTHERREACH_COSMOLOGY.md` | The Other, Otherhome, Otherways, Otherwhere/Otherwhen, Otherborn, Otherfall and related vocabulary |
| `RACES.md` | Updated eight-race design with stronger morphology, silhouettes, hybrid/enhancement notes |
| `WORLD_BUILDING_AND_PROPERTY_DESIGN.md` | Wilderness construction, town property, ownership, settlements, jurisdiction |
| `TRAVEL_AND_TRAVERSAL.md` | Walking through mounts, flight, gates, Otherways and space-time travel |
| `AI_NARRATIVE_SERVICE.md` | Optional/provider-independent LLM layer for dialogue, memory, quests and prose |
| `ENGINE_VALIDATION.md` | Godot 3D/open-world stress-test plan before deep presentation commitment |
| `ECONOMY.md` | Regional production, consumption, money sources/sinks, contracts and safeguards |
| `NPC_SIMULATION.md` | Autonomous adventurers, simulation tiers, parties, homes, work and failure |
| `CRAFTING_AND_ITEMIZATION.md` | Component grammar, hybrid gear, materials, techniques, repair and reforging |
| `CHARACTER_CREATION_AND_LINEAGE.md` | Lineage, morphology, origin, culture, background, hybrids and enhancements |
| `SKILLS_AND_DISCIPLINES.md` | Skill webs, learned traditions, biological vs cultural capability |
| `HUD_INPUT_AND_ACTIONS.md` | Contextual HUD, resource channels, unrestricted action access |
| `INVENTORY_STORAGE_AND_LOGISTICS.md` | Carried containers, storage networks, remote transfer and cross-character storage |
| `MAPS_CARTOGRAPHY_AND_WORLD_KNOWLEDGE.md` | Knowledge-driven maps, cartography, rumors, fragments, tracking layers |
| `PETS_ANIMALS_AND_ECOLOGY.md` | Functional pets, animal roles, hunting, detection, ecology and resource interaction |
| `COMBAT_DAMAGE_ARMOR_AND_DEATH.md` | Physical combat model, weapons, armor coverage, wounds, defeat and resurrection |
| `STEALTH_DETECTION_AND_THREAT.md` | Perception channels, awareness, pulling/kiting, information propagation and assassination |
| `POST_M2_DOCUMENTATION_RECONCILIATION.md` | What to update in existing normative docs after M2 audit |

## Design topics still needing dedicated discussion

Recommended order after this pack:

1. Crime, law, witnesses, justice and reputation
2. Companions, hirelings, relationships, romance and party tactics
3. Quests, dungeons, bosses, repopulation and world events
4. Full magic schools/resources/ritual design
5. Seasons, weather, survival and environmental hazards
6. Social skills, persuasion, teaching and knowledge transmission
7. Endgame, long-term mastery and legacy
8. Modding/community-server policy and later multiplayer specifics
9. Difficulty/accessibility and assist options

## Authority rule

When one of these documents conflicts with a current normative Phase 0 document, **do not silently choose this pack**. Record the conflict and reconcile it after M2, with special care around `PROGRESSION.md`, `DATA_MODEL.md`, `SYSTEMS.md`, `ROADMAP.md`, and persistence/identity contracts.
