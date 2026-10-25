using System.Diagnostics;
using UNNAMED.Application;
using UNNAMED.Domain.Character;
using UNNAMED.Persistence;
using Xunit.Abstractions;

namespace UNNAMED.Soak.Tests;

/// <summary>
/// PERSISTENCE.md §9 and §10: T-24, the save-size budget - a long-lived world's save stays inside §9's budget, its sections do not grow
/// while nothing changes, and a second Reckoning of the world living on does not grow it without bound; and T-25, the autosave hitch - what
/// the frame pays to take a save (the capture; everything else is written in the background, §8.2) stays inside its budget. The P99 half
/// of T-25 is held to 2 ms only on the target machine (OTHERREACH_TARGET_MACHINE=1); elsewhere the median is, and the tail is printed.
/// </summary>
[Collection(Timing.Alone)]
public sealed class SaveBudgetTests(ITestOutputHelper output)
{
    /// <summary>§9's budget, the top of each range: a section past it fails the build.</summary>
    private static readonly Dictionary<string, long> Budget = new(StringComparer.Ordinal)
    {
        [SaveFormat.Manifest] = 4 * 1024,
        [SaveFormat.Player] = 200 * 1024,
        [SaveFormat.Cells] = 20L * 1024 * 1024,
        [SaveFormat.Entities] = 40L * 1024 * 1024,
    };

    /// <summary>A second Reckoning may add no more than this to the whole save: the world lives on, its record does not pile up.</summary>
    private const long SecondReckoningGrowthBytes = 256 * 1024;

    private static Dictionary<string, long> Sizes(string slot) =>
        Directory.EnumerateFiles(slot).ToDictionary(f => Path.GetFileName(f)!, f => new FileInfo(f).Length, StringComparer.Ordinal);

    [Fact, Trait("Category", "Smoke")]
    public void T24_AWorldThatLivedAReckoning_SavesInsideTheBudget_AndASecondDoesNotPileUp()
    {
        using var w = new SoakWorld();
        var sim = w.New(24);
        long reckoning = 183 * w.Session.Setup.Clock.TicksPerDay;

        sim.Advance(reckoning);
        var first = Save(w, sim, "t24_first");
        var again = Save(w, sim, "t24_again");
        sim.Advance(reckoning);
        var second = Save(w, sim, "t24_second");

        foreach (var (file, bytes) in first.Concat(second))
            output.WriteLine($"{file}: {bytes} bytes");
        foreach (var (file, limit) in Budget)
        {
            Assert.True(first[file] <= limit, $"{file} is {first[file]} bytes after a Reckoning, past its {limit}-byte budget (§9)");
            Assert.True(second[file] <= limit, $"{file} is {second[file]} bytes after two Reckonings, past its {limit}-byte budget (§9)");
        }
        // Nothing changed between the two: the sections are the same size (T-03's minimality, measured in bytes).
        foreach (string file in new[] { SaveFormat.Player, SaveFormat.Cells, SaveFormat.Entities })
            Assert.Equal(first[file], again[file]);
        long growth = second.Where(kv => Budget.ContainsKey(kv.Key)).Sum(kv => kv.Value) - first.Where(kv => Budget.ContainsKey(kv.Key)).Sum(kv => kv.Value);
        Assert.True(growth <= SecondReckoningGrowthBytes, $"a second Reckoning grew the save by {growth} bytes (bound {SecondReckoningGrowthBytes})");
    }

    private static Dictionary<string, long> Save(SoakWorld w, World.Runtime.Simulation sim, string slot)
    {
        w.Store.Save(SaveSlots.Manual(slot), SaveDocuments.Capture(sim.World, sim.CaptureRecord(), w.Session.Content, sim.WorldTick, 0));
        return Sizes(w.Store.SlotPath(SaveSlots.Manual(slot)));
    }

    /// <summary>§8.2: the capture the frame pays for a save, at P99, on the target machine (RAZER).</summary>
    private const double CaptureBudgetMs = 2.0;

    /// <summary>
    /// Off the target machine the P99 tail is the machine's (a shared cloud box's scheduler and GC), not the game's: the median must still
    /// be inside the budget, and the P99 inside this ceiling, which catches a capture that has grown by a multiple. On RAZER, set
    /// OTHERREACH_TARGET_MACHINE=1 and the P99 itself is held to <see cref="CaptureBudgetMs"/>.
    /// </summary>
    private const double OffTargetP99CeilingMs = 10.0;

    private static bool OnTargetMachine => Environment.GetEnvironmentVariable("OTHERREACH_TARGET_MACHINE") == "1";

    [Fact, Trait("Category", "Smoke")]
    public void T25_TakingASave_CostsTheFrameNoMoreThanItsBudget_P99_InALivedInWorld()
    {
        string root = Path.Combine(Path.GetTempPath(), "otherreach-t25-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var session = GameSession.Boot(new GameOptions(Path.Combine(SoakWorld.Repo, "content"), root));
            var sim = session.NewGame(CharacterSpec.Quick("Wanderer", null), seed: 25);
            sim.Advance(183 * session.Setup.Clock.TicksPerDay);   // a loaded world: a Reckoning of the world living on
            for (int i = 0; i < 40; i++)
                session.Frame(session.TickSeconds);
            session.WaitForSaves(TimeSpan.FromSeconds(60));

            const int Samples = 200;
            var costs = new List<double>(Samples);
            for (int i = 0; i < 20 + Samples; i++)
            {
                session.Frame(session.TickSeconds);
                long started = Stopwatch.GetTimestamp();
                session.SaveInBackground(SaveSlots.Quick);   // the frame's whole share: the capture, then the hand-off to the writer
                double ms = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                if (i >= 20)   // the first few warm the JIT
                    costs.Add(ms);
                session.WaitForSaves(TimeSpan.FromSeconds(60));
            }
            costs.Sort();
            double p50 = costs[Samples / 2], p99 = costs[(int)Math.Ceiling(Samples * 0.99) - 1], max = costs[^1];
            output.WriteLine($"capture on the frame: P50 {p50:0.000} ms, P99 {p99:0.000} ms, max {max:0.000} ms over {Samples} saves " +
                             $"(budget {CaptureBudgetMs} ms at P99 on the target machine; this run {(OnTargetMachine ? "is" : "is not")} on it)");
            Assert.True(p50 <= CaptureBudgetMs, $"taking a save cost the frame {p50:0.000} ms at the median, past the {CaptureBudgetMs} ms budget (§8.2)");
            double ceiling = OnTargetMachine ? CaptureBudgetMs : OffTargetP99CeilingMs;
            Assert.True(p99 <= ceiling, $"taking a save cost the frame {p99:0.000} ms at P99, past {ceiling} ms (§8.2)");
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}

/// <summary>A timing is measured with nothing else running: xUnit runs this collection after the others, never beside them.</summary>
[CollectionDefinition(Alone, DisableParallelization = true)]
public sealed class Timing
{
    public const string Alone = "timing, alone";
}
