# WP7 living-sky audit (2026-10-18)

The simulation already publishes `Simulation.Weather`, `Simulation.Calendar` and
`SimulationSetup.Clock/Calendar`. These are deterministic functions of the world
seed, region and tick, including offline catch-up. No saved presentation state is
needed.

## Original connections (before this change)

- Main supplies the world-start hour and tick/day rate to SkyView. Sky3D's own clock
  is disabled; the moving hour controls sun, air and night fill.
- SkyView applies `weather_states.json` once at Ready, using only `--visual weather`
  or fair. WP7 fronts never reach that call. Only clear/overcast/storm have authored
  profiles; WP7 rain/fog/wind have none, and Fold remains reserved.
- The default Sunshine compositor replaces Sky3D's flat clouds. Its coverage,
  density and wind keep the resource/driver defaults, so changing Sky3D cumulus
  coverage alone cannot change the clouds the player actually sees.
- AtmosphereView filters particle recipes by weather/day/night while building.
  Emitters excluded then cannot appear later. Followers subsequently check only
  ground type, so included pollen and rain do not follow later day/weather changes.
- Sky3D TimeOfDay keeps its default Gregorian date. The hour repeats the same sun
  declination and first moon orbit every day. CelestialView's additional two moons
  also repeat positions by hour, without calendar days.
- The accepted daytime ring and nighttime galaxy/aurora already exist. Preserve
  them, their source imagery and their visual language.
- FoldscarRegister and FoldEcho already draw restrained doubling, delayed shadows,
  activation and faint post-activation light from the real barrier/switch state.
  They must continue following the moving sun. No source establishes a recurring
  Hollow Day explosion, eclipse, Fold weather, or explanation for the Foldscar.

## Required correction

Read the current and preceding front from the simulation, blending by elapsed
world ticks at the front boundary. Loading within that transition must reproduce
the same blend without requiring a previous rendered frame. Apply absolute
profiles against a captured environment baseline, avoiding compounded fog scales.
Drive both cloud renderers and vegetation/particle wind. Build camera emitters
once, then gate emission by the live weather, daylight, ground and exterior state.

Map the continuous WP7 day (including Hollow Day) into the sun's seasonal cycle
and moon phase/orbits. Keep numbered dates in the HUD and the existing three moon
textures; any unestablished orbital periods are presentation tuning, not new lore.
Do not add a mystery-solving cosmic event or make reserved Fold weather active.

Proof needs same-place weather sequences, a day/night sequence, seasonal and moon
comparisons, a save/reload mid-transition check, and a post-sky performance rerun.

## Implementation checkpoint

SkyCycle now projects the continuous WP7 date into Sky3D's astronomical day,
including the Reckoning boundary and Hollow Day. SkyWeatherView reads adjacent
regional fronts and applies absolute profiles with a tick-based smoothstep over
ten game minutes. Both cloud renderers, fog and shared wind receive the blend.
Atmosphere and ambient-life followers now respond during play rather than only
at scene construction. The existing moon imagery and restrained Foldscar effects
are retained; additional moon periods (28/42 days) are artistic tuning, not canon.

The pure clock/blend checks and native SkyClockCheck pass, including repeatable
mid-transition seeks, fog baseline restoration, seasonal sun movement and night
key-light selection. SkyShots supplies weather, full-day, seasonal and moon-date
sequences in the real scene.

Rendered review: `D:\Otherreach_Scratch\frontier\sky\sky_d2fd8427` has 69 full-feature
High/native-1080p screenshots. All five live weather types are visibly distinct:
fair clears to blue, overcast darkens and softens the light, fog loses distant
contrast, rain adds falling streaks, and wind changes cloud/vegetation movement.
The full day progresses through dawn, daylight, dusk and a readable dark night.
The original ground-level moon views were obstructed by scenery, so
`D:\Otherreach_Scratch\frontier\sky\calendar_d80a9039` adds 57 calendar study shots
with a clearly labelled camera 30 m above the same feet, fair weather pinned,
ambient animals disabled, four bearings and a zenith view. These show changing
moon discs and positions, the retained ring, galaxy and aurora, and continuous
Hollow Day/Reckoning dates. Atmospheric moss lights remain in those study views;
they are foreground particles, not additional celestial bodies. Original files
and `sky_study.json` retain every projection tick and diagnostic condition.

The native 1,090-frame headless smoke passes with an identical save/load digest;
content lint and xref pass. The full serial WP7 checkpoint suite passed 1,799 tests
with no failures and three existing long-run skips. Latest presentation/architecture
checks pass (246/23); post-sky performance acceptance remains outstanding. The latest drain captures still fail the performance target;
this checkpoint is not integration or playtest-wide acceptance.


Roofed spaces: camera-following rain and snow now use one native upward ray on the
camera collision layer. A roof suppresses emission and hides already-live drops
that would otherwise fall through it. Native roof/outdoor/hidden/layer controls
pass. This is camera-local shelter gating, not world-wide particle collision;
rain outside a window is not separately simulated. A weather-only SkyShots mode
records the actual camera position and shelter/emission state for an interior
review. The new in-game lodge sequence is still pending.
