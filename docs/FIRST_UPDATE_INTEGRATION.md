# First update integration candidate — 2026-10-27

Branch `first-update-integ`, own worktree `D:\Otherreach_Scratch\first_update_integ`.
Base `claude/pp11-fixes` / `7c1ae1336ec885d8d051b855ae0b564bb3cb310f`.
Local candidate only: no push, packaging, installer changes or GPU run.

## Inputs, in merge order

| Input | Pinned source | Integration |
|---|---|---|
| C1 combat, difficulty, alpha/grazer polish | `019f1efe` | Merge |
| C2 population, inn, accepted art/side-angle follow-up | `455878b0` | Merge |
| C3 camps and first-hour access | `0c557e43` | Merge; includes earlier H-heal cherry-pick |
| Bedroll polish requested after C3 acceptance | `b16f4a09` | Native folded linen mesh/UVs instead of the slab; rolled head and leather ties |
| D3 quest cards/tracker and banner queue polish | `a0c8cd68` | Merge |
| DeepSeek D1 crafting quantity | `cdafa5ac` | Cherry-pick `88756732` |
| DeepSeek D1 lantern/oil and light wear | `c3445139` | Cherry-pick `0a453843` |
| DeepSeek D1 people-specific hints | `6e6f2706` | Cherry-pick `fd6cdb37` |
| Claude H-heal and midsummer sky | `e186c872`, `a92fcd7e` | Merge `claude/first-update-fixes` |

DeepSeek D2 rain, Waystone/Foldscar effects, audio and review-route commits are excluded. The existing rainy-weather configuration remains unchanged. No face, Sedge or NPC-idle branches included.

## Conflicts and resolutions

- C1 and C2: no textual conflicts.
- C3: `Main.cs` scripted-run predicate. Retained C1's guarded review isolation and added the camp review inside the existing predicate's parentheses.
- D3: `Main.cs` autostart review dispatch. Retained both independently gated C1 and D3 paths, each returning before ordinary autostart. `Main.Hud.cs`: retained both camp-return drawing and quest-card visibility.
- D1: all three specified cherry-picks applied without textual conflicts.
- Claude fixes: `PlayerController.cs` H-use selection. Retained C3's ordering by heal strength before the shared heal-first fallback, so the small water sip does not outrank a salve.

## Integration issues found by the full suite

- Eight new items and the water-sip effect lacked icons. Bound them to existing shipped flask, torch, survival and cloth slots; no new icon assets.
- The pinned content catalogue omitted accepted C1/C3 additions and retained the three removed C2 roles. Refreshed the literal expected catalogue to 715 IDs, retaining exact equality. Delta receipt lists 47 additions and three removals. Recipe and hint pins now include the four camp recipes and three new first-hour trigger moments.
- The old overlay hash/no-roof-groups assertion predates C2. Updated the hash to the reviewed C1/C2 overlay; the semantic gate now requires exactly `longhouse`, `forge`, `ah_inn_upper`. Separate Barrowworks/ring exclusions and gated-overlay checks remain.
- `BurningLightsSystem` implemented `IAbstractAdvance` but was missing from dispatch. Registered its existing no-op advance, preserving D1's rule that carried lights do not burn while the player is away. The soak coverage gate remains intact.
- The guard and adventurer occupied the same gate anchor at 08:00. Moved the guard work post from 464/748 to 465.5/750 metres; the daily minimum-spacing test remains intact.
- C2 captive-barrier reference validation rebuilt the region inside the routine builder, causing malformed-region tests to throw instead of returning lint errors. Validate against the already safely built layout in the existing validation pass; preserve the named-NPC/barrier constraint and error code.
- Real-kit quick-bar tests assumed the salve was the sole usable starting item. C3 makes the flask usable too. Updated exact bar/event expectations to keep the flask in slot two while testing cleared salve slots, swapping, pickup and persistence; the player behavior is unchanged.
- A gear-timing fixture was interrupted by C1's new forced-stagger heavy attack. Isolated that fixture to a basic bite threat and added an explicit no-refusals assertion. The production heavy attack and equip interruption rules remain.
- An attribute-scaling fixture compared a level-one baseline to level-six attribute variants, confounding C1's intentional level-gap multiplier. All variants now remain level six, varying only the tested allocation.
- Population fixtures now pin five adventurers/nine role residents. The three-person departure regression retains three test-only adventurers. C2's bunk homes require physical separation, while Cairnwold's separate homes retain full talk-distance separation. The Hollow cookfire crowd test now expects six people and still requires every empty-room exit to remain open with the crowd.
- Resident-corpus attribution uses the World channel so hearing does not depend on proximity to the moved C2 work posts; five actual corpus lines were heard in its three-hour fixture. Named speakers remain excluded.
- Lore-reading and companion-route fixtures no longer walk to obsolete furniture coordinates. The lore test follows the authored shelf; the companion still traverses the common room and circles the building, using its aisle and going around the new north annex. The original fifteen-second snag bound remains.
- Halda's quest exits through the aisle rather than diagonally through a new table. Story fixtures use walkable floor height and approach Renn from the clear aisle, preserving their save/load and mobile-speaker checks. The faction-report test meets freed Tavar at his current town post after absence, rather than at his former captive anchor; report-only faction knowledge assertions remain.
- Kera's dialogue and teaching view pins include the approved field recipes. A bounty-payment fixture uses a level-five hunter rather than testing C1's difficulty scaling accidentally with an under-levelled character.
- The NPC lesson-event test now checks all seven approved lessons and each teacher source. Resident-count pins use nine. The pet-credit fixture keeps its owner near the chase so a following pet can finish a wounded stationary threat; it still requires the pet's direct attacker ID, owner combat XP, bounty progress and deed credit. The salve test now also verifies the C3 flask sip and resulting empty bottle instead of asserting the flask is unusable.
- The spawn-saturation fixture uses a peer-level hunter so both wolves die under C1 combat tuning; the exact original first/second refill deadlines remain asserted.
- Acceptance approaches the corpse after C1 moves the fight, then returns to town and waits out combat before its equipment step. The separate in-combat equipment tests remain and pass. No acceptance failure is added to the known-bugs allowlist.
- C1 added the presentation-only `CreatureView.AttackTick` alongside the non-persisted action phase. The dump now lists/omits it alongside the existing phase omissions; the full saved/live round-trip comparison remains intact for persistent state. No schema change.
- Refreshed the existing generated M3d behavior matrix and Barrowworks balance table for the intended C1 tuning. All original archetype and balance bounds passed. The balance probe now stops on the authoritative defeated flag, since ongoing healing can leave nonzero health while defeated; it previously continued through respawn and then lost its target. Both level-five boss loadouts win 12/12 reference fights; no balance bound was lowered.

