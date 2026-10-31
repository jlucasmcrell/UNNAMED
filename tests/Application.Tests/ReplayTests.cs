using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Domain.Character;
using UNNAMED.Domain.Spatial;
using UNNAMED.Persistence;
using UNNAMED.World.Runtime;

namespace UNNAMED.Application.Tests;

/// <summary>
/// CMB-126 (PERSISTENCE.md §5.7, T-28): every command that goes into the simulation is kept in <c>command_log.jsonl</c> beside the
/// character's saves, the save names its digest, the log is capped and rotated, and a recorded session replayed from its log alone reaches
/// a world whose state dump is the one that was saved - leaf for leaf.
/// </summary>
public class ReplayTests
{
    private static GameOptions ScratchRoot(TempProfile profile) =>
        new(Path.Combine(Harness.RepoRoot(), "content"), profile.Root) { LockProfile = false };

    /// <summary>A session with a script in it: walked, a door opened, a few ticks to let the world answer.</summary>
    private static (TempProfile Profile, GameSession Session) Played(TempProfile profile)
    {
        var session = Harness.Boot(profile);
        var simulation = session.NewGame(Harness.Named("Wanderer"), seed: 42);
        Assert.True(Harness.WalkPath(session, (44, 138), (54.5, 134), (53, 128)));
        session.Submit(new InteractCommand(simulation.PlayerId, "door.longhouse"));
        Assert.True(Harness.WalkPath(session, (60, 128), (100, 100)));
        Harness.Ticks(session, 30);
        return (profile, session);
    }

