# Act I content port — 2026-10-17

Source: `jlucasmcrell/otherreach-canon` PR #2, approved revision `05737c7dbdcb7c307ddb66a718360183659855fa`, especially `proposals/new/story/A_BREATH_LATE_ACT1/PORT_MAP.md` and M1–M8/L1. Base: `cloud/base-20261017@685dc539`, schema 26. This port changes content, its integration tests and this handoff only. No production C#, save schema, routines, clock, generation rules, networking or assets are changed.

## Available beats

| Beat | Ported content |
|---|---|
| M1 Arrival | Existing `quest.main.the_road_in` summary/objective text; OD-01/PJ06 provenance retained, resolved placeholder/TODO wording removed. Four unique start-only relics in one-slot authored pickups near the wake point, using existing loot interaction. Sel can catalogue each without taking it. Player-supplied robin/dandelion names use supported choice effects/journal lines, not an origin assertion. |
| M2 Folded Note | Explicit new starting note, separate from the unreadable relic page; Renn's note-reading with optional remembered/uncertain/private replies; lodge visit and Berrin acknowledgement. No absent firstplay node or quest is patched. |
| M3 Wrong Side | Main account overlays the existing Quiet Stone rescue; Sel's grazer observation and Tavar's afternoon. The early trust reply requires both unset `world.main.gauge_seen` and an unheard account. It cannot be repeated for trust. The future M5 flag is defined but never falsely written in this port. |
| M4 Closed Seam | Existing iron/crafting quest remains intact. One ledger pickup at Blackvein, its deliberate-seal observation and Kera's cut comparison; no second Cairnwold seam. Durn's brief optional packing discussion preserves the proposed voice; physical crag breach remains the separate Q-05 level/prop task below. |
| M6 Moss Lights | Extends, without replacing, Mara's original three-ashbloom hand-in. Inspect the moss wet boundary, compare with Mara, visit the lodge and hear Berrin's earlier records. Daylight/shade fallback; no compulsory night wait or pretend scripted escort. Berrin's doors remain an optional practical comparison. |
| M7 Three Accounts | Actual shrine field circuit, return/read accounts, defer or choose one carrier. Three `dialogue_choice` alternatives under `branch: first`; `choice` effects write **only** the outpost cell. Saved carrier values 1/2/3 map to the roadwarden/Daun/Ilesa. Packets remain **flag-only**; no item definitions, inventory transfer or invented faction/pricing deltas. |
| M8 Road East | Existing Berrin `rim_stone` replies start the main rite quest. Existing stone interaction wakes/learns Rim; binding remains separate. Renn voices both the same-afternoon correlation and older-bleed evidence, once Tavar's account has been heard (The Wrong Side complete). Original end-of-playtest feedback stays, with consistent road-east wording and an honest playable boundary. |
| L1 Ledger | Side-track Lantern introduction uses the same M4 ledger and Kera node. No duplicate ledger, item consumption or fabricated epic track. The Order and forge reference are readable; closure purpose/history remain protected. |

All original opening node/reply IDs, Call recognition, training, trade, rescue and side-quest hooks remain. Tam's `haldas_tally`, Thirty-one, sister relationship, token and Halda's Bow are untouched; its tests account only for the intended added Renn menu entries. Five-to-seven bark sets and their cadence remain unchanged.

## NPC life and persistent facts

Conversations follow the live NPC body and use supported `talk_to`/`dialogue_choice` facts. They do not test the NPC's old authored coordinate or require a speaker to stand beside a fixed prop. Renn unfolds a portable map. Mara gives directions rather than being teleported into an escort. Berrin explicitly says the lodge book stays at the lodge when he leaves; visiting it and hearing his reading are separate objectives. Work-hour guidance names the existing 07:00–19:00 anchors, with normal meal/sleep interruptions; no pause or rest-until is introduced.

The integration tests exercise all three carrier branches with Renn at 12:30 away from his work anchor, including defer and save/load. Other story scenes use current NPC positions in an unchanged WP12 setup. Test-only observer positioning between scenes is not a production teleport command or a claim of complete route navigation.

