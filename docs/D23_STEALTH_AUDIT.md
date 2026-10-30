# D23 stealth: the 68-row audit, re-run against `systems/stealth`

Run 2026-10-29 against worktree `D:\Otherreach_Scratch\ds_stealth_wt`, branch `systems/stealth` (off `build2-integ` `303bdbe8`).
Source of the rows: `canon/STEALTH.md` §1. States: **IMPL** built and exercised; **PART** some of it; **DES** designed and content reserved;
**MISS** absent.

## Counts

| run | IMPL | PART | DES | MISS |
|---|---|---|---|---|
| baseline (`build2-integ`, 2026-10-28) | 15 | 22 | 19 | 12 |
| after S0+S1 | 24 | 20 | 16 | 8 |
| after S2+S3+S4 (this run) | **35** | **15** | **12** | **6** |

## What moved, and where to look

| row | subject | now | evidence in this branch |
|---|---|---|---|
| 3 | hearing by radius, per surface | IMPL | `Surfaces.Factor` (gravel 1.5 … mud 0.7) on every footfall; `tests/Domain.Tests/SoundTests.cs` |
| 9 | lit torch / bright gear seen further | IMPL | a held light is a light standing where the body is; `StealthVisibilityTests.AnAuthoredLight…`, `ACarriedTorch…` |
| 10 | sampled light level at the body | IMPL | authored `lights:` + `LightRules.At`; the HUD gem and the sight check read the same number |
| 12 | time of day changes detection | IMPL | ambient by the sky clock; night hunters keep 0.9 in the dark |
| 14 | cover, foliage, tall grass | IMPL | authored `covers:` with a fraction; `Visibility.SeenFraction`, three sample points |
| 18 | HUD light gem and noise ring | IMPL | `Ui/LightGem.cs`; seen drawing in `work/proof_s0s1/boot.png` |
| 19 | HUD per-enemy awareness with direction | IMPL | `Ui/AwarenessMeter.cs` (eye/ear glyph, bearing wedge) |
| 20 | surfaces change footstep noise | IMPL | `Surfaces.At` + `SoundWorldTests.GravelCarriesFurtherThanMud` |
| 21 | distractions (thrown objects) | IMPL | `ThrowCommand`, `item.misc.lure_stone`; `AThrownStoneMakesItsNoiseWhereItLands` |
| 22 | rain and water masking | IMPL | `Perception.MaskedRadiusMm`, `SystemContext.WeatherNow` |
| 24 | search from the last known position | IMPL | widening golden-angle pattern; `StealthSearchTests` |
| 28 | uneasy after a search | IMPL | awareness kept at a third of the way; same tests |
| 31 | bodies discovered raise alarm | IMPL | `CorpseInSight`; `StealthBodiesTests` |
| 33 | animals by scent and wind | IMPL | `ScentRules`, `WindRules`; `StealthScentTests` (downwind smells, upwind does not) |
| 41 | body carry / drag | PART | carry into cover and hiding done (`HideBodyCommand`); drag as a distinct action not built |
| 42 | hide bodies | IMPL | a corpse in 100 % cover stops being a sign |
| 45 | ranged sneak multiplier | IMPL | bow 2.0, crossbow 2.5, thrown 2.0; `MarksmanshipTests` |
| 46 | aimed region / headshot | IMPL | per-creature `hit_zones`, Weak-point Shot |
| 51 | a silent kill tells no one | IMPL | kill noise 2 m vs a 20 m cry for a wound |
| 52 | shot noise, and where it is heard | IMPL | the weapon's own `shot_noise_m` at the shooter, impact noise at the stop |
| 56 | stealth techniques in content | PART | one of the brief's records (Weak-point Shot) |
| 64 | `HOOK.STEALTH.*` in the hooks lint | IMPL | COVER, LIGHT and SURFACE audited with a counted evidence form |

## Still owed, precisely

- **S4**: takedowns through doors; drag as an action of its own; takedown animations (no clip set is owned); an end-to-end ledge test
  (the reach rule is tested, the arena helper cannot place a body at height).
- **S5**: the flight maths is in and tested (`Domain/Combat/Projectiles.cs`) but **not wired** — the combat path still resolves a shot at
  release, so nothing drops yet. Wind, scope with steadying breath, retrievable arrows, the marksman rifle and the sleep dart all follow that
  wiring.
- **S6**: stealth XP for unnoticed actions and the ghost/non-lethal bonuses; stealth routes in the build-2 areas.
- **S7**: cloaks, sound dampeners, detection devices and wards.
- **S3**: missing patrol members needs patrol rosters in the role data; guards hold a post today.
- **Proof**: no phase is marked FINISHED. The route harness runs (`--stealth-review`), the probe proves the world boots, and the first pass
  produced 56 frames and 8 beats with correct simulation numbers — but its camera filmed the sky and its fixture carried a lit torch, so its
  stills prove nothing. Both are fixed (`2559d63f`) and the re-shoot is waiting on a GPU window.
