# First update integration 02 — 2026-10-27

Branch `first-update-integ02`, worktree `D:\Otherreach_Scratch\first_update_integ02`.
Base: accepted integration/package01 `e50a02d8dfd6e6d066805200fbd2b92fd1253acb`.
Scope: accepted updates only; no packaging, GPU proof, performance/savings work or push.

## Inputs

| Accepted input | Pinned commit | Integration |
|---|---|---|
| NPC varied idles, talking, relaxed hands and native mouth materials | `6b684b17` | Merge `073c14a1` |
| D2 rain, near-camera fade, rain enabled, Waystone response/audio routing | `9faac395` | Merge `9d3cf560` |
| D4 collision extents and creature hours matched to midsummer | `cf477f26` | Already an ancestor of `9faac395`; explicit merge reports up to date |
| C1 alpha coat/move polish | `019f1efe` | Already in base; no newer missing source commits at integration start |
| C3 accepted camp/first-hour fixes and folded-cloth bedroll | `0c557e43`, integration01 bedroll polish | Already in base; no newer missing source commits at integration start |

Rain weight is `2`; streak near-fade is `[0.6, 2.2]`. D4 moves night start/day start to19:45/04:15 to match the accepted midsummer sky. Existing C1 moves, C2 floors/schedules, C3 camp/hints and D3 quest cards remain included. Unaccepted F1/creature replacements, Sedge, D6 jump changes and later branch tips are not pulled in.

## Conflicts and corrections

- `Main.cs`, NPC creation: retain C2's active-layout walkable-floor height sampler and add the NPC review's explicit fixture-gated subject filter. Using terrain height alone would put upstairs NPCs on the lower ground.
- `Main.cs`, AutoStart: keep C1, D3 and NPC review dispatch as three separately fixture-gated paths with independent returns and variable names.
- `tests/Content.Tests/ValidationTests.cs`: retain integration01's exact715-ID catalogue instead of the older D1 list. No new catalogue IDs are introduced by this batch.
- The textual merge silently added lantern/oil icon keys already present from integration01. The full suite's two ItemIcon checks caught the duplicate JSON property names. Commit `1d078ad1` removes only the redundant identical mappings; focused correction2/2PASS. No coverage assertion weakened.
- The calendar/weather content fixture still expected rain weight0 from before D2 acceptance. Updated its exact expected map to rain2; the affected single case passes (`first_update_integ02_weather_recheck.log`).

## Private asset handoff for the next requested package

Source integration is complete; these private deliveries remain outside public Git and are NOT packaged by this task:

- NPC motion: `D:\Otherreach_Scratch\npc_life_20261027\asset_delivery02` ONLY. Its delivery manifest lists28 exact-target libraries, each50states (49original+idle_observe), preserving the original key inventory. Install the supplement only on the matching original bodies; future F1 bodies require their own exact-target motion. Keep the delivery body pins.
- Mouth: `D:\Otherreach_Scratch\npc_life_20261027\mouth08\delivery`. Copy `rigged/humgen_mouth` and its three native2K C/N/ROUGH maps; copy the three BC7/mipmap DDS files and merge their texture-cache entries BY KEY into the existing manifest. Never replace the world's cache manifest with the three-entry supplement. `HumGenMouth` requires these maps for every HumGen body.
- D2 uses the existing copied sound set; the source routing fix resolves it from the workspace actually loaded. Accepted Waystone chimes remain. The separately annotated unprovided ambient Waystone hum remains silent as authored; no new audio is fabricated here.
- Retain package01's private C1 native wolf supplement, accepted alpha shader/bindings, C2 kit assets and C3 native blanket surface/materials. No source/body/texture downscaling.

## Validation

One complete unfiltered Release suite started on merge `9d3cf560`:

`dotnet test src/UNNAMED.sln -c Release --nologo --logger "console;verbosity=normal" --logger "trx;LogFilePrefix=integ02" --results-directory D:\Otherreach_Scratch\first_update_integ02_results -- xUnit.MaxParallelThreads=8`

Full-suite log: `D:\Otherreach_Scratch\first_update_integ02_release.log`.
The icon correction has its own two-test TRX and `first_update_integ02_icons_recheck.log`. No repeat full suite.
Production Presentation Release build on corrected source `1d078ad1`: PASS,0errors/28warnings; `first_update_integ02_build.log`.

Full suite completed all nine projects. Raw totals: 2485 passed, 3 failed, 3 existing skips. Application705/705PASS (34.4minutes). All three failures were corrected and passed focused rechecks: two duplicate icon-key checks and the stale rain-weight fixture. Final coverage: 2488 passing cases, 3 existing skips, zero unresolved failures. The full command exits1 because its raw failures are retained; no single unfiltered all-green exit is claimed, and no second full suite was run. Corrected source candidate: b69bfd88; final report commit follows.


| Project (raw full run) | Passed | Failed | Standing skips |
|---|---:|---:|---:|
| EntityRegistry | 23 | 0 | 0 |
| Presentation | 521 | 2 | 0 |
| Domain | 454 | 0 | 0 |
| Architecture | 23 | 0 | 0 |
| World | 90 | 0 | 0 |
| Persistence | 295 | 0 | 0 |
| Soak | 10 | 0 | 3 |
| Content | 364 | 1 | 0 |
| Application | 705 | 0 | 0 |

Receipts: C:\Users\jluca\Documents\Codex\2026-10-18\from-local-claude-for-the-owner\outputs\First_Update_Integration02_20261027 (all raw TRXs, full-suite log, build log, both focused correction logs and release_results.json). No packaging, upload, push or GPU work.


