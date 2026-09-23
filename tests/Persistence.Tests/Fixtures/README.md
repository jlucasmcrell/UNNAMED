# Historical save fixtures

One committed save per schema version that has shipped (M2b §11, `PERSISTENCE.md` §6.2). CI loads every
one under the current code and migrates every one through the real commit path
(`HistoricalFixtureTests`), so a schema change that cannot read an old save fails the build.

## Policy

1. **A fixture is written by its own version's writer and never edited.** Its bytes, including
   `sections.sha256`, are exactly what that build wrote. `.gitattributes` stores them as binary: a
   line-ending conversion would break the integrity root.
2. **Every schema bump, in the same commit:**
   - keeps every older fixture unchanged;
   - adds `vN/`, written by the new writer (`dotnet tests/M2.Probe/bin/Debug/net8.0/M2.Probe.dll fixture <dir>`, then copy `<dir>/quick` to `vN/quick`);
   - adds the `N-1 -> N` step to `SchemaMigrations.Production`;
   - updates every `expected.json` whose current state gained or changed a field.
   `EveryShippedSchemaVersion_HasAFixture_AndNoneFromTheFuture` fails until all of this is done.
3. **`expected.json` is the current state the fixture must load to.** Change it only when the
   current shape intentionally changes. Never change it to make a failing migration pass. To regenerate
   after an intended shape change, run the fixture tests once with `UNNAMED_WRITE_FIXTURE_EXPECTATIONS=1`,
   then review the diff line by line. Apart from generated ULIDs, every fixture must describe the world
   below.
4. The fixtures load against `content/` (fixture content 0.2.0) and `worldgen_profile.json`. Those are
   test data, not game content. `content-0.1.0/` is the pack the fixtures were written with.

## The fixture world

The same logical world at every version: seed `0x5C1A9E7B4D2F0083`, the profile in
`worldgen_profile.json`, and fixture content 0.1.0 (content hash
`sha256:7f522a30f46119bbe25e50c29a9db0a4ff21be31b080dc7a5eba0f225379353d`).

| What | Where | Why it is here |
|---|---|---|
| Player "Aelin", `chr_01HF7YAT00041061050R3GG28A`, position (150250, 12000, -40125) mm | player | Fully serialized state |
| `item.weapon.iron_sword` x1, `item.potion.healing_draught` x3 | player inventory | The potion was **renamed** to `item.potion.minor_healing` in content 0.2.0 (`content/_aliases.yaml`) |
| `world.door.cellar_open = 1` | cell `r_0_0:c_00_00` | A changed cell (flag) |
| First node of the cell harvested at tick 1000 | cell `r_0_0:c_00_03` | Keyed `node.<cell>.0` in schema 1; `node.<cell>.iron_vein.00` from schema 2 |
| Deer population alive = 1 | cell `r_0_0:c_00_05` | Spawn budget divergence |
| `world.lever.mill_gate = 3` | cell `r_0_0:c_00_07` | A second flag |
| Wolf slot 00 killed | entity in `r_0_0:c_00_02` | A **tombstoned baseline entity**: it must stay dead |
| Deer slot 01 moved to (1234, 5678) cm | entity in `r_0_0:c_00_04` | Positional state carried as-is |

## Provenance

| Version | Written by | How |
|---|---|---|
| `v1/` | M2's writer, commit `7ff4c57` | In a scratch worktree of `7ff4c57`, with the one-off probe addition below: `M2.Probe fixture <dir> sha256:7f522a30...` |
| `v2/` | The M2b schema-2 writer | `M2.Probe fixture <dir>` (`M2Fixtures.Historical`) |

The one-off addition to `7ff4c57`'s probe that wrote `v1/`. It is not compiled into this build, since
that build's world API no longer exists:

```csharp
public static void Write(string profileRoot, string contentHash)
{
    var world = new WorldDelta(M2Fixtures.Generator(), M2Fixtures.Tuple(contentHash), new Registry());
    var cells = M2Fixtures.TenCells;
    world.SetFlag(cells[0], "world.door.cellar_open", 1);
    world.HarvestNode(cells[3], world.Baseline(cells[3]).Nodes[0].NodeKey, tick: 1_000);
    world.SetPopulationAlive(cells[5], "pop.r_0_0.c_00_05.deer", 1);
    world.SetFlag(cells[7], "world.lever.mill_gate", 3);
    var wolves = world.Baseline(cells[2]).Populations.Single(p => p.PopulationId.EndsWith(".wolves", StringComparison.Ordinal));
    world.KillOccupant(wolves.Slots[0].SlotKey);
    var deer = world.Baseline(cells[4]).Populations.Single(p => p.PopulationId.EndsWith(".deer", StringComparison.Ordinal));
    world.MoveOccupant(deer.Slots[1].SlotKey, 1_234, 5_678);
    var player = new PlayerRecord(M2Fixtures.Player().Id, "Aelin", 150_250, 12_000, -40_125, new[]
    {
        new InventoryEntry(EntityId.Create(EntityKind.Item, 1_700_000_000_001, new byte[] { 9, 9, 9, 9, 9, 9, 9, 9, 9, 1 }), "item.weapon.iron_sword", 1),
        new InventoryEntry(EntityId.Create(EntityKind.Item, 1_700_000_000_002, new byte[] { 9, 9, 9, 9, 9, 9, 9, 9, 9, 2 }), "item.potion.healing_draught", 3),
    });
    new SaveStore(profileRoot).Save("quick", SaveDocuments.Capture(world, player, "0.1.0", 5_000, playtimeSeconds: 321.5));
}
```