Diagnostic run one was stopped after the first fixes, with seven completed project receipts retained. Run two finished eight projects and was stopped while Application was still running obsolete fixture binaries. Neither interrupted run is reported as a completed full suite. Raw logs and receipts are retained. Focused correction runs passed the affected scenarios, including unchanged balance/matrix bounds, save/live comparison, crowd exits and companion snag timing.

Unfiltered Release run: source `464f8cde4fb073edb9c2f76095cd936dbaf6db40`, all nine projects, `dotnet test src/UNNAMED.sln -c Release --nologo --logger "trx;LogFilePrefix=first-update-clean" --results-directory D:\Otherreach_Scratch\first_update_integ_results3 -- xUnit.MaxParallelThreads=4`. Eight projects completed green. Application was replaced by a complete 705-test project rerun after its fixture corrections; its older duplicate process was stopped to free CPU. All production source (`src/` and `content/`) is identical between the retained eight results and the corrected Application source; subsequent changes are test fixtures only. The [supported xUnit VSTest concurrency setting](https://xunit.net/docs/config-runsettings) caps workers per assembly; it does not filter tests. No new skips or test suppression.

## Verification

The owner's 13:15 rule limits integration to one full-suite pass and correction of observed failures. No additional full run, re-audit, GPU capture or new memory-saving work is planned. The owner's 13:35 clarification preserves the existing cache, compression, mipmap, LOD, impostor and streaming practices where fidelity changes are negligible; a remaining foliage disappearing/reappearing bug may still be fixed. No new savings measures or character-quality reductions are included here.

| Release project | Passed | Failed | Standing skips |
|---|---:|---:|---:|
| EntityRegistry | 23 | 0 | 0 |
| Domain | 454 | 0 | 0 |
| Presentation | 521 | 0 | 0 |
| Architecture | 23 | 0 | 0 |
| World | 90 | 0 | 0 |
| Persistence | 295 | 0 | 0 |
| Content | 365 | 0 | 0 |
| Soak | 10 | 0 | 3 |
| Application (raw full pass; six corrections below) | 699 | 6 | 0 |

Eight completed projects: 1,781 passed, zero failed, three existing skips. Application completed all 705 discovered cases in 36 minutes 47 seconds: 699 passed, six failed, zero skipped. Each of those six failures was then resolved by a passing focused recheck on the corrected fixture. Final coverage is 2,486 passing cases, three existing skips and zero unresolved failures; this includes the six correction rechecks. Six stale-fixture failures observed in its already compiled binaries have been corrected and passed focused rechecks:

| Application case | Passing correction receipt |
|---|---|
| ASaturatedSpawnCluster_TakesTwiceAsLongToRefill | `first_update_spawn_recheck.log` |
| Kera_TeachesTheForge_AsATeacher_AndOpensHerWares | `first_update_kera_recheck.log` |
| APetsLastHit_CreditsItsOwnersCombatXpAndBountyCount | `first_update_pet_recheck3.log` |
| TheWaystationsPeople_StandWhereTheRegionPutsThem | `first_update_pet_recheck.log` (resident case passed; obsolete pet failure superseded above) |
| TheMainFlow_WalkedFromANewCharacterToTheReloadedLogin | `first_update_acceptance_recheck.log` |
| TheSalve_Mends18Over6Seconds_AndIsUsedUp | `first_update_pet_flask_recheck.log` (salve case passed; obsolete pet failure superseded above) |

This is retained full-suite coverage plus focused failure corrections, not a claim that one unfiltered command exited green. Raw logs and TRX receipts are retained in `C:\Users\jluca\Documents\Codex\2026-10-18\from-local-claude-for-the-owner\outputs\First_Update_Integration_20261027`. The final code candidate is `f0d330d11d7f504b75b9670054ca66c59fe780da`; this report is committed afterward. No further full run was started after the owner's minimum-testing instruction.

Content lint: 715 definitions, zero errors. Native bedroll appearance has not had an additional GPU review; the C3 accepted gameplay proof is unchanged and the polish uses existing native cloth geometry/materials. No High launch or walk retry.




