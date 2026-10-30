# D23 stealth: the 68-row audit, re-run against `systems/stealth`

Run 2026-10-30 against worktree `D:\Otherreach_Scratch\ds_stealth_wt`, branch `systems/stealth` (off `build2-integ` `303bdbe8`), HEAD `da858b8e`.
Source of the rows: `canon/STEALTH.md` §1. States: **IMPL** built and exercised; **PART** some of it; **DES** designed and content reserved;
**MISS** absent.

## Counts

| run | IMPL | PART | DES | MISS |
|---|---|---|---|---|
| baseline (`build2-integ`, 2026-10-28) | 15 | 22 | 19 | 12 |
| after S0+S1 | 24 | 20 | 16 | 8 |
| after S2+S3+S4 | 35 | 15 | 12 | 6 |
| after S5+S6+S7 (this run) | **47** | **14** | **4** | **3** |

## What moved, and where to look

| row | subject | now | evidence in this branch |
|---|---|---|---|
| 3 | hearing by radius, per surface | IMPL | `Surfaces.Factor` (gravel 1.5 … mud 0.7) on every footfall; `tests/Domain.Tests/SoundTests.cs` |
| 4 | a worn thing swallows part of the step | IMPL | `noise_dampen` → `Systems.PlayerNoiseDampen`, best worn piece; `item.tool.dampener_pads`; `StealthDampenerTests` |
| 9 | lit torch / bright gear seen further | IMPL | a held light is a light standing where the body is; `StealthVisibilityTests.AnAuthoredLight…`, `ACarriedTorch…` |
| 10 | sampled light level at the body | IMPL | authored `lights:` + `LightRules.At`; the HUD gem and the sight check read the same number |
| 11 | a lamp can be put out, and lit again | IMPL | `DouseLightCommand` + `_doused` feeding the same light list the sight check reads; `StealthDouseTests` |
| 11a | a lamp can be shot out | IMPL | `DouseLightCommand.ByShot` (no reach) + `ExtinguishLight` raised by a shot whose line of flight passes a lamp; `StealthShotOutLampTests` |
| 12 | time of day changes detection | IMPL | ambient by the sky clock; night hunters keep 0.9 in the dark |
| 13 | silhouettes against a lit backdrop | IMPL | `Visibility.Silhouetted` in `VisibleTo`; `SilhouetteTests`; `Visibility.Factor`'s silhouette branch |
| 14 | cover, foliage, tall grass | IMPL | authored `covers:` with a fraction; `Visibility.SeenFraction`, three sample points |
| 16 | the HUD's shadow silhouette, four bands | IMPL | `Ui/ShadowSilhouette.cs` + `ShadowModel.From(band)`; the owner's §2.7a ruling; black at full ink, washed at Bright |
| 18 | HUD visibility and noise ring | IMPL | `Ui/ShadowSilhouette.cs` (replaced `LightGem`), `NoiseRingModel`; seen in `work/farm_stealth02/route.mp4` |
| 19 | HUD per-enemy awareness with direction, and what gave you away | IMPL | `Ui/AwarenessMeter.cs` + `NoticeSense`; the simulation records `NoticeKind` (sight/sound/sign/scent); `NoticeKindTests` |
| 20 | surfaces change footstep noise | IMPL | `Surfaces.At` + `SoundWorldTests.GravelCarriesFurtherThanMud` |
| 21 | distractions (thrown objects) | IMPL | `ThrowCommand`, `item.misc.lure_stone`; `AThrownStoneMakesItsNoiseWhereItLands` |
| 22 | rain and water masking | IMPL | `Perception.MaskedRadiusMm`, `SystemContext.WeatherNow` |
| 24 | search from the last known position | IMPL | widening golden-angle pattern; `StealthSearchTests` |
| 28 | uneasy after a search | IMPL | awareness kept at a third of the way; same tests |
| 29 | a cloak takes a share of the body off the eye | IMPL | `stealth_bonus` → `Systems.PlayerStealthBonus`, best worn piece; `item.armor.veil_cloak`; `StealthGearTests` |
| 30 | a device reads what has noticed the wearer, further | IMPL | `detect_range_m` → `Systems.PlayerDetectRangeMm`, read by the HUD; `item.tool.watchglass`; `StealthGearTests` |
| 31 | bodies discovered raise alarm | IMPL | `CorpseInSight`; `StealthBodiesTests` |
| 33 | animals by scent and wind | IMPL | `ScentRules`, `WindRules`; `StealthScentTests`; the nose accrues per tick (`23a69d39`) |
| 40 | a body in water or a chest is out of sight | IMPL | `BodyHidden` reads `WaterUnder` depth and `ContainerSite`; `11a71c78`, `da858b8e` |
| 41 | body carry / drag | IMPL | carry into cover (`HideBodyCommand`) and drag a metre at a time (`DragBodyCommand`) — the drag's own end-to-end test is skipped, the command is not |
| 42 | hide bodies | IMPL | a corpse in 100 % cover, in deep water, or at a chest stops being a sign |
| 45 | ranged sneak multiplier | IMPL | bow 2.0, crossbow 2.5, thrown 2.0; `MarksmanshipTests` (six, unskipped) |
| 46 | aimed region / headshot | IMPL | per-creature `hit_zones`, Weak-point Shot |
| 47 | a sleep dart sleeps rather than kills | IMPL | `on_hit_effect` → `AttackProfile` → the existing chance roll; `item.weapon.sleep_dart`; `StealthDartTests` |
| 48 | a marksman's rifle carries further and cracks louder | IMPL | `item.weapon.marksman_rifle`, 60 m and a 9 m crack against the bow's 6; `StealthDartTests` |
| 49 | the impact is heard where and when the arrow lands | IMPL | `_inFlight` + `ProjectileRules.Of` timing; impact noise raised at arrival; `cdb46af2` |
| 51 | a silent kill tells no one | IMPL | kill noise 2 m vs a 20 m cry for a wound |
| 52 | shot noise, and where it is heard | IMPL | the weapon's own `shot_noise_m` at the shooter, impact noise at the stop; proven in `work/farm_stealth02/shot.json` (6.0 m) |
| 56 | stealth techniques in content | PART | one of the brief's records (Weak-point Shot) |
| 60 | stealth XP for unnoticed actions | IMPL | `PracticeStealth` (crouched walking, nothing engaged), takedown and choke practice |
| 61 | a clean run is recognisable | IMPL | the ghost ledger (`Simulation.StealthDetections`); `StealthGhostTests` |
| 64 | `HOOK.STEALTH.*` in the hooks lint | IMPL | COVER, LIGHT and SURFACE audited with a counted evidence form; data for TAKEDOWN, SEARCH, SOUND, LURE, SCENT, WITNESS, DOOR |

