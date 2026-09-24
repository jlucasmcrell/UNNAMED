# M5 status - Quest framework and quest debugger

**Date:** 2026-09-24. **Branch:** `claude/phase1`. **State:** implemented and verified. The execution prompt's M5 (§17): the declarative quest graph and objective system, the mandatory debugger ("What is this quest waiting on right now?"), at least one purposeful non-kill objective working, and no large quest volume.

**Entry:** M4 complete (`d8fe385`): world state is queryable and NPC state persists, so quest objectives can be predicates over it (D-07).

## Scope, as applied

- **The framework is the closed vocabulary of `DATA_MODEL.md` §4.11, built where its systems exist.** Eleven objective types are built; the rest of the closed set (construction, bosses, factions, puzzles, companions, knowledge, the time of day...) is named, and the lint refuses each with what it waits for. The same for reward kinds. ROADMAP M5 gained a "Phase-1 build" note saying so; its work, exit criteria and proof are unchanged.
- **One quest is authored:** the content bible's Quest 1, *Iron Under Ash* (§15), given by Kera Voss. It has no kill objective at all, so every objective in it is a purposeful non-kill one. The bible's Quest 2 ends in recruiting Tavar, the companion, and lands with M6.
- **The debugger is a tool in the running game as well as an API:** `Simulation.Diagnose(questId)`, and the quest debugger panel on F4.

## What M5 built

