# Otherreach (codename UNNAMED) - agent guide

Single-player open-world fantasy RPG: full-body third-person / over-the-shoulder, with seamless zoom into first person. Godot 4 + C# (.NET 8). Authoritative game state lives in an engine-independent domain layer; Godot is presentation only.

## Current status

- Phase 1 (Playable Prototype). Done: M0, M1, M1b, M2 (`docs/M2_STATUS.md`), M2b - save migration and baseline compatibility (`docs/M2B_STATUS.md`), M2c - progression spine (`docs/M2C_STATUS.md`; the ratified model is `docs/PROGRESSION_AXIS_RECONCILIATION.md`), M3 - player, camera, movement, interaction, world cells (`docs/M3_STATUS.md`; its performance gate waits on the owner's RAZER measurement window). Next: content-validation hardening, then M3b, per `docs/CLAUDE_PHASE1_EXECUTION_PROMPT.md`. The run stops after M6 for the owner's playtest.
- The running world is `src/World/Runtime`: `Simulation` (the composition root, command queue and fixed 20 Hz tick) and its systems, each owning declared `StateSlice`s. Movement, terrain, collision and tiers are pure functions in `src/Domain/Spatial`; `Kinematics.Step` is the one movement function, shared by the simulation and presentation's prediction. `src/Application/GameSession.cs` boots content, runs new game / load / save, and the frame loop.
- Progression rules live in `src/Domain/Progression` as pure functions (`ProgressionEngine`); their numbers are content (`content/config/progression.yaml` and friends, built by `ProgressionContent`). Each axis advances only through its own currency type - never add an overload that takes gold, items or another axis's currency.

## Roles and worktrees (owner ruling, 2026-09-23)

- Claude is the primary gameplay/code agent through M6. It works only in the `G:\UNNAMED_CLAUDE` worktree on branch `claude/phase1`, pushes that branch, and keeps a draft PR into `main` open (the draft PR is what runs CI). It never merges to `main`; the owner merges at milestone gates.
- DeepSeek/DSH owns asset generation, rigging, animation and asset-pipeline tooling. Qwen is paused from gameplay implementation.
- DeepSeek-owned paths - never stage, edit or commit them from a gameplay branch; read them for context only: `tools/asset_pipeline/**`, `tools/godot_validate/**`, `docs/WAVE_0_*.md`, `docs/ANIMATION_*.md`, `docs/CANONICAL_BODY_AND_SKELETON.md`, `docs/ASTRAL_HOST.md`, `assets/`.
- The GitHub repository is public: anything pushed is published.

## Build and test (from the repository root)

- Build: `dotnet build src/UNNAMED.sln`
- Test: `dotnet test src/UNNAMED.sln` (runs every test project, including Persistence.Tests)
- One test: `dotnet test tests/<Project>.Tests --filter "FullyQualifiedName~<TestName>"`
- Content lint: build Content in Release, then `dotnet exec src/Content/bin/Release/net8.0/UNNAMED.Content.dll lint --content-root content --verbose` (a space, not `=`: the tool ignores `--content-root=...` and lints `./content`)
- Save tool: `dotnet src/SaveTool/bin/Debug/net8.0/UNNAMED.SaveTool.dll save:migrate --dry-run <save-dir> --content-root <dir> --worldgen <profile.json>` (also `save:migrate` and `save:inspect`)

## Where the code is

- Source: `src/<Project>/`. Tests: `tests/<Project>.Tests/`. Content definitions (YAML): `content/`.
- Projects: Domain, Application, Content, EntityRegistry, World, Persistence, SaveTool, Presentation. `tests/Architecture.Tests` asserts the architecture rules below.
- `tests/M2.Probe` is a console app that Persistence.Tests runs as a separate process (cross-process determinism, a real kill mid-save or mid-migration, fixture writing). It is part of the test suite, not scratch.
- `tests/Persistence.Tests/Fixtures/` holds one committed save per schema version. Never edit them; a schema change follows `Fixtures/README.md`.
- `src/World/Legacy` is worldgen 1, frozen: the schema 1 -> 2 migration needs it. Never change it, and never generate new content with it.
- The `.rar` archives at the repository root are local backups, ignored by git. They are not the code; do not read them.
- `assets/` is generated asset-pipeline output, written by another machine. It is not code; do not read it for coding tasks.

## Architecture rules (`docs/ARCHITECTURE.md`)

- Only `src/Presentation` may reference Godot. `Domain` and `Application` must not - a compile-time boundary.
- `IWorldStateWriter` is internal to Domain; systems receive it through `ISystem.Configure`, which is internal too. `WorldDelta`'s mutators are internal to World. Nothing else can write authoritative state, and `tests/Architecture.Tests` fails the build if that changes.
- One system, one responsibility. A system changes another system's state only by submitting a command or reacting to an event - never by reaching into its state. Events are not commands: a listener responds by submitting a command.
- Domain never references Application or Presentation, and never reads files to load content - it receives a built catalogue. Content is referenced by string ID.
- No static or singleton mutable state: two world instances in one process must not share anything mutable.
- Save/load order is owned by `docs/PERSISTENCE.md` section 7.4. All save I/O goes through `SaveStore` (src/Persistence). A load never falls back to a backup silently, and never regenerates a world whose baseline differs from the save's: every changed cell is proven by its `baseline_hash` (section 6.4).
- Generation randomness comes only from `RngChannel` (`Random(seed, cell, subsystem, semantic_key, sample)`), never from call order or `System.Random`, and never keyed on `content_hash`. A generator change must keep the pinned probe tests green, or bump `worldgen_version` and register a transition.
- Instance IDs are `<prefix>_<ULID>` from `EntityId.NewId(kind)` or the registry; never build one by hand.

## Conventions

- net8.0, nullable reference types enabled, implicit usings, xUnit 2.9. Root namespace `UNNAMED.<Project>`.
- Reproduce bugs as tests inside the real test project. Never add scratch projects or debug files at the repository root.

## Documentation - large files: grep a heading, then read that range

- Authority: `docs/IMPLEMENTATION_PRECEDENCE_AND_DESIGN_STATUS.md` restates the hierarchy of `PHASE_0_COMPLETE.md` §1 together with the owner's current rulings. Read it before any Phase-1 work. `PROTOTYPE.md` governs what is in Phase 1; `ROADMAP.md` governs when. If `DECISIONS.md` and `ARCHITECTURE.md` disagree, `DECISIONS.md` is right.
- Phase-1 execution brief: `docs/CLAUDE_PHASE1_EXECUTION_PROMPT.md`. Orientation: `docs/OTHERREACH_MASTER_HANDOFF_2026-09-23_V3.md`. Neither is design authority.
- Core technical docs and sizes: `ARCHITECTURE.md` ~26 KB, `DECISIONS.md` ~28 KB, `PERSISTENCE.md` ~60 KB, `SYSTEMS.md` ~54 KB, `ROADMAP.md` ~54 KB, `DATA_MODEL.md` ~60 KB. `M2B_SAVE_MIGRATION_AND_BASELINE_COMPATIBILITY.md` is the owner-approved M2b refinement.
- `docs/INDEX.md` lists every design-extension document with its owning milestone. They are directional, NOT normative, until their milestone reconciles them: do not change the identity or persistence contracts (D-04 IDs, the `PERSISTENCE.md` save format) to match them.

## Godot

- `src/Presentation` is the Godot project (`project.godot`, `Godot.NET.Sdk/4.7.2`, matching the installed editor; change it only to match the editor version). It renders and submits commands; it never writes state (`tests/Architecture.Tests` scans its sources). Commit the `.uid` files; `.godot/` is ignored.
- Build it with `dotnet build src/Presentation/Presentation.csproj`, then, with the console editor binary: headless smoke `godot --headless --path src/Presentation -- --smoke` (exit 0 = pass), boot check `godot --headless --path src/Presentation --quit-after 300`, performance capture `godot --path src/Presentation -- --perf [--perf-out <dir>] [--perf-seconds <n>]`, 2x2 km spike `... -- --spike`. A capture belongs on RAZER, in a window the owner agrees (`docs/M3_STATUS.md`).
