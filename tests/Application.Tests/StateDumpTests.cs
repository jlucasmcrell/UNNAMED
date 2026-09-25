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
