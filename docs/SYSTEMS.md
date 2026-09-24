# SYSTEMS.md — Runtime Systems and Responsibilities

**Project:** Otherreach (codename UNNAMED) — full-body third-person with seamless first-person zoom, solo-first, open-world fantasy RPG
**Phase:** 0 — Architecture. **Status:** Draft for owner review.
**Reads:** `PROJECT_CHARTER.md` (authoritative vision), `PHASE_0.md` (instructions), `DECISIONS.md` (D-01..D-12).
**Audience:** an implementation session that must build these systems from this document plus `DATA_MODEL.md` and `PERSISTENCE.md`.

This document defines **what each runtime system decides, owns, reads, persists, rebuilds, and exposes**. It does not define content volume, balance numbers, or implementation code (see `PHASE_0.md` prohibitions).

---

## 1. Architectural contract

| Rule | Statement | Source |
|---|---|---|
| C-1 | All authoritative gameplay state lives in engine-agnostic C# domain assemblies under `src/Domain/`. Only `src/Presentation/` may reference Godot types, enforced by separate `.csproj` with no Godot reference. | D-02 |
| C-2 | State changes only by **commands** dispatched to systems; systems publish **events**; views subscribe. Nothing writes state directly. | D-02, D-11 |
| C-3 | One **World State Store** holds authoritative mutable gameplay values as plain data. It is storage, not a rule-holder: it does not validate, resolve, or interpret. | D-02, D-10 |
| C-4 | The **Entity Registry** assigns ULID instance IDs at creation and resolves ID→instance. It owns **no gameplay rules** and must never grow any. | D-04, D-10 |
| C-5 | There is **no universal GameManager**. A thin composition root wires systems together at boot and owns no gameplay state. | D-10, charter |
| C-6 | The Presentation layer never mutates state. Prediction is for feel only and is never authoritative. | D-11 |
| C-7 | Persistence is a sparse delta from a deterministic baseline: save = manifest + player/companion state + world delta. | D-05 |
| C-8 | Every world actor is assigned a simulation tier A/B/C/D. Systems must declare which tiers they operate at. | D-06 |
| C-9 | Content is YAML under `content/`, validated at load against C# schemas, referenced by string definition IDs. No content in game code. | D-03 |
| C-10 | No networking code in Phase 0–2. Only the command/event seam and stable identities exist as extension points. | D-02, D-12 |

### Execution model

One **world tick** (fixed 20 Hz, domain-side; presentation interpolates). Per tick:

1. Collect commands from input adapters, AI, and scripts into the command queue.
2. Drain the queue in a **deterministic system order** — the order systems are listed in §2 is the required order; ties broken by ascending instance ID.
3. Each system validates commands it owns and applies mutations to the World State Store.
4. Events are queued during the drain and delivered synchronously at the **end of the tick**, after mutations commit. Views therefore never observe a half-applied tick.
5. Rejected commands emit `CommandRejected` with a machine-readable reason code. Silent dropping is a bug class.

- **Overall determinism contract:** given the replay tuple `(world_seed, generator contract, content_hash, command_log)`, tick outcomes must be reproducible; generated baselines depend on the baseline tuple `(world_seed, generator contract)` alone, and content identity is never a generation input (`PERSISTENCE.md` §1.3, M2b). Systems must not read wall-clock time, unseeded RNG, frame delta, or the iteration order of unordered collections. Comparison and sorting must be **ordinal** (`StringComparison.Ordinal` / `StringComparer.Ordinal`) and the numeric policy must be pinned (`double` for simulation state unless a schema says otherwise); both are set once in `Directory.Build.props` and asserted in CI. `command_log` is named here as an input, so it must be **persisted for every save** (it is a few KB) and exercised by a replay test — an earlier draft recorded it only in debug saves, which made every saved world unreplayable in principle and the contract unverifiable. See `PERSISTENCE.md` for the manifest fields that carry this tuple.

---

## 2. System specifications (listed in required execution order)

Phase membership is summarized in §3. Ordering is the required command/mutation order.

### S-01 Command / Event Bus

- **Responsibility.** Serialize and order all state mutation and all state-change notification.
- **Owns.** Command queue, event queue, subscriber table, per-tick mutation log.
- **Reads.** Nothing (infrastructure).
- **Persistent.** **The command log — persisted to `command_log.jsonl` for every save, not only diagnostic ones** (`PERSISTENCE.md` §5.7). It is a few KB, and it is the enabler of the replay tuple: without it a save is unreplayable and the determinism contract below cannot be verified. Its digest is recorded in the manifest as `command_log_sha256`. **Superseded wording:** an earlier revision of this file said the log is "written only by the save system when a diagnostic/debug save is requested"; that is no longer true and must not be implemented. Events are **not** persisted — they are derivable from commands plus the deterministic system order.
- **Transient.** Queue contents, subscriptions, tick counters.
- **Events/interfaces.** `Dispatch(ICommand)`, `Subscribe<TEvent>(handler)`, `DrainTick()`; emits every domain event including `CommandRejected`.

### S-02 Entity Registry

- **Responsibility.** Assign and resolve stable identities for every runtime instance.
- **Owns.** ULID allocation, `InstanceId → EntityRecord(kind, definition_id, tier, owner_cell)` index, creation/destruction ledger.
- **Reads.** Nothing.
- **Persistent.** The destruction tombstone set (so a deterministically-baselined entity is not respawned), and the ULID monotonic clock anchor.
- **Transient.** All live lookup indexes, rebuilt by replaying baseline + delta (D-05).
- **Events/interfaces.** `Create(kind, definitionId) → InstanceId`, `Resolve(id)`, `TryResolve`, `Enumerate(kind)`, `Destroy(id)`; emits `EntityCreated`, `EntityDestroyed`. **Contains no gameplay rules (D-10).**

### S-03 World State Store

- **Responsibility.** Hold authoritative mutable gameplay values keyed by instance ID and world cell.
- **Owns.** Component blobs per entity (health, position, inventory ref, faction, quest refs), cell-level world flags, world clock value.
- **Reads.** Nothing.
- **Persistent.** Only divergences from the deterministic baseline are serialized (D-05).
- **Transient.** The whole store; at load it is rebuilt by regenerating the baseline from the baseline tuple (`PERSISTENCE.md` §1.3) and applying the delta, after each changed cell's recorded `baseline_hash` has proven that baseline (`PERSISTENCE.md` §6.4).
- **Events/interfaces.** `Read<T>(id)` on `IWorldState` (public); `Write<T>(id, value)` on **`IWorldStateWriter`**, which is **`internal` to `Domain`** so `Application` and `Presentation` cannot name it at all. Writes are callable only by domain systems inside a tick drain. **Superseded wording:** an earlier revision exposed `Write<T>` directly on `IWorldState` with the note "callable only from systems inside a tick drain" — a sentence rather than a boundary. The split interface is the contract (`ARCHITECTURE.md` §5); the compile error is the enforcement. A write to a component key no system declared ownership of is a **startup error**.

### S-04 Time & Calendar — split: core in **Phase 1**, calendar in **Phase 2**

**Why this is split rather than simply deferred.** Fourteen Phase-1 systems declare a read on S-04 (spawn timing, node regrowth, craft timers, NPC schedule bands, merchant restock, quest deadlines, travel time), so a plain "S-04 is Phase 2" would leave the Phase-1 system set unable to satisfy its own declared dependencies. The resolution is to ship the **clock** in Phase 1 and the **calendar** in Phase 2. `PROTOTYPE.md` independently reaches the same conclusion: "a static `world_time` counter is enough to test respawn."

**Phase 1 — the clock (mandatory, small).**