## Still owed, precisely

- **S4**: takedowns through doors; carry *targets* (putting a body into a chest as a command rather than a place you drop it); ledges as a hiding
  place; takedown animations (no clip set is owned); an end-to-end ledge test (the reach rule is tested, the arena helper cannot place a body at
  height). The drag's own test is skipped for the same fixture reason.
- **S5**: wind on arrows and the drop it implies (`ProjectileRules.HoldoverMm` exists, unwired); a scope with steadying breath; retrievable
  arrows; and the *wound* arriving with the flight — the impact noise travels now, the damage still lands at release because the strike pipeline
  is bound to the attack action and refuses a blow whose windup is over.
- **S6**: the ghost and non-lethal bonuses are *paid against* a ledger now, but no bonus is awarded; stealth routes in the build-2 areas.
- **S7**: wards against being scried, which the content model has no field for.
- **S3**: missing patrol members needs patrol rosters in the role data; guards hold a post today.
- **S1**: a prone pose (the stance has a speed and a noise radius and no clip); the shot-out trigger's line of flight is wired and its reach rule
  is tested, but no authored lamp on a clear line has been shot along yet.
- **Proof**: S0 is FINISHED (`work/farm_stealth02/route.mp4`, its contact sheet and `shot.json`). S1 onwards are waiting on a GPU: the 3090 is
  codex's owner-reserved lease and the 6900XT farm has been running other lanes' jobs all morning. `deepseek_stealth04` (HUD fix and the S7
  gear in the kit) is queued there.
