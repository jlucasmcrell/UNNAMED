# Game content

The content pack the game boots from (D-03). One definition per file; the directories and kinds are the closed
table in `docs/DATA_MODEL.md` §1, and `PROTOTYPE.md` §4 lists exactly what Phase 1 contains.

Lint it before committing (from the repository root):

```
dotnet build src/Content -c Release
dotnet exec src/Content/bin/Release/net8.0/UNNAMED.Content.dll lint --content-root content --verbose
```

A pack that does not lint does not boot. Test fixtures live under `tests/`, never here.

## World hooks

`_world_hooks.yaml` is the machine-readable list of what the world must place for a skill or system to be usable,
with a minimum per area type (brief D15). `content hooks` audits the regions, locations, spawns and nodes against it
and reports, per area, which required hooks are placed, missing, or impossible for the content model to express:

```
dotnet exec src/Content/bin/Release/net8.0/UNNAMED.Content.dll hooks --content-root content
```

A missing hook is a WARNING while its area is in development and an ERROR once the area's `status` is `released`.
The prose, the dossiers and the gate live in the canon repo: `canon/WORLD_HOOKS.md`, `canon/areas/`.

An unmodelled hook names in its `evidence.why` the thing the content model cannot say yet. D18 moved three after
finding the engine supports them: a climbable face is a structure whose id starts a `climbable_prefixes` entry, a
teaching book is an item with `use.grants` lying in a container or handed over by an NPC's `transfer_item`, and a
ladder stays unmodelled because there is no ladder traversal. Whatever is still unmodelled belongs to
`ROADMAP\SYSTEMS_NEEDED.md`.