    [Fact]
    public void T28_ARecordedSession_ReplaysFromItsLogToAnIdenticalStateDump()
    {
        using var profile = new TempProfile();
        var (_, session) = Played(profile);
        using (session)
        {
            var simulation = session.Simulation!;
            string saved = StateDump.Render(simulation, replayable: true);
            session.Save(SaveSlots.Quick);

            // The log is beside the slots, it holds every command the simulation logged, and the save names its digest.
            string log = ReplayLog.PathOf(session.SaveFolder);
            Assert.True(File.Exists(log), $"no command log at {log}");
            var contents = ReplayLog.Read(session.SaveFolder);
            Assert.Null(contents.Refusal);
            Assert.Equal(simulation.CommandLog.Count, contents.Entries.Length);
            Assert.Equal(simulation.CommandLog.Select(c => c.Sequence), contents.Entries.Select(e => e.Sequence));
            var manifest = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllBytes(Path.Combine(session.SaveFolder, SaveSlots.Quick, SaveFormat.Manifest)))!;
            Assert.False(string.IsNullOrEmpty(manifest["command_log_sha256"]?.GetValue<string>()));

            // Replay the log into a fresh world, from its own tuple, in a profile of its own.
            using var scratch = new TempProfile();
            var result = Replay.FromSave(ScratchRoot(scratch), session.SaveFolder, SaveSlots.Quick);
            Assert.Null(result.Refusal);
            Assert.Equal(simulation.WorldTick, result.Tick);
            string? disagreement = result.Disagreement(contents);
            Assert.True(disagreement is null, disagreement);

            // The point of the row: the field-by-field dump of the replayed world is the saved one.
            var differences = StateDump.Compare(saved, result.Dump, out int leaves);
            Assert.Empty(differences);
            Assert.True(leaves > 100, $"only {leaves} fields were compared");
        }
    }

    [Fact]
    public void AWorldSavedBeforeItsFirstTick_ReplaysToTheWorldItWasSavedAs()
    {
        // Nothing has been dispatched before tick 0, so the save names no digest - and it is still replayable: the log's own start is
        // all a replay of it needs.
        using var profile = new TempProfile();
        using var session = Harness.Boot(profile);
        var simulation = session.NewGame(Harness.Named("Wanderer"), seed: 11);
        string saved = StateDump.Render(simulation, replayable: true);
        session.Save(SaveSlots.Quick);

        using var scratch = new TempProfile();
        var result = Replay.FromSave(ScratchRoot(scratch), session.SaveFolder, SaveSlots.Quick);
        Assert.Null(result.Refusal);
        Assert.Equal(0, result.Tick);
        Assert.Empty(StateDump.Compare(saved, result.Dump, out _));
    }

    [Fact]
    public void AReplayOfThatReplay_IsTheSameWorldAgain()
    {
        using var profile = new TempProfile();
        var (_, session) = Played(profile);
        string log = ReplayLog.PathOf(session.SaveFolder);
        long tick = session.Simulation!.WorldTick;
        string root = session.SaveFolder;
        session.Dispose();

        using var scratch = new TempProfile();
        var first = Replay.Run(ScratchRoot(scratch), root, tick);
        using var again = new TempProfile();
        var second = Replay.Run(ScratchRoot(again), root, tick);
        Assert.Null(first.Refusal);
        Assert.Null(second.Refusal);
        Assert.Equal(first.Dump, second.Dump);

        // And it dispatched the same commands at the same ticks, in the same order.
        var contents = ReplayLog.Read(root);
        Assert.Equal(contents.Entries.Select(e => (e.Tick, e.Sequence, e.Type)),
            first.Replayed.Select(c => (c.Tick, c.Sequence, c.Command.GetType().Name)));
    }

    [Fact]
    public void AnEditedLog_IsRefusedBeforeItsReplayIsBelieved()
    {
        using var profile = new TempProfile();
        var (_, session) = Played(profile);
        session.Save(SaveSlots.Quick);
        string root = session.SaveFolder;
        session.Dispose();

        string log = ReplayLog.PathOf(root);
        string text = File.ReadAllText(log);
        int at = text.IndexOf("\"rejected\":null", StringComparison.Ordinal);
        File.WriteAllText(log, at >= 0 ? text.Remove(at, 15).Insert(at, "\"rejected\":\"x\"") : text + " ");

        using var scratch = new TempProfile();
        var result = Replay.FromSave(ScratchRoot(scratch), root, SaveSlots.Quick);
        Assert.NotNull(result.Refusal);
        Assert.Contains("does not match the digest", result.Refusal);
        Assert.Empty(result.Dump);
    }

    [Fact]
    public void TheLogIsCapped_AndRotated_AndALogRotatedPastItsStartCannotReplay()
    {
        using var root = new TempProfile();
        var start = new ReplayLogStart(7, new CharacterSpec("Wanderer", Pronouns.They, "people.veth", "body.veth.standard", null),
            "content-hash", "fingerprint");
        using (var writer = new ReplayLogWriter(root.Root, start, player: null, resume: false, maxPartBytes: 2_048, maxParts: 3))
        {
            var commands = new List<LoggedCommand>();
            for (int i = 0; i < 120; i++)
            {
                commands.Add(new LoggedCommand(i, i, new MoveCommand(EntityId.NewId(EntityKind.Character),
                    new MoveIntent(i, 1_000 - i, Gait.Walk, i * 1_000)), null));
                writer.Sync(commands);
            }
            Assert.Equal(commands.Count, writer.Written);
        }

        var parts = ReplayLog.Parts(root.Root);
        Assert.Equal(3, parts.Length);   // the folder keeps the cap it was given
        Assert.True(parts.Length >= 2, "a 2 KB cap over 120 commands must rotate at least once");
        Assert.EndsWith(ReplayLog.FileName, parts[^1], StringComparison.Ordinal);
        foreach (string part in parts)
            Assert.True(new FileInfo(part).Length <= 2_048 + 1_024, $"{Path.GetFileName(part)} is {new FileInfo(part).Length} bytes");

        // The oldest parts were dropped, so the session's first command is no longer in the log: it says so instead of pretending.
        var contents = ReplayLog.Read(root.Root);
        Assert.NotNull(contents.Refusal);
        Assert.Contains("rotated past its start", contents.Refusal);
        using var scratch = new TempProfile();
        var result = Replay.Run(ScratchRoot(scratch), root.Root, 120);
        Assert.NotNull(result.Refusal);
        Assert.Contains("rotated past its start", result.Refusal);
    }

    [Fact]
    public void TheCurrentPart_SaysWhereTheSessionBegan_AndItsLinesCarryTheirTick()
    {
        using var profile = new TempProfile();
        var (_, session) = Played(profile);
        using (session)
        {
            var simulation = session.Simulation!;
            session.Save(SaveSlots.Quick);
            string[] lines = File.ReadAllLines(ReplayLog.PathOf(session.SaveFolder));
            var header = System.Text.Json.Nodes.JsonNode.Parse(lines[0])!;
            Assert.Equal("log", header["kind"]!.GetValue<string>());
            Assert.Equal(0, header["from_sequence"]!.GetValue<long>());
            Assert.Equal(World.WorldSeed.Format(42), header["seed"]!.GetValue<string>());
            Assert.NotNull(header["spec"]);
            Assert.Equal(simulation.CommandLog.Count, lines.Length - 1);
            // Every line names the tick it was dispatched on, in dispatch order.
            var ticks = lines.Skip(1).Select(l => System.Text.Json.Nodes.JsonNode.Parse(l)!["tick"]!.GetValue<long>()).ToList();
            Assert.Equal(ticks.OrderBy(t => t), ticks);
            Assert.All(ticks, t => Assert.True(t >= 0));
        }
    }
}
