# Otherreach (codename UNNAMED) - agent guide

Single-player, first-person open-world fantasy RPG. Godot 4 + C# (.NET 8). Authoritative game state lives in an engine-independent domain layer; Godot is presentation only.

## Current status

- Phase 1 (Playable Prototype). Done: M0, M1, M1b, M2 (evidence, decisions and deferrals: `docs/M2_STATUS.md`). Unblocked next: M2b - Save Migration Harness, and M2c - Progression Spine (`docs/ROADMAP.md`).

## Build and test (from the repository root)

- Build: `dotnet build src/UNNAMED.sln`
- Test: `dotnet test src/UNNAMED.sln` (runs every test project, including Persistence.Tests)
- One test: `dotnet test tests/<Project>.Tests --filter "FullyQualifiedName~<TestName>"`
- Content lint: build Content in Release, then `dotnet exec src/Content/bin/Release/net8.0/UNNAMED.Content.dll lint --content-root=content --verbose`

## Where the code is

- Source: `src/<Project>/`. Tests: `tests/<Project>.Tests/`. Content definitions (YAML): `content/`.
- Projects: Domain, Application, Content, EntityRegistry, World, Persistence, Presentation.
- `tests/M2.Probe` is a console app that Persistence.Tests runs as a separate process (cross-process determinism, a real kill mid-save). It is part of the test suite, not scratch.
- Ignore - these are not the code: the empty `Application/`, `Domain/`, `Presentation/` directories at the repository root; root-level `bin/`, `obj/`, `temp_test/`, `test_definition_id.*`; the `.rar` archives and `M1b_changes.patch`.
- `assets/` is generated asset-pipeline output, written by another machine. It is not code; do not read it for coding tasks.

## Architecture rules (`docs/ARCHITECTURE.md`)

- Only `src/Presentation` may reference Godot. `Domain` and `Application` must not - a compile-time boundary.
- `IWorldStateWriter` is internal to Domain. Systems receive it through `Register(...)`; nothing else can write authoritative state.
- One system, one responsibility. A system changes another system's state only by submitting a command or reacting to an event - never by reaching into its state. Events are not commands: a listener responds by submitting a command.
- Domain never references Application or Presentation, and never reads files to load content - it receives a built catalogue. Content is referenced by string ID.
- No static or singleton mutable state: two world instances in one process must not share anything mutable.
- Save/load order is owned by `docs/PERSISTENCE.md` section 7.4. All save I/O goes through `SaveStore` (src/Persistence). A load never falls back to a backup silently, and never regenerates a world whose baseline differs from the save's.
- Instance IDs are `<prefix>_<ULID>` from `EntityId.NewId(kind)` or the registry; never build one by hand.

## Conventions

- net8.0, nullable reference types enabled, implicit usings, xUnit 2.9. Root namespace `UNNAMED.<Project>`.
- Reproduce bugs as tests inside the real test project. Never add scratch projects or debug files at the repository root.

## Documentation - large files: grep a heading, then read that range

- Authority order: `PROJECT_CHARTER.md` > `PHASE_0.md` > `DECISIONS.md` > everything else. If `DECISIONS.md` and `ARCHITECTURE.md` disagree, `DECISIONS.md` is right.
- Core technical docs and sizes: `ARCHITECTURE.md` ~25 KB, `DECISIONS.md` ~26 KB, `PERSISTENCE.md` ~45 KB, `SYSTEMS.md` ~52 KB, `ROADMAP.md` ~52 KB, `DATA_MODEL.md` ~58 KB.
- `docs/00_DESIGN_PACK_INDEX.md` lists the design extension pack. It is directional, NOT normative: do not change the M2 identity or persistence contracts (D-04 IDs, the `PERSISTENCE.md` save format) to match it.

## Godot

- `src/Presentation` references `GodotSharp` / `GodotSharpEditor` 4.7.2, matching the installed editor. Change both together, and only to match the editor version.
