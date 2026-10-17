# WP12 status: NPC life simulation (simulation part)

SYSTEMS_COMPLETENESS_PLAN_20261017 §2.13 (residents and vendors) and the WP12 brief in §6. The presentation and animation parts (role-resident dressing, walking and activity idles on the AnimationTree) are local Claude's.

> **Owner cut, 2026-10-17 (wave 4 integration, schema 29): NPC life was going "too Sims".** Gone: the needs (hunger, rest, social, coin) and the utility chooser, meals, NPC-NPC pairs and visits, shopping, and rain shelter, with their config (`needs`, `meal_hours`), routine fields (`weights`, `pairs`, `eat`/`social` anchors) and tests. Each routine now has a fixed daily schedule: work in work hours, asleep at home at night, the rest at home or at a spot a `schedule` band names (`{ hours: [from, to], at: <anchor> }`; none authored yet). Kept: homes and work hours (merchants trade and trainers teach at work), the mover and navigation, tiers B/C/D, the offline catch-up, hireability and the hired state, trips and SettingOut, arrivals, jobs. The sections below describe the package as built before the cut.


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

# WP12b: NPC life follow-up (the items WP12 left out)

## What is built

- **Town roles and derived hireability.** A routine names `roles:` from `config.npc_life`'s vocabulary; each role says what it hires out as (`hires_as`, the party's hireling roles) and which chatter speaker type it talks as. Hireability is derived, never stored (`NpcHireability`): a `vendor` or `quest_giver` never hires on; anyone else does when a role of theirs hires out, they have a body to work with (`resident_hireling` for residents, the NPC file's `hireling` block for a named NPC) and they are about their own life: living, in town, not injured, not hired, not setting out. All 12 residents hire out; no named NPC does (vendors, quest givers, the steward, the companion).
  - `Simulation.Hiring` (`INpcHireability`: `IsHireable`, `Hireability`) and `Simulation.IsHireable`; `HireabilityChanged(Key, Hireable, Reason, Tick)` is raised whenever the derived answer changes (hire, dismissal, injury, departure, arrival, a trip).
  - The party's hire refusal asks it first (`CompanionSystem.Unhireable`, one hook in `Party.cs`).
- **The `hired` life state and the party-life hookup.** `NpcPartyLife` is WP15b's `IPartyLife`, always wired: joining the party (hired, recruited or allied) sets `NpcCondition.Hired` - no needs, no choices, no walking of their own; leaving resumes the life where the body stands and they walk home (`IPartyLife.Walks` stops `Party.Leave` from putting them there). Let go during an offline catch-up, they stay hired until the first hour after the leave tick, so any split of the absence frees them at the same hour (T-22). A companion gravely wounded rests at home, injured, until well. The `HomeLife` stand-in is gone; `Simulation.Start`'s `life` is now only an extra listener.
  - Companion home anchors: NPL007 requires a routine for anyone who can join or be hired in a region with routines - the home they walk back to.
  - A resident hireling who dies leaves a vacancy (DN4), and the arrival is someone new; the slot is not lost to the party ledger's fallen list.
- **The trip planner** (`NpcTrips`, `NpcSystem : ISettingOutSource`). A routine may have a `trip:` (purpose, the creature/bounty/item that names it, a meeting place and a destination, each a location's name and a spot, a day's chance, the hours it may leave, how long it keeps them away). Whether a day has a trip, and when, is one `RngChannel` draw per routine and day: a pure function, so any span asked of it answers the same. The NPC waits at the meeting place for the hour before departure (`set_out`), then is `away` at the destination; the offer is posted `trip_lead_minutes` (40; 20-60) before departure. `Between` returns them in tick order, which #17's `LfgSystem` posts to World chat. The `SettingOuts.None` stand-in is gone: a null `SimulationSetup.SettingOuts` means the planner; `SettingOuts.Of` stays for tests of the contract.
  - Content: the Ashen Hollow hunter and guard, and the Cairnwold hunter, trapper and woodcutter, set out now and then.
- **Tiers from a set of observers.** `TierSystem` gives each cell the tier of its distance to the nearest observer (`ITierObservers`, `TierObservers.PlayerOnly` by default, `TierObservers.With(...)` for others); NPC budget admission orders by the nearest observer too. `Simulation.Observers` reads them.
- **Job output = max(floor, jobs).** `EconomyEngine.Step` takes the day's jobs: a good's production is the larger of its floor and its jobs, never their sum. With today's numbers every floor is above its jobs, so the shipped economy is unchanged.
- **Residents as chatter speakers** (`NpcSystem : IChatterSpeakers`): the role residents not in the party, each as their roles' speaker type, in their town or in the wilds while away. Named NPCs never are. The `ChatterSpeakers.None` stand-in is gone.
- **The crowd trap.** The eat anchors round both cookfires are re-spaced on a 3.3 m ring (about 2 m apart; at least 1.8 m, pinned by a test), and NPCs stay solid but make way: a tier-A NPC the character is pressing into - touching, and the character's next step would go farther without them - takes one walk step aside across the character's way. It is derived from the bodies and this tick's intent, so nothing is saved.

## Tests

- `tests/Domain.Tests/Social/NpcLifeTests.cs`: hireability from roles, the hired life suspended and freed at the next hour, the planner's draw and phases, a trip before the routine.
- `tests/Domain.Tests/Economy/EconomyEngineTests.cs`: production is max(floor, jobs).
- `tests/Application.Tests/NpcLifeLinksTests.cs`: roles and hireability over the game's content; hiring a resident (life suspended over two hours, saved and loaded hired, dismissed without a teleport, walking home); injury; a resident hireling's death leaving a vacancy; the planner (lead of 20 minutes or more, deterministic per world, split-invariant); a SettingOut posted to World chat with the NPC waiting then away; T-22 over lives, trips and offers; residents as chatter speakers; observer-set tiers; eat-anchor spacing; the crowd at each cookfire letting the character out every way the town leaves open, NPCs solid throughout (it fails at Cairnwold without the step aside).
- `tests/Content.Tests/NpcLifeLinksContentTests.cs`: the roles, trips, and NPL005-NPL007.

## Persistence

Schema 28 (one final commit, renumbered from 27 at the wave 4 integration; the soak took 27): an NPC life's condition may be `hired`, a life's activity `set_out` or `away`, and the party's records (relationships, the party, its ledger, its sheets) may name a role resident by their slot key (`routine.`). The section shapes are unchanged, so 26 -> 27 only moves the version.