- **Responsibility.** Advance a monotonic world clock and expose elapsed-time arithmetic. Nothing in Phase 1 may read wall-clock time.
- **Owns.** `world_tick` (monotonic integer), `world_time` in game minutes, the tick→game-minute conversion, and the config-derived units from `config.time` (`DATA_MODEL.md` §4.19).
- **Reads.** The fixed domain tick. Nothing else.
- **Persistent.** `world_tick` / absolute game time. This single value is in the save manifest and is what `RK-12`'s offline catch-up advances.
- **Transient.** Per-frame interpolation.
- **Events/interfaces.** `Now()`, `ElapsedSince(tick)`, `Advance(ticks)`, `IsDue(deadline)`; emits `HourPassed`, `DayPassed`. Durations are stored as **absolute deadlines** (`ready_tick = last_event_tick + window_ticks`), never as remaining-time counters, so they survive a variable `tick_delta` across a save boundary — which is exactly what makes `RK-12` resolvable as a pure function of `(state, tick)`.

**Phase 2 — the calendar (rich, deferred).**

- **Adds.** Day/night phase, seasons, calendar date and named weekdays, dawn/dusk boundaries, timescale controls (pause / rest / fast-forward with the `config.rest` increments).
- **Why deferred.** `PROTOTYPE.md` defers "day/night cycle, weather, seasons" to the vertical slice, and nothing in Phase 1 depends on the phase of day — only on elapsed time.
- **The interface does not change.** Phase-1 callers keep working; `IsNight()` and calendar queries become answerable rather than returning a default.

**Recorded consequence:** a Phase-1 acceptance criterion that requires a *day/night* cycle (rather than elapsed time) is a Phase-2 criterion. `ROADMAP.md` M4's exit criteria were rewritten for exactly this reason.

### S-05 Character Stats & Attributes

- **Responsibility.** Compute derived character values from a base-plus-modifiers graph.
- **Owns.** For every character: base attribute values, the modifier list, and the cached derived stat block (max Health, max Stamina, max Focus, Resonance, Strain tolerance, armor mitigation, carry capacity, resistances). There is no mana (`PROGRESSION.md` §4.1).
- **Reads.** Equipment (equipped item modifiers), Status Effects (active modifiers), Skills (passive contributions), Species/Race definition, Blessings/world modifiers.
- **Persistent.** Base attributes, allocated attribute points, unspent points.
- **Transient.** Modifier list and derived stat cache; fully recomputed on load and on any dependency event.
- **Events/interfaces.** `GetStat(id, StatKey)`, `AddModifier`, `RemoveModifier(sourceId)`; emits `StatsRecalculated`, `AttributeChanged`. **Never** adds two systems' modifiers ad hoc — all contributions arrive as typed modifiers with a source ID so removal is exact.

### S-06 Health & Resource Pools

- **Responsibility.** Track current/max for Health, Stamina and Focus, accumulated Strain against its tolerance, and any contextual resource a definition declares (charges, essence), and decide depletion outcomes.
- **Owns.** Current values, regeneration accumulators, downed/dead state, death cause record.
- **Reads.** S-05 (maxima), S-04 (time for regen).
- **Persistent.** Current values, regen remainder, alive/dead flag, death cause and time.
- **Transient.** Accumulators, status flags, damage-number bookkeeping.
- **Events/interfaces.** `Spend(id, pool, amount) → bool`, `Restore`; emits `PoolChanged`, `Downed`, `Died`. Death is a state transition here; consequences (corpse, penalties) are decided by S-07's consumer rules, not here.
- **M3e reconciliation.** Focus returns and Strain ebbs after a pause from the last working (`config.magic`); both are on the progression record's pools, saved since schema 4, with Strain held between zero and the tolerance. As with stamina, the pause itself is not saved: a load starts at rest.

### S-07 Death & Recovery

- **Responsibility.** Decide what dying costs, where the character returns, and how remains are recovered.
- **Owns.** Player death penalty state (temporary injuries, XP debt, durability damage, corpse record with instance ID and cell), respawn point selection.
- **Reads.** S-06, S-03 (cell ownership), S-15 (equipment for durability), S-30 (nearest discovered safe point).
- **Persistent.** Corpse records (instance ID, cell, contents snapshot ref, timestamp), active injury/debt debuffs.
- **Transient.** Respawn UI state.
- **Events/interfaces.** `OnDeath(entityId, cause)`, `RecoverCorpse(corpseId)`; emits `PlayerDied`, `PlayerRespawned`, `CorpseRecovered`, `PenaltyApplied`. Penalty severity is a difficulty option, read from settings, never hardcoded (charter: avoid infuriating death).

### S-08 Experience & Level

- **Responsibility.** Award experience and convert thresholds into character levels and attribute points.
- **Owns.** Level and progress within it, unspent attribute points, XP debt, and the `AG-2`/`AG-3` guard state.
- **Reads.** S-12 (kill events), S-29 (quest completion), S-30 (discovery), S-17 (crafting), S-32 (building).
- **Persistent.** Level, progress, unspent attribute points, XP debt, lifetime XP per source kind, the anti-farm guard state, per-source diminishing-returns records for repeatable turn-ins.
- **Transient.** Pending award buffer for the current tick.
- **Events/interfaces.** `AwardXp(characterId, amount, source)`; emits `XpGained`, `LevelGained`, `PointGranted`. Anti-exploit: repeatable sources declare an `xp_award` with a decay key (see `DATA_MODEL.md` quest schema). Levels grant **breadth only** (D-09): a level-up grants attribute points and nothing else — no skill, technique or talent points.

### S-09 Skills & Disciplines

- **Responsibility.** Track per-discipline competence — weapon families, magic domains, crafting, gathering, world and social skills (`PROGRESSION.md` §4.2) — and expose skill checks.
- **Owns.** Skill values and progress; the common ceiling; (from M12) mastery designations.
- **Reads.** S-12 (weapon use events), S-13 (casting events), S-17 (crafting practice), S-19 (gathering), S-22 (world actions).
- **Persistent.** Skill levels and progress-to-next.
- **Transient.** Check modifiers cache.
- **Events/interfaces.** `Practice(characterId, skillId, difficulty, outcome, novelty)` — grants XP only past the difficulty gate; `Check(characterId, skillId, difficulty) → RollResult`; emits `SkillImproved`. Every discipline exists at 0; nothing is "learned" here, and no other system can grant skill. Skills answer *competence* (D-09); specialization is the mastery band.

### S-10 Techniques & Formulas

- **Responsibility.** Own what a character knows how to do — techniques (`ability`), formulas (`spell`) and craft techniques/recipes (`recipe`) — how it was learned, and whether one may be used right now (`PROGRESSION.md` §4.4).
- **Owns.** The knowledge record (known IDs with their learning source), hotbar/binding assignment, per-technique cooldown timers, charge counts.
- **Reads.** S-09 (skill prerequisites), S-06 (resource cost feasibility), S-11 (status gates such as silence/stun), S-05 (scaling inputs).
- **Persistent.** The knowledge record.
- **Transient.** Cooldown timers, charge state, pending-cast state (rebuilt from zero on load; cooldowns do **not** survive except where a definition marks them `persist_cooldown`).
- **Events/interfaces.** `Learn(characterId, definitionId, source)` — the only way knowledge enters, with a typed learning source; `TryUse(abilityId, target)`, `GetCooldown(id)`; emits `TechniqueLearned`, `AbilityUsed`, `AbilityFailed(reason)`. There are no talent points and no level-granted techniques.

### S-11 Status Effects

- **Responsibility.** Own timed/conditional effect instances on characters and the modifier sets they contribute.
- **Owns.** Active effect instances per character (definition ref, caster, stacks, remaining duration in game minutes, tick schedule).
- **Reads.** S-04 (time), S-09/S-05 (resistance and save inputs), S-12 (damage/removal triggers).
- **Persistent.** Active effect instances with remaining duration and stack count, because players save mid-fight.
- **Transient.** Per-tick evaluation cursors, cached aggregated modifier sets.
- **Events/interfaces.** `Apply(effectDefId, targetId, sourceId, stacks, duration)`, `Remove`, `Dispel(category)`; emits `StatusApplied`, `StatusTick`, `StatusExpired`, `StatusBroken`. All modifiers bubble up to S-05 tagged with the effect instance ID.

