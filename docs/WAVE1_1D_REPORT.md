# Wave 1 WP 1D: calendar, seasons and sky

Branch `claude/wave1-1D`, cut from `e38fcdf4` on `playtest/humgen-main` (the base the brief names). By the time I fetched, the remote branch had moved five commits on, to `117f1b8a`. Rebase before merging. The brief and its rows come from `docs/SYSTEMS_CHECKLIST.md` (`docs/systems-checklist`).

## What was built

| Row | What it is now | Status |
|---|---|---|
| WLD-33 Seasons | `Season` (spring, summer, autumn, winter) is a function of the calendar day, one cycle per Reckoning. Its start days live in the new `content/config/seasons.yaml`. Its consumers are the sky (WLD-34), the weather's per-season weights (`config.weather` `season_weights`), the market flows (WLD-56) and the windows (WLD-58). `Simulation.Season` reads it, and `SeasonChanged` is raised at the midnight a season turns. | DONE. The mapping is approved by the owner (2026-10-25). |
| WLD-34 Calendar-driven sky | `SkyCalendar.Of` lays each season over the sky's year, centred on its solstice or equinox, and `SkyView.Advance` sets Sky3D's date from it. The sun's declination and day length move through the Reckoning, with midsummer on about 21 June and midwinter on about 21 December. Sky3D's own moon runs through its phases about once a tally. The two extra moons (`CelestialView`) move along their orbits by world days, every 9 and 23 days; both periods are presentation tuning, not lore. The ring, galaxy, aurora and textures are untouched. | DONE (needs the video) |
| WLD-38 Weather presented, blended by world ticks | `WeatherView.Previous` is the front the current one took over from. `Weather.Blend(view, tick, fadeTicks)` is the cross-fade as a function of the tick. `WeatherStates.Follow` now draws `Lerp(previous look, current look, blend)` from those absolute looks instead of a 40-second wall-clock fade, so a load in the middle of a change draws the same half-way sky. `--visual weather` still pins one look. | PARTIAL. The sky, fog, clouds, wind and wet ground follow. The particle and atmosphere gating, and a look for Fold, are still open. |
| WLD-56 Season effects on the economy | `ISeasonalGoods` is a daily hook in `MarketSystem`, built like the jobs and adventurers hooks; `CalendarSystem` implements it from `config.seasons` `market_flows`. Autumn hunting adds hides at Cairnwold. Winter burns ash hafts at Cairnwold and uses salves at Ashen Hollow. All four flows are working numbers. | DONE for the engine; the numbers are tuning. |
| WLD-58 Seasonal content windows and festivals | `CalendarWindow` (festival or seasonal, by day of the Reckoning, and may wrap round the year's turn), `Simulation.OpenWindows`, and the `CalendarWindowOpened` and `CalendarWindowClosed` events. Shipped windows: the Hollow Day (festival), bloom, midsummer, harvest, migration and midwinter. Their ids carry no lore text. | DONE for the engine. Quests, chatter and 1H's Director can now key on the events. |

"Weather fronts on ticks that survive reload": the fronts were already a pure function of (seed, region, game hour). What did not survive a reload was the presentation's fade; that is now tick-based.

## Determinism and the save

The season, the windows, the sky date and the blend are all functions of the tick, so nothing new is saved and there is **no schema-43 commit**. `CalendarSystem` holds no state. On load it reads where the calendar stands and announces nothing; after an absence it is closed-form and raises only the difference between the old and new day. The seasonal market flows step inside the markets' existing daily loop, so an absence still settles the same as the same absence in pieces. A test checks this over a 150-day absence. The cursor-free weather on `humgen-main` is unchanged.

## Changes outside 1D's own files (small seams)

| Change | Where | Why |
|---|---|---|
| Composition | `Simulation.cs`: `SimulationSetup.Seasons`, `CalendarSystem` registration (tick, advance, `Markets.Seasons`), the `Season`, `OpenWindows` and `SkyDate` views | One registration per new system (append-only rule) |
| Market seam | `Markets.cs`: `ISeasonalGoods` plus a loop of about 7 lines in `SettleTo` | WLD-56 needs a daily input |
| Content builders | `WorldContent.cs`: `BuildSeasons`, and `season_weights` in `BuildWeather`; `GameSession.cs`: one setup line | Config must build and lint |
| Frame loop | `Main.cs`: two calls changed (`SkyView.Advance(..., clock.SkyDate)` and the new `WeatherStates.Follow` signature) | The sky reads the calendar and the tick |
| Content | New `content/config/seasons.yaml`; `season_weights` added to `content/config/weather.yaml` | Data, not code |

## Gates

- `tests/World.Tests/CalendarAndWeatherTests.cs` (17 tests). New ones cover:
  - seasons over the Reckoning, and refused season rules;
  - window coverage, including a window that wraps the year's turn;
  - the sky's year (season anchors, solstices inside the midsummer and midwinter windows, never moving backwards);
  - seasonal weather weights;
  - the tick blend.
- `tests/Application.Tests/WeatherTests.cs` (8 tests). New ones cover:
  - a reload 300 ticks into a change draws the same blend and finishes it;
  - season turns and window open/close events, with no announcement on load;
  - the Hollow Day as a winter festival;
  - winter drawing down the market, with the chunk-invariant absence;
  - the game's own content.
- `tests/Content.Tests/WorldContentTests.cs`: the seasons build from content. Lint refuses seasons out of order, a window past the Reckoning, a flow for a good the market does not keep, and an unknown season in the weather.
- Content lint and xref are clean.
- **Full Release suite** (`dotnet test src/UNNAMED.sln -c Release`):
  - EntityRegistry 23, Domain 424, Persistence 278 (1 skipped), World 96, Presentation 434, Soak 10 (3 skipped), Application 680: all pass.
  - That first run also had two failures, both fixed and rerun green in Release:
    - Architecture `StateAssemblies_HoldNoStaticMutableState`: two static lookup arrays in `SkyCalendar` became `ImmutableArray`. Architecture now passes 23/23.
    - Content `ACopyOfTheGameConfig_Lints`: in a config-only copy of the pack, the generic xref treated the seasons' market flows as references to markets that copy doesn't have. They now use plain `market`/`item` keys, which `BuildSeasons` checks against the whole pack. The affected Content tests pass 35/35, and in the first run the other 365 Content tests passed.
  - After the fixes, World (96) and the Application weather, offline-advance and economy tests (32) were rerun in Release and pass.
- I could not run the headless smoke or boot check: this container has no Godot editor. The coordinator's local run is the presentation gate.

## What to film (time-lapse per SKY_WP7_GAP)

Use one fixed place with a clear southern view: the spawn, looking toward the ring arc.

1. **One day and night**, same place: confirm the ring, galaxy and aurora still read as before.
2. **Seasons**: the same place and hour (`--visual hour=12`) at days 0, 63, 120 and 154 of the Reckoning. The sun should stand highest on day 63 and lowest on day 154, and the night should be longer at midwinter.
3. **Moons**: the same place at 22:00 on five days in a row. Each of the three moons should be at a different phase and place from one night to the next.
4. **Weather change**: a front boundary in real time (about 40 s). The sky, fog, clouds, wind and wet ground should cross-fade with no cut.
5. **Reload mid-front**: save about 15 s into a change, load, and film the first 30 s. It should continue from the same half-way sky, not cut and not restart the fade.
6. **Hollow Day**: the sky on day 182, which should be late-winter ordinary. There is deliberately no special event, because no source establishes one.
7. **Performance**: rerun a frame-time capture after the sky change. The date is set once per game day and the moons are updated every frame, as before.

## Owner answers (2026-10-25)

1. **Tally-to-season mapping: APPROVED as proposed.** Spring is tallies 1-3, summer 4-6, autumn 7-9, and winter 10-13 plus the Hollow Day, so the Hollow Day falls at the end of winter. `config.seasons` and `SeasonRules.Default` now say approved.
2. **The five seasonal windows (bloom, midsummer, harvest, migration, midwinter): KEPT as mechanics.** Lore is attached later; each is marked "no lore yet" in `content/config/seasons.yaml`.
3. **Placeholder seasonal weather weights and market flows: fine for now.**
4. **The open items below are noted for later and not started:** particles following live weather, a look for Fold weather, the HUD season line, and day length.

## Not done / follow-ups (noted for later by the owner, 2026-10-25)

- **WLD-38 remainder**:
  - AtmosphereView builds its emitters once by day and weather; it should gate their emission live.
  - The Sunshine cloud compositor follows only the properties the weather file names.
  - Fold has no look and stays reserved.
- **Visible season**: `CalendarWords` (HUD and map date line) does not show the season yet. That file is outside 1D.
- **WLD-17 and WLD-37** (road mud by season, weather intensity and spatial fronts) are not in 1D.
- **Day-length range**: Sky3D's latitude is still its default 16°, so day length moves only about ±1 hour through the year. A larger range is a one-line latitude change, if the owner wants stronger seasons.
