using System.Collections.Immutable;
using UNNAMED.Application;
using UNNAMED.Domain;
using UNNAMED.Domain.Companions;
using UNNAMED.Domain.Spatial;
using UNNAMED.Persistence;
using UNNAMED.SaveTool;
using UNNAMED.World;
using UNNAMED.World.Runtime;
using Xunit.Abstractions;
using Registry = UNNAMED.EntityRegistry.EntityRegistry;

namespace UNNAMED.Soak.Tests;

public sealed class SaveFuzzTests(ITestOutputHelper output)
{
    [Fact, Trait("Category", "Smoke")]
    public void SaveLoadContinuation_Smoke() => Fuzz(8, 103);

    [SoakFact, Trait("Category", "Soak")]
    public void SaveLoadContinuation_183DaysAtRandomPoints() => Fuzz(128, 104);

    private void Fuzz(int checkpoints, int seed)
    {
        using var w = new SoakWorld();
        var setup = SoakWorld.WithHireling(w.Session.Setup);
        var kera = setup.Layout.Npcs.Single(n => n.NpcId == "npc.ashen_hollow.kera_voss");
        var sim = w.New((ulong)seed, setup, r => SoakWorld.Place(SoakWorld.Fund(r, 20_000), setup, kera.XMm, kera.ZMm));
        Assert.Null(w.Command(sim, new HireCommand(sim.PlayerId, SoakWorld.Hireling, HirelingRole.Porter, 30, true)));
        var continuous = w.RoundTrip(sim, "start");
        var resumed = w.Load(setup, "start");
        var rng = new Random(seed);
        long total = 183 * setup.Clock.TicksPerDay;
        var cuts = Enumerable.Range(0, checkpoints - 1).Select(_ => rng.NextInt64(1, total)).Append(total).Order().ToArray();
        long last = 0;
        foreach (long point in cuts)
        {
            continuous.Advance(point - last);
            resumed.Advance(point - last);
            // Mid-trade: open the actual dialogue and buy; the next command is identical in both branches.
            foreach (var branch in new[] { continuous, resumed })
            {
                SoakWorld.AwaitTrade(branch, kera.NpcId);
                Assert.Null(w.Command(branch, new TalkCommand(branch.PlayerId, kera.NpcId)));
                Assert.Null(w.Command(branch, new ChooseCommand(branch.PlayerId, "trade")));
                var arrow = branch.Wares(kera.NpcId)!.Wares.FirstOrDefault(e => e.ItemId == "item.ammo.arrow_rough");
                if (arrow is not null) Assert.Null(w.Command(branch, new BuyCommand(branch.PlayerId, kera.NpcId, arrow.Ref, 1)));
            }
            var before = resumed;
            resumed = w.RoundTrip(resumed);
            SoakWorld.Equal(before, resumed, $"seed={seed} checkpoint={point} exact save/load");
            SoakWorld.Equal(continuous, resumed, $"seed={seed} checkpoint={point} after load", replayable: true);
            for (int t = 0; t < 20 + rng.Next(40); t++)
            {
                // Refresh intent every tick, as the client does; keep the actor within trade reach.
                foreach (var branch in new[] { continuous, resumed })
                {
                    branch.Enqueue(new MoveCommand(branch.PlayerId, MoveIntent.Idle(0)));
                    branch.DrainCommands(); branch.Step();
                }
            }
            SoakWorld.Equal(continuous, resumed, $"seed={seed} checkpoint={point} continuation", replayable: true);
            last = point;
        }
        output.WriteLine($"seed={seed}: {checkpoints} random save/load points across 183 days; trade and porter contract exercised");
    }

    [Fact, Trait("Category", "Smoke")]
    public void EveryHistoricalFixture_MigratesAndAdvancesADay()
    {
        using var w = new SoakWorld();
        string fixtures = Path.Combine(SoakWorld.Repo, "tests", "Persistence.Tests", "Fixtures");
        var content = SaveToolApp.ReadContent(Path.Combine(fixtures, "content"), "0.2.9", TextWriter.Null)!;
        var generator = new CellBaselineGenerator(WorldgenProfileFile.Read(Path.Combine(fixtures, "worldgen_profile.json")));
        var versions = Directory.EnumerateDirectories(fixtures, "v*").Select(p => int.Parse(Path.GetFileName(p)[1..])).Order().ToArray();
        Assert.Equal(Enumerable.Range(SaveFormat.OldestSupportedSchema, SaveFormat.SchemaVersion - SaveFormat.OldestSupportedSchema + 1), versions);
        foreach (int v in versions)
        {
            string root = Path.Combine(w.Root, "fixture-" + v), target = Path.Combine(root, SaveSlots.Quick);
            Directory.CreateDirectory(target);
            foreach (string file in Directory.EnumerateFiles(Path.Combine(fixtures, "v" + v, SaveSlots.Quick)))
                File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
            var loaded = new SaveStore(root).Load(SaveSlots.Quick, new LoadContext(generator, content, new Registry()));
            Assert.True(loaded.IsComplete, $"v{v} incomplete");
            var sim = w.Start(w.Session.Setup, loaded.Player, loaded.World, loaded.Manifest.WorldTick);
            sim.Advance(sim.Setup.Clock.TicksPerDay);
            Assert.Equal(loaded.Manifest.WorldTick + sim.Setup.Clock.TicksPerDay, sim.WorldTick);
            output.WriteLine($"v{v} -> v{SaveFormat.SchemaVersion}: loaded and advanced one day");
        }
    }
}