### S-12 Combat & Damage Resolution

- **Responsibility.** Decide the outcome of an attack: hit or miss, how much damage, of what type, applied where.
- **Owns.** The damage pipeline, hit resolution, critical determination, block/parry/dodge resolution, stagger/poise state, combat-state flags (in-combat timer). There is no threat or aggro table: who an actor fights is that actor's own record, derived from what it perceived, inferred or was told, and owned by S-23 (`STEALTH_DETECTION_AND_THREAT.md` §1, §6).
- **Reads.** S-05 (stats), S-15 (equipped weapon and armor), S-11 (offensive/defensive effects), S-13 (spell damage packets), S-02 (target identity), S-22 (positioning/reach).
- **Persistent.** In-combat timers and poise are transient; only outcomes that mutate other systems' state persist (health via S-06, durability via S-15, death via S-06/S-07).
- **Transient.** Full pipeline working set, RNG stream cursor (seeded, per-cell, so replay is deterministic).
- **Events/interfaces.** `ResolveAttack(AttackRequest) → DamageResult`, `ApplyDamagePacket(packet)`; emits `AttackResolved`, `DamageApplied`, `CriticalHit`, `Blocked`, `Dodged`, `Staggered`, `Killed`, `CombatStarted`, `CombatEnded`. Exactly **one** system computes damage; spells, abilities, traps, and environmental hazards all submit damage packets to this pipeline rather than computing their own.
- **M3c reconciliation** (with `COMBAT_DAMAGE_ARMOR_AND_DEATH.md` and `STEALTH_DETECTION_AND_THREAT.md`). The pipeline is `CombatRules.Resolve` in `Domain.Combat`, run by the runtime `CombatSystem`: attack, contact region (head, torso, limbs), that region's armor, penetration, then damage, stagger and on-hit effect (COMBAT §18). Level is never an input, so no level makes a body immune to a physical blow (§32); reach, the front arc, walls between, and the timing of windup, active window and recovery decide whether a blow lands at all. Effect ticks submit their harm to the same system, the one place health falls and death is noticed. The current pools stay on the progression record, where M2c put them (S-06's persistent current values); S-07's death consequences are XP debt (AG-8), a respawn at the region's spawn and `effect.weakened`. Until S-23 arrives (M3d), a creature knows only what it has felt: it turns on whoever wounds it and gives up past its leash.

### S-13 Magic & Spellcasting

- **Responsibility.** Govern casting: domain rules, Focus and Strain cost, cast time, interruption, and the delivery of a formula's effect payload.
- **Owns.** Domain-skill interactions at cast time (efficiency, stability, Strain), cast-in-progress state, interruption resolution, contextual resource bookkeeping (essence, reagents, charges if a formula declares them), summon lifetime bookkeeping.
- **Reads.** S-09 (domain skill), S-10 (known formulas), S-06 (Focus, Strain, contextual resources), S-11 (silence/confusion), S-12 (to submit damage packets), S-02 (summoned entity creation via registry).
- **Persistent.** Currently active summons (as entity refs), lingering conjured effects that outlive logout.
- **Transient.** Cast bars, pending payloads, channel state.
- **Events/interfaces.** `BeginCast(casterId, spellId, target)`, `Interrupt(reason)`; emits `CastStarted`, `CastCompleted`, `CastInterrupted`, `Summoned`, `SpellResisted`. Domains may differ mechanically (charter §6) but must express differences through definition data + the same closed payload vocabulary, not new domain code per domain. Known formulas stay castable above the caster's skill, at higher Strain and failure risk; there are no attunement slots.
- **M3e reconciliation.** Casting is the player's action, so its cast-in-progress state lives with the rest of the player's combat state (`CombatSystem`, `StateSlice.Combat`), and a working's blow goes through S-12's one pipeline. `CastCommand` spends the formula's Focus and starts its tell (the cast time). At release the working takes its Strain - more above the domain skill, less below it - then fizzles (a chance only above one's skill) or takes hold; past tolerance the excess is paid in health (`StrainBacklash`), never refused. A wound during the tell breaks the cast (`CastInterrupted`, reason `wounded`), and a dodge abandons it (`dodged`); a blow on the guard or a dodged blow does not. Its domain skill learns only from a working that mattered: a bolt that wounded, or a working cast in the thick of a fight. Resonance, not Might, scales a working's force. Phase 1 builds `self` and `projectile` targeting and the `damage`, `apply_effect` and `remove_effect` payloads; the content bible's three formulas, one per domain, are the whole set.

### S-14 Inventory & Containers

- **Responsibility.** Own item location: which item instance is in which container at which slot/stack.
- **Owns.** Container definitions (capacity, slot rules, allowed categories, owner), item-instance placement, stack counts, container access permissions.
- **Reads.** S-02 (item instance identity), S-18 (item definitions and weight/value), S-32 (owned property/container ownership).
- **Persistent.** Container records and their item placements; contents of world containers that changed from baseline.
- **Transient.** UI-side sorted views, cached weight totals.
- **Events/interfaces.** `Transfer(itemId, fromContainer, toContainer, count) → TransferResult`, `Split`, `Merge`, `Discard`; emits `ItemAdded`, `ItemRemoved`, `ItemTransferred`, `TransferRejected(capacity|permission|category)`. This is the **only** sanctioned path for item movement; crafting, loot, merchants, and quests all route through it (mandatory test surface, charter TESTING).
- **M4 reconciliation (merchants).** A trader's wares are a container at the trader, keyed by their merchant profile: their authored stock until the first trade, then a changed container like any other (schema 6's record, so nothing new is saved). Only a trade moves anything into or out of them: `BuyCommand` and `SellCommand` become one internal `Trade` - the stack's move and the coin the other way, all or nothing. What a trader buys joins their wares; their purse is bottomless in Phase 1.

### S-15 Equipment

- **Responsibility.** Own what is equipped in which slot and validate equip legality.
- **Owns.** Per-character equipment slots → item instance IDs, equip requirement checks, set-bonus aggregation.
- **Reads.** S-14 (item must be in an accessible container), S-18 (item/weapon/armor definitions), S-09 (requirement skills), S-05 (attribute requirements).
- **Persistent.** Slot→item ID map per character; condition of equipped items lives on the item instances.
- **Transient.** Aggregated stat/modifier contributions and set-bonus state (recomputed on load).
- **Events/interfaces.** `Equip(characterId, itemId, slot)`, `Unequip(slot)`, `SwapFrom`; emits `ItemEquipped`, `ItemUnequipped`, `SetBonusChanged`. Emits modifier deltas consumed by S-05.

### S-16 Items & Loot Generation

- **Responsibility.** Materialize item instances from definitions and roll loot from tables.
- **Owns.** Item-instance construction (rolls for quality, material, affixes, durability, sockets, seeds), loot roll execution, corpse/container loot population records.
- **Reads.** S-18 (all item-like definitions), S-02 (instance creation), S-12 (`Killed` events), S-31 (dungeon/region loot modifiers), S-30 (discovery bonus), S-04 (rare spawn windows).
- **Persistent.** Generated instances and their rolled properties (these are **not** regenerable), plus the "already looted" flag on the source container/corpse.
- **Transient.** Loot roll working state, RNG stream cursors.
- **Events/interfaces.** `CreateItem(defId, overrides) → InstanceId`, `RollLoot(tableId, context) → [InstanceId]`; emits `ItemCreated`, `LootGenerated`, `RareDrop`. Item generation must be reproducible from `(rngSeed, tableId, context)` so a save reload before looting yields the same result.

### S-17 Crafting

