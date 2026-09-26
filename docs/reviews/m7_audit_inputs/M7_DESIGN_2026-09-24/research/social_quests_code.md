# M7 research - social, quests, companions and combat-against-NPCs: the code as it stands (key `social_quests_code`)

Source: read-only snapshot of `origin/main` at `e10d2c4`. Every `path:line` below is repo-relative and valid at that commit. Untracked working-tree docs in `G:/UNNAMED/docs` are cited as "untracked, authority unconfirmed". Nothing here is implemented or proposed as code; this is a map.

Legend: **FACT** = read in the cited source. **INFERENCE** = my reading or a design implication; not in any source.

---

## 0. The short version (for a designer who reads nothing else)

1. **FACT** There is **no faction, team, disposition, reputation, witness, crime or NPC-knowledge code anywhere in the runtime.** The words appear only in refusal lists (`faction_reputation`/`faction_state` objective types and the `reputation` reward are "not built", `src/Domain/Quests/Quests.cs:154-155`, `:206`; `faction_ref` on a quest is refused, `src/Content/QuestContent.cs:28-29`) and in the content kind table (`faction` and `fact` kinds are registered, `src/Content/SchemaResolution.cs:158-164`, `:272-278`, with `faction_ref`/`fact_ref` reference fields, `src/Content/ContentChecks.cs:34`, `:37`). No `content/factions/` or `content/facts/` directory exists.
2. **FACT** The only social state is **per-NPC, player-directed relationship values** on five named dimensions (`affection`, `fear`, `grudge`, `respect`, `trust`), each an `int` clamped to **[-100, 100]**, 0 not stored (`src/Domain/Social/Social.cs:30-40`). Owned by `RelationshipSystem` (`src/World/Runtime/Social.cs:177-198`), changed only by the internal command `ChangeRelationship(NpcId, Dimension, Delta, EventKey)` (`src/World/Runtime/Social.cs:94`), which has exactly **two** callers: a dialogue reply's `record_relationship_event` (`src/World/Runtime/Social.cs:370-372`) and a completed quest's `relationship` reward (`src/World/Runtime/Quests.cs:165-168`). No combat, trade, theft, gift or kill touches it. There is no memory log; the `EventKey` lives only on the non-persisted `RelationshipChanged` event.
3. **FACT** **No shipped content reads a relationship value.** Nine writes (all positive), zero reads: no dialogue `relationship` condition and no `relationship_value` objective exists in `content/`. (There is no "Sel trust gate"; Sel's trust is only ever raised.)
4. **FACT** **Hostility today = a creature's mind being `Engaged`** (`CreatureView.Hostile => Mind == CreatureMind.Engaged`, `src/World/Runtime/Creatures.cs:36`). Every creature is a potential enemy of the player; creatures perceive **only the player** (and fight a companion only because the companion stands nearer, `src/World/Runtime/Creatures.cs:844-861`). **The player cannot harm any NPC or companion**: melee sweeps, arrows and projectile formulas iterate `State.Creatures` only (`src/World/Runtime/Combat.cs:496`, `:554`; `src/World/Runtime/Magic.cs:112-116`). Non-companion NPCs have no health at all (`NpcState(Definition, InstanceId, Site, Body)`, `src/World/Runtime/Social.cs:88`). So **no "assault", "murder" or "attack legality" act can occur today**; M7's crime/attack-legality seam has nothing to observe until NPCs become combatants.
5. **FACT** **World flags are the closest "fact" primitive, and they are location-scoped, not knower-scoped**: a `world.*` definition ID with a `long` value per 100 m cell, 0 = baseline and not stored, persisted in the cell delta (`src/World/WorldDelta.cs:250-262`, `:18-24`). Dialogue reads/writes flags **in the speaker's current cell** (`src/World/Runtime/Social.cs:389-402`); quests read/write them **in the cell of a named location** (`src/World/Runtime/Quests.cs:256-259`, `:279-280`).
6. **FACT** **Information is already "magically global" in dialogue**: any NPC's reply may be gated on the player's inventory, any quest's state, lines the player heard in *another* NPC's conversation (`VisitedCondition.DialogueId`, `src/Domain/Social/Social.cs:65-68`), or another NPC's relationship value (a `relationship` condition names any `npc_ref`), and a reply may move *another* NPC's relationship (`record_relationship_event` names any `npc_ref`). **INFERENCE:** ruling 3's "information is not magically global" is violated by construction today (in content it is used only benignly: Sel knows whether Tavar has been spoken to). M7 needs a rule for which conditions an NPC is allowed to know.
7. **FACT** **Trade is not gated by anything social.** `TradeSystem.Trader` checks only: actor is the player, not defeated, NPC exists, offers `trade` with a `merchant_ref`, within `TalkReachMm` (`src/World/Runtime/Social.cs:535-550`). `open_service` is only an event for presentation (`ServiceOpened`, `src/World/Runtime/Social.cs:373-375`; UI opens on it, `src/Presentation/Main.cs:763-767`); the simulation keeps no "service open" state, so a `BuyCommand` works without the conversation. Price = `ceil(item.ValueBase * stock.PriceBias)`, sell = `floor(item.ValueBase * 0.4)` (`src/Domain/Items/Items.cs:225-232`, `content/config/economy.yaml` `sell_ratio: 0.4`); no relationship, reputation, quality or haggling modifier.
8. **FACT** The runtime has **no in-simulation event listeners**; events are for presentation only ("Events are past tense, immutable, and never persisted. Views subscribe to them; nothing a view does with one reaches the simulation", `src/World/Runtime/Events.cs:10-11`). Cross-system reactions are synchronous **internal commands** routed by `Simulation.Dispatch` (`src/World/Runtime/Simulation.cs:369-404`). The fan-in precedent for "things the player did that another system cares about" is `RecordDeed(Deed)` (`src/World/Runtime/Quests.cs:62`), dispatched from crafting, gathering, creature death and dialogue delivery. **INFERENCE:** an M7 act/offense record would most naturally mirror `RecordDeed` - a new internal command dispatched by the system where the act happens - not an event subscription.

---

## 1. The relationship model in code

### 1.1 Types and storage

- **FACT** `public static class Relationships` (`src/Domain/Social/Social.cs:30-40`):
  - `public const int Min = -100;` `public const int Max = 100;` (`:32-33`)
  - `public static readonly ImmutableArray<string> Dimensions = ImmutableArray.Create("affection", "fear", "grudge", "respect", "trust");` (`:35`) - alphabetical, a closed set; strings, not an enum.
  - `IsDimension(string)` (`:37`), `Clamp(int) => Math.Clamp(value, Min, Max)` (`:39`).
  - Doc comment: "What one NPC thinks of the player, on named dimensions (SYSTEMS.md S-26): there is no single good-or-evil meter. Each value is held in [-100, 100]; a value of 0 is not stored." (`:26-29`)
- **FACT** Runtime storage: `RuntimeState.Relationships : ImmutableSortedDictionary<string /*npc definition ID*/, ImmutableSortedDictionary<string /*dimension*/, int>>` (`src/World/Runtime/RuntimeState.cs:135`), ordinal-sorted. Read with `RelationshipOf(npcId, dimension)` - returns 0 when absent (`:292-293`). Written only through `SetRelationship(owner, npcId, dimension, value)`, which removes a 0 and removes an NPC with no values left (`:296-302`). Slice: `StateSlice.Relationships` - "What each NPC thinks of the player, per dimension (S-26; M4). Saved with the player (schema 10)." (`:63-64`).
- **FACT** Keyed by the **NPC definition ID** (e.g. `npc.ashen_hollow.sel_arien`), not the runtime `EntityId`. There is **no subject other than the player** (no NPC-to-NPC values, no faction values). `SocialContent` refuses `relationships_init` on an NPC ("is not built in Phase 1", `src/Content/SocialContent.cs:85-89`), so every value starts at 0.

### 1.2 How it changes

- **FACT** Internal command: `internal sealed record ChangeRelationship(string NpcId, string Dimension, int Delta, string EventKey) : InternalCommand;` (`src/World/Runtime/Social.cs:94`).
- **FACT** `RelationshipSystem.Handle(ChangeRelationship, tick)` (`src/World/Runtime/Social.cs:188-197`):
  1. refuses an unknown dimension (`"'{dim}' is not a relationship dimension"`);
  2. `from = RelationshipOf(...)`, `to = Clamp(from + Delta)`;
  3. `SetRelationship`;
  4. publishes `RelationshipChanged(NpcId, Dimension, From, To, EventKey, Tick)` (`:59`) - **even when `From == To`** (a clamp at +100 still emits the event; no early return).
  - It does **not** check that the NPC exists (content lint guarantees `npc_ref` resolves at build time).
- **FACT** Callers (exhaustive grep of `new ChangeRelationship`):
  - `DialogueSystem.Apply` on `RelationshipEventConsequence` (`src/World/Runtime/Social.cs:370-372`), `EventKey` = content `event:` string. The dispatch result is ignored (fire and forget).
  - `QuestSystem.Grant` on `RelationshipReward` (`src/World/Runtime/Quests.cs:165-168`), `EventKey` = **the quest ID**, once, at completion.
- **FACT** Content constraints: `record_relationship_event` needs `npc_ref` (any NPC, not just the speaker), `dimension` in the closed set, `delta` non-zero with `|delta| <= 100`, and an `event` string (`src/Content/SocialContent.cs:200-205`). Quest `relationship` reward: `npc_ref`, `dimension`, `amount` non-zero, `|amount| <= 100` (`src/Content/QuestContent.cs:199-202`).
- **FACT** Attribution: "Every change names its reason" (`src/World/Runtime/Social.cs:174-175`), but the reason is **not persisted** - only `RelationshipValue(NpcId, Dimension, Value)` is saved (`src/World/PlayerState.cs:38`). M4 decision: "Five relationship dimensions ... zero not stored, no memory log yet: the event names on `RelationshipChanged` are the attribution." (`docs/M4_STATUS.md:57`). SYSTEMS S-26 M4 reconciliation: "Phase 1 keeps no memory log and no NPC-to-NPC values" (`docs/SYSTEMS.md:300`).

### 1.3 How it is read

- **FACT** Dialogue condition `relationship`: `RelationshipCondition(string NpcId, string Dimension, int Min, int Max)` (`src/Domain/Social/Social.cs:77`); holds when `facts.Relationship(npc, dim)` is in `[Min, Max]` inclusive (`:153`). Content defaults `min: -100`, `max: 100` (`src/Content/SocialContent.cs:175-176`); cannot take `not:` (`:163-164`). Any `npc_ref` - not only the speaker.
- **FACT** Quest objective `relationship_value`: `RelationshipValueObjective(string NpcId, string Dimension, int Min, int Max)` (`src/Domain/Quests/Quests.cs:122-125`), defaults `int.MinValue`/`int.MaxValue` (`src/Content/QuestContent.cs:143-144`), evaluated as a state predicate (`src/Domain/Quests/Quests.cs:364-368`).
- **FACT** Both facts interfaces answer through `State.RelationshipOf` (`src/World/Runtime/Social.cs:407`, `src/World/Runtime/Quests.cs:282`). The debugger prints the current value in "needs ... in [min, max]; it is N" (`src/World/Runtime/QuestDebugger.cs:380`).
- **FACT** Presentation: a HUD log line per `RelationshipChanged` - "`{NPC}: {dimension} +{delta}`" (`src/Presentation/Main.cs:778-779`). No relationship panel.

### 1.4 Every relationship write in shipped content (and zero reads)

| Where | NPC | Dimension | Delta | EventKey | Gate |
|---|---|---|---|---|---|
| `content/dialogue/ashen_hollow/kera_voss.yaml:20` and `:37` | Kera | trust | +5 | `asked_to_learn` | "teach" reply (greet once; again: not visited `lesson`) |
| `content/dialogue/ashen_hollow/kera_voss.yaml:52` | Kera | respect | +10 | `showed_fine_work` | carries spear `quality_min: 1`, not visited `praised` |
| `content/quests/ashen_hollow/iron_under_ash.yaml:50` | Kera | respect | +5 | `quest.ashen_hollow.iron_under_ash` | quest completion |
| `content/dialogue/ashen_hollow/renn_vale.yaml:38` | Renn | respect | +5 | `went_to_the_cut` | carries iron ore, not visited `shelf_news` |
| `content/dialogue/ashen_hollow/sel_arien.yaml:77` | Sel | trust | +5 | `given_the_primer` | primer node (once) |
| `content/dialogue/ashen_hollow/sel_arien.yaml:26` and `:64` | Sel | trust | +5 | `brought_tavar_back` | visited Tavar's `greet` (and, in `again`, not visited `tavar_back`) |
| `content/quests/ashen_hollow/three_quiet_stones.yaml:50` | Sel | trust | +10 | `quest.ashen_hollow.three_quiet_stones` | quest completion |
| `content/dialogue/ashen_hollow/tavar_orr.yaml:20` and `:26` | Tavar | trust | +10 | `freed_from_the_fold` | his greet (once), either reply |

- **FACT** `grep` of `content/` finds no `kind: relationship` dialogue condition and no `relationship_value` objective. `affection`, `fear`, `grudge` are never written. All deltas are positive.
- **INFERENCE** Because nothing reads them, relationship values can be re-scaled or re-modelled in M7 with no content behaviour change; only saves (schema 10+ `relationships` array) carry them.

---

## 2. Event and command catalogues (complete, `src/World/Runtime` - the only place events and commands are defined)

### 2.1 Mechanics

- **FACT** `public abstract record GameCommand(EntityId Actor);` (`src/World/Runtime/Commands.cs:14`) - "the only way presentation changes anything (D-11)" (`:10-13`). Queued by `Simulation.Enqueue`, applied in order at a tick boundary by `DrainCommands` (`src/World/Runtime/Simulation.cs:265-306`); every command is logged as `LoggedCommand(Tick, Sequence, Command, RejectedReason)` (`src/World/Runtime/Commands.cs:29`), a rejection is published as `CommandRejected` (`src/World/Runtime/Simulation.cs:300-302`). "The same commands at the same boundaries produce the same state ... replayed from its command log end identical (PROTOTYPE.md C3)" (`:61-64`).
- **FACT** `internal abstract record InternalCommand;` "A command one system sends another. Presentation cannot construct these." (`src/World/Runtime/Systems.cs:14-15`). Routed synchronously by `Simulation.Dispatch` (`src/World/Runtime/Simulation.cs:369-404`), returning `null` or a refusal string. An internal command's tick is `Now` = the tick being stepped, or the boundary tick while draining (`:366-367`).
- **FACT** Step order (fixed): movement, tiers, tier simulations, player combat, creatures, companions, NPCs, conversations, status effects, death, discovery, **quests last**, clock (`src/World/Runtime/Simulation.cs:308-333`). `Step` is not re-entrant; "an event handler may enqueue commands, never step the world" (`:314-315`).
- **FACT** Every event carries `long Tick`. Events are never persisted (`src/World/Runtime/Events.cs:10-11`).

### 2.2 Public commands (`GameCommand`) - 23

| Command | Fields | File:line | Handler |
|---|---|---|---|
| `MoveCommand` | `Actor, MoveIntent Intent` | `src/World/Runtime/Commands.cs:17` | Movement |
| `JumpCommand` | `Actor` | `Commands.cs:20` | Movement |
| `CrouchCommand` | `Actor, bool Crouched` | `Commands.cs:23` | Movement |
| `InteractCommand` | `Actor, string TargetKey` (door or switch) | `Commands.cs:26` | Interaction |
| `MoveItemCommand` | `Actor, string Item, ItemPlace From, ItemPlace To, int Count` | `src/World/Runtime/Items.cs:48` | Inventory |
| `TakeAllCommand` | `Actor, string ContainerKey` | `Items.cs:54` | Inventory |
| `EquipCommand` | `Actor, EntityId Item` | `Items.cs:57` | Equipment |
| `UnequipCommand` | `Actor, EquipSlot Slot` | `Items.cs:59` | Equipment |
| `AttackCommand` | `Actor` (no target: spatial) | `src/World/Runtime/Combat.cs:51` | Combat |
| `BlockCommand` | `Actor, bool Raised` | `Combat.cs:54` | Combat |
| `DodgeCommand` | `Actor, int DirXPermille, int DirZPermille` | `Combat.cs:57` | Combat |
| `UseItemCommand` | `Actor, EntityId Item` | `Combat.cs:60` | Inventory |
| `CastCommand` | `Actor, string FormulaId` | `src/World/Runtime/Magic.cs:25` | Combat |
| `GatherCommand` | `Actor, string NodeKey` | `src/World/Runtime/Crafting.cs:33` | Gathering |
| `CraftCommand` | `Actor, string RecipeId` | `Crafting.cs:36` | Crafting |
| `TalkCommand` | `Actor, string NpcId` | `src/World/Runtime/Social.cs:30` | Dialogue |
| `ChooseCommand` | `Actor, string ReplyId` (`"continue"` on a reply-less line) | `Social.cs:33` | Dialogue |
| `LeaveCommand` | `Actor` | `Social.cs:36` | Dialogue |
| `BuyCommand` | `Actor, string NpcId, string Ware, int Count` | `Social.cs:39` | Trade |
| `SellCommand` | `Actor, string NpcId, EntityId Item, int Count` | `Social.cs:42` | Trade |
| `OrderCompanionCommand` | `Actor, string NpcId, CompanionOrder Order` | `src/World/Runtime/Companions.cs:16` | Companion |
| `ReviveCommand` | `Actor, string NpcId` | `Companions.cs:19` | Companion |
| `SpendAttributeCommand` | `Actor, CharacterAttribute Attribute` | `src/World/Runtime/Systems.cs:319` | Progression |

(That is 23 distinct records; `DrainCommands` routes all 23, `src/World/Runtime/Simulation.cs:273-298`. Every handler refuses an `Actor` that is not the player - there is one actor.)

- **FACT** No command targets an NPC for harm. No "steal", "pickpocket", "trespass", "bribe", "persuade", "give gift" (gifts exist only as dialogue `transfer_item`), "dismiss companion", or "join faction" command exists.

### 2.3 Internal commands - 32

| Internal command | Fields | File:line | Owner |
|---|---|---|---|
| `SetWorldFlag` | `CellKey Cell, string FlagId, long Value` | `src/World/Runtime/Systems.cs:18` | WorldFlagSystem |
| `AwardExperience` | `XpAward Award` | `Systems.cs:21` | Progression |
| `ChangePools` | `int Health, int Stamina` (+ init props, e.g. `Strain`) | `src/World/Runtime/Combat.cs:258` | Progression |
| `PracticeSkill` | `SkillPractice Practice` | `Combat.cs:266` | Progression |
| `RecordDeath` | - | `Combat.cs:269` | Progression |
| `Relocate` | `Body Body` | `Combat.cs:272` | Movement |
| `ApplyEffect` | `EntityId Target, string EffectId` | `Combat.cs:275` | StatusEffects |
| `ClearEffects` | `EntityId Target` | `Combat.cs:278` | StatusEffects |
| `Harm` | `EntityId Target, string Source, int Amount` | `Combat.cs:281` | Combat |
| `Heal` | `EntityId Target, string Source, int Amount` | `Combat.cs:284` | Combat |
| `EndFight` | - | `Combat.cs:287` | Combat |
| `ConsumeItem` | `string DefId, int Count` | `Combat.cs:290` | Inventory |
| `LearnTechnique` | `TechniqueLearning Learning` | `src/World/Runtime/Magic.cs:50` | Progression |
| `RemoveEffect` | `EntityId Target, string EffectId` | `Magic.cs:53` | StatusEffects |
| `WoundCreature` | `EntityId Target, HitResult Hit, string Source` + init `Attacker?`, `AttackerDefId?`, `FromXMm`, `FromZMm` | `src/World/Runtime/Creatures.cs:96` | Creatures |
| `HarmCreature` | `EntityId Target, string Source, int Amount` | `Creatures.cs:105` | Creatures |
| `HealCreature` | `EntityId Target, string Source, int Amount` | `Creatures.cs:107` | Creatures |
| `CreatureStrike` | `EntityId Attacker, AttackProfile Attack` | `Creatures.cs:110` | Combat |
| `ForgetPlayer` | - | `Creatures.cs:113` | Creatures |
| `CorpseEmptied` | `string CorpseKey` | `Creatures.cs:116` | Creatures |
| `DiscardContainer` | `string Key` | `Creatures.cs:119` | Inventory |
| `ExchangeItems` | `ImmutableArray<StackTake> Takes, string? ItemId, int Count, int Quality` | `src/World/Runtime/Items.cs:91` | Inventory |
| `Trade` | `MoveItemCommand Move, long Coin` | `Items.cs:97` | Inventory |
| `AddCurrency` | `long Amount` | `Items.cs:100` | Inventory |
| `GrantItem` | `string ItemId, int Count, int Quality` | `Items.cs:106` | Inventory |
| `StartQuest` | `string QuestId, string? GiverId` | `src/World/Runtime/Quests.cs:59` | Quests |
| `RecordDeed` | `Deed Deed` | `Quests.cs:62` | Quests |
| `ChangeRelationship` | `string NpcId, string Dimension, int Delta, string EventKey` | `src/World/Runtime/Social.cs:94` | Relationships |
| `Recruit` | `string NpcId` | `src/World/Runtime/Companions.cs:72` | Companions |
| `OrderCompanion` | `string NpcId, CompanionOrder Order` | `Companions.cs:75` | Companions |
| `CompanionStruck` | `EntityId Attacker, string NpcId, AttackProfile Attack` | `Companions.cs:78` | Companions |
| `PlaceNpc` | `string NpcId, Body Body` | `Companions.cs:81` | NpcSystem |

(32 records; all routed at `src/World/Runtime/Simulation.cs:371-402`.)

- **FACT** `Deed(DeedKind Kind, string Subject, int Count, int Quality, string? NpcId, long Tick)`, `enum DeedKind { Crafted, Harvested, Killed, Delivered }` (`src/Domain/Quests/Quests.cs:297-300`). Dispatch sites: harvest (`src/World/Runtime/Crafting.cs:110`), craft (`Crafting.cs:205`), a **player** kill (`src/World/Runtime/Creatures.cs:706`, only `if (killer == _player)`), a dialogue hand-over to an NPC (`src/World/Runtime/Social.cs:355`, `NpcId` set).

### 2.4 Events - 67 records (66 published event types plus the `DeathRecapLine` payload), grouped

World/core (`src/World/Runtime/Events.cs`):
- `CommandRejected(GameCommand Command, string Reason, long Tick)` `:13`
- `BodyMoved(EntityId Actor, Body From, Body To, long Tick)` `:15`
- `Jumped(EntityId Actor, long Tick)` `:18`
- `StanceChanged(EntityId Actor, Stance Stance, long Tick)` `:21`
- `WorldFlagChanged(string CellKey, string FlagId, long From, long To, long Tick)` `:23`
- `DoorToggled(EntityId Actor, string DoorKey, bool Open, long Tick)` `:25`
- `SwitchSet(EntityId Actor, string SwitchKey, long Tick)` `:28`
- `LocationDiscovered(string LocationId, DiscoveryMethod Method, long Tick)` `:30`
- `ExperienceGained(XpSource Source, long Awarded, long Repaid, int LevelsGained, int Level, long Tick)` `:32`
- `CellTierChanged(string CellKey, SimulationTier From, SimulationTier To, long Tick)` `:34`
- `AttributeSpent(CharacterAttribute Attribute, int Value, int Unspent, long Tick)` `src/World/Runtime/Systems.cs:322`

Combat (`src/World/Runtime/Combat.cs`):
- `AttackStarted(EntityId Attacker, string Source, int WindupTicks, int ActiveTicks, int RecoveryTicks, long Tick)` `:65`
- `AttackMissed(EntityId Attacker, string Source, long Tick)` `:68`
- `ShotLoosed(EntityId Attacker, string Source, long FromXMm, long FromZMm, long ToXMm, long ToZMm, EntityId? Target, long Tick)` `:74`
- `HitResolved(EntityId Attacker, string AttackerDefId, EntityId Target, string Source, BodyRegion Region, int Damage, bool Critical, bool Blocked, bool Dodged, bool Staggered, int HealthAfter, long Tick)` `:77-79` - `AttackerDefId` is `"player"` for the character (`src/World/Runtime/Creatures.cs:670`), the NPC ID for a companion, the creature def ID for a creature.
- `HealthChanged(EntityId Target, string Source, int Delta, int HealthAfter, long Tick)` `:82`
- `GuardBroken(EntityId Actor, long Tick)` `:85`
- `EffectApplied(EntityId Target, string EffectId, int Stacks, long ExpiresTick, long Tick)` `:87`
- `EffectExpired(EntityId Target, string EffectId, long Tick)` `:89`
- `CreatureKilled(EntityId Creature, string DefId, EntityId Killer, long Tick)` `:91` - `Killer` is the player's ID or a companion's NPC instance ID.
- `DeathRecapLine(string AttackerDefId, string Source, int Damage, long Tick)` `:94` (a payload type, not published alone)
- `PlayerDied(string KillerDefId, string Cause, ImmutableArray<DeathRecapLine> Recap, long DebtAdded, long Tick)` `:100`
- `PlayerRespawned(Body Body, long Tick)` `:102`
- `SkillPracticed(string SkillId, long Xp, int Level, long Tick)` `:105`
- `ItemUsed(EntityId Actor, string DefId, string? EffectId, long Tick)` `:108`
- `ItemConsumed(EntityId Actor, string DefId, int Count, long Tick)` `:110`

Magic (`src/World/Runtime/Magic.cs`): `CastStarted(EntityId Caster, string FormulaId, int CastTicks, long Tick)` `:30`; `CastCompleted(EntityId Caster, string FormulaId, int Strain, long Tick)` `:33`; `CastFizzled(EntityId Caster, string FormulaId, int Strain, long Tick)` `:36`; `CastInterrupted(EntityId Caster, string FormulaId, string Reason, long Tick)` `:39`; `StrainBacklash(EntityId Caster, int Damage, long Tick)` `:42`; `TechniqueLearned(string DefinitionId, string Source, long Tick)` `:45`.

Creatures (`src/World/Runtime/Creatures.cs`): `CreatureNoticed(EntityId Creature, string Key, CreatureMind Mind, long Tick)` `:44`; `CreatureCalled(EntityId Creature, string Key, long XMm, long ZMm, long Tick)` `:47`; `CreatureRespawned(EntityId Creature, string Key, int Generation, long Tick)` `:49`; `SpawnerSaturated(string SpawnKey, long Tick)` `:52`; `CorpseGone(string CorpseKey, long Tick)` `:54`; `CreatureStunned(EntityId Creature, string Key, long Tick)` `:57`.

Items (`src/World/Runtime/Items.cs`): `ItemMoved(EntityId Actor, string DefId, int Count, ItemPlace From, ItemPlace To, long Tick)` `:61`; `TookAll(EntityId Actor, string ContainerKey, int Taken, int Left, string? Why, long Tick)` `:64`; `ItemEquipped(EntityId Actor, EquipSlot Slot, EntityId Item, long Tick)` `:66`; `ItemUnequipped(EntityId Actor, EquipSlot Slot, EntityId Item, long Tick)` `:68`.

Crafting (`src/World/Runtime/Crafting.cs`): `NodeGathered(string NodeKey, string NodeDefId, string ItemId, int Count, bool Spent, long Tick)` `:41`; `ItemCrafted(string RecipeId, string ItemId, int Count, int Quality, long Tick)` `:44`.

Social (`src/World/Runtime/Social.cs`): `ConversationStarted(string NpcId, string DialogueId, long Tick)` `:46`; `ConversationLine(string NpcId, string DialogueId, string NodeId, long Tick)` `:49`; `ReplyChosen(string NpcId, string DialogueId, string NodeId, string ReplyId, long Tick)` `:51`; `ConversationEnded(string NpcId, long Tick)` `:53`; `ServiceOpened(string NpcId, string Service, long Tick)` `:56`; `RelationshipChanged(string NpcId, string Dimension, int From, int To, string EventKey, long Tick)` `:59`; `ItemBought(string NpcId, string ItemId, int Count, long Price, long Tick)` `:61`; `ItemSold(string NpcId, string ItemId, int Count, long Price, long Tick)` `:63`.

Quests (`src/World/Runtime/Quests.cs`): `QuestStarted(string QuestId, string? GiverId, long Tick)` `:21`; `ObjectiveActivated(string QuestId, string ObjectiveId, long Tick)` `:23`; `ObjectiveSatisfied(...)` `:25`; `ObjectiveFailed(...)` `:28` (timed objective ran out); `ObjectiveClosed(...)` `:31` (branch not taken, or still active at quest end); `QuestBranchTaken(string QuestId, string BranchPoint, string Branch, long Tick)` `:33`; `QuestCompleted(string QuestId, string ByObjective, long Tick)` `:35`; `QuestFailed(string QuestId, string By, long Tick)` `:37`; `RewardGranted(string QuestId, string Kind, string What, long Tick) { string? Ref; long Amount; }` `:40-45`.

Companions (`src/World/Runtime/Companions.cs`): `CompanionRecruited(string NpcId, long Tick)` `:23`; `CompanionOrdered(string NpcId, CompanionOrder Order, long Tick)` `:25`; `CompanionCaughtUp(string NpcId, string Reason, Body From, Body To, long Tick)` `:28`; `CompanionDowned(string NpcId, string ByDefId, long Tick)` `:30`; `CompanionRevived(string NpcId, int Health, long Tick)` `:32`; `CompanionFell(string NpcId, Body At, long Tick)` `:35`.

- **FACT** Silent state changes (no public event of their own): a dialogue `transfer_item` in either direction and quest item/coin rewards go through `ExchangeItems`/`GrantItem`/`AddCurrency`, which publish nothing (`src/World/Runtime/Items.cs:327-374`); they are visible only as `ReplyChosen` / `RewardGranted`. `RecordDeed` has no event (trace only). Starting a quest already started is refused silently for dialogue (`src/World/Runtime/Social.cs:376-379`).

---

## 3. The quest model

### 3.1 Definition

- **FACT** `QuestDefinition(string Id, string Title, string Summary, string? GiverId, string Entry, ImmutableArray<ObjectiveDefinition> Order, ImmutableArray<ObjectiveCondition> FailIf, ImmutableArray<QuestReward> Rewards)` with a derived `Objectives` map (`src/Domain/Quests/Quests.cs:14-26`). "The engine evaluates predicates and holds no per-quest logic (D-07). `Order` is the authored order, which decides ties" (`:10-12`).
- **FACT** `ObjectiveDefinition(string Id, string Description, ObjectiveCondition Condition, ImmutableArray<string> Next)` + `AllOf` (join), `FirstBranch` (`branch: first`), `Hidden` (`visibility: hidden`), `TimeLimitTicks?`, `OnFail?` (objective ID or `fail_quest`), `Terminal => Next.IsEmpty` (`src/Domain/Quests/Quests.cs:35-49`).
- **FACT** Built objective types (11), `ObjectiveTypes.Built` (`src/Domain/Quests/Quests.cs:136-138`):
  - state predicates: `talk_to(NpcId, DialogueId, Nodes)` (any node heard) `:64`; `visit_location(LocationId)` (discovered, ever) `:70`; `explore_location(LocationId, WithinMm)` (standing within now) `:76`; `acquire_item(ItemId, Count, QualityMin)` (carried now) `:82`; `world_state(FlagId, LocationId, Min, Max)` `:116`; `relationship_value(NpcId, Dimension, Min, Max)` `:122`; `wait_until(AfterTicks)` since activation `:128`;
  - deed-counting (`CountsDeeds => true`, only while active): `craft_item` `:88`, `harvest_resource` `:95`, `kill_creature(CreatureId, Count)` `:102`, `deliver_item(NpcId, ItemId, Count)` `:109`.
- **FACT** Named and refused, each "with what it waits for" (`src/Domain/Quests/Quests.cs:141-159`): `discover_secret`, `dialogue_choice` ("choice records; talk_to a node the reply leads to instead"), `use_recipe`, **`construct_building` = "building (M7)"**, `upgrade_settlement` (M10), `kill_named`/`defeat_boss`/`survive_encounter`/`defend_location`/`solve_puzzle` (M8), `escort_npc`, **`faction_reputation` = "factions (M7)"**, **`faction_state` = "factions (M7)"**, `companion_present` (no quest needs one yet), **`know_fact` = "knowledge facts"**, `time_window`. The QST001 lint refuses them by name (`src/Content/QuestContent.cs:120-121`).
- **FACT** Rewards built (7): `xp`, `currency`, `item`, `recipe`, `spell`, `relationship`, `world_flag` (`src/Domain/Quests/Quests.cs:203`); not built: `ability`, **`reputation`**, `title`, **`access`**, `companion`, `property`, `permanent_ability`, `transformation` (`:205-206`). Quest-level `faction_ref` refused: "`faction_ref is not built in Phase 1`" (`src/Content/QuestContent.cs:28-29`, test `tests/Content.Tests/QuestContentTests.cs:118-123`).

### 3.2 Runtime state and evaluation

- **FACT** `QuestState(string QuestId, QuestStatus Status, long StartedTick, long? EndedTick, string? EndedBy, ImmutableArray<ObjectiveState> Objectives)`; `ObjectiveState(string Id, ObjectiveStatus Status, long ActivatedTick, long? EndedTick, int Progress)`; `QuestStatus { Active, Completed, Failed }`; `ObjectiveStatus { Active, Satisfied, Failed, Closed }` (`src/Domain/Quests/Quests.cs:211-227`). Saved keys are snake_case strings, never ordinals (`QuestKeys`, `:230-275`); a condition sees `not_started` / `not_reached` before existence (`:266-274`).
- **FACT** `QuestRules.Advance(quest, state, facts, tick)` (`src/Domain/Quests/Quests.cs:407-539`): evaluates active objectives in authored order, cascading until nothing changes (bounded by `Order.Length + 1` passes); a satisfied objective activates each `Next` whose `AllOf` are all satisfied; a `FirstBranch` point's first satisfied alternative emits `BranchTaken` and **closes** the others (even never-reached ones); timers after predicates ("holding at the deadline counts", `:437`); then `fail_if` (state predicates only, `src/Content/QuestContent.cs:73-74`); a satisfied terminal objective completes the quest; `End` closes everything still active. Satisfied/failed/closed never change again.
- **FACT** `QuestSystem` owns `StateSlice.Quests` (`src/World/Runtime/Quests.cs:64-70`): `Handle(StartQuest)` refuses a started quest (`:88-99`) and publishes `QuestStarted` + `ObjectiveActivated(entry)`; `Handle(RecordDeed)` adds to matching active objectives (`:101-116`); `Tick` runs `Advance` for every active quest **last in the step** (`:118-137`), publishes one event per transition (`:240-254`), and on completion `Grant`s rewards once through owners' internal commands (`:140-178`): `AwardExperience(XpSource.QuestObjective)`, `AddCurrency`, `GrantItem` (quality Standard), `LearnTechnique(LearningSource.Quest)`, `ChangeRelationship(..., quest.Id)`, `SetWorldFlag(CellOfLocation(loc), flag, value)`; each followed by `RewardGranted`.
- **FACT** Quests start **only** from dialogue: `StartQuestConsequence` → `StartQuest(questId, speakerNpcId)` (`src/World/Runtime/Social.cs:376-379`); the lint requires every quest to be started by some reply ("nothing starts it", `src/Content/QuestContent.cs:290-291`).
- **FACT** There is no quest-level choice other than `branch: first`; "Offer and acceptance are one reply. No offer, decline, abandon or repeat until something needs them." (`docs/M5_STATUS.md:87`). Neither shipped quest uses `branch: first`, `time_limit_min` or `fail_if` (`content/quests/ashen_hollow/*.yaml`). Three Quiet Stones uses a join: `o_steady` `all_of: [o_north, o_southwest, o_southeast]` (`content/quests/ashen_hollow/three_quiet_stones.yaml:37-42`).
- **FACT** Journal: `QuestView`/`ObjectiveView` hide closed objectives and unsatisfied hidden ones (`src/World/Runtime/Quests.cs:180-194`).

### 3.3 Quests and world flags

- **FACT** Quests read flags in "the cell that holds the place": `WorldFlag(flagId, locationId) => State.World.GetFlag(CellOfLocation(locationId), flagId)` (`src/World/Runtime/Quests.cs:279-280`; `CellOfLocation`, `:256-259`); a `world_flag` reward writes the same way (`:169-171`). M5 decision 6: "A world flag is read in the cell of a named place, because flags live in cells: `world_state` and the `world_flag` reward take a `location_ref`." (`docs/M5_STATUS.md:85`).
- **FACT** Quest outcomes set flags only via the `world_flag` reward (none in shipped content). Quest 2's flags are set by region **switches** through `InteractionSystem.Work` (`src/World/Runtime/Systems.cs:276-293`), and the quest reads them (`three_quiet_stones.yaml:22-42`).

### 3.4 The quest debugger (read-only)

- **FACT** `Simulation.Diagnose(questId) : QuestDiagnosis` (`src/World/Runtime/Simulation.cs:250`; `src/World/Runtime/QuestDebugger.cs:92-159`): one-line answer to "What is this quest waiting on right now?", per waiting objective its `Term`s (label, value, wanted, holds) and "ways" (`Ways`, `:174-246`), problems (unsatisfiable supply, dead joins, stall, unknown saved objective), and a run-length-collapsed trace of the last 64 distinct evaluations (`src/World/Runtime/Quests.cs:73`, `:213-225`; transient). For replies it lists each condition that blocks it (`Reply`/`DescribeCondition`, `QuestDebugger.cs:363-387`), evaluated "as it would in a conversation with" that NPC (`SpeakerFacts`, `src/World/Runtime/Social.cs:425-459`).
- **INFERENCE** Any M7 predicate (`reputation`, `faction_state`, `know_fact`) must add a `DescribeCondition` arm and an `Evaluate` arm with `Term`s to keep the M5 debugger guarantee ("every stage is diagnosable", ROADMAP M8 exit, `docs/ROADMAP.md:293`). The `relationship_value` "ways" list only dialogue replies and not quest `relationship` rewards (`QuestDebugger.cs:234-241`) - a small existing gap.

---

## 4. World flags: definition, set, read, persistence, scope

- **FACT Definition.** Content kind `world_flag`, ID prefix `world.`, directory `world_flags/` (`src/Content/SchemaResolution.cs:280-286`). Each YAML has `type: bool|int` and `default: 0` (or `false`) - lint WLD006: "string flags have no cell-delta encoding yet" / "must default to 0 (false): the cell delta records divergence from 0" (`src/Content/WorldContent.cs:374-384`). The six shipped flags all carry `owning_system: S-22` (e.g. `content/world_flags/foldscar/steadied.yaml:7-9`), a descriptive field the code does not read. Flags: `world.foldscar.{steadied, stone_north_aligned, stone_southeast_aligned, stone_southwest_aligned}`, `world.hollow.{forge_shed_door_open, longhouse_door_open}`.
- **FACT Storage.** `WorldDelta.SetFlag(CellKey cell, string flagId, long value)` - internal; throws unless `flagId` is a valid definition ID starting `world.`; value 0 removes the entry (`src/World/WorldDelta.cs:250-259`). `GetFlag(cell, flagId)` returns 0 when absent (`:261-262`). Persisted as `CellDeltaRecord.Flags : ImmutableArray<KeyValuePair<string,long>>` in `cells.msgpack`, proven by the cell's `BaselineHash` (`src/World/WorldDelta.cs:13-24`).
- **FACT Ownership.** `WorldFlagSystem` owns `StateSlice.WorldFlags` (`src/World/Runtime/Systems.cs:295-316`; `RuntimeState.cs:33-34`). `Handle(SetWorldFlag)` is a no-op when unchanged, else sets and publishes `WorldFlagChanged(CellKey, FlagId, From, To, Tick)`.
- **FACT Writers** (all `SetWorldFlag` sites): door toggle (`Systems.cs:269`), switch (`Systems.cs:288`, only when every `requires` flag in the same cell is non-zero, `:286`), dialogue `set_world_flag` in the speaker's cell (`src/World/Runtime/Social.cs:367-369`), quest `world_flag` reward in a location's cell (`src/World/Runtime/Quests.cs:169-171`).
- **FACT Readers.** Doors/switches/barriers (`SystemContext.IsOpen/IsSet/IsLifted`, `src/World/Runtime/Systems.cs:41-45`) - a barrier blocks movement, blows and sight while its flag is 0 (`docs/M6_STATUS.md:41`); dialogue `world_state` (default `min: 1`, `max: long.MaxValue`, `src/Content/SocialContent.cs:171-172`) in the speaker's cell (`src/World/Runtime/Social.cs:400-402`); quest `world_state` in a location's cell.
- **FACT Scope.** Cells are 100 m squares (`src/World/Coordinates.cs:16`). A flag has no global value: the same ID can hold different values in different cells, and the debugger warns when a flag is set in a cell an objective does not read (`src/World/Runtime/QuestDebugger.cs:276-282`). The speaker's cell is computed from the NPC's **current body** (`SpeakerCell`, `src/World/Runtime/Social.cs:389-393`), so a moving NPC (a companion) reads/writes different cells as he walks.
- **INFERENCE (knowledge).** Flags are world facts ("the stone is turned"), not beliefs: there is no holder, no provenance, no confidence, no "who knows". They are the nearest existing primitive to a "fact", but they model *what is*, not *what an NPC knows*. A world flag is also cell-delta state: adding flags dirties cells, which must then be proven by `baseline_hash` on load. Faction-wide state (war, alliance) is not naturally cell-scoped; using cell flags for it would tie a faction fact to one arbitrary cell.

---

## 5. Hostility and targeting today

### 5.1 Who is hostile

- **FACT** There is no team, side, faction or attitude field on any actor. `CreatureDefinition` has a `Family` (beast/construct/undead) and tags (`src/Domain/Combat/Combat.cs:253-261`; `content/creatures/beast/*.yaml` `tags: [creature, beast]`); used for effect immunities (`src/World/Runtime/Combat.cs:886`), not hostility.
- **FACT** Every creature is hostile-by-default to the player once it has found him. Mind machine `CreatureMind { Unaware, Suspicious, Engaged, Searching, Returning, Fleeing }` (`src/World/WorldDelta.cs:90-109`). `Decide` (`src/World/Runtime/Creatures.cs:324-370`): **Engaged** when it sees the player with awareness at `Perception.Full` (100) and the target is inside the ground it keeps; Searching after 20 ticks unseen; Suspicious at `awareness >= suspicious` (30); Returning when the player is defeated, beyond the leash, or suspicion decays; Fleeing below `flee_below_percent`. A pounce role goes Engaged on a footfall in its territory. A wound makes it Engaged (or Fleeing) at once and tells it where the attacker stood (`:643-672`).
- **FACT** Role config (`content/config/creature_behaviour.yaml`): `den_guardian`, `pack_hunter`, `sleeper`, `stray`, `roamer`, `hunter`, `sentinel`, `territorial`, `ambusher` - fields `unaware`, `territory_m`, `wander_m`, `calls_for_help`, `answers_calls`, `flank_m`, `keep_distance_m`, `strike_within_m`, `flee_below_percent`, `sleep_hearing_percent`, `pounce_on_noise` (`CreatureRole`, `src/Domain/Creatures/Perception.cs:49-76`). None is "passive" or "neutral": even the boar "ignores those who leave it be" only by territory.
- **FACT** The player's "target" is never tracked by the creature as an identity; creatures perceive the player's body (`Perceive`, `src/World/Runtime/Creatures.cs:290-322`, `target = !State.PlayerCombat.Defeated`, `:295`).

### 5.2 Who can be attacked, by whom

| Attacker → target | Possible? | Evidence |
|---|---|---|
| Player → creature (melee, arrow, bolt formula) | Yes | melee sweep over `State.Creatures` `src/World/Runtime/Combat.cs:495-504`; `RangedTarget` over creatures `:548-567`; formula projectile `src/World/Runtime/Magic.cs:112-116` |
| Player → NPC (Renn, Kera, Sel) | **No** | no NPC in any target set; NPCs have no health (`NpcState`, `src/World/Runtime/Social.cs:88`); `SocialContent` refuses `combat_profile` (`src/Content/SocialContent.cs:85-89`) |
| Player → companion (Tavar) | **No** (no friendly fire) | same target sets; companion health changes only via `CompanionStruck` from creatures (`src/World/Runtime/Companions.cs:213-245`) |
| Arrows/bolts vs NPC bodies | **Pass through** | `Walled` checks only static blockers + closed doors/barriers (`Combat.cs:569-570`); NPCs are not in it |
| Companion → creature | Yes, only creatures `Engaged` with the character within `fight_radius_m` 10 of him and `leash_m` 16 of the character (waiting: `guard_radius_m` 4) | `Target` `src/World/Runtime/Companions.cs:416-433`; `Swing` hits every living creature in arc `:456-474`; `Hit` dispatches `WoundCreature { Attacker = npc.InstanceId, AttackerDefId = npcId }` `:477-494` |
| Companion → player/NPC | No | `Swing` iterates creatures only |
| Creature → player | Yes | `CreatureStrike` `src/World/Runtime/Combat.cs:633+` |
| Creature → companion | Yes, when the companion stands nearer than the player by more than `FoeMarginMm = 1_000`, never with a charge | `Foe` `src/World/Runtime/Creatures.cs:844-861`; `:504`; blow routing `:629` |
| Creature → other NPC | No | creatures do not perceive or target NPCs |
| Creature → creature | No | - |

- **FACT** `AttackCommand` carries no target (`src/World/Runtime/Combat.cs:51`); targeting is spatial at the active window (reach + arc + no wall). **INFERENCE:** making NPCs attackable in M7 means adding NPC bodies (and health) to these sweeps, which is also where an "attack legality" check or an "assault" act would be recorded.
- **FACT** Companion kills and wounds: `CreatureKilled.Killer` = companion NPC instance ID; "a kill by the character earns combat XP ... and counts for their quests - a companion's kill does neither (M6)" (`src/World/Runtime/Creatures.cs:696-714`).
- **FACT** Obstacles: non-companion NPC bodies block the player (`SystemContext.Obstacles`, `src/World/Runtime/Systems.cs:78-86`); a companion does not ("so a narrow door is never held shut by a friend"); creatures are blocked by companions (`src/World/Runtime/Creatures.cs:864-867`).

### 5.3 Design text that constrains M7 hostility (for cross-reference)

- **FACT** "**It never decides who attacks:** tactical hostility and attack legality are derived from faction relation, war state, legal status, identity knowledge and perception, not from a standing tier" (`docs/SYSTEMS.md:309`). `PROGRESSION.md` §10: "Standing is access, not an enemy-state variable. Whether an actor becomes hostile is decided separately by: relationship; legal status; faction relation and war state; identity knowledge; perception" (`docs/PROGRESSION.md:444-449`) and "A low standing makes a faction refuse service; it does not, by itself, make its members attack on sight." (`:451`).
- **FACT** Execution prompt (Phase 1): "Actors react only to what they perceive, infer, or learn through communication." (`docs/CLAUDE_PHASE1_EXECUTION_PROMPT.md:543`); "Do not introduce global faction aggro." (`:552`); "A faction relation can influence policy after identity/knowledge exists; it does not give every remote actor omniscience." (`:554`); "Preserve the future systemic-hostility seam, but do not implement the full crime/faction system yet." (`:556`).

---

## 6. Existing player acts that could be faction-relevant, and the exact marker

| Act | Public event(s) | Internal marker | Who/what is identified | Notes |
|---|---|---|---|---|
| Kill a creature | `CreatureKilled(Creature, DefId, Killer, Tick)` | `RecordDeed(Killed, defId, 1, …)` only if player; `AwardExperience(Combat, Kill = KillContext(species, level, spawnerKey))` | creature def + instance, spawner key | `src/World/Runtime/Creatures.cs:701-714`. Companion kills emit the event but no deed/XP |
| Wound a creature | `HitResolved(Attacker, AttackerDefId, Target, …)`, maybe `CreatureNoticed` | `WoundCreature` | attacker = player or companion | `:643-672` |
| Loot a corpse / container | `ItemMoved(Actor, DefId, Count, From, To)`, `TookAll(...)` | `SetContainer` | container key (`container.waystation_chest`, `container.den_cache`, `container.merchant_cart`, corpse keys) | **No owner concept**: `ContainerSite(Key, LootTableId, XMm, ZMm, StackSlots)` (`src/Domain/Spatial/RegionLayout.cs:37`); the waystation chest (`content/regions/ashen_hollow.yaml:148`) is freely lootable. No theft act exists |
| Take from a trader's wares without paying | impossible | - | - | refused: "`{key} is a trader's wares: buy and sell`" (`src/World/Runtime/Items.cs:456-459`) |
| Buy / sell | `ItemBought(NpcId, ItemId, Count, Price)`, `ItemSold(...)` | `Trade(MoveItemCommand, Coin)` | trader NPC ID | `src/World/Runtime/Social.cs:484-519` |
| Choose a dialogue reply | `ReplyChosen(NpcId, DialogueId, NodeId, ReplyId)`; `ConversationLine` per node | consequences as internal commands | speaker NPC ID | `src/World/Runtime/Social.cs:248-277` |
| Hand an item to an NPC | only `ReplyChosen` | `ExchangeItems` (spend) then `RecordDeed(Delivered, itemId, count, worstQuality, npcId)` | NPC | `:333-357`. NPCs hold no inventory: the stack is destroyed |
| Receive an item from an NPC | only `ReplyChosen` | `ExchangeItems(…, itemId, count, Standard)` | - | the item is minted |
| Learn from a teacher | `TechniqueLearned(DefinitionId, Source)` | `LearnTechnique(TechniqueLearning(id, LearningSource.Teacher){SourceRef = npcId})` | teacher NPC | `:363-366` |
| Relationship change | `RelationshipChanged(NpcId, Dimension, From, To, EventKey)` | `ChangeRelationship` | NPC + reason key | only dialogue and quest rewards |
| Start / branch / complete / fail a quest | `QuestStarted(QuestId, GiverId)`, `QuestBranchTaken`, `QuestCompleted`, `QuestFailed`, `RewardGranted` | `StartQuest`, rewards | giver NPC | `src/World/Runtime/Quests.cs:21-45` |
| Recruit / order / revive a companion | `CompanionRecruited`, `CompanionOrdered`, `CompanionRevived` | `Recruit`, `OrderCompanion` | NPC | `src/World/Runtime/Companions.cs:160-208` |
| Companion downed / falls | `CompanionDowned(NpcId, ByDefId)`, `CompanionFell` | - | the creature def that downed him | `:243`, `:521` |
| Work a switch / open a door | `SwitchSet`, `DoorToggled`, `WorldFlagChanged` | `SetWorldFlag` | switch/door key, cell | `src/World/Runtime/Systems.cs:251-293` |
| Enter a place | `LocationDiscovered(LocationId, Visited)` | `DiscoveryRecord` | location | `src/World/Runtime/Systems.cs:444-445` |
| Gather / craft | `NodeGathered`, `ItemCrafted` | `RecordDeed(Harvested|Crafted)` | node key / recipe | `src/World/Runtime/Crafting.cs:107-110`, `:204-205` |
| Die | `PlayerDied(KillerDefId, Cause, Recap, DebtAdded)` | `RecordDeath` | killer creature def | `src/World/Runtime/Combat.cs:100` |

- **FACT** Absent acts (no code path): assault/murder of an NPC, theft, trespass, lock-picking, bribery, lying/persuasion, witness report, arrest, bounty, pardon, joining/leaving a group, harming a companion, harvesting "owned" nodes (nodes have no owner), building.
- **INFERENCE** For ruling 3's "the same act can affect two factions differently", the existing candidate acts in content are: killing a creature of a family/spawner, completing a quest, a specific dialogue reply (`ReplyChosen` with its `ReplyId`), and trading. All already carry a stable definition ID (creature def, quest ID, dialogue+node+reply ID, NPC ID) that a data-defined reaction table could key on. The ROADMAP M7 exit "the same act moves two factions in opposite directions in a fixture" (`docs/ROADMAP.md:284`) can be met with one of these without new act kinds.

---

## 7. Witnesses, perception and NPC knowledge today

- **FACT** **NPCs perceive nothing.** `NpcSystem.Tick` only turns an NPC toward the player while talking, else back to the authored facing (`src/World/Runtime/Social.cs:149-164`). No sight, hearing, memory or awareness exists for NPCs. `NpcState` has no mind (`:88`).
- **FACT** **Creature perception** is the only perception code (`src/Domain/Creatures/Perception.cs`), pure and deterministic:
  - `Senses(long SightMm, long FieldOfViewMdeg, long HearingMm)` (`:10`).
  - `Sees(Body eye, Senses, targetX, targetZ, IEnumerable<Blocker> walls) : double?` - within sight range, within FOV cone around the facing, no wall crossing the line; returns distance (`:87-107`).
  - `Hears(Body ear, long hearingMm, Noise noise)` - inside both the noise's carry and the listener's hearing (`:109-114`).
  - `Accrue(awareness, seenAtMm, sightMm, AwarenessRules, tickMs)` - 0..100 awareness, faster when close, decays unseen (`:120-128`). Rules from `config.creature_behaviour` `awareness:` (suspicious 30, sight 40/s at range, 200/s close, decay 10/s, heard_noise 60, heard_call 80, search 6 s) and `noise_m:` (walk 3, run 8, sprint 16, swing 10, blow 20, call 45).
  - `Noise(XMm, ZMm, RadiusMm, bool Call, string? CallerKind)` (`:27`); the player emits footfalls by gait and a swing (`PlayerNoises`, `src/World/Runtime/Creatures.cs:241-261`); a blow on a creature emits a `BlowMm` noise; a call is heard only by the caller's own kind with `answers_calls` (`:309-310`).
  - "There is no shared awareness: a packmate out of earshot of a howl knows nothing (STEALTH §1, §5, §6)." (`src/World/Runtime/Creatures.cs:124-125`). Creatures act only in tier-A cells.
  - What a creature knows is persisted with it: `CreatureRecord` holds `Mind`, `Awareness`, `Knows`, `KnownXMm/ZMm`, `LastSeenTick`, `SearchUntil`, `HasCalled` (`src/World/WorldDelta.cs:153-185`) - "what it knows ... a target an actor still hunts".
- **INFERENCE** `Sees`/`Hears` take a body and a point and are actor-agnostic; they are the ready primitive for "did NPC X witness act Y at tick T". A witness check would be a pure function over (witness body, senses, act position, walls incl. closed doors/barriers, and later building walls). The creature call (`CreatureCalled` + a `Noise` with `Call`) is the existing model of **local communication** ("learn through communication"), and is already range-limited and kind-filtered.
- **FACT** Knowledge-like primitives that do exist:
  - the player's **heard lines** per conversation (`RuntimeState.Conversations`, saved as `ConversationMemory(DialogueId, Heard)`, `src/World/PlayerState.cs:41`) - the player's memory, not an NPC's;
  - `DiscoveryRecord(LocationId, DiscoveryMethod Method, long Tick)` with `DiscoveryMethod { Sighted, Visited, Told, Purchased, Magical }` (`src/World/PlayerState.cs:25-35`) - only `Visited` is ever produced (`src/World/Runtime/Systems.cs:444`); `Told` is an unused seam;
  - world flags (section 4) - world truth, not belief;
  - the reserved `fact` content kind (`src/Content/SchemaResolution.cs:272-278`) and `know_fact` objective type (refused, "knowledge facts", `src/Domain/Quests/Quests.cs:157`).
- **FACT** Dialogue conditions read **global player state**, not the speaker's knowledge: `has_item` (what the player carries now), `quest_state` (any quest), `visited` with `dialogue_ref` (what the player heard from someone else - "Sel knows whether Tavar has been spoken to", `docs/M6_STATUS.md:45`), `relationship` on any `npc_ref`, `companion_present` for any NPC. **INFERENCE:** under ruling 3 these are acceptable only as "the NPC can see it" (carried items, a companion standing there) or "the player told them" (the reply text itself); reputation must not be readable this way by an NPC who could not know it, which argues for M7 conditions being evaluated against what the speaker's faction/the speaker knows rather than a global standing.

---

## 8. Trade and services: what gates what

- **FACT** Services: `NpcServices.Trade = "trade"`, `Built = [trade]` (`src/Domain/Social/Social.cs:19-24`); content refuses any other service (`src/Content/SocialContent.cs:78-79`) and requires `merchant_ref` iff `trade` (`:81-82`). DATA_MODEL's longer list (`trade|repair|train|craft_station|rest|stable|bank`, `docs/DATA_MODEL.md:295`) is not built.
- **FACT** `TradeSystem` "Owns no state" (`src/World/Runtime/Social.cs:462-468`). Gate `Trader(...)` (`:535-550`): actor is player; not defeated; NPC present; offers trade with a known merchant; within `TalkReachMm` (= inventory reach + body radius; 1.6 m + 0.35 m = 1.95 m, `src/World/Runtime/Systems.cs:67`, `docs/M6_STATUS.md:41`). **No relationship, reputation, quest, dialogue or time gate.** The simulation does not remember `open_service`; the wares panel opens because presentation reacts to `ServiceOpened` (`src/Presentation/Main.cs:763-767`), but `BuyCommand`/`SellCommand` are accepted without it.
- **FACT** Prices (`src/Domain/Items/Items.cs:220-232`): `MerchantStock(string ItemId, int Count, double PriceBias)`; `Merchant(string Id, ImmutableArray<MerchantStock> Stock, ImmutableArray<string> BuysCategories)`; `Pricing(double SellRatio)` with `BuyPrice = (long)Math.Ceiling(item.ValueBase * stock.PriceBias)`, `SellPrice = null` when `NoSell` or category not bought, else `(long)Math.Floor(item.ValueBase * SellRatio)`. A ware the trader did not stock (bought back from the player) is asked at bias 1.0 (`src/World/Runtime/Social.cs:531-533`). Kera's stock: 60 arrows, hide vest, hide cap, all `price_bias: 1.0`; `buys_tags: [material, consumable, weapon, armor, misc]` (`content/merchants/ashen_hollow/kera_voss.yaml:8-13`). Purse bottomless; quality ignored (`docs/M4_STATUS.md:61`).
- **FACT** Wares are a container keyed by the merchant ID at the NPC's authored site, 48 stack slots (`src/World/Runtime/Systems.cs:55-64`); authored stock until first trade, then a changed container persisted like any other (`docs/M4_STATUS.md:21`). Only a `Trade` may move them (`src/World/Runtime/Items.cs:199-200`, `:456-459`).
- **INFERENCE (for "reputation-gated service")** An enforceable gate must live in the simulation (`TradeSystem.Trader` or a service-availability rule it asks), not only in dialogue conditions, because the simulation accepts `BuyCommand` without any conversation. A price modifier would enter at `AskFor` / `Pricing.BuyPrice` / `SellPrice`; `Pricing` uses `double` arithmetic today. DATA_MODEL names a `set_price_modifier` dialogue consequence (`docs/DATA_MODEL.md:529`) and `reputation_tiers[].services` (`:568-572`) - neither exists.

---

## 9. The dialogue condition and consequence language

### 9.1 Structure

- **FACT** `DialogueDefinition(string Id, ImmutableArray<string> Participants, string Root, ImmutableSortedDictionary<string, DialogueNode> Nodes)` (`src/Domain/Social/Social.cs:45`); `DialogueNode(string Id, string Text, ImmutableArray<DialogueChoice> Choices, string? Next, bool Once, string? NextIfExhausted)` (`:52`); `DialogueChoice(string Id, string Text, ImmutableArray<DialogueCondition> Conditions, ImmutableArray<DialogueConsequence> Consequences, string? Next)` (`:55-56`). A node has replies or `next`, never both (`src/Content/SocialContent.cs:141-142`). Text is inline until localization.
- **FACT** A reply is offered iff **all** its conditions hold (AND only; no OR, no NOT-group; `not:` only on single `visited`, `has_item`, `quest_state`, `companion_present`, `src/Content/SocialContent.cs:162-164`). Unoffered replies are hidden, not greyed (`src/World/Runtime/Social.cs:304-306`).
- **FACT** Once-lines: entering a node marks it heard (`MarkVisited`, `src/World/Runtime/Social.cs:320-325`); `DialogueRules.Arrive` passes a spent once-node to `next_if_exhausted` or ends (`src/Domain/Social/Social.cs:168-178`); the lint refuses a loop of spent lines (`src/Content/SocialContent.cs:253-258`).
- **FACT** Conversation lifecycle: `TalkCommand` refuses when defeated, NPC absent, no dialogue, companion downed, out of reach; closes any open conversation first (`src/World/Runtime/Social.cs:225-246`). It ends on `LeaveCommand`, death, or the player being more than `TalkReachMm + 1_000` mm away (`:290-296`). The open conversation is transient (a load starts outside one, `:90-91`).

### 9.2 Conditions (8 built; closed set)

| Kind (content) | Record | Semantics | Evidence |
|---|---|---|---|
| `visited` (`node`, opt. `dialogue_ref`, opt. `not`) | `VisitedCondition(NodeId, Negated){DialogueId}` | the player has heard that node (of this or another conversation) | `Social.cs:65-68`, `:150` |
| `world_state` (`flag_ref`, `min`=1, `max`=long.Max) | `WorldStateCondition(FlagId, Min, Max)` | flag in the **speaker's cell** in range | `:71`, `:151` |
| `has_item` (`item_ref`, `count`=1, `quality_min`=-1, opt. `not`) | `HasItemCondition(ItemId, Count, QualityMin, Negated)` | carried count at that quality | `:74`, `:152` |
| `relationship` (`npc_ref`, `dimension`, `min`=-100, `max`=100) | `RelationshipCondition(NpcId, Dimension, Min, Max)` | that NPC's value toward the player in range | `:77`, `:153` |
| `skill` (`skill_ref`, `min`) | `SkillCondition(SkillId, Min)` | skill level ≥ | `:80`, `:154` |
| `level` (`min`) | `LevelCondition(Min)` | character level ≥ | `:83`, `:155` |
| `quest_state` (`quest_ref`, opt. `objective`, `is`, opt. `not`) | `QuestStateCondition(QuestId, ObjectiveId?, State, Negated)` | quest `not_started/active/completed/failed`, objective `not_reached/active/satisfied/failed/closed` | `:89`, `:156` |
| `companion_present` (`npc_ref`, opt. `order: follow|wait`, opt. `not`) | `CompanionPresentCondition(NpcId, CompanionOrder?, Negated)` | NPC is in the roster (under that order) | `:95`, `:157` |

- **FACT** Evaluation is a closed `switch` in `DialogueRules.Holds` (`src/Domain/Social/Social.cs:148-159`) over `IDialogueFacts` (`:125-144`: `Visited`, `WorldFlag`, `Carried`, `Relationship`, `SkillLevel`, `Level`, `QuestState`, `CompanionOrderOf`), implemented by `DialogueSystem` (`src/World/Runtime/Social.cs:397-422`) and `SpeakerFacts` for the debugger (`:432-459`). Content parsing is a matching closed `switch` with the list `Conditions` (`src/Content/SocialContent.cs:26-27`, `:159-184`).
- **INFERENCE (adding a faction predicate)** A `reputation`/`faction_standing`/`faction_state` condition needs, in lockstep: a record deriving `DialogueCondition`, an `IDialogueFacts` method, a `Holds` arm, a `SocialContent` parse arm + list entry, a `DescribeCondition` arm in the debugger, and both `IDialogueFacts` implementations. The parallel quest objective is already named (`faction_reputation`, `faction_state`) and must move from `NotBuilt` to `Built` with an `IQuestFacts` method and `Evaluate` arm.

### 9.3 Consequences (8 built; closed set)

| Command (content) | Record | Effect | Evidence |
|---|---|---|---|
| `transfer_item` (`item_ref`, `count`, `to: player|npc`) | `TransferItemConsequence(ItemId, Count, ToPlayer)` | at most one per reply; runs **first**, all or nothing; its refusal refuses the whole reply | `Social.cs:101`; `src/World/Runtime/Social.cs:268-271`, `:333-357`; lint `src/Content/SocialContent.cs:152-153` |
| `give_recipe` (`recipe_ref`) | `GiveRecipeConsequence(RecipeId)` | `LearnTechnique(Teacher, SourceRef = npc)` | `:104`; `:363-366` |
| `set_world_flag` (`flag_ref`, `value`=1) | `SetWorldFlagConsequence(FlagId, Value)` | `SetWorldFlag(speaker's cell, …)` | `:107`; `:367-369` |
| `record_relationship_event` (`npc_ref`, `dimension`, `delta`, `event`) | `RelationshipEventConsequence(NpcId, Dimension, Delta, EventKey)` | `ChangeRelationship` | `:110`; `:370-372` |
| `open_service` (`service`) | `OpenServiceConsequence(Service)` | publishes `ServiceOpened` only | `:113`; `:373-375` |
| `start_quest` (`quest_ref`) | `StartQuestConsequence(QuestId)` | `StartQuest(quest, speaker)`; already started is no failure | `:116`; `:376-379` |
| `recruit_companion` | `RecruitCompanionConsequence` | `Recruit(speaker)` | `:119`; `:380-382` |
| `order_companion` (`order`) | `OrderCompanionConsequence(CompanionOrder)` | `OrderCompanion(speaker, order)` | `:122`; `:383-385` |

- **FACT** "dialogue emits these as commands and never writes state itself" (`src/Domain/Social/Social.cs:97`). Only `transfer_item` can refuse a reply; the other dispatch results are ignored. `ReplyChosen` is published after the consequences (`src/World/Runtime/Social.cs:272-274`).
- **FACT** Cross-checks: an `open_service` must be offered by a participant; `recruit_companion`/`order_companion` need a participant who can join; cross-dialogue `visited` must name an existing line (`src/Content/SocialContent.cs:263-291`).

---

## 10. NPCs and identity (supporting facts)

- **FACT** `NpcDefinition(string Id, string Name, string Role, ImmutableArray<string> Services, string? MerchantId, string? DialogueId) { CompanionProfile? Companion }` (`src/Domain/Social/Social.cs:10-16`). Roles closed set (DATA_MODEL §4.5): `villager, merchant, guard, craftsperson, quest_giver, trainer, innkeeper, noble, bandit, scholar, steward` (`src/Content/SocialContent.cs:23-24`). `unique: true` required - "Phase 1 builds named NPCs only" (`:83-84`). Refused NPC fields: `schedule_ref`, `anchors`, `combat_profile`, `relationships_init`, `name_pool` (`:85-89`). **`faction_ref` on an NPC is not in that refusal list**; `npc` maps to a plain `ContentEnvelope` schema (`src/Content/SchemaTypeMapper.cs:26`) and `BuildNpcs` ignores unlisted keys - **INFERENCE (not test-verified):** an NPC `faction_ref` naming a defined `faction.*` would pass the reference pass and be silently ignored.
- **FACT** Cast (`content/npcs/ashen_hollow/`): Kera Voss (`craftsperson`, trade, `merchant.ashen_hollow.kera_voss`); Renn Vale (`steward`); Sel Arien (`scholar`); Tavar Orr (`villager`, `companion: { health: 100, weapon_item_ref: item.weapon.march_spear, armor: { head: 0, torso: 3, limbs: 1 } }`, `tavar_orr.yaml:12-15`). Placements: `content/regions/ashen_hollow.yaml:151-155` (Renn in the lodge, Sel at her survey table, Kera by the anvil, Tavar inside the fold at (145, 42)).
- **FACT** Identity: `NpcSystem.InstanceIdOf(npcId) = EntityId.Create(EntityKind.Npc, 1, hash("unnamed.npc/v1" + npcId)[0..10])` - derived, the same in every world and load; registered in the entity registry on populate (`src/World/Runtime/Social.cs:120-139`). **INFERENCE:** this is not a `<prefix>_<ULID>` from `EntityId.NewId`; it is a deterministic derived ID for authored unique NPCs (D-10). Generated/generic NPCs (M7 faction members?) would need ULIDs via the registry.
- **FACT** Non-companion NPC bodies are transient ("Transient in Phase 1: identity is derived and nothing moves them", `src/World/Runtime/RuntimeState.cs:60-61`); nothing about them is saved except a companion's body inside `CompanionRecord`.

---

## 11. Companions: the state machine (not movement)

- **FACT** Domain: `CompanionOrder { Follow, Wait }`, `CompanionCondition { Up, Downed }`, `CompanionProfile(int MaxHealth, string WeaponId, ImmutableSortedDictionary<BodyRegion,int> Armor)`, `CompanionTuning(...)` 15 fields (`src/Domain/Companions/Companions.cs:11-72`); words for condition: healthy ≥75%, wounded ≥30%, critical, downed (`:89-93`). Tuning `content/config/companion.yaml:7-21` (follow_near 2.5 m, run 4, sprint 8, catch_up 30, snag 4 s, trail step 1 m / 48 marks, fight_radius 10, leash 16, guard_radius 4, attack_pause 0.6 s, revive_window 60 s, revive 40%, regen after 10 s at 2/s).
- **FACT** `CompanionState(NpcId, Profile, Attack, Order, Condition, Health) { DownedTick, StuckTicks, LastCombatTick, Trail, Action, NextAttackTick, TargetKey }` (`src/World/Runtime/Companions.cs:59-69`); owner `CompanionSystem`, `StateSlice.Companions` (`:85-93`).
- **FACT** Transitions:
  - *not a companion → Up/Follow*: `Recruit(npcId)` from a reply; refused if the NPC cannot join; idempotent (`:160-171`). **No trust/relationship requirement in code**; gating is only in dialogue conditions. No dismiss exists.
  - *Follow ↔ Wait*: `OrderCompanionCommand` (key G, all companions) or reply `order_companion`; refused when downed or not in roster; same order is a no-op; clears trail/target (`:173-190`).
  - *Up → Downed*: `CompanionStruck` from a creature brings health to 0 (`:213-245`), publishes `CompanionDowned(npc, creatureDefId)`.
  - *Downed → Up*: `ReviveCommand` within `TalkReachMm`, health = max(1, 40%) (`:192-208`).
  - *Downed → Up/Wait at the Ashen Waystone*: after `ReviveWindowTicks`, `Fall` places him on a ring near the spawn, whole, order Wait, publishes `CompanionFell` (`:258-262`, `:514-523`).
  - Each tick while Up and in tier A: mend, mark trail, finish a swing, face the speaker if in conversation, else fight a `Target` or follow/stand (`:258-294`).
- **FACT** A downed companion cannot be talked to (`src/World/Runtime/Social.cs:235-236`). Dialogue can ask `companion_present` and order him. Roster uncapped ("Nothing here assumes how many companions there are", `src/World/Runtime/Companions.cs:91-92`).
- **FACT** Saved as `CompanionRecord(NpcId, Order, Condition, XMm, ZMm, FacingMdeg, Health) { DownedTick, StuckTicks, LastCombatTick, Trail }` (schema 12, `src/World/PlayerState.cs:52-62`).
- **FACT** Not built by ruling: "contracts, wages, morale, companion progression and equipment, anchors, dismissal, personal quests, an affinity ladder" (`docs/SYSTEMS.md:290`); M6 ruling "no affinity ladder, personal quest, full party, romance or advanced tactics" (`docs/M6_STATUS.md:9`).
- **INFERENCE** A companion is an NPC who fights *for* the player but has no faction or allegiance field; if M7 gives NPCs factions, Tavar (Orenth, Sel's guide) acting against his faction's members is a case the model must answer (companion loyalty vs faction), even though M7 should not build morale.

---

## 12. Persistence of social/quest state

- **FACT** Save schema is **13** (`src/Persistence/SaveModel.cs:20`). Steps: 9→10 "the player gains relationships and conversation memory" (`src/Persistence/Migrations.cs:597`); 10→11 quests (`:637`); 11→12 companions (`:675`); 12→13 posture (`:714`). Frozen DTO shapes `src/Persistence/Sections/SchemaV10.cs`..`SchemaV12.cs`.
- **FACT** Relationship DTO: `RelationshipDto { npc_id, dimension, value:int }` (`src/Persistence/SectionCodec.cs:114-119`), `[Key("relationships")]` on the player section (`:50`); required from schema 10 - "player.msgpack has no relationships (required from schema 10)" (`:405`); a schema-10 player without it is corrupt, not defaulted (test `tests/Persistence.Tests/MigrationTests.cs:318`).
- **FACT** Load resolves every NPC/dialogue/quest ID through the alias pass; a removed NPC "takes its record with it", reported as loss (`src/Persistence/SaveLoader.cs:316-357`).
- **FACT** World flags persist in the cell delta (`CellDeltaRecord.Flags`), proven by `baseline_hash`; creature minds in `CreatureRecord`.
- **INFERENCE** Faction standing, crime records and knowledge would be new save surfaces (a schema bump with a frozen v13 shape and a fixture, per `tests/Persistence.Tests/Fixtures/README.md` and AGENTS.md). DATA_MODEL lists "faction reputation, tiers, crime records and bounties" as save-version-sensitive (`docs/DATA_MODEL.md:846`). Player-scoped standing fits the player section (like relationships); faction-scoped state (war, alliance) is world state and would not naturally be per-player or per-cell.

---

## 13. Where faction/reputation acts would come from (INFERENCE, grounded in the seams above)

1. **Fan-in like `RecordDeed`.** The acting system dispatches one internal command describing the act (subject, actor, target/victim definition + instance, place/cell, tick). Existing dispatch points to extend: `CreatureSystem.Die` (`src/World/Runtime/Creatures.cs:701-714`), `DialogueSystem.Apply`/`Handle(ChooseCommand)` (`src/World/Runtime/Social.cs:248-277`, `:359-387`), `QuestSystem.Grant`/`Publish` (`src/World/Runtime/Quests.cs:140-178`, `:240-254`), `TradeSystem.Handle(Buy/Sell)` (`src/World/Runtime/Social.cs:484-519`), `InventorySystem.Move` for container takes (`src/World/Runtime/Items.cs:202+`), and - once NPCs can be struck - the melee/ranged sweeps (`src/World/Runtime/Combat.cs:495-504`, `:548-567`).
2. **Content-declared consequences.** A dialogue consequence (`add_reputation` per DATA_MODEL `:528`) and a quest reward kind `reputation` (already named, `src/Domain/Quests/Quests.cs:206`; accepted by the reference pass `src/Content/ContentChecks.cs:58-60`) are the smallest data-driven sources, and are "explicit acts" with no witness problem (the faction's own representative is present). They satisfy D-07 (no per-quest logic).
3. **Witnessed acts.** For acts against a faction's members/property (none possible today), a pure `Perception.Sees/Hears` check per candidate witness at the act's tick would decide who knows; the witness's faction learns only through that witness (communication modelled like `CreatureCalled`: range-limited, deterministic). This keeps "information is not magically global" and is replayable from the command log.
4. **Reads.** Dialogue conditions, quest predicates, `TradeSystem.Trader` (service gate) and `AskFor` (price) - the four places M7 standing would be consumed. Tactical hostility stays a separate derivation (`docs/SYSTEMS.md:309`), today only the creature mind machine.
5. **Relationship vs reputation.** The code already keeps per-NPC `trust/fear/grudge/respect/affection` separate from anything faction-level (there is nothing faction-level). Ruling 3's list (personal relationship, trust/fear/grudge, reputation, legal status, faction relationship, war state, known identity, tactical threat, attack legality) maps to: relationship = existing `Relationships`; trust/fear/grudge = existing dimensions; everything else = absent.

---

## 14. Owner rulings touching this topic: where recorded, and conflicts

| Ruling | Recorded (tracked) | Recorded (untracked, authority unconfirmed) | Code status / conflicts |
|---|---|---|---|
| 3. Smallest faction set; no global morality meter; keep the listed social quantities separate; same act can affect two factions differently; information not magically global | No morality meter: `docs/ROADMAP.md:282`, `docs/PROGRESSION.md:436`, `docs/SYSTEMS.md:299`, code comment `src/Domain/Social/Social.cs:27`. Hostility separate from standing: `docs/SYSTEMS.md:309`, `docs/PROGRESSION.md:444-451`. Opposite-direction fixture: `docs/ROADMAP.md:284`. No global aggro / omniscience: `docs/CLAUDE_PHASE1_EXECUTION_PROMPT.md:543`, `:552`, `:554`; `docs/IMPLEMENTATION_PRECEDENCE_AND_DESIGN_STATUS.md:128-129` | The full "keep separate" list: `OTHERREACH_MASTER_HANDOFF_2026-09-24_V4.md` §40 (lines 1400-1415); "Witnesses can be wrong, bribed or deceptive" (`:1426`); "no psychic faction omniscience" (`:459`) | Code has no faction state, so nothing conflicts yet. **Tension:** dialogue conditions read global player state and any NPC's relationship (section 7) - "magically global" by construction. **Doc conflict:** DATA_MODEL §4.13 `laws: { offense: assault, response: guards_hostile }` and `enemy_of ... (hard hostility edges)` (`docs/DATA_MODEL.md:562-563`, `:574-575`) make a faction definition decide hostility directly; ruling 3 / S-27 derive hostility from separate state (relation, war, legal status, identity, perception). Not a contradiction in words, but the §4.13 shape would couple them |
| 4. C10 (den forces sword, bow, every spell) retired | `docs/PROTOTYPE.md:298` "Revised by owner ruling (2026-09-24) ... No single encounter is required to force every combat tool"; `docs/M6_STATUS.md:105`, `:141` | V4 handoff §18 "DEN / MAGIC BALANCE - OPEN" (`OTHERREACH_MASTER_HANDOFF_2026-09-24_V4.md:609-622`), "not yet formally owner-ratified" | **Conflict:** the untracked V4 handoff still calls C10 open/unratified; tracked `PROTOTYPE.md:298` records the ruling. The tracked record wins |
| 5. No core action may require a radial menu | Content bible §20: `docs/PHASE1_ASHEN_HOLLOW_PLAYABLE_CONTENT_BIBLE.md:639-645`, `:288`; companion orders "no radial menu (bible §9)" `docs/M6_STATUS.md:56` | `PHASE1_HUD_UI_CONTROLS_AND_FEEDBACK_SPEC.md:17-23`; V4 handoff `:78-82` | Code complies: companion orders are key G or dialogue (`OrderCompanionCommand`), dialogue is numbered keys. **Mild doc tension:** `docs/HUD_INPUT_AND_ACTIONS.md:30` lists "radial menus" as an access method and `:36` says "Controller users should retain the same underlying capability through context/radials" - compatible only if "context" alone suffices |
| 6. No networking in M7; preserve seams | - (not searched in depth; another reader) | V4 handoff `:47`, `:131`, `:1514` | Code: single actor; every handler refuses a non-player `Actor`, but commands already carry `Actor` - a seam |
| 7. Engine-independent C#; presentation observes/submits; dotted IDs; ULIDs; sparse deltas | `AGENTS.md`; `src/World/Runtime/Commands.cs:10-13`; `src/World/Runtime/Events.cs:10-11` | - | Complies. NPC IDs are derived, not ULIDs (section 10) - by design (D-10) |
| 1, 2 (navigation, one storey) | ROADMAP M7 exit "the navmesh updates on placement" (`docs/ROADMAP.md:284`) and entry "NPCs and companions path reliably" (`:281`) | - | Out of this topic; noted only because "navmesh" wording could be read as Godot navigation (ruling 1). Code today: no pathfinding at all - companions walk trail marks and straight lines (`docs/M6_STATUS.md:57`), other NPCs never move |

---

## 15. Contradictions found (doc vs doc, doc vs code, code vs code)

1. **Code comment vs code:** `DialogueCondition` doc says "Phase 1 builds seven kinds" (`src/Domain/Social/Social.cs:58`); eight are built (`src/Content/SocialContent.cs:26-27`).
2. **Code comment vs code:** `StateSlice.Creatures` says "A creature's mind is transient" (`src/World/Runtime/RuntimeState.cs:51`), but `CreatureRecord` persists `Mind`, `Awareness`, `Knows`, known position etc. (`src/World/WorldDelta.cs:153-184`). Only a blow in progress is transient (`src/World/Runtime/Creatures.cs:127-128`).
3. **Doc vs code (units):** DATA_MODEL's dialogue example gates on `{ kind: relationship, ..., dimension: trust, min: 0.1 }` (`docs/DATA_MODEL.md:544`) and §4.13 uses `attitude_default` in [-1, 1] (`:560`, `:565`); code relationships are integers in [-100, 100] (`src/Domain/Social/Social.cs:32-33`) and a fractional `min` would fail the content parser ("must be a whole number", `src/Content/SocialContent.cs:303-306`).
4. **Doc vs doc (reputation scale):** `PROGRESSION.md` §10 defines an 11-step tier ladder `Anathema(-5) … Exalted(+5)` (`docs/PROGRESSION.md:432-434`, ladder at `:434`); DATA_MODEL §4.13 uses named tiers with numeric mins `outsider -100 / tolerated 0 / trusted 150 / sworn 400` (`docs/DATA_MODEL.md:568-572`); `VERTICAL_SLICE.md:169` says "Five tiers per faction". Three different ladders.
5. **Doc vs code (dialogue vocabulary):** DATA_MODEL's closed condition list includes `reputation`, `knows_fact`, `time_of_day`, `faction_state` and its consequence list `advance_quest`, `complete_objective`, `fail_objective`, `add_reputation`, `award_xp`, `know_fact`, `set_price_modifier`, `start_combat`, `relocate_npc`, `unlock_travel_node`, `play_scene` (`docs/DATA_MODEL.md:525-529`); code builds none of those, and builds `recruit_companion`/`order_companion`, which are "outside §4.12's list" (acknowledged at `docs/DATA_MODEL.md:555`).
6. **Doc vs code (S-26 scope):** SYSTEMS S-26 "Owns. Per-pair relationship values ... the memory log of attributed events ... and derived disposition tiers" and reads "S-12 (who the player attacked)", "S-14/S-36 (gift and trade events - an item given or sold to an NPC is what moves affinity)" (`docs/SYSTEMS.md:294-296`); code has player-only values, no log, no tiers, and neither combat, gifts nor trade move relationships. Its own M4 reconciliation (`:300`) records the subset, so this is acknowledged deferral, not an error.
7. **Doc vs code (trade gating):** M4 says "a service opens the panel" (`docs/M4_STATUS.md:31`) - true for presentation, but the simulation does not require it: `BuyCommand` is accepted without a conversation (section 8). Any doc that assumes dialogue-gated trade is enforced is wrong about the simulation.
8. **Doc vs code (quest debugger reach):** M5 status says a `relationship_value` objective lists "a reply of a conversation" as a way; quest `relationship` rewards are also a way but are not listed (`src/World/Runtime/QuestDebugger.cs:234-241`). Minor.
9. **Untracked vs tracked:** V4 handoff §18 treats the C10 change as open (`OTHERREACH_MASTER_HANDOFF_2026-09-24_V4.md:609-622`); tracked `docs/PROTOTYPE.md:298` records the owner ruling.
10. **Stale status text (historical, not a defect):** `docs/M4_STATUS.md:18` lists six conditions and five consequences; `docs/M5_STATUS.md:29` says "within 20 m of the waystation" while content is `within_m: 25` (`content/quests/ashen_hollow/iron_under_ash.yaml:30`, changed in M6, `docs/M6_STATUS.md:24`).

---

## 16. Numbers and names a designer may need (quick reference)

- Relationship: `int` in [-100, 100]; 5 dims `affection, fear, grudge, respect, trust`; content delta 1..100 either way; 0 not stored.
- Dialogue reach: `TalkReachMm` = inventory reach + body radius (1.95 m); conversation ends beyond reach + 1 m.
- Flags: `world.*` IDs, `long`, per 100 m cell, 0 = baseline; `type: bool|int`, `default: 0`.
- Quest evaluation: once per tick, last in the step; trace 64 entries.
- Prices: buy `ceil(value * price_bias)`, sell `floor(value * 0.4)`; wares 48 stack slots.
- Perception: awareness 0..100; suspicious 30; sight gain 40/s at range to 200/s close; decay 10/s; heard noise 60; heard call 80; search 6 s; noise radii walk 3 / run 8 / sprint 16 / swing 10 / blow 20 / call 45 m.
- Companion: fight radius 10 m, leash 16 m, guard 4 m, revive window 60 s at 40%, creature turns on a companion nearer by > 1 m.
- Save schema 13; relationships since 10, quests 11, companions 12.
- Reserved but unbuilt content kinds: `faction` (`faction.` prefix, `factions/`), `fact` (`fact.` prefix, `facts/`).

---

## 17. Open questions this reading raises

1. Which dialogue conditions may an NPC legitimately "know"? Today any NPC can react to any quest's state, anything carried, lines heard elsewhere, and any other NPC's relationship value. Should M7 restrict reputation reads to the speaker's faction and to what that faction has learned?
2. Is reputation per player-faction pair only (like relationships are per player-NPC), or do faction-to-faction relations and war state live as world state? If world state, where (not cell flags)?
3. Should `open_service` become enforced simulation state (service availability owned by a system that `TradeSystem` asks), so reputation gating cannot be bypassed by a direct `BuyCommand`?
4. Should relationship attribution (event keys) finally be persisted (a bounded memory log, S-26), since reputation reactions will want "why"? That is a save-schema change.
5. Can M7 meet "the same act moves two factions in opposite directions" with existing acts (a creature kill, a quest completion, a dialogue reply) keyed by definition ID, so that no NPC-combat is needed in M7? Or does the owner want NPCs attackable in M7 (which forces health, targeting, attack legality and witness checks)?
6. How does a companion's allegiance interact with factions (Tavar is Orenth and Sel's guide)?
7. Do generated faction members need ULID instance IDs and persisted life state (DATA_MODEL/SYSTEMS S-24 say yes: "deaths and relocations must never be silently dropped", `docs/DATA_MODEL.md:846`), given today's NPC bodies are transient and IDs derived?
8. Which reputation ladder is canonical: `PROGRESSION.md` §10 (−5..+5 named tiers), DATA_MODEL §4.13 (outsider/tolerated/trusted/sworn with numeric mins), or VERTICAL_SLICE (five tiers)?
9. Does "information is not magically global" also cover *player-facing* knowledge in dialogue content already shipped (Sel's cross-dialogue `visited` check)? It is benign fiction today (she watched from the rise, per her line), but a rule is needed before faction content multiplies it.
