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
4. The fixtures load against `content/` (fixture content 0.2.9) and `worldgen_profile.json`. Those are
   test data, not game content. `content-0.1.0/` is the pack v1-v3 were written with; `content-0.1.1/` -
   0.1.0 plus the skill, formula and recipe definitions the progression record names - is the pack v4
   was written with; `content-0.1.2/` - 0.1.1 plus a region, the place the discovery record names, and the
   movement and tier config a region needs - is the pack v5 and v6 were written with; `content-0.1.3/` - 0.1.2
   plus the two effects the effect record names - is the pack v7 was written with; `content-0.1.4/` - 0.1.3 plus the
   creature a creature record names - is the pack v8 and v9 were written with; `content-0.1.5/` - 0.1.4 plus the NPC and the
   conversation the relationship and conversation records name - is the pack v10 was written with; `content-0.1.6/` - 0.1.5 plus the two
   quests the quest records name, and the warden's replies that start them - is the pack v11 to v14 were written with; `content-0.1.7/` - 0.1.6 plus what the schema-15 records name (five pieces, among them `piece.fixture.old_wall`; the factions `faction.fixture.keepers` and `faction.fixture.delvers`, seated at `location.wolf_den`; `npc.fixture.smith` and where he stands; timber; and the building, faction and navigation config) - is the pack v15 was written with. Content 0.2.0 renamed the
   potion; 0.2.1 renamed the formula; 0.2.2 renamed the place; 0.2.3 gave the items and creatures their Phase-1
   schema fields, which today's content checks require; 0.2.4 gave the sword its attack timing (M3c requires it)
   and renamed the weakness; 0.2.5 renamed the ash hound; 0.2.6 gave the recipe the fields today's recipe checks
   require (M3f); 0.2.7 renamed the warden and the warden's conversation (M4); 0.2.8 added the quests and renamed the errand (M5); 0.2.9 added the M7 definitions (the five pieces, the two factions seated at `location.den_mouth`, the smith placed 2 m from its anchor, timber, and the building and faction config) and renamed the old wall to `piece.fixture.wall` and the delvers to `faction.fixture.diggers` (M7). The current pack deliberately omits the optional `config.navigation` (the owner's ruling on the M7 E2.3 STOP): its times need `config.time`, which switches on combat and magic checks the fixture's stub creatures and spell do not meet, so the pack navigates with `NavConfig.Default`, which `NavConfigDefault_IsTheShippedFile` pins to the shipped file. If a later M7 lint rejects the pack, its content is fixed under 0.2.10, never the lint, with the version constants, the probe mirror and this paragraph in the same commit. The writer packs are historical and are never edited, so they need not pass today's
   checks; the current pack must.

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
| `effect.bleeding` x2 (expires tick 5100, next tick 5020); `effect.weakness` (expires 5600) | player effects | Schema 7. The weakness was **renamed** to `effect.weakened` in content 0.2.4: the rename must reach the effect record. v1-v6 migrate to none |
| `spawn.fixture.den#0`: a grey wolf moved to (12345, 67890) mm, facing 90000, health 21, searching where it last saw its target at tick 4990 (awareness 45, search until 5110, has called) | creature record in `r_0_0:c_00_01` | Schema 8: a creature's body and mind. v1-v7 migrate to no creature records |
| `spawn.fixture.den#1`: a corpse since tick 4800, half searched - `corpse.fixture_den.m1_g0` holds one `healing_draught` | creature record and changed container in `r_0_0:c_00_01` | Schema 8: a corpse is a creature record plus an ordinary changed container; the potion inside is renamed |
| `spawn.fixture.ridge#0`: `creature.beast.ash_hound`, generation 2, gone since tick 4900, due back at tick 30000 | creature record in `r_0_0:c_00_02` | Schema 8. The species was **renamed** to `creature.beast.ash_ember_hound` in content 0.2.5: the rename must reach the creature record |
| The carried sword is fine (quality 1); the dropped `healing_draught` x3 is fine; the chest's `healing_draught` x4 is crude (quality -1) | player inventory, created instance, changed container | Schema 9: quality on every kind of stack, both directions. v1-v8 migrate to standard (0) everywhere |
| `npc.fixture.warden` thinks Aelin trust 12, respect -3; of `dialogue.fixture.warden` Aelin has heard `greet` and `rumour` | player relationships and conversations | Schema 10. The warden and the conversation were **renamed** to `npc.fixture.warden_sera` and `dialogue.fixture.warden_sera` in content 0.2.7: the renames must reach both records. v1-v9 migrate to none |
| `quest.fixture.errand` active since tick 4000 - `o_ask` satisfied at 4001, `o_den` active - and `quest.fixture.cull` completed at tick 3500 by `o_cull`, its three wolves counted (progress 3) | player quests | Schema 11. The errand was **renamed** to `quest.fixture.wardens_errand` in content 0.2.8: the rename must reach the quest record. v1-v10 migrate to none |
| `npc.fixture.warden` has joined Aelin: following, up, 64 health, at (148.75, -41.5) facing 45 degrees, two ticks without headway, last in a fight at tick 4950, three trail marks ahead | player companions | Schema 12. The warden was **renamed** to `npc.fixture.warden_sera` in content 0.2.7: the rename must reach the companion record. v1-v11 migrate to none |
| Aelin crouched, on the ground | player posture | Schema 13 (the owner's M6 playtest: jump and crouch). v1-v12 migrate to standing on the ground; a schema-13 player without a posture is corrupt, not defaulted |
| `spawn.fixture.den#0` may charge again at tick 5060, is immune to a stagger until 5020, and is stunned - 40 ticks from 4995; two sounds wait to be heard: a howl of `creature.beast.ash_hound` at (12345, 67890) mm carrying 30 m, and a blow at (14000, 66000) mm carrying 12 m | creature record continuation, entities `noises` | Schema 14 (the Phase-1 technical audit, L-06). The howl's kind is **renamed** to `creature.beast.ash_ember_hound`, as the creature record's is. v1-v13 migrate to no cooldown, stagger or immunity and nothing to hear; a schema-14 creature without its continuation, or a section without its noises, is corrupt, not defaulted |
| The warden walks a planned route: goal (150250, -40125) mm, three corners, planned at tick 4990 with stamp `0x0123456789ABCDEF`, watching (129000, -61000)-(171000, -20000) mm, cut short at its corner limit (partial) | player companion route | Schema 15 (M7): every route field set. v1-v14 migrate to no route (`none`); a schema-15 companion without a route is corrupt, not defaulted |
| The faction ledger: next act 3; act 1 a `creature.beast.wolf_grey` killed in `r_0_0:c_00_02` at (20000, 250000) mm, tick 4100; act 2 `world.lever.mill_gate` set in `r_0_0:c_00_07`, tick 4200, known by no faction. `faction.fixture.delvers` learned of act 1 from `npc.fixture.smith` at 4300 (-100) and `faction.fixture.keepers` from `npc.fixture.warden` at 4150 (+100); standing delvers -100, keepers +100 | player factions | Schema 15 (M7): one act moves two factions in opposite directions. The delvers were **renamed** to `faction.fixture.diggers` in content 0.2.9 (the rename must reach knowledge and standing), and the warden's rename reaches the `via`. v1-v14 migrate to the empty ledger, although the world holds a wolf corpse and a set lever: nothing is reconstructed |
| Six placed pieces around the z = 500 m seam, ordinals 1, 2, 3, 5, 7 and 9, and structure sequence 9 (4, 6 and 8 were taken down): a pad whose square straddles `c_00_04`/`c_00_05`; a doorway with its door open; `piece.fixture.old_wall`, damaged to 150 and hosted in `c_00_05`; a chest whose site lies in `c_00_05`; and a foreign owner's pad, turned once | entities `pieces`, `structure_seq` | Schema 15 (M7). The old wall was **renamed** to `piece.fixture.wall` in content 0.2.9. v1-v14 migrate to nothing built, sequence 0 |
| The chest's container, `container.pce_<ulid lower-case>`, with its derived `cnt_` ID, holds `item.material.timber` x3 | changed container in `r_0_0:c_00_05` | Schema 15 (M7): a piece chest is an ordinary changed container with a derived key and ID |
| `npc.fixture.smith` walks home (`to_home`) from a work place since taken back, at (40000, 120000) mm facing 180 degrees, three ticks without headway, two corners of his route to go | entities `npc_errands`, hosted in `r_0_0:c_00_00` | Schema 15 (M7): an errand with its route. v1-v14 migrate to no errand |

## Provenance

| Version | Written by | How |
|---|---|---|
| `v1/` | M2's writer, commit `7ff4c57` | In a scratch worktree of `7ff4c57`, with the one-off probe addition below: `M2.Probe fixture <dir> sha256:7f522a30...` |
| `v2/` | The M2b schema-2 writer, commit `b310979` | `M2.Probe fixture <dir>` (`M2Fixtures.Historical`) |
| `v3/` | The M2b schema-3 writer | `M2.Probe fixture <dir>` |
| `v4/` | The M2c schema-4 writer | `M2.Probe fixture <dir>` (writes content identity 0.1.1) |
| `v5/` | The M3 schema-5 writer | `M2.Probe fixture <dir>` (writes content identity 0.1.2) |
| `v6/` | The M3b schema-6 writer | `M2.Probe fixture <dir>` (writes content identity 0.1.2) |
| `v7/` | The M3c schema-7 writer | `M2.Probe fixture <dir>` (writes content identity 0.1.3) |
| `v8/` | The M3d schema-8 writer | `M2.Probe fixture <dir>` (writes content identity 0.1.4) |
| `v9/` | The M3f schema-9 writer | `M2.Probe fixture <dir>` (writes content identity 0.1.4) |
| `v10/` | The M4 schema-10 writer | `M2.Probe fixture <dir>` (writes content identity 0.1.5) |
| `v11/` | The M5 schema-11 writer | `M2.Probe fixture <dir>` (writes content identity 0.1.6) |
| `v12/` | The M6 schema-12 writer | `M2.Probe fixture <dir>` (writes content identity 0.1.6) |
| `v13/` | The owner-playtest schema-13 writer | `M2.Probe fixture <dir>` (writes content identity 0.1.6) |
| `v15/` | The M7 schema-15 writer | `M2.Probe fixture <dir>` (writes content identity 0.1.7) |

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