- **Responsibility.** Convert inputs into outputs under skill, station, and knowledge constraints, and decide quality (`PROGRESSION.md` §9).
- **Owns.** Station capability state, craft-in-progress jobs (for timed crafts), experimentation/discovery records.
- **Reads.** S-14 (material availability and consumption), S-18 (recipe/resource/item definitions), S-09 (crafting skill), S-10 (known craft techniques and recipes), S-32 (station buildings), S-04 (craft timers).
- **Persistent.** Discovered experimental results, queued/in-progress jobs with remaining time, station state if modified.
- **Transient.** Craft progress bars, preview computation, available-recipe filtering.
- **Events/interfaces.** `StartCraft(recipeId, context)`, `CancelCraft`, `Experiment`; emits `CraftStarted`, `CraftCompleted(outputItems, quality)`, `CraftFailed(reason)`, `ExperimentDiscovered`. A discovery teaches through S-10's `Learn`; there are no profession ranks. Outputs are created via S-16 and inserted via S-14; crafting never writes inventory slots directly.
- **M3f reconciliation.** Phase 1 builds one instant craft and no jobs, experiments or station state: `CraftCommand` works a known recipe at a station of its kind within reach, from carried materials. The runtime `CraftingSystem` owns no state; it asks the inventory to spend the inputs and receive the output in one all-or-nothing exchange (`ExchangeItems`), so a refused craft spends nothing. The best carried stacks are spent first, the weakest caps the work, and the smith's skill against the recipe's complexity moves the output one quality step at most (`CraftingRules`, `config.crafting`). The quality lands on the made stack. The work trains its skill through the difficulty gate; the first of each output also earns level XP, once (AG-7). The event is `ItemCrafted(recipe, item, count, quality)`.

### S-18 Content Definitions (Item/Weapon/Armor/Resource/Recipe)

- **Responsibility.** Load, validate, index, and serve all *definition* data to systems; never hold instance state.
- **Owns.** The compiled definition registry (ID → immutable record), the load-time validation report, the definition deprecation/alias map, the content version stamp.
- **Reads.** YAML files under `content/` (D-03).
- **Persistent.** Nothing at runtime; the content **version** is recorded in the save manifest.
- **Transient.** Compiled in-memory tables plus the optional binary parse cache (D-03).
- **Events/interfaces.** `Get<T>(defId)`, `TryGet`, `Query(kind, tag)`, `Validate() → ValidationReport`; emits `ContentReloaded` (developer builds only). A definition ID that does not resolve is a **load-time error with file and line**, never a null reference mid-quest (D-03).

### S-19 Resources, Harvesting & Respawn

- **Responsibility.** Own harvestable resource nodes and their depletion/respawn lifecycle.
- **Owns.** Node instances (resource definition ref, remaining yield, regrowth timer), depletion records, harvest skill-check results.
- **Reads.** S-04 (regrowth timing), S-09 (gathering skill), S-18 (resource definitions), S-03 (cell baseline for procedural placement).
- **Persistent.** Only nodes that **differ from baseline**: depleted nodes with their regrowth deadline, and player-planted/gardened nodes.
- **Transient.** Node → visual/entity binding, harvest progress.
- **Events/interfaces.** `Harvest(nodeId, toolId)`, `Plant`; emits `ResourceHarvested`, `NodeDepleted`, `NodeRespawned`. Respawn is computed from the world clock, so it is correct after a long absence without simulating anything.
- **M3f reconciliation.** The runtime `GatheringSystem` owns the node records (`StateSlice.Nodes`). Phase 1's nodes are authored in the region and are part of their cells' baselines; a node's record is its last harvest tick and its harvest count, and whether it is ready is derived from them and the world clock. `GatherCommand` harvests a node within reach: the yield is rolled from the node and its harvest (not the gatherer), a skill passive adds to it (survival's +1 at 3), the item goes to the pack through the same exchange crafting uses, and the harvest trains its skill through the difficulty gate. There are no tools, planting or harvest progress. The event is `NodeGathered(node, item, count, spent)`.

### S-20 World Persistence & Cell Delta Store

- **Responsibility.** Own the deterministic world baseline and the sparse delta that replaces it on load (D-05).
- **Owns.** Baseline generator interface, per-cell delta records, cell dirty flags, the mutation journal that decides what enters a delta, migration application.
- **Reads.** S-02 (entity records), S-19, S-32 (buildings), S-24 (NPC life state), S-14 (changed world containers), S-31 (spawn population state — S-31 owns baseline populations and budgets), S-34 (dungeon state).
- **Persistent.** Everything it owns, by definition: cell deltas, entity overrides, tombstones, migration results.
- **Transient.** Loaded cell baselines, dirty-cell set, in-memory delta staging.
- **Events/interfaces.** `MarkDirty(entityId | cellId, reason)`, `GetDelta(cellId)`, `ApplyDelta`, `GenerateBaseline(worldSeed, cell)` on a generator whose contract (`worldgen_version`, `rng_contract_version`, placement data) is fixed and fingerprinted; emits `CellDirtied`, `BaselineRegenerated`, `DeltaApplied`. **Hard invariant:** baseline generation is deterministic, every saved changed-cell delta records the `baseline_hash` of the baseline it was made against, and a delta is applied only to a baseline with that hash or through a registered transition - never silent regeneration (D-05, `RK-01`, `PERSISTENCE.md` §6.4). **Superseded (M2b):** an earlier revision widened the signature to `(worldSeed, worldgenVersion, contentHash)`. It was right that `(seed, content_version)` alone was too weak, but feeding the whole content hash into generation made every content edit move every cell. The generator contract replaced the version as an input, the per-cell hash replaced the content hash as the proof, and content identity is no longer an input.

### S-21 World Streaming

- **Responsibility.** Decide which world cells and interiors are loaded, at which fidelity, and promote/demote simulation tiers.
- **Owns.** Loaded-cell set, residency rings, tier assignment per entity, streaming budget and hysteresis, interior/dungeon instance lifecycle.
- **Reads.** S-22 (player position), S-20 (cell baseline + delta), S-31 (spawn populations), S-23 (AI tier demand), and the streaming budget settings defined in `WORLD_ARCHITECTURE.md` §12 (configuration, not a system).
- **Persistent.** Nothing operationally (the tier of an entity is derived from player position at load); persisted only indirectly via S-20 deltas.
- **Transient.** All of it: residency, LOD state, pooled object bindings, tier table.
- **Events/interfaces.** `RequestCell(cellId)`, `ReleaseCell`, `SetTier(entityId, tier)`, `ResidencyOf(cellId)`; emits `CellLoaded`, `CellUnloaded`, `EntityPromoted`, `EntityDemoted`. Tier promotion must **reconcile** an entity to a legal state, never naively adopt abstract state (D-06).

### S-22 Player Character & Interaction

- **Responsibility.** Bind player intent to commands and own the player's persistent identity record.
- **Owns.** Player entity ID, character identity (name, species, appearance seed, archetype), input mapping to command bindings, interaction targeting for the focused world object.
- **Reads.** S-03, S-02, S-14 (carried items for contextual actions), S-28 (dialogue availability), S-32 (build mode), S-34 (dungeon entry).
- **Persistent.** Name, species, appearance seed, starting archetype, unlocked input bindings, journal/known-facts set, per-character settings that affect rules (difficulty flags).
- **Transient.** Interaction focus target, input state, camera state (presentation).
- **Events/interfaces.** `FocusInteractable(id)`, `Interact(targetId)`, `Move(direction)`; emits `InteractionOffered`, `InteractionPerformed`, `PlayerMoved`. Presentation submits these as commands; it never moves the player by writing position (D-11).
- **M6 reconciliation.** Phase 1's interactables are doors and switches, both named by an `InteractCommand` and measured from the body to the thing's footprint. A switch sets its world flag in its cell once (`SwitchSet`), and only while the flags it requires are set there; a set switch refuses again, and a switch that is not ready refuses with its own words. A barrier is a footprint that blocks movement, blows and sight like a closed door until a switch sets its flag. The Foldscar's stones and heart are switches, and the fold that holds Tavar is a barrier.

### S-23 Combat AI (behavior)

