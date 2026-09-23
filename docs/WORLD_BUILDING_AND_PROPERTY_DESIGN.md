# OTHERREACH — World Building, Property & Settlement Design

**Status:** Strong working design; future implementation  
**M2 impact:** None

## 1. World composition

Otherreach should use:

- **hand-authored macro geography** for mountain ranges, valleys, rivers, coastlines, roads, towns, landmarks, major ruins, dungeons and sightlines;
- **deterministic procedural assistance** for trees, grass, rocks, clutter, minor resource placement, wildlife and environmental variation;
- **handcrafted important spaces** for settlements, dungeons, story locations, unique interiors and memorable landmarks.

The goal is a world players can learn and remember, not an endlessly randomized landscape.

## 2. Region/cell model

Retain the current region/cell concept as the world-management foundation.

- Regions are large authored units.
- Cells are streaming/simulation/persistence units.
- Player buildings are persistent world-state changes layered over baseline world generation.
- Streaming reconstructs authored baseline + deterministic baseline + persistent deltas + player construction.

## 3. Seamless-world target

Exterior movement should be seamless wherever practical.

Towns should generally be entered without visible loading screens. Interiors may technically occupy separate streamed spaces, but transitions should be hidden naturally through:

- doors;
- caves;
- tunnels;
- stairs;
- lifts;
- gates;
- magical thresholds;
- Otherways.

Visible loads are acceptable for exceptional transitions such as very distant teleportation if technical limits require them.

## 4. Building freedom

Default rule:

> If the terrain, jurisdiction and physical constraints permit a structure, the player should usually be allowed to build it.

Do not restrict construction to a small set of pre-designated “player home plots” unless a settlement's law requires it.

The wilderness is the broadest building space, but freedom comes with consequences.

## 5. Property states

Useful conceptual ownership states:

- `Unclaimed`
- `WildernessClaim`
- `Leased`
- `Owned`
- `FactionGranted`
- `SettlementControlled`
- `PlayerSettlement`

These are design concepts, not present schema commitments.

## 6. Jurisdiction

A location may have jurisdiction such as:

- none;
- tribal;
- village;
- town;
- city;
- faction;
- religious;
- disputed.

Jurisdiction can determine:

- whether building is permitted;
- taxes/rent;
- required permits;
- prohibited crafts;
- weapon restrictions;
- necromancy/magic law;
- guard protection;
- theft/trespass rules;
- available services.

Different cultures should have genuinely different laws.

## 7. Wilderness property

Advantages:

- little or no tax;
- freedom of construction;
- freedom to practice otherwise restricted crafts/magic;
- direct access to resources;
- opportunity to found a new settlement.

Costs:

- danger;
- distance from markets/services;
- no automatic guard response;
- logistical difficulty;
- maintenance;
- possible raids or environmental threats.

Base attacks should be **rare enough not to make building annoying**, and ideally configurable or reducible through defenses, reputation and location choice.

## 8. Settlement property

Inside established towns/cities, the player may:

- rent;
- lease;
- purchase;
- receive property from a quest/faction;
- inherit or otherwise earn access.

Benefits:

- safety;
- nearby services;
- established markets;
- banks/storage;
- fast travel;
- law enforcement;
- social access.

Costs:

- taxes;
- rent;
- building codes;
- cultural restrictions;
- limited land.

## 9. Settlement growth

A player-founded location can evolve organically:

**camp → shelter → cabin → homestead → workshop → hamlet → village**

Growth should result from capabilities and population rather than a “Become Mayor” button.

NPCs may be attracted by:

- beds/housing;
- safety;
- food;
- employment;
- crafting stations;
- trade;
- transportation;
- nearby resources;
- religious/cultural facilities.

## 10. NPC construction

NPCs may also establish property, especially autonomous adventurers and economic agents.

NPC construction should normally use:

- authored house archetypes;
- modular room kits;
- approved footprints;
- furniture sets;
- upgrade paths.

This produces believable growth without asking general AI to solve arbitrary architecture.

## 11. Building technology

Use socket/snap/module systems where helpful for:

- persistence;
- pathfinding;
- structural legibility;
- AI use;
- visual polish.

The player should still have enough freedom that every home does not look identical.

## 12. Settlement simulation

A settlement may eventually track:

- population;
- housing capacity;
- food;
- safety;
- employment;
- production;
- trade access;
- services;
- reputation;
- infrastructure;
- jurisdiction;
- travel connectivity.

Do not over-simulate before these values produce meaningful gameplay.

## 13. Relationship with the economy

Construction consumes real economic goods:

- lumber;
- stone;
- metal;
- textiles;
- tools;
- labor;
- specialist services;
- magical/technological components.

Remote construction therefore creates contracts, transport needs and opportunities for player/NPC merchants.

## 14. Relationship with the Other

Later possibilities:

- settlements near Otherfolds attract scholars/traders;
- Othergates create valuable transportation hubs;
- dangerous folds reduce safety;
- governments regulate gate construction;
- special Otherwrought structures require rare materials.

## 15. Design rule

> Property should feel like part of the world economy and jurisdiction, not a detached housing minigame.
