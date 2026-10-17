# WP12 status: NPC life simulation (simulation part)

SYSTEMS_COMPLETENESS_PLAN_20261017 §2.13 (residents and vendors) and the WP12 brief in §6. The presentation and animation parts (role-resident dressing, walking and activity idles on the AnimationTree) are local Claude's.

## What is built

- **Navigation** (ported by hand from `claude/m7-factions-building` of jlucasmcrell/UNNAMED, never merged):
  - `src/Domain/Spatial/Nav*.cs`: the 250 mm clearance-class lattice tiled per 100 m cell, the A* planner with enclosure probes and exact string pulling, the route follower (replans, door opening, arrival), the placement edit check (V-N1 to V-N4, for WP8), counters and scratch.
  - `src/World/Runtime/Navigation.cs`: the region's grid (built on first use, about 35 ms for `region.starting_frontier`), gate state, views.
  - `content/config/navigation.yaml` with the NAV001-NAV007 lints (`src/Content/NavigationContent.cs`).
  - The m7 errand `BeginWork` path is not ported: it was tied to player-built pieces. WP8 and WP15a's crafter walk through the same mover, a goal per NPC.
- **NPC life** (`src/Domain/Social/NpcLife.cs`, pure):
  - needs as 0-3 bands (hunger, rest, social, coin);
  - a utility routine chooser over work, eat, sleep, socialise, shop, shelter and idle, constrained by work hours, night, meal hours, trading hours and rain;
  - homes and anchors (home, work, eat, social);
  - jobs whose daily output joins the town market (WP14's job hook);
  - NPC-NPC pairs (friend, rival, partner) driving visits;
  - life states (living, injured);
  - role slots refilled by arrivals 3-7 game days after a resident leaves (DN4). The 8 named NPCs are essential and are refused any departure.
- **Running world** (`src/World/Runtime/Npcs.cs`, `NpcLife.cs`, `NpcMover.cs`), by tier:
  - Tier A walks bodies a tick at a time with collision, opening doors from a hand's reach and never placing a body.
  - Tier B glides along the route every 10 ticks (2 Hz).
  - Tiers C and D park bodies at their goal at the hour's turn, only when both the body and the goal are out of sight.
  - Tier budgets (A=60, B=300) are admission control, never caps.
  - Talking pauses the routine. Merchants trade only at work, 06:00-20:00 (`Simulation.TradeRefusal`). Teachers teach only at their work anchor in work hours (`Simulation.TeachRefusal`, WP9's hook). Quest givers can always be talked to.
- **Offline catch-up**: `NpcSystem : IAbstractAdvance`, stepped per game hour on absolute boundaries; the same hour step the running world takes. It runs before the markets in the advance order, so job output is read consistently.
- **Content**:
  - 20 routines in `content/routines/` for `region.starting_frontier`: the 8 named NPCs, and 6 role residents per town named from `people.veth`'s Suggest list (WP2);
  - `content/config/npc_life.yaml`;
  - NPL001-NPL004 lints (anchors standable and reachable, merchants work the trading hours).
- **No population cap** anywhere:
  - lives are a keyed collection;
  - the save stores them as a list with their cell;
  - tier figures are budgets;
  - the structural test runs 1,000 synthetic NPCs through save/load, catch-up and tier changes.

## Tests

- `tests/Domain.Tests/Spatial/NavigationTests.cs`: the planner, doors, follower, restamp byte-equality, edit check, routes, geometry.
- `tests/Domain.Tests/Social/NpcLifeTests.cs`: needs, chooser, pairs, departures and arrivals, job output, injury, 1,000 lives.
- `tests/Application.Tests/NpcLifeWorldTests.cs`:
  - casts and names;
  - T-22 chunk invariance, and going on alike after it;
  - the world living while away;
  - merchants' and teachers' hours;
  - replacement;
  - 14 game hours of walking with no visible teleport (half a metre a tick) and doors opened;
  - job output;
  - the 1,000-NPC structural test.

## Persistence

Schema 23 (the next free version after 22; the integrator renumbers): the entities section gains `npc_life` with every life (pose, route, needs, activity, condition), the vacancies and the pairs' last meetings. It is null before lives first ran.