- **Responsibility.** Choose what a hostile/neutral actor does each decision interval, given perception and its role.
- **Owns.** Behavior profile selection, perception records (seen/heard targets with timestamps), decision timers, ability selection policy, morale/flee state, group-coordination role, patrol/leash state.
- **Reads.** S-05, S-12 (in-combat state and the blows an actor has taken), S-21 (tier — only tier A/B actors run full decisions), S-22 (movement target), S-27 (NPC faction and disposition), S-04 (schedule phase for behavior mode).
- **Persistent.** Only durable divergence: morale-broken state for a named NPC, a target an actor still hunts after a cell unload (its own perception record, never a shared table), and any scripted behavior flags.
- **Transient.** Perception, decision timers, path requests, role assignment. Rebuilt at load; an actor with no transient AI state behaves as its definition and tier dictate.
- **Events/interfaces.** `SetBehaviorProfile(entityId, profileId)`, `SetAlarmed(entityId, bool)`, `ForceTarget(entityId, targetId)`; emits `TargetAcquired`, `TargetLost`, `Fleeing`, `CalledForHelp`, `BehaviorChanged`. Tier C/D actors **do not** run this system; they run S-27's abstract model (D-06).
- **M3d reconciliation** (with `STEALTH_DETECTION_AND_THREAT.md`). The runtime `CreatureSystem` owns every creature. Its perception record is its own: awareness from 0 to 100 built by sight (faster close), a heard noise's place, what it knows of its target and where, when it last saw it, and a search deadline. A mind (unaware, suspicious, engaged, searching, returning, fleeing) replaces a behavior profile. What a creature does at rest and how far it goes is its **role**, a data layer in `config.creature_behaviour` over the creature **archetype** (its body and blows); a spawner assigns a role per member. The only information passed between creatures is a call: a howl reaches its own kind, and only roles that answer calls come. The mind is saved (schema 8), so a hunt survives a reload; an attack mid-windup is transient. Only tier-A creatures decide; tier transitions are M4's (owner ruling). Events: `CreatureNoticed` (every change of mind), `CreatureCalled`, `CreatureStunned`.

### S-24 NPCs

- **Responsibility.** Own the identity, role, and life-state of non-player characters.
- **Owns.** NPC identity (definition ref, instance ID, generated name/voice/appearance), role (merchant, guard, craftsperson, quest giver, farmer), schedule assignment and current schedule phase, home/work anchors, disposition toward factions and toward the player, life-state (alive/dead/moved/fled), greetings and service availability.
- **Reads.** S-04 (schedule phase), S-27 (faction, reputation), S-21 (tier), S-28 (dialogue), S-26 (relationship values), S-30 (discovery records — S-24 owns its *own* alive/dead state, so it does not read a "killed" set from elsewhere).
- **Persistent.** Identity, role, schedule assignment, current schedule phase and coarse position if diverged, disposition, alive/dead state and cause, anchor reassignment after building changes.
- **Transient.** Path state, animation state, interaction cooldowns, nearby-list caches.
- **Events/interfaces.** `SetSchedulePhase`, `Kill(id, cause)`, `Relocate(id, anchorId)`, `SetDisposition`; emits `NpcSpawned`, `NpcDied`, `NpcPhaseChanged`, `NpcRelocated`, `NpcServiceChanged`, `NpcGreeting`. Important NPCs retain identity, relationships, and history; generic NPCs use cheap archetypes (PHASE_0 STEP 10).
- **M4 reconciliation.** Phase 1's population is the content bible's three named NPCs, all simulated in full all the time (owner ruling: no tiers, no schedules until they are introduced). The runtime `NpcSystem` owns their bodies (`StateSlice.Npcs`): each stands where the region places them and turns to face whoever talks to them. Their instance IDs are derived from their definitions (`EntityKind.Npc`), so nothing about them needs saving while they are non-combatants who take no harm (`PROTOTYPE.md` §5); their bodies block movement. Life state, relocation and death arrive with the first system that can change them (the companion, M6).

### S-25 Companions & Hirelings

- **Responsibility.** Own recruited followers: their presence, orders, loyalty, and their own progression.
- **Owns.** Roster (entity ID, role, contract type: hireling vs. companion), follow/party state, active order set, companion equipment, companion XP/level/skills, personal-quest state, morale, dismissal and re-hire records, wages owed.
- **Reads.** S-24 (NPC identity), S-23 (combat behavior), S-09/S-10 (their abilities), S-15 (their equipment), S-32 (housing assignment), S-04 (wages, schedules), S-03 (player location for follow).
- **Persistent.** Full roster with all of the above; hireling wage state; housing/settlement assignment.
- **Transient.** Path following state, formation slot, order-execution stack, combat micro-state.
- **Events/interfaces.** `Recruit(npcId, contract)`, `Dismiss`, `IssueOrder(order)`, `AssignToAnchor(anchorId)`, `PayWages`; emits `CompanionRecruited`, `CompanionDismissed`, `OrderChanged`, `CompanionDowned`, `MoraleChanged`, `WagesDue`. Companion AI must be *reliable* (charter §15): orders are validated against legality before acceptance, and an unexecutable order returns a reason rather than silently failing.

### S-26 Relationships & Memory

- **Responsibility.** Track what individual NPCs think of the player and of each other, and why.
- **Owns.** Per-pair relationship values on named dimensions (trust, affection, fear, respect, grudge), the memory log of attributed events (event key, actor, timestamp, weight), and derived disposition tiers.
- **Reads.** S-24 (NPC identity), S-28 (dialogue choices), S-12 (who the player attacked), S-25 (companion interactions), S-14/S-36 (gift and trade events — an item given or sold to an NPC is what moves affinity; there is no separate `gift` system), S-29 (quest outcomes).
- **Persistent.** Relationship values and the bounded memory log (cap per pair; eviction is oldest-lowest-weight first and must be deterministic — save-version sensitive).
- **Transient.** Aggregated disposition cache, dialogue-condition evaluation cache.
- **Events/interfaces.** `RecordEvent(subjectId, objectId, eventKey, weight, sourceRef)`, `GetRelationship(a, b, dimension)`, `GetDispositionTier(a, b)`; emits `RelationshipChanged`, `MemoryRecorded`, `DispositionTierChanged`. There is **no** single good/evil meter (charter §20); every consequence is attributed per observer.
- **M4 reconciliation.** The runtime `RelationshipSystem` owns what each NPC thinks of the player (`StateSlice.Relationships`), saved with the player (schema 10): a value per named dimension (trust, respect, affection, fear, grudge) in [-100, 100], zero not stored. A change comes as an internal command naming its reason and is published as `RelationshipChanged(npc, dimension, from, to, event)`. Phase 1 keeps no memory log and no NPC-to-NPC values; the event names are the attribution until the log arrives.

### S-27 Factions, Reputation & Offense

- **Responsibility.** Own faction definitions in play and the player's standing with each, plus the legal/offense state derived from it.
- **Owns.** Faction membership graph, per-faction player reputation value and tier, cross-faction attitude relations, crime records (offense type, location, witnesses, bounty), and pardon/expiry state.
- **Reads.** S-26 (individual memories), S-24 (faction of each witness), S-22 (player actions), S-28 (dialogue consequences), S-12 (assault/kill events).
- **Persistent.** Reputation values and tiers, faction-state flags (e.g. at-war, alliance broken), crime records and bounties, pardons.
- **Transient.** Witness-propagation working set, service-availability cache, guard-alert state per settlement.
- **Events/interfaces.** `AddReputation(factionId, delta, reason)`, `ReportCrime(offense)`, `Pardon`, `SetFactionState`; emits `ReputationChanged`, `StandingTierChanged`, `FactionStateChanged`, `BountyPlaced`, `BountyCleared`. Reputation answers *access* (D-09) — it gates services, dialogue, and territory, and is never converted into character power directly. **It never decides who attacks:** tactical hostility and attack legality are derived from faction relation, war state, legal status, identity knowledge and perception, not from a standing tier (`PROGRESSION.md` §10).

### S-28 Dialogue

