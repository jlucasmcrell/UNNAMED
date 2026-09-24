# M4 status - Settlement NPCs, persistence, dialogue

**Date:** 2026-09-24. **Branch:** `claude/phase1`. **State:** implemented and verified; Phase-1 subset only (the owner's ruling: a small NPC population, the prototype's, fully simulated; tier transitions and the 200-promotion spike deferred to the first milestone with simulation tiers; schedules deferred to the vertical slice; NPCs keep identity, relevant life state and basic continuity; dialogue deterministic and structured, no runtime language model; social scope is structured dialogue and minimal continuity).

**Entry:** M3f complete (`5a9d45f`): items, crafting and combat stand, so an NPC can exist, act and be dealt with.

## The ruling, as applied

- **The prototype's population, as the content bible casts it** (`PHASE1_ASHEN_HOLLOW_PLAYABLE_CONTENT_BIBLE.md` §9): Renn Vale the waystation's steward, Kera Voss the smith and Sel Arien the archivist. They take the places of `PROTOTYPE.md`'s keeper, smith and archivist-to-be. The bible's fourth, Tavar Orr, is the companion (M6).
- **Fully simulated, no tiers, no schedules.** Every NPC is simulated all the time; each stands at their place in the settlement and turns to whoever talks to them. ROADMAP M4 was rewritten to this scope first, keeping the risk spike's text as deferred.
- **Continuity:** identity (derived, D-10), what the character has heard of each conversation, and what each NPC thinks of the character survive save and load.
- **Structured dialogue only:** conversations are data - closed sets of conditions and consequences over world state - and a consequence is a command to the system that owns what it changes. No language model, no persuasion, rumour or language simulation.

## What M4 built

| Layer | What | Where |
|---|---|---|
| Domain | `NpcDefinition`, `NpcServices` (trade), `Relationships` (five named dimensions, held in [-100, 100]), and dialogue as data: `DialogueDefinition`, nodes and replies, six conditions (`visited`, `world_state`, `has_item`, `relationship`, `skill`, `level`), five consequences (`transfer_item`, `give_recipe`, `set_world_flag`, `record_relationship_event`, `open_service`), and `DialogueRules` (whether a condition holds, whether a reply is offered, where a spent line passes on to) | `src/Domain/Social/Social.cs` |
| World | `TalkCommand`, `ChooseCommand`, `LeaveCommand`, `BuyCommand`, `SellCommand`. `NpcSystem` owns the NPCs' bodies (`StateSlice.Npcs`); `RelationshipSystem` owns what they think (`StateSlice.Relationships`); `DialogueSystem` owns the lines heard and the open conversation (`StateSlice.Conversations`); `TradeSystem` owns nothing. A trader's wares are a container at the trader, and a trade is one internal `Trade` - a stack's move and the coin the other way. NPC bodies block movement. Events: `ConversationStarted`, `ConversationLine`, `ReplyChosen`, `ConversationEnded`, `ServiceOpened`, `RelationshipChanged`, `ItemBought`, `ItemSold`; views: `NpcView`, `ConversationView`, `WaresView` | `src/World/Runtime/Social.cs`, `Items.cs`, `Systems.cs`, `RuntimeState.cs`, `Simulation.cs` |
| Content | Three NPCs (`npcs/ashen_hollow/`) and their conversations (`dialogue/ashen_hollow/`); the region's `npcs` placements (WLD012); M3b's merchant renamed `merchant.ashen_hollow.kera_voss`; the Resonance Primer moved from the longhouse shelf to Sel (the shelf and its loot table are gone); the two recipes moved from the starting package to Kera. `SocialContent` builds and lints them (SOC001). 89 definitions | `content/`, `src/Content/SocialContent.cs`, `WorldContent.cs` |
| Persistence | **Schema 10**: the player's relationships (`npc_id`, `dimension`, `value`) and conversation memory (`dialogue_id`, `heard`), both required and through the definition-ID pass. The 9 -> 10 step gives older saves none; the schema-9 player shape is frozen (`Sections/SchemaV9.cs`). A trader's wares need nothing new: once touched they are a changed container (schema 6) | `src/Persistence/`, `src/World/PlayerState.cs` |
| Presentation | NPCs as full-body mannequins in their own colours; "[E] Talk to ..." within reach; a conversation panel with the speaker, the line and numbered replies (keys 1 to 9 answer, Escape walks away); a trader's wares beside the inventory (Buy 1, Buy all, Sell per carried stack, at their prices); toasts for trades, the log for what an NPC thinks | `src/Presentation` |

