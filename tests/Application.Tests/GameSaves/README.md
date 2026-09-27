# Game saves

Saves the game itself wrote - through its own new game, play and save - kept so today's build loads them and plays on
(`GameSaveTests`; the Phase-1 technical audit, T-02). The historical fixtures in `tests/Persistence.Tests/Fixtures` are
written by a probe over a fixture world and never ticked; these are the game's own world, content and layout.

Never edit a save here. `save/` is byte-exact - `sections.sha256` hashes every file, and `.gitattributes` stores it as
binary. A load records its proof beside the slot, so the tests copy a save into a temporary profile before loading it.
`state_saved.json` is the dump the writing build made at the save (`StateDump.Render`, before the `live` section existed);
a test compares today's load with it, and names each field a schema step since has added.

| Save | Schema | Written by | How |
|---|---|---|---|
| `m7_crossing_start/` | 15 | M7 E5 (the Crossing Workshop's start save S0, content 0.3.0 `sha256:6525916…`) | `UNNAMED_WRITE_BUILD_START=1 dotnet test tests/Application.Tests --filter "FullyQualifiedName~TheCrossingWorkshopStart_IsWrittenOnlyWhenAsked"` (refused under `CI=true`): `CrossingWorkshop.Start()` at tick 0, seed `0x0A5E202609240001`, saved through `SaveStore`. `save/` is the `quick` slot. No `state_saved.json`: `TheCommittedBuildStart_IsTheCrossingWorkshopStart` compares it with the builder after the definition pass, and `--build-shots` copies it into its own profile |
| `m6_acceptance/` | 12 | commit `3e6dbcc` (M6: the husk patrols Blackvein; the perf walk keeps clear of creatures) - the build `docs/acceptance/m6` was made with | on a checkout of that commit, `godot --path src/Presentation -- --playthrough <dir>`: the content bible's acceptance path, from its fixed seed, ending home with Tavar after a ward. `save/` is `<dir>/profile/manual_acceptance`; `state_saved.json` is `<dir>/state_saved.json` |