- **Responsibility.** Execute a dialogue graph against world state and produce the resulting commands.
- **Owns.** Active dialogue session (participants, current node, visited-node set), condition evaluation, choice availability, and the mapping from chosen line to consequences.
- **Reads.** S-18 (dialogue definitions), S-24 (speaker identity/role), S-26 (relationship conditions), S-27 (reputation conditions), S-10 (skill-gated lines), S-14 (item-gated lines), S-22 (known-facts set — owned by S-22, not S-30, which owns *locations*), S-29 (quest state conditions).
- **Persistent.** Visited-node sets that gate one-time content, and per-NPC conversation state that must not reset on load (e.g. "already told you about the ruin").
- **Transient.** The live session, resolved text, choice list, camera/audio state (presentation).
- **Events/interfaces.** `BeginDialogue(npcId)`, `Choose(choiceId)`, `EndDialogue`; emits `DialogueStarted`, `DialogueNodeEntered`, `ChoiceOffered`, `ChoiceSelected`, `DialogueEnded`, plus whatever consequence commands the node specifies. Dialogue **never** mutates state directly; it emits commands (e.g. `AddReputation`, `StartQuest`, `Transfer`). No visual scripting language (D-07 spirit).
- **M4 reconciliation.** `TalkCommand`, `ChooseCommand` and `LeaveCommand`; events `ConversationStarted`, `ConversationLine`, `ReplyChosen`, `ConversationEnded` and `ServiceOpened`. The runtime `DialogueSystem` owns the lines each conversation has shown the player (`StateSlice.Conversations`, saved with the player) and the open conversation (transient). Entering a node marks it heard; a `once` node that was heard passes on to its `next_if_exhausted`. Replies are offered while their conditions hold (Phase 1 builds `visited`, `world_state`, `has_item`, `relationship`, `skill`, `level`), and a reply's consequences go to their owners as commands (`transfer_item` to the inventory - first, all or nothing, one a reply - `give_recipe` to progression as a teacher's lesson, `set_world_flag` in the speaker's cell, `record_relationship_event`, `open_service`). A conversation ends when the character walks away or dies. Conversations are deterministic and structured: no runtime language model. **M5:** a reply may start a quest (`start_quest`, a command to S-29; one already started is not started again) and be offered by a quest's state (`quest_state`: a quest `not_started`, `active`, `completed` or `failed`, or one of its objectives `not_reached`, `active`, `satisfied`, `failed` or `closed`). **M6:** a `visited` condition may ask about a line of another conversation (`dialogue_ref`).

### S-29 Quests

