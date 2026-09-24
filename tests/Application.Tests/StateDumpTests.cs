using UNNAMED.Persistence;

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
