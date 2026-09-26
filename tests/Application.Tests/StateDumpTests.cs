using System.Text.Json.Nodes;
using UNNAMED.Persistence;
using UNNAMED.World;
using UNNAMED.World.Runtime;
using Registry = UNNAMED.EntityRegistry.EntityRegistry;

namespace UNNAMED.Application.Tests;

/// <summary>M6: the field-by-field state dump the acceptance playthrough compares across a save and a relaunch (PROTOTYPE.md §9 item 2).</summary>
public class StateDumpTests
{
    private static GameSession Played(TempProfile profile)
    {
        var session = Harness.Boot(profile);
        var simulation = session.NewGame("Wanderer", seed: 42);
        Assert.True(Harness.WalkPath(session, (44, 138), (54.5, 134), (53, 128)));
        session.Submit(new UNNAMED.World.Runtime.InteractCommand(simulation.PlayerId, "door.longhouse"));
        Assert.True(Harness.WalkPath(session, (60, 128), (100, 100)));
        return session;
    }

    [Fact]
    public void ASaveAndALoad_CompareEqual_FieldByField()
    {
        using var profile = new TempProfile();
        var session = Played(profile);
        string saved = StateDump.Render(session.Simulation!);
        session.Save(SaveSlots.Quick);

        var reloaded = Harness.Boot(profile);
        reloaded.Load(SaveSlots.Quick);
        string loaded = StateDump.Render(reloaded.Simulation!);

        Assert.Empty(StateDump.Compare(saved, loaded, out int leaves));
        Assert.True(leaves > 100, $"only {leaves} fields were compared");
        Assert.Contains("\"world_tick\"", saved);
        Assert.Contains("world.hollow.longhouse_door_open", saved);   // the world delta is in it, not just the player
    }

    /// <summary>
    /// F-E6 (M7): the whole M7 state through <see cref="GameSession"/> - the workshop's step 1 with its bench and a chest holding timber,
    /// Kera on her way to work at the bench, and a faction ledger with an act the waystation knows - saved, loaded, saved and loaded again,
    /// compares equal field by field, with at least 265 more fields than a new game's.
    /// </summary>
    [Fact]
    public void ABuiltStaffedAndKnownWorld_CompareEqual_FieldByField()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var fresh = Harness.Boot(profile);
        string newGame = StateDump.Render(fresh.NewGame("Wanderer", seed: 42));
        Assert.Empty(StateDump.Compare(newGame, newGame, out int newGameLeaves));

        var arena = BuildingTests.Builder(session, (102.0, 102.0));
        BuildingTests.WorkshopStepOne(arena);
        Assert.Null(BuildingTests.Place(arena, "piece.station.anvil", 100_500, 103_500, 3));
        Assert.Null(BuildingTests.Place(arena, "piece.storage.chest", 103_500, 103_500, 0));
        var chest = arena.Simulation.Pieces.Single(p => p.DefId == "piece.storage.chest");
        var timber = arena.Simulation.Player.Inventory.First(e => e.DefId == "item.material.timber");
        Assert.True(arena.WalkTo(103.5, 102.9), $"the walk to the chest stopped at {arena.Simulation.Player.Body}");
        Assert.Null(arena.Submit(new MoveItemCommand(arena.Player, timber.ItemId.Value, ItemPlace.Carried, ItemPlace.In(chest.ContainerKey!), 2)));
        ErrandTests.ToKera(arena, (100.5, 100.3), (100.5, 97.6), (97.0, 97.0));
        Assert.Null(ErrandTests.Assign(arena, "npc.ashen_hollow.kera_voss", arena.Simulation.Pieces.Single(p => p.DefId == "piece.station.anvil").Id));
        arena.Tick(40);
        Assert.Equal(NpcErrandPhase.ToWork, arena.Simulation.World.NpcErrand("npc.ashen_hollow.kera_voss")!.Phase);

        var act = new UNNAMED.Domain.Factions.ActRecord(1, UNNAMED.Domain.Factions.ActKinds.CreatureKilled, "creature.construct.animated_armour",
            CellKey.OfWorld(120, 63).ToString(), 120_000, 63_000, 10);
        var ledger = new UNNAMED.Domain.Factions.FactionLedger(2, System.Collections.Immutable.ImmutableArray.Create(act),
            System.Collections.Immutable.ImmutableArray.Create(new UNNAMED.Domain.Factions.FactionKnowledge("faction.ashen_hollow.waystation", 1,
                UNNAMED.Domain.Factions.Identities.Identified, UNNAMED.Domain.Factions.KnowledgeSources.Reported, "npc.ashen_hollow.kera_voss", 12, 100)),
            System.Collections.Immutable.ImmutableArray.Create(new UNNAMED.Domain.Factions.FactionStanding("faction.ashen_hollow.waystation", 100)));
        new SaveStore(profile.Root).Save(SaveSlots.Manual("built"), SaveDocuments.Capture(arena.Simulation.World,
            arena.Simulation.CaptureRecord() with { Factions = ledger }, session.Content, arena.Simulation.WorldTick, 0));