- **Responsibility.** Own quest instances, evaluate their objective predicates against world state, and decide completion/failure/branching.
- **Owns.** Quest instances (definition ref, state, objective progress, chosen branches, deadlines, offered/declined/accepted history), the objective predicate evaluator, watcher counters, reward granting, cooldown/repeat policy.
- **Reads.** S-03 (world flags, counters), S-14 (item possession), S-17 (craft events), S-32 (building), S-27 (reputation), S-26 (relationships), S-24 (NPC life-state), S-04 (time), S-30 (discovery records), S-12 (kills), S-25 (companions).
- **Persistent.** Quest instance state including per-objective progress; hidden-objective progress; deadlines as absolute game time; branch selections; failure records; the accepted/declined ledger.
- **Transient.** Evaluating predicates, watcher counters for event-derived predicates (rebuilt from world state at load; anything not derivable from world state is promoted into persisted objective progress instead).
- **Events/interfaces.** `Offer`, `Accept`, `Decline`, `Abandon`, `ReportEvent(eventKey, payload)`, `EvaluateObjective`, `SetObjectiveState`; emits `QuestOffered`, `QuestAccepted`, `QuestDeclined`, `QuestAbandoned`, `ObjectiveProgressed`, `ObjectiveCompleted`, `QuestCompleted`, `QuestFailed`, `QuestBranchTaken`, `RewardGranted`. Objectives are declarative predicates over a **closed type set** (D-07); adding a quest is content, not engine code. A **quest debugger** answering "what is this quest waiting on right now" is a Phase-1 requirement (D-07), served by `EvaluateObjective` returning its blocking predicate trace.
- **M5 reconciliation.** The runtime `QuestSystem` owns the player's quests (`StateSlice.Quests`, saved with the player, schema 11): each started quest's status, when it started and ended and what ended it, and per objective its status (`active`, `satisfied`, `failed` - a timer ran out - or `closed` - a branch not taken, or still open when the quest ended), when it became active and ended, and its progress. The rules are pure functions (`QuestRules`): a quest starts at its entry objective; every tick after everything else has moved, active objectives are evaluated in authored order, and one that holds is satisfied and activates what follows - so a run of objectives the world already satisfies completes in one tick, which is how a place visited or an item got before the quest counts; a join waits for all its prerequisites; a timed objective fails at its deadline unless it holds on that tick; `fail_if` ends a quest still active; a satisfied objective with nowhere to go completes it; satisfied, failed and closed objectives never change again. State predicates read the world as it stands (heard lines, discovered places, distances, the pack, world flags in the cell of a named place, relationships); deed predicates (`craft_item`, `harvest_resource`, `kill_creature`, `deliver_item`) count deeds the crafting, gathering, creature and dialogue systems report (`RecordDeed`) while the objective is active - that count is the persisted progress. `StartQuest` comes from dialogue. Rewards (`xp`, `currency`, `item` - into the pack, or at the character's feet if it cannot hold it - `recipe`, `spell`, `relationship`, `world_flag`) are commands to their owners, granted once. Events: `QuestStarted`, `ObjectiveActivated`, `ObjectiveSatisfied`, `ObjectiveFailed`, `ObjectiveClosed`, `QuestBranchTaken`, `QuestCompleted`, `QuestFailed`, `RewardGranted`. The debugger is `Simulation.Diagnose(questId)`: the one-line answer, every active objective's terms with their current values and wanted values, what in this world would satisfy each (a node and whether it is worked out, a recipe and whether it is known and its inputs carried, a trader's stock, a container, a creature's drops, a reply and each condition stopping it), problems that mean it cannot complete as things stand (nothing can supply an item, a join that can never activate, a stall), and a run-length trace of its last 64 distinct evaluations (transient). Offer/accept/decline/abandon and repeat policy are not built: a conversation starting a quest is the offer and its acceptance.

### S-30 Exploration & Discovery

- **Responsibility.** Own what the player has discovered, how the map is revealed, and what discovery awards.
- **Owns.** Discovered-location set (definition ref, discovery time, discovery method: sighted/visited/told/purchased/magical), map-knowledge resolution per region, landmark and point-of-interest records, discovery XP and lore awards, cartography skill interactions.
- **Reads.** S-22 (position), S-09 (cartography/perception), S-04 (time), S-28 (NPC-given knowledge), S-34 (dungeon entrances), S-27 (faction-gated regions), S-14 (purchased maps as items).
- **Persistent.** Discovered set with method and timestamp, purchased/told knowledge records, revealed map regions, one-time discovery award flags.
- **Transient.** Proximity sampling, visibility tests, map-UI render cache.
- **Events/interfaces.** `Discover(locationDefId, method)`, `GrantMapKnowledge(regionId, level)`, `IsDiscovered(id)`; emits `LocationDiscovered`, `MapRegionRevealed`, `SecretFound`, `HiddenObjectiveRevealed`. Discovery deliberately does **not** place a quest marker (charter §3); it records knowledge and lets S-28/S-29 conditions read it.

### S-31 Spawning & Population Management

- **Responsibility.** Decide which creatures/NPCs/resources exist at a place and time, and enforce population budgets.
- **Owns.** Spawn populations per cell/zone, spawn points and their respawn timers, global and regional population caps, spawn-tier assignment, rare/named/wandering spawn windows, despawn policy, encounter composition rules.
- **Reads.** S-20 (baseline populations), S-21 (residency and tier), S-04 (time windows), S-18 (definitions), S-12 (kill records for population accounting), S-27 (faction control of a region).
- **Persistent.** Kill records with respawn deadlines, despawned-rare-spawn windows, permanently removed unique spawns, altered faction control affecting spawn tables.
- **Transient.** Active spawn budget accounting, pending spawn queue, timer cursors.
- **Events/interfaces.** `PopulateCell(cellId)`, `RequestSpawn(pointId)`, `Despawn(entityId)`, `SetPopulationOverride`; emits `EntitySpawned`, `EntityDespawned`, `RareSpawnWindowOpened`, `PopulationBudgetExceeded`. No global level scaling: spawn tables are authored per zone with level bands (charter §1), and the player's level is never an input to spawn selection.
- **M3d reconciliation.** In Phase 1 the spawners are the `CreatureSystem`'s: each member is placed from rolls keyed by its spawner and index, identified by `spawner#member` and a generation. A death stores its respawn deadline as an absolute tick (the window, doubled while AG-3 has the cluster saturated, which emits `SpawnerSaturated`). The creature returns once the deadline has passed and the player is past the leash from its home, as the next generation with a new ULID (`CreatureRespawned`). A corpse is the dead creature's container until emptied (`CorpseGone`) or decayed. Population caps, habitat selection and rare windows are later.

### S-32 Buildings & Construction

- **Responsibility.** Own player-placed structures, ownership, and per-piece condition.
- **Owns.** Building instance records (piece definition ref, socket graph position/rotation, owner, cell), per-piece health and damage state, repair state, stations and storage attached to a building, NPC/companion assignment to anchors, garden/farm plots, defense structures and their ammunition/cooldown state, settlement-level aggregates (population capacity, services offered).
- **Reads.** S-18 (piece definitions and socket compatibility), S-14 (materials to consume and storage contents), S-20 (cell delta), S-09/S-10 (masonry skill and known construction techniques), S-29 (construction objectives), S-25 (assignees), S-12 (raid damage events), S-24 (NPC housing).
- **Persistent.** Every building instance (these are pure player-authored state and are never regenerable), piece damage, assignments, container contents via S-14, farm plot crops and growth deadlines, defense state.
- **Transient.** Ghost/preview placement, snap candidates, navmesh dirty regions, construction progress.
- **Events/interfaces.** `PlacePiece(pieceDefId, socketId, rotation)`, `RemovePiece`, `DamagePiece`, `RepairPiece`, `AssignOccupant`, `IsValidPlacement`; emits `PiecePlaced`, `PieceRemoved`, `PieceDamaged`, `PieceDestroyed`, `BuildingCompleted`, `SettlementStateChanged`, `HomeAttacked`. **No structural simulation** (D-08): snapping guarantees navigability and persistence; "destruction" is per-piece health applied by explicit rules. Attack frequency is a player-facing option and defaults low (charter §14).

### S-33 Save / Load

- **Responsibility.** Serialize and restore the world per D-05, including versioning, migration, and atomicity.
- **Owns.** Save manifest (`PERSISTENCE.md` §4.2), the serialization walk over persistent system state, the migration chain and historical fixtures, the definition-ID pass, baseline proof, atomic write (staging + flush + rename + verify) and rolling backup slots, autosave policy.
- **Reads.** Every system's declared persistent state, S-18 (content version), S-20 (world delta).
- **Persistent.** Its own artifacts: manifest, slot index, backup slots, autosave rotation.
- **Transient.** Serialization buffers, migration scratch state, progress reporting.
- **Events/interfaces.** `Save(slot, kind)`, `Load(slot)`, `Migrate(slot)` (committed through the same atomic write), `PlanMigration(slot)` (the non-mutating dry run behind `save:migrate --dry-run`), `VerifyIntegrity`; emits `SaveStarted`, `SaveCompleted`, `SaveFailed`, `LoadStarted`, `LoadCompleted`, `MigrationApplied`, `SaveCorrupted`.

  **The load sequence is NOT specified here.** It is owned by `PERSISTENCE.md` §7.4 and implemented as a **gated state machine**, so an out-of-order call is a state error rather than a silent misbehaviour. This file previously published its own inline sequence (`validate manifest → validate content version → apply migrations → build registry baseline → apply world delta → deserialize player/companion state → emit WorldLoaded`) which **omitted alias/tombstone resolution and derived-cache recomputation** and therefore contradicted the authority it deferred to. That inline sequence is **superseded**; implement §7.4, which is additionally ordered so that the schema chain runs first, definition IDs resolve *before* baseline proof and validation, and derived caches recompute *last*.

  **Partial load vs. backup fallback.** The previous text said "corrupt saves fall back to a backup slot rather than partially loading". That is now precise: a **`save_format` mismatch refuses** (a container layout cannot be guessed) and the backup slot is the remedy; a **corrupt `cells`/`entities`/`buildings` section loads without it**, recording `flags.quarantined_sections` and stating what was lost, because partial recovery beats none. Both paths exist and are distinct (`PERSISTENCE.md` §7.2).

  The sequencing invariant is `PERSISTENCE.md` `I-6`, asserted by `T-27` (order) and `T-28` (command-log replay).

### S-34 Dungeons & Interiors

- **Responsibility.** Own the lifecycle and gating of interior spaces treated as their own cells.
- **Owns.** Interior instance state (entrance used, doors/keys opened, shortcuts unlocked, boss defeated, puzzle completion, one-time loot taken, exploration percentage), difficulty band metadata, multi-entrance topology records, reset policy (dungeons reset on a schedule or never, per definition).
- **Reads.** S-20 (interior baseline + delta), S-21 (streaming), S-31 (populations), S-29 (objectives), S-12 (boss defeat events), S-32 (player structures inside interiors).
- **Persistent.** Per-interior progress flags, doors/keys, boss defeat records, taken-loot flags, reset deadline.
- **Transient.** Loaded interior geometry binding, ambient state, encounter pacing state.
- **Events/interfaces.** `EnterInterior(interiorId, entranceId)`, `ExitInterior`, `MarkProgress(flag)`, `ResetInterior`; emits `InteriorEntered`, `InteriorExited`, `DoorUnlocked`, `ShortcutOpened`, `PuzzleSolved`, `BossEncounterStarted`, `BossDefeated`, `InteriorReset`. Dungeons are places with identity, not generated corridors (charter §17).

### S-35 Bosses

- **Responsibility.** Own encounter-level state for bosses and named/unique foes: phases, triggers, and rewards.
- **Owns.** Active boss encounter record (boss instance, current phase, phase transition thresholds, arena state, add-spawn schedule, enrage/deadline timer, lockout state), attempt counters, guaranteed reward assignment.
- **Reads.** S-12 (damage and health), S-06 (health thresholds), S-11 (phase-granted effects), S-31 (add spawning), S-16 (reward item creation), S-29 (objective reporting), S-10 (boss ability selection via S-23's profile).
- **Persistent.** Boss defeat records and lockouts, phase-relevant world flags, guaranteed-reward claim records. In-progress encounter state is **not** persisted — a boss resets on load (this is deliberate and must be stated in the UI).
- **Transient.** Phase timers, add schedules, arena state, threat overrides.
- **Events/interfaces.** `BeginEncounter(bossId)`, `AdvancePhase`, `EndEncounter(outcome)`; emits `BossEncounterStarted`, `BossPhaseChanged`, `BossAddSpawned`, `BossDefeated`, `BossWiped`, `LockoutSet`. Boss power comes from mechanics and phases, not HP inflation (charter §7).

### S-36 Merchants, Trade & Prices

- **Responsibility.** Decide what a merchant will buy/sell and at what price, and own money movement.
- **Owns.** Merchant inventory state (stock, restock deadlines, gold/reserve), price computation inputs (base value, condition, local supply/demand modifier, faction and reputation modifier, trade-skill modifier), transaction records used for anti-exploit limits.
- **Reads.** S-18 (definitions and values), S-14 (transfer), S-27 (reputation), S-09 (trade skill), S-32 (settlement market state), S-04 (restock timing), S-31 (regional supply from spawn/population).
- **Persistent.** Merchant stock and reserve, restock deadlines, per-merchant transaction history for the diminishing-returns rule, player currency, price-modifier world flags (famine, war, boom).
- **Transient.** Computed price quotes, UI listing cache.
- **Events/interfaces.** `Buy(merchantId, itemId, count)`, `Sell`, `Repair(itemId)`, `Restock`; emits `TransactionCompleted`, `TransactionRejected(reason)`, `PriceModifierChanged`, `MerchantStockChanged`, `MerchantGoldChanged`. Exploit controls are explicit and testable: buy price > sell price for the same item in the same market, sell-price decay against per-merchant purchase history, and repair costs derived from durability delta (charter §19: prevent trivial infinite-money exploits).

### S-37 Time & Weather

- **Responsibility.** Own weather state and how weather alters gameplay conditions.
- **Owns.** Weather state machine per region (current pattern, intensity, transition timeline, forecast chain), temperature/fog/wind values, and the gameplay modifiers weather contributes (visibility, ranged accuracy, fire magic, travel speed, creature spawn biases).
- **Reads.** S-04 (time and season), S-31 (spawn biases), S-18 (region definitions), S-03 (world flags for scripted weather).
- **Persistent.** Current weather state per region **and the RNG stream position** so weather does not reroll on reload; scripted/quest-locked weather flags.
- **Transient.** Particle/audio presentation state, transition interpolation.
- **Events/interfaces.** `SetWeather(regionId, patternId, intensity, duration)`, `ForcePattern`; emits `WeatherChanged`, `WeatherSeverityChanged`, `WeatherModifiersChanged`. Weather contributes modifiers only through the same typed modifier path as S-05/S-11 — it does not reach into combat directly.

### S-38 Fast Travel & Travel Networks

- **Responsibility.** Own travel nodes, their discovery/unlock state, and executing travel.
- **Owns.** Network nodes (teleport stones, mage portals, caravan routes, boat routes, mount-friendly roads, settlement network links), unlock records per node and per link, costs (gold, item, spell, time), and player-created anchors (their instance IDs, anchor cell, and expiry).
- **Reads.** S-30 (discovery), S-27 (faction access), S-28 (dialogue-obtained access), S-10 (known recall/teleport formulas), S-04 (travel time elapsed), S-21 (destination must be streamable), S-32 (settlement network membership).
- **Persistent.** Unlocked nodes and links, discovered-but-locked nodes, player-created anchors, paid-access records, network membership changes.
- **Transient.** Route computation, travel cinematic/progress state, destination validation.
- **Events/interfaces.** `UnlockNode(nodeId, method)`, `Travel(fromNodeId, toNodeId)`, `CreateAnchor`, `DestroyAnchor`; emits `TravelNodeUnlocked`, `TravelStarted`, `TravelCompleted`, `TravelRejected(reason)`, `AnchorCreated`, `AnchorExpired`. Fast travel is **earned** (charter §3); unlock methods are discovery, purchase, reputation, spell, or construction, and each is recorded so UI can explain how a node was earned.

---

## 3. Phase mapping

| Phase | Systems |
|---|---|
| **Phase 1 (playable prototype)** | S-01 Bus, S-02 Registry, S-03 World Store, **S-04 Time — clock half only**, S-05 Stats, S-06 Pools, S-07 Death, S-08 XP/Level, S-09 Skills, S-10 Techniques, S-11 Status Effects, S-12 Combat, S-13 Magic, S-14 Inventory, S-15 Equipment, S-16 Items/Loot, S-17 Crafting (two recipes), S-18 Definitions, S-19 Resources, S-20 Persistence/cell deltas, S-21 Streaming (single region), S-22 Player, S-23 Combat AI, S-24 NPCs (small count), S-25 Companions (one), S-28 Dialogue, S-29 Quests (one chain), S-31 Spawning, S-33 Save/Load |
| **Phase 2 (vertical slice)** | **S-04 Time — calendar half**, S-26 Relationships, S-27 Factions/Reputation, S-30 Exploration/Discovery, S-32 Buildings, S-34 Dungeons, S-35 Bosses, S-36 Merchants/Economy, S-37 Weather, S-38 Fast Travel |
| **Deferred (post-slice, unscheduled)** | Mounts, farming/crop simulation beyond garden plots, settlement population growth simulation, creature taming, crime/trial/court systems beyond bounties, regional economic simulation beyond merchant modifier flags, cartography as a player-facing map editor, ship/boat travel, post-cap mastery and Great Works (`ENDGAME_MASTERY_LEGACY_AND_GREAT_WORKS.md`), world-event director, LAN/WAN co-op and any networking code (D-12) |

Phase 1 exists to prove the loop: move → fight → loot → equip → progress → craft → talk → quest → save. Do not build a Phase 2 system to "get it out of the way".

## 4. Explicit anti-patterns

| Rejected | Why |
|---|---|
| A universal `GameManager` / service locator holding gameplay state | Forbidden by the charter; D-10 exists to prevent it. Use the composition root for wiring only. |
| Systems reading each other's internal state directly | Breaks determinism and replay; read the World State Store or subscribe to events. |
| Presentation writing position, health, or inventory | Violates D-11; prediction is for feel only. |
| A second damage calculation inside spells, traps, or DoTs | All damage flows through S-12's pipeline as packets. |
| Items created outside S-16, or moved outside S-14 | Breaks instance identity (D-04) and the mandatory transfer tests. |
| Per-quest, per-spell, or per-item gameplay code branches | Violates D-03 and D-07; if a new quest needs engine code, the objective type set is wrong. |
| Wall-clock or frame-delta reads inside domain systems | Breaks D-05 baseline determinism. Use S-04 and seeded RNG only. |
| Global creature level scaling | Contradicts charter §1; spawn tables are authored with fixed bands. |
| Persisting live AI, pathing, or animation state | Rebuilt at load; persisting it creates load-order bugs and save bloat. |
| Any structural-integrity or physics-collapse simulation for buildings | D-08. |

## Assumptions

1. **Superseded — all sibling Phase 0 documents now exist.** This assumption was written while the document set was still being produced in parallel and originally read "`PERSISTENCE.md`, `WORLD_ARCHITECTURE.md`, and `PROGRESSION.md` are not in the repository yet". All twelve deliverables plus `INDEX.md` are now present in `docs/`. The minimum contracts stated here for `D-05`/`D-06` have been reconciled against `PERSISTENCE.md`, `WORLD_ARCHITECTURE.md`, and `PROGRESSION.md`; where those documents give more detail, **they govern**, and no contradiction is intended. Readers should treat the detail in those three documents as authoritative over the summaries here.
2. **Simulation tier definitions** are taken from D-06 and mapped here as: Tier A = full simulation with S-23 AI; Tier B = simplified regional (schedule + coarse position, no combat); Tier C = abstract schedule/economy only; Tier D = stored state, no ticking. PHASE_0 STEP 10's three named tiers map onto A/B/C with D as the "merely stores state" case.
3. **Fixed 20 Hz domain tick** is an assumption, not a decision. It is a one-line constant and is not architecturally load-bearing; if profiling in the vertical slice favors a different rate, only `D-05`'s determinism requirement constrains the change.
4. **Definition-file hot reload is developer-build only** (D-03 allows it); shipped builds use the compiled cache and never reload content mid-session, because S-20's baseline determinism depends on a frozen content version.
5. **In-progress boss encounters and cooldowns do not survive load** except where a definition opts in with `persist_cooldown`. This is a design choice within D-05's scope, flagged here because it is player-visible.
6. **Assumption recorded per instruction, not a disagreement:** D-06's tier-transition risk ("NPC teleporting") is treated as a Phase-2 validation gate. If tier demotion/promotion looks wrong in playtest, the sanctioned response is reconciling to a legal state, not adding simulation fidelity at tier C.
