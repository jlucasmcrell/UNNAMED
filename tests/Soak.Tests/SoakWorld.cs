using System.Collections.Immutable;
using UNNAMED.Application;
using UNNAMED.Domain;
using UNNAMED.Domain.Companions;
using UNNAMED.Domain.Combat;
using UNNAMED.Domain.Social;
using UNNAMED.Domain.Spatial;
using UNNAMED.Persistence;
using UNNAMED.World;
using UNNAMED.World.Runtime;
using Registry = UNNAMED.EntityRegistry.EntityRegistry;

namespace UNNAMED.Soak.Tests;

// Explicit opt-in rather than Trait alone: dotnet test has no implicit category exclusion.
public sealed class SoakFactAttribute : FactAttribute
{
    public SoakFactAttribute(string level = "Soak")
    {
        if (Environment.GetEnvironmentVariable("OTHERREACH_SOAK") is not "1" &&
            Environment.GetEnvironmentVariable("OTHERREACH_SOAK") != level)
            Skip = $"Opt-in {level}: set OTHERREACH_SOAK={level}; see README.md";
    }
}

internal sealed class SoakWorld : IDisposable
{
    public static string Repo => FindRepo();
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "otherreach-soak-" + Guid.NewGuid().ToString("N"));
    public GameSession Session { get; }
    public SaveStore Store { get; }
    private readonly Dictionary<Simulation, EventBus> buses = new();
    public SoakWorld()
    {
        Session = GameSession.Boot(new GameOptions(Path.Combine(Repo, "content"), Root));
        Store = new SaveStore(Root);
    }

    public Simulation New(ulong seed, SimulationSetup? setup = null, Func<PlayerRecord, PlayerRecord>? change = null)
    {
        setup ??= Session.Setup;
        var player = Simulation.NewCharacter(setup, EntityId.NewId(EntityKind.Character), "Soak", 7);
        return Start(setup, change?.Invoke(player) ?? player,
            new WorldDelta(Session.Generator, seed, new Registry()), 0);
    }

    public Simulation RoundTrip(Simulation simulation, string slot = "soak")
    {
        Store.Save(SaveSlots.Manual(slot), SaveDocuments.Capture(simulation.World, simulation.CaptureRecord(), Session.Content,
            simulation.WorldTick, 0));
        return Load(simulation.Setup, slot);
    }

    public Simulation Load(SimulationSetup setup, string slot = "soak")
    {
        var loaded = Store.Load(SaveSlots.Manual(slot), new LoadContext(Session.Generator, Session.Content, new Registry()));
        Assert.True(loaded.IsComplete, $"incomplete save: {slot}");
        return Start(setup, loaded.Player, loaded.World, loaded.Manifest.WorldTick);
    }

    public Simulation Start(SimulationSetup setup, PlayerRecord player, WorldDelta world, long tick)
    {
        var bus = new EventBus();
        var sim = Simulation.Start(setup, player, world, tick, bus);
        buses.Add(sim, bus);
        return sim;
    }
    public EventBus Bus(Simulation sim) => buses[sim];

    /// <summary>The Ashen porter, a resident who hires on (WP12b), living their whole day a step from Kera's counter, so the soak can hire them and trade with her from one place.</summary>
    public const string Hireling = "routine.ashen_hollow.porter";

    public static SimulationSetup WithHireling(SimulationSetup setup)
    {
        var kera = setup.Layout.Npcs.Single(n => n.NpcId == "npc.ashen_hollow.kera_voss");
        var spot = new NpcAnchor(kera.XMm - 1_200, kera.ZMm, 90_000);
        var porter = setup.NpcLife.Routines[Hireling];
        return setup with
        {
            NpcLife = setup.NpcLife with
            {
                Routines = setup.NpcLife.Routines.SetItem(Hireling, porter with
                {
                    Anchors = porter.Anchors.ToImmutableSortedDictionary(kv => kv.Key, _ => spot, StringComparer.Ordinal),
                    Trip = null,
                }),
            },
        };
    }

    public string? Command(Simulation sim, GameCommand command)
    {
        string? reason = null;
        void Rejected(CommandRejected e) => reason = e.Reason;
        var bus = buses[sim];
        bus.Subscribe<CommandRejected>(Rejected);
        try { sim.Enqueue(command); sim.DrainCommands(); }
        finally { bus.Unsubscribe<CommandRejected>(Rejected); }
        return reason;
    }

    /// <summary>
    /// WP12's merchants walk their day and trade only at work, in trading hours. Wait them out, as the playthrough waits out a meal: a
    /// few steps while they walk back to their post, an hour at a time while they are shut, eating or away.
    /// </summary>
    public static void AwaitTrade(Simulation sim, params string[] npcIds)
    {
        long hour = sim.Setup.Clock.TicksPerDay / 24;
        bool Away() => npcIds.Any(id => sim.TradeRefusal(id) is not null);
        for (int i = 0; i < 48 && Away(); i++)
        {
            for (int t = 0; t < 400 && Away(); t++)
                sim.Step();
            if (Away())
                sim.Advance(hour);
        }
        Assert.All(npcIds, id => Assert.Null(sim.TradeRefusal(id)));
    }

    public static void Equal(Simulation expected, Simulation actual, string context, bool replayable = false)
    {
        string a = StateDump.Render(expected, replayable), b = StateDump.Render(actual, replayable);
        Assert.True(a == b, context + "\n" + string.Join("\n", StateDump.Compare(a, b, out _).Take(20)));
        if (!replayable) Assert.Equal(expected.StateDigest(), actual.StateDigest());
    }

    public static InventoryEntry Stack(string id, int count) => new(EntityId.NewId(EntityKind.Item), id, count);
    public static PlayerRecord Fund(PlayerRecord r, long coins, params InventoryEntry[] items) =>
        new(r.Id, r.Name, r.XMm, r.YMm, r.ZMm, r.AppearanceSeed, r.Inventory.Concat(items), r.Progression,
            r.FacingMdeg, r.Discoveries, r.Equipment, coins, r.Effects, companions: r.Companions);

    public static PlayerRecord Place(PlayerRecord r, SimulationSetup setup, long x, long z) =>
        new(r.Id, r.Name, x, setup.Layout.Space.Terrain.HeightAtMm(x, z), z, r.AppearanceSeed, r.Inventory, r.Progression,
            r.FacingMdeg, r.Discoveries, r.Equipment, r.Currency, r.Effects, companions: r.Companions);

    private static string FindRepo()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "src", "UNNAMED.sln"))) return d.FullName;
        throw new DirectoryNotFoundException("src/UNNAMED.sln");
    }
    public void Dispose() { if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true); }
}