## How it works

- **Where they stand.** Renn in the longhouse, facing its door; Sel in the longhouse's old-books corner; Kera in the forge shed by the anvil. Talking and trading need the character within a hand's reach of their body (1.6 m plus their 0.35 m).
- **Renn** greets the character once, then offers orientation (the place, who needs a hand) and acknowledges the world: carrying iron ore from the shelf earns a line and his respect, once; carrying a spear, another. PROTOTYPE's branching conversation: six nodes, two branch points.
- **Kera** teaches the forge - both recipes, a teacher's lesson (`PROGRESSION.md` §4.4) - and opens her wares. Shown a spear, she judges it by its quality: a fine one earns her respect, once.
- **Sel** lends the Resonance Primer when asked, once, and afterwards talks of Strain and of the Foldscar stones, uncertainly.
- **Once-lines.** A line is marked heard when shown; a `once` line already heard passes on to its `next_if_exhausted`. Greetings, lessons and gifts are spent this way, and stay spent across a load.
- **Consequences.** An item changing hands goes first and all or nothing - a full pack refuses the reply before anything else happens. A recipe is taught through progression, a relationship moves through its system and says why, a flag is set in the speaker's cell, and a service opens the panel.
- **Trade.** A trader asks each ware's value times their bias for it and pays 40% of value for what their trade takes (M3b's `config.economy`). What they buy joins their wares. Coin and goods move in one exchange; a refused trade moves nothing. Their purse has no bottom.

## Divergences from the content bible (recorded, not rebuilt - the M3d ruling)

| The bible | M4 | Why |
|---|---|---|
| The settlement in Cell A (X 0-100, Z 100-200): Kera's smithy at (58, 142), the steward's lodge at (44, 128), Sel's survey area at (72, 122) | Renn and Sel in the M3 longhouse, Kera in the M3 forge shed, in the outpost at (35-75, 25-65) | The working M3 outpost holds the settlement's buildings; the layout reconciliation to the bible's four cells is due before M6 acceptance |
| Kera's teaching belongs to Quest 1, "Iron Under Ash" | Kera teaches when asked | Quests are M5's; the quest will wrap this conversation |
| Tavar Orr in the roster | Not placed | The companion is M6's |

## Exit criteria (`ROADMAP.md` M4, as rewritten)