        var first = Harness.Boot(profile);
        Assert.True(first.Load(SaveSlots.Manual("built")).IsComplete);
        string once = StateDump.Render(first.Simulation!);
        first.Save(SaveSlots.Quick);
        var second = Harness.Boot(profile);
        Assert.True(second.Load(SaveSlots.Quick).IsComplete);
        string twice = StateDump.Render(second.Simulation!);

        var differences = StateDump.Compare(once, twice, out int leaves);
        Assert.True(differences.Count == 0, string.Join("; ", differences.Take(6)));
        Assert.True(leaves >= newGameLeaves + 265, $"{leaves} fields, {leaves - newGameLeaves} beyond a new game's {newGameLeaves}");
        Assert.Contains("npc.ashen_hollow.kera_voss", once);
        Assert.Contains("faction.ashen_hollow.waystation", once);
    }

    [Fact]
    public void ALoadMidHunt_ComparesTheWorldTheGameRebuilt_NotOnlyItsRecords()
    {
        // The Phase-1 technical audit, T-01: the dump reads the creatures, containers and pools the running game holds after a load,
        // not only the records it rebuilt them from - one wolf dead and searched-for, the other wounded and hunting when the save is made.
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = Arena.OpenCreatures(session, session.Setup, (120, 60), 0,
            new[] { (Arena.Wolf, 120.0, 62.0, "pack_hunter"), (Arena.Wolf, 124.0, 60.0, "pack_hunter") });
        arena.Fight(arena.Creature(0), 800);
        arena.Fight(arena.Creature(1), 40);
        arena.Tick(10);
        string saved = StateDump.Render(arena.Simulation);

        var store = new SaveStore(profile.Root);
        store.Save(SaveSlots.Manual("hunt"), SaveDocuments.Capture(arena.Simulation.World, arena.Simulation.CaptureRecord(), session.Content,
            arena.Simulation.WorldTick, 0));
        var loaded = store.Load(SaveSlots.Manual("hunt"), new LoadContext(session.Generator, session.Content, new Registry()));
        Assert.True(loaded.IsComplete);
        var again = Simulation.Start(arena.Simulation.Setup, loaded.Player, loaded.World, loaded.Manifest.WorldTick, new EventBus());

        Assert.Empty(StateDump.Compare(saved, StateDump.Render(again), out _));
        var live = JsonNode.Parse(saved)!["live"]!;
        var creatures = live["creatures"]!.AsArray();
        Assert.Equal(2, creatures.Count);
        Assert.Contains(creatures, c => c!["Condition"]!.GetValue<string>() == "Corpse");
        Assert.Contains(creatures, c => c!["Condition"]!.GetValue<string>() == "Alive" && c["Health"]!.GetValue<int>() < c["MaxHealth"]!.GetValue<int>());
        Assert.Contains(live["containers"]!.AsArray(), c => c!["Site"]!["Key"]!.GetValue<string>() == arena.Creature(0).CorpseKey);
        Assert.Equal(new[] { "creatures[].Phase", "creatures[].PhaseTicksLeft", "npcs[].Talking", "npcs[].Body.FacingMdeg", "companions[].Doing",
                "combat.Phase", "combat.PhaseTicksLeft", "combat.AttackSource", "combat.Blocking", "combat.Casting" }.Order(StringComparer.Ordinal),
            live["left_out"]!.AsArray().Select(n => n!.GetValue<string>()).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void ADifference_IsNamedByItsPath()
    {
        using var profile = new TempProfile();
        var session = Played(profile);
        string before = StateDump.Render(session.Simulation!);
        Harness.Ticks(session, 1);
        string after = StateDump.Render(session.Simulation!);

        var differences = StateDump.Compare(before, after, out _);
        Assert.Contains(differences, d => d.StartsWith("$.world_tick: expected ", StringComparison.Ordinal));
    }

    [Fact]
    public void TheSameScript_PlaysTheSameGame_WhateverTheFreshIdsAre()
    {
        using var first = new TempProfile();
        using var second = new TempProfile();
        string a = StateDump.Render(Played(first).Simulation!, replayable: true);
        string b = StateDump.Render(Played(second).Simulation!, replayable: true);

        Assert.Equal(a, b);
        Assert.DoesNotContain("AppearanceSeed", a);
        Assert.Contains("\"chr#1\"", a);   // the character, by kind and order, not by its fresh ULID
    }
}
