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
   - freezes the previous current section shapes as `Sections/V{N-1}` and repoints the step that
     produces schema N-1 at the frozen types, so every step stays a fixed `V(n) -> V(n+1)` function;
   - updates every `expected.json` whose current state gained or changed a field.
   `EveryShippedSchemaVersion_HasAFixture_AndNoneFromTheFuture` fails until all of this is done.
3. **`expected.json` is the current state the fixture must load to.** Change it only when the
   current shape intentionally changes. Never change it to make a failing migration pass. To regenerate
   after an intended shape change, run the fixture tests once with `UNNAMED_WRITE_FIXTURE_EXPECTATIONS=1`,
   then review the diff line by line. Apart from generated ULIDs, every fixture must describe the world
   below.
4. The fixtures load against `content/` (fixture content 0.2.3) and `worldgen_profile.json`. Those are
   test data, not game content. `content-0.1.0/` is the pack v1-v3 were written with; `content-0.1.1/` -
   0.1.0 plus the skill, formula and recipe definitions the progression record names - is the pack v4
   was written with; `content-0.1.2/` - 0.1.1 plus a region, the place the discovery record names, and the
   movement and tier config a region needs - is the pack v5 was written with. Content 0.2.0 renamed the
   potion; 0.2.1 renamed the formula; 0.2.2 renamed the place; 0.2.3 gave the items and creatures their
   Phase-1 schema fields, which today's content checks require. The writer packs are historical and are
   never edited, so they need not pass today's checks; the current pack must.

## The fixture world

The same logical world at every version: seed `0x5C1A9E7B4D2F0083`, the profile in
`worldgen_profile.json`, and fixture content 0.1.0 (content hash
`sha256:7f522a30f46119bbe25e50c29a9db0a4ff21be31b080dc7a5eba0f225379353d`); v4 used 0.1.1
(`sha256:0a46ac0214a763a5af84d68d7a83ae69a57fab4d749574f3480e7232819c1f61`), which adds only definitions.

| What | Where | Why it is here |
|---|---|---|
| Player "Aelin", `chr_01HF7YAT00041061050R3GG28A`, position (150250, 12000, -40125) mm | player | Fully serialized state |
| Appearance seed `0x32599743E39279EF` | player | Required from schema 3. The v1 and v2 saves predate it, so the 2 -> 3 step derives it from the ULID. The v3 writer wrote the same value |
| `item.weapon.iron_sword` x1, `item.potion.healing_draught` x3 | player inventory | The potion was **renamed** to `item.potion.minor_healing` in content 0.2.0 (`content/_aliases.yaml`) |
| `world.door.cellar_open = 1` | cell `r_0_0:c_00_00` | A changed cell (flag) |
| First node of the cell harvested at tick 1000 | cell `r_0_0:c_00_03` | Keyed `node.<cell>.0` in schema 1; `node.<cell>.iron_vein.00` from schema 2 |
| Deer population alive = 1 | cell `r_0_0:c_00_05` | Spawn budget divergence |
| `world.lever.mill_gate = 3` | cell `r_0_0:c_00_07` | A second flag |
| Wolf slot 00 killed | entity in `r_0_0:c_00_02` | A **tombstoned baseline entity**: it must stay dead |
| Deer slot 01 moved to (1234, 5678) cm | entity in `r_0_0:c_00_04` | Positional state carried as-is |
| `item.weapon.iron_sword` placed at (500, 600) cm | created instance in `r_0_0:c_00_06` | A **created persistent entity**. v3 and later: schema 3 is the first that can record one |
| Level 3 (120 progress, 35 XP debt), might +1, 1 unspent point, a quest grant of endurance +1 | player progression | The **progression record**. v4 and later: schema 4 is the first that can record one; v1-v3 migrate to its empty value |
| `skill.one_hand_blade` 5, `skill.athletics` 2 | player progression | Skills, resolved by the definition-ID pass |
| Knows `spell.ember.firebolt` (teacher) and `recipe.alchemy.salve_minor` (book) | player progression | The formula was **renamed** to `spell.ember.bolt` in content 0.2.1: the rename must reach the knowledge record |
| First-produced `item.potion.healing_draught`; novelty claimed for the recipe | player progression | The production record names the renamed potion too, so the load applies that alias twice |
| Health 87, Focus 40, Strain 6; three wolf kills on day 0 at the wolves' cluster | player progression | Pools (a nil pool is full) and the AG-2/AG-3 guard memory |
| Facing 123456 millidegrees | player | Schema 5. v1-v4 migrate to 0 (+Z) |
| Discovered `location.wolf_den` (visited, tick 3000) | player discoveries | Schema 5. The place was **renamed** to `location.den_mouth` in content 0.2.2: the rename must reach the discovery record. v1-v4 migrate to none |
| The sword in `main_hand`; 40 coin | player equipment, purse | Schema 6. v1-v5 migrate to nothing equipped and no coin |
| `item.potion.healing_draught` x3 dropped | created instance in `r_0_0:c_00_08` | Schema 6: a created instance keeps its count, and the renamed potion must be renamed in the world too |
| `container.fixture_chest`: a sword and `healing_draught` x4 | changed container in `r_0_0:c_00_09` | Schema 6: a changed container's whole contents, with identities; the potion is renamed inside it |

## Provenance

| Version | Written by | How |
|---|---|---|
| `v1/` | M2's writer, commit `7ff4c57` | In a scratch worktree of `7ff4c57`, with the one-off probe addition below: `M2.Probe fixture <dir> sha256:7f522a30...` |
| `v2/` | The M2b schema-2 writer, commit `b310979` | `M2.Probe fixture <dir>` (`M2Fixtures.Historical`) |
| `v3/` | The M2b schema-3 writer | `M2.Probe fixture <dir>` |
| `v4/` | The M2c schema-4 writer | `M2.Probe fixture <dir>` (writes content identity 0.1.1) |
| `v5/` | The M3 schema-5 writer | `M2.Probe fixture <dir>` (writes content identity 0.1.2) |
| `v6/` | The M3b schema-6 writer | `M2.Probe fixture <dir>` (writes content identity 0.1.2) |

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