`world.main.carrier` and the two player-name flags are anchored by objective effects at `location.outpost`; remote speakers do not create cell-local copies. `world.main.gauge_seen` is reserved there for M5. Relationship-event names are not an idempotency mechanism: the Tavar reward also has a persistent `visited` guard, tested against repeated selection.

## Engine lint compatibility

The WP4 objective parser supports `waystone_known`, but the generic xref registry on this base rejects its `waystone_ref` field (XREF004). This content-only port does not change/bypass that validator. M8 observes the existing `world.road.rim_stone_woken` at `location.road.basin_rim`: in the current single-character world the actual `TouchStoneCommand` both sets that flag and grants stone knowledge. The test checks both knowledge and unchanged return point. A future shared-world port should use a lint-supported personal `waystone_known` objective once the registry seam is reconciled. No new dialogue condition or C# system was necessary.

## Deliberately pending integration / visual checks

- **TODO Barrowworks / UNVERIFIED:** M5 Readings Resumed and L2/L3 Codex/Foot. No blocked quest graphs, archive/Blind Store definitions, invented boss/room IDs, new journal track or fake Foot effect ship here. Verify real merged definitions before wiring them. Preserve/dismantle motion, witness staging, three Codex sources and two Foot reveal sites remain future work.
- **M1 visual:** familiar-to-Otherreach sky slide, prone wake/get-up, paired shadow/audio, reduced-effect staging, robin and dandelion art, relic meshes/icons and their presentation. The four content pickups currently use the existing container/loot affordance; verify that their final ground props are visible and usable at each terrain height. Their collision-clear positions, distance, unique contents and save persistence are covered headlessly.
- **World Leak:** no shared event is synthesized on new-game, arrival, ridge entry or first control. Its seed/epoch schedule/prior history remains a presentation/world-event task. At 07:30 only the player start is guaranteed; a fresh world must not imply that each arrival causes a shared Leak. Personal sky staging must not publish that event.
- **M4 optional Q-05:** physical Durn inspection recess/openable packing requires the approved level placement, collision delta and prop. His discussion is available during his routine; it does not pretend to open a nonexistent recess or add an iron deposit.
- **M6 visual:** actual night lights, wet/dry comparison and water/sluice consequences need the appropriate art/controller work. This port selects the approved static/daylight fallback. WP12 routines are not a general authored-escort system.
- **M7:** physical packet items/props, destination standing/access/prices and Act II handoff remain explicit dependencies. The 70/30 active/conversation split is an **unmeasured** playtest target, not an automated result.
- **M8 visual/continuation:** touch/rite animation, conditional planet teaser and audio pairing; check actual approach and map/binding feedback. K1 must accept an already-woken Rim. Vessmere is beyond the current playable map; no new region transition is implied.

## Verification commands and limits

```powershell
dotnet build src/UNNAMED.sln -c Release
dotnet test src/UNNAMED.sln -c Release --no-build
dotnet test tests/Application.Tests -c Release --no-build --filter FullyQualifiedName~ActOneStoryTests
dotnet exec src/Content/bin/Release/net8.0/UNNAMED.Content.dll lint --content-root content --verbose
dotnet exec src/Content/bin/Release/net8.0/UNNAMED.Content.dll xref --content-root content --format json
```

The engine content lint runs directly against the actual port; the canon proposal's `--engine-lint` overlay helper is not reapplied on top of already-added nodes/IDs. Release build, full-suite results and final counts are recorded in the PR. Exact content-ID/start-kit/menu assertions are updated for their intended additions; no production tests are skipped or weakened.

The existing Godot headless `--playthrough` was attempted with the installed 4.7.2 console editor and a fresh scratch profile. It reached the Ashen Waystone, then could not complete because screenshot capture receives no viewport image under headless rendering (`Main.SaveScreenshot`, line 1135). The checkout also lacks configured autoload add-ons (InputHelper, QuickAudio, Cogito, Godot AI). The run was stopped; no presentation code or assets were changed to mask this. Run the full visual acceptance playthrough in the configured presentation checkout. The .NET integration scenarios cover content mechanics and persistence, not those visuals.