| Layer | What | Where |
|---|---|---|
| Domain | `QuestDefinition` (objectives in authored order), `ObjectiveDefinition` (`next`, `all_of`, `branch: first`, hidden, a time limit and `on_fail`), eleven objective conditions, seven reward kinds, `QuestState`/`ObjectiveState` and their saved keys, `IQuestFacts`, deeds, and `QuestRules` as pure functions: `Start`, `Evaluate` (a predicate's terms with their values), `Record` (a deed toward active objectives), `Advance` (one evaluation: cascade, branches, joins, timers, `fail_if`, completion). Dialogue gains `quest_state` and `start_quest` | `src/Domain/Quests/Quests.cs`, `src/Domain/Social/Social.cs` |
| World | `QuestSystem` owns `StateSlice.Quests`: `StartQuest` from dialogue, `RecordDeed` from crafting, gathering, creatures and dialogue, an evaluation of every active quest each tick after everything else has moved, rewards through their owners' commands once (`AddCurrency` and `GrantItem` are new to the inventory). Events `QuestStarted`, `ObjectiveActivated`, `ObjectiveSatisfied`, `ObjectiveFailed`, `ObjectiveClosed`, `QuestBranchTaken`, `QuestCompleted`, `QuestFailed`, `RewardGranted`; the journal's `QuestView`. `QuestDebugger` and `Simulation.Diagnose` | `src/World/Runtime/Quests.cs`, `QuestDebugger.cs`, `Social.cs`, `Crafting.cs`, `Creatures.cs`, `Items.cs`, `Simulation.cs`, `RuntimeState.cs` |
| Content | `quests/ashen_hollow/iron_under_ash.yaml`; Kera's "teach" reply starts it, her lesson now asks for the iron, and she says where it is while it is needed; `QuestContent` builds and lints quests (QST001); a quest's `giver_ref` resolves as an NPC. 90 definitions | `content/`, `src/Content/QuestContent.cs`, `SocialContent.cs`, `ContentChecks.cs` |
| Persistence | **Schema 11**: the player's quests - status, start, end and what ended it, and per objective its status, activation, end and deed progress - required, through the definition-ID pass; the schema-10 player shape frozen (`Sections/SchemaV10.cs`); v11 fixture (writer pack 0.1.6: an errand half done and a cull finished; pack 0.2.8 renames the errand) | `src/Persistence/`, `src/World/PlayerState.cs` |
| Presentation | The quest tracker at the top right (the bible's §19: the title and what it asks now); the journal (J); the quest debugger panel (F4; F9 stays quickload); toasts for a quest started, a step done, a quest complete or failed, and rewards in the log | `src/Presentation/Ui/JournalPanel.cs`, `Hud.cs`, `Main.cs` |

## How it works

- **Starting.** A conversation's reply starts a quest (`start_quest`); starting one already started does nothing. Its entry objective is active from that tick.
- **Evaluating.** Once a tick, after movement, combat, conversations and discovery have run, each active quest's active objectives are evaluated in authored order. One that holds is satisfied and activates what follows it, and the evaluation repeats until nothing more changes, so whatever the world already satisfies completes at once and in order. This is how Iron Under Ash tolerates the shelf visited, or ore got, before Kera asked (bible §32).
- **Two kinds of predicate.** State predicates read the world as it stands: lines heard, places discovered, the distance to a place, what is carried, a world flag in the cell of a named place, what an NPC thinks. Deed predicates (`craft_item`, `harvest_resource`, `kill_creature`, `deliver_item`) count what the systems that do those things report while the objective is active. That count is the objective's saved progress, the one thing a quest keeps that the world cannot derive (S-29).
- **Structure.** `next` is a sequence or a fork; `branch: first` makes the fork a choice, where the first alternative satisfied is the branch taken and the others close; `all_of` is a join. A timed objective fails at its deadline unless it holds on that tick, and `on_fail` names what becomes active instead (a setback, per the quest design doc's "prefer consequence over a simple QUEST FAILED") or `fail_quest`. `fail_if` ends a quest still active. A satisfied objective with nowhere to go completes the quest, and everything still open closes. Satisfied, failed and closed objectives never change again, and a finished quest is never evaluated again, so its rewards are paid once.
- **Iron Under Ash** (`o_speak` -> `o_shelf` -> `o_ore` -> `o_return` -> `o_billet` -> `o_spear` -> `o_show`): Kera's lesson heard; the iron shelf discovered; raw iron ore carried; within 20 m of the waystation; a billet made while that step is active; then the spear; then Kera's word on it (`praised` or `judged`). It pays 120 XP, 25 coin and Kera's respect +5.

## The debugger

`Simulation.Diagnose(questId)` answers the question in one line, then gives, for every objective it waits on, each predicate term with its value now and the value wanted, and every way this world offers to satisfy it: a node and whether it is worked out, a recipe and whether it is known and its inputs carried, a trader's stock and the coin carried, a container's contents, a creature's drops and how many live, a reply of a conversation with each condition that keeps it from being offered, a door or reply that sets a flag, and the flag's value in any other cell. Then the problems that mean the quest cannot complete as things stand: nothing in the world can supply an item, a join whose prerequisite closed, a stall with nothing active, a saved objective the quest no longer has. Then the trace: every evaluation, with identical consecutive ones collapsed into one entry and counted, so the last 64 entries reach back past any wait. A quest not started says what starts it; a finished one says when and by what, and for a failure why.

Transcript of a deliberately broken quest being resolved (`TheDebugger_ExplainsAConversationThatCannotReachItsLine_AndTheFixCompletesIt`, written with `UNNAMED_QUEST_TRANSCRIPT` set). The quest waits for Kera's praise; the character's spear is standard:

```text
Worth Praising (quest.test.praise) - active
What is this quest waiting on right now? Waiting on o_praise (Earn Kera's praise.): heard 'praised' (dialogue.ashen_hollow.kera_voss) = no, wanted yes.
  o_praise [talk_to] Earn Kera's praise. - active since tick 0
    [ ] heard 'praised' (dialogue.ashen_hollow.kera_voss) = no (wanted yes)
    - talk to Kera Voss (68.6, 31.6), within 2.0 m; now 1.5 m away
    - 'praised' is reached by Kera Voss's reply 'show_fine' at the line 'again' (dialogue.ashen_hollow.kera_voss): not offered now - needs 1 item.weapon.march_spear of quality >= 1 carried; 0 are
Trace (latest last):
  tick 1
    o_praise: heard 'praised' (dialogue.ashen_hollow.kera_voss) = no (wanted yes) waiting

--- after the fix (a fine spear shown) ---
Worth Praising (quest.test.praise) - completed
What is this quest waiting on right now? Nothing: completed at tick 1 by o_praise.
Trace (latest last):
  tick 1
    o_praise: heard 'praised' (dialogue.ashen_hollow.kera_voss) = yes (wanted yes) holds
    -> o_praise satisfied
    -> quest completed by o_praise
```

## Divergences from the content bible (recorded, not rebuilt - the M3d ruling)

| The bible | M5 | Why |
|---|---|---|
| "Reach Blackvein Cut" (Cell C) | Reach the iron shelf north of the hollow (`location.iron_shelf`) | The M3 layout's iron is on the shelf; the layout reconciliation is due before M6 acceptance |
| "Return to Ashen Hollow" | Within 20 m of the outpost | The same |
| "Receive *Iron Under Ash*" at about 2:00, from Kera | Given when Kera is asked to teach the forge | Her lesson and her quest are one conversation, so knowing the forge and being asked for iron arrive together |
| Quest 2, *The Three Quiet Stones* | Not built | It ends in Tavar's recruitment (M6) and needs the Foldscar's stones, which the layout reconciliation places |

## Exit criteria (`ROADMAP.md` M5)

| Criterion | Evidence |
|---|---|
| The charter's TESTING item "quest state" is green: persistence across save/load, branch integrity, no orphaned objectives | `QuestState_ContinuesAcrossASaveAndLoad` (identical digest, the same journal, and the quest goes on after the load); the schema-11 fixture and migration tests; `ABranch_TakesTheFirstAlternativeSatisfied_AndClosesTheRest`, `AJoin_WaitsForAllItsObjectives_InAnyOrder`, `ATimedObjective_FailsAtItsDeadline_UnlessItHolds_AndFailureGoesWhereItSays`, `FailIf_EndsTheQuest_WhenItsPredicateHolds_AndClosesWhatWasActive`, `ASatisfiedObjective_StaysSatisfied_AndAFinishedQuestIsNeverEvaluatedAgain`, `Deeds_CountOnlyWhileTheirObjectiveIsActive_AndOnlyTheDeedItWaitsFor`; orphans, loops and impossible joins refused by the lint (`AnOrphan_ALoop_AndAMissingObjective_AreRefused`, `AJoinThatCanNeverHappen_IsRefused`) |
| The debugger correctly explains three deliberately-failing quests | `TheDebugger_ExplainsAQuestTheWorldCanNoLongerSatisfy` (nine ore wanted, the seam worked out: "nothing in this world can supply item.material.iron_ore now"), `TheDebugger_ExplainsAConversationThatCannotReachItsLine_AndTheFixCompletesIt` (the reply and the condition stopping it; the fix completes it), `TheDebugger_ExplainsAQuestThatRanOutOfTime` (why it failed, with the collapsed trace of forty identical evaluations); also `TheDebugger_FindsAJoinThatCanNeverHappen` (a shape the lint refuses, still diagnosed for a save made under older content) and `TheDebugger_SaysWhatIronUnderAshIsWaitingOn_AndHowItStarts` |
| A content-validation rule rejects a quest referencing a nonexistent objective type, item, NPC or faction | `AnObjectiveTypeOutsideTheClosedSet_IsRefused`, `AnItemThatDoesNotExist_IsRefused`, `AnNpcThatDoesNotExist_IsRefused`, `AFactionThatDoesNotExist_IsRefused` (each a QST001 error, and for references the reference pass's XREF error too); also a type not built yet, a quest nothing starts, a line the conversation lacks, a craft no recipe makes, and a `quest_state` naming an objective the quest lacks |
| Playable state: the game has real objectives, and a session can diagnose why one will not complete | `IronUnderAsh_PlaysEndToEnd_WithoutAFight_AndPaysOnce` walks the real world from Kera to the shelf and back and pays once; the journal, the tracker and the F4 debugger in the windowed run |

The execution prompt's "at least one purposeful non-kill objective must work": Iron Under Ash has no kill objective at all, and the end-to-end test asserts no blow was struck.

## Decisions (flippable)

1. **Eleven types built, the rest named and refused** until their systems exist.
2. **`branch: first`** is the one choice mechanism; alternatives that are never reached are closed too, so nothing reaches them later.
3. **State predicates count what came before; deed predicates do not.** A place visited or an item carried before the quest counts (bible §32); a craft, harvest, kill or delivery counts only while its objective is active (PROTOTYPE's "crafted since O1").
4. **Evaluation once a tick, last in the step,** cascading within the tick.
5. **Objectives before timers, timers before `fail_if`:** holding on the deadline tick counts, and a quest that completes and fails on one tick completes.
6. **A world flag is read in the cell of a named place,** because flags live in cells: `world_state` and the `world_flag` reward take a `location_ref`.
7. **A reward item never disappears:** it goes into the pack, or where the character stands when the pack cannot take it.
8. **Offer and acceptance are one reply.** No offer, decline, abandon or repeat until something needs them.
9. **Kera's lesson starts the quest** (above), and she says where the iron is while it is needed.
10. **The debugger's trace is transient** and keeps 64 distinct evaluations per quest.

## Verification

- `dotnet test` (from `src/`): **634 passed**, 0 failed. Architecture 14, Content 125, Domain 142, EntityRegistry 23, World 60, Persistence 146, Application 124.
- Content lint: 90 definitions, 0 errors; the fixture pack 21, 0 errors.
- Godot 4.7.2 headless smoke `PASS`: after Sel's book, the conversation that starts a quest is found in the data and followed (Kera, *Iron Under Ash*, one objective done at once), then the book is read, a ward worked and a swing missed, and the quicksave loads back to the identical digest with the quest where it was.
- The windowed `--ui-shots` run (ASTRAL) plays M4's route with the quest in it. The ore and the shelf are behind the character when Kera teaches the forge, so the lesson satisfies four objectives at once and the quest asks for a billet: `lesson.png` (the tracker at the top right), `journal.png` (four done, the billet next), `quest_debug.png` (F4: the billet objective's term, the recipe known and its ore carried, and the trace with the cascade tick and the collapsed evaluations since); after the hearth, the anvil and the trade, `shown.png` (Kera's word on the spear), `quest_done.png` ("Quest complete: Iron Under Ash", +120 XP, coin 25, Kera's respect +5 in the log) and `journal_done.png` (all seven done); then the M3f loop's spear, thrust and death.

## Not done, and why

- Quest 2 and Tavar: M6, with the layout reconciliation that places the Foldscar and its stones.
- The rest of the closed vocabulary, optional objectives, composition (`any_of`, `not`), requirements, time-of-day gates, other timer anchors, chains, offer/decline/abandon and repeat: none is needed by Phase 1's quests.
- Journal entries per step (`journal_entries`) and localization keys: the objective descriptions are the journal.
- The M3 performance gate still waits on the RAZER window.

## Commit, state, next

- **Commit:** one M5 commit on `claude/phase1` (its hash opens M6's status), pushed to draft PR #1.
- **State:** the worktree is clean after it.
- **Next:** M6, the companion, then the stop for the owner's playtest.