| Criterion | Evidence |
|---|---|
| Each NPC stands in the world and can be talked to | `TheWaystationsPeople_StandWhereTheRegionPutsThem` (three, where the region puts them, the same derived instance in every world, solid), `AConversation_OpensWithinReach_AndItsRepliesFollowFromWorldState` (out of reach and unknown names refused) |
| Lines and replies follow from their conditions; consequences happen through their owners' commands | `AConversation_OpensWithinReach_AndItsRepliesFollowFromWorldState` (a spent greeting passes on; what is carried changes what may be said), `Kera_TeachesTheForge_AsATeacher_AndOpensHerWares` (two recipes learned from a teacher, trust +5 with its reason, trade opened), `ASpearShownToKera_GetsTheWordItsQualityEarns_Once`, `Sel_LendsThePrimer_AndAReplyThatCannotGiveIt_IsRefusedWhole` (a full pack refuses the reply and nothing else happens), `AFlagSetInConversation_LivesInTheSpeakersCell_WhereItsConditionReadsIt` (Renn opens the longhouse door by its flag), `DialogueRulesTests` |
| What is said once stays said, and what each NPC thinks stays thought, across save/load | `WhatWasSaidAndThought_ContinuesAcrossASaveAndLoad` (identical digest; the lines heard and Sel's trust survive; her greeting and gift stay spent); the headless smoke's quicksave; the schema-10 fixture |
| Buying and selling move coin and goods exactly, refuse cleanly, and the trader's stock survives save/load | `BuyingAndSelling_MoveCoinAndGoodsExactly_AndRefuseCleanly` (20 arrows for 20 coin; two hides at 40% of value join her wares; too little coin, an unknown ware, a quest token, an equipped sword, out of reach and a steward who does not trade are all refused; her wares are no chest), `ATradersWares_SurviveASaveAndLoad` |

Also proven: the SOC001 lint refuses a reply to a node that is not there, a condition or consequence Phase 1 does not build, a loop of spent lines, a trader without stock, a service opened by someone who does not offer it, a nameless crowd and a relationship on a dimension there is not (`SocialContentTests`); a conversation ends when the character walks away, and the speaker turns back to their work (`WalkingAway_EndsTheConversation_AndTheSpeakerTurnsBack`); the M3f loop now learns the forge from Kera (`TheLoop_PlaysEndToEnd_GatherSmeltForgeEquipFight`); schema 9 migrates to no relationships and no conversations, and a schema-10 player without either is corruption; all ten historical fixtures load and migrate, v10 with the warden's renamed ID reaching both records.

## Decisions (flippable)

1. **The cast and where they stand**, above: the bible's three, in the M3 outpost.
2. **Identity is derived** from the NPC's ID, and nothing about an NPC's body is saved while nothing can move or harm them. Life state, relocation and death arrive with the companion (M6), the first thing that moves an NPC.
3. **Five relationship dimensions** (trust, respect, affection, fear, grudge) in [-100, 100], zero not stored, no memory log yet: the event names on `RelationshipChanged` are the attribution.
4. **Lines are inline text** until localization; `text_key` is their later form.
5. **One item a reply, first, all or nothing**, so a reply is never half-done.
6. **Dialogue's world flags live in the speaker's cell.**
7. **Trade through the wares container**: the stock is the authored stock until the first trade, what the trader buys joins it (buy-back at full value), the trader's purse is bottomless, and quality does not change a price yet.
8. **The primer and the recipes come from people**: Sel and Kera, so the starting package is empty again - the bible's starting character knows no craft and no working.
9. **The panel's keys**: 1 to 9 answer, Escape walks away; formulas cannot be cast while a conversation is open.

## Verification

- `dotnet test` (from `src/`): **598 passed**, 0 failed. Architecture 14, Content 111, Domain 133, EntityRegistry 23, World 60, Persistence 142, Application 115.
- Content lint: 89 definitions, 0 errors.
- Godot 4.7.2 headless smoke `PASS`: into the longhouse; the conversation that hands over a teaching book found in the data and followed (Sel, three lines); the primer read, a ward worked, a swing that misses; a quicksave that loads to the identical digest with the conversation remembered.
- The windowed `--ui-shots` run (ASTRAL) greets the people before the M3e and M3f pictures: `renn.png` (Renn's greeting and its three replies), `sel.png` and `sel_primer.png` (Sel lends the primer; `learned.png` is it read), `kera.png` and `lesson.png` (Kera teaches the forge, trust +5 in the log) before the hearth and the anvil, then `trade.png` and `traded.png` after the spear is forged (the rusted sword sold for 6, six arrows bought for 6; a stack worth nothing offers no Sell), then the M3f loop's spear, thrust and death.

## Not done, and why

- Simulation tiers, tier transitions and the promotion spike, and schedules: deferred by the owner's ruling.
- NPC death, relocation and follow: the companion, M6.
- Quests (M5) will wrap Kera's and Sel's conversations; faction and reputation are Phase 2 (M7); knowledge facts, memory logs, persuasion, rumour and languages are future (`SOCIAL_INTERACTION_LANGUAGES_AND_KNOWLEDGE.md`).
- The layout reconciliation to the bible's four cells, before M6 acceptance.
- The M3 performance gate still waits on the RAZER window.

## Commit, state, next

- **Commit:** one M4 commit on `claude/phase1` (its hash opens M5's status), pushed to draft PR #1.
- **State:** the worktree is clean after it.
- **Next:** M5, the quest framework and its debugger.
