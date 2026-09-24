# Game content

The content pack the game boots from (D-03). One definition per file; the directories and kinds are the closed
table in `docs/DATA_MODEL.md` §1, and `PROTOTYPE.md` §4 lists exactly what Phase 1 contains.

Lint it before committing (from the repository root):

```
dotnet build src/Content -c Release
dotnet exec src/Content/bin/Release/net8.0/UNNAMED.Content.dll lint --content-root content --verbose
```

A pack that does not lint does not boot. Test fixtures live under `tests/`, never here.
