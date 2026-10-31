// UNNAMED Application - replaying a session's command log into a fresh world (PERSISTENCE.md §5.7, T-28; CMB-126)
// No Godot references - pure C#

using System.Collections.Immutable;
using System.Text.Json.Nodes;
using UNNAMED.Persistence;
using UNNAMED.World.Runtime;

namespace UNNAMED.Application;

/// <summary>
/// What a replay produced: the dump of the world it reached, the commands it dispatched, and why it could not run at all
/// (<see cref="Refusal"/>). A completed replay can be compared with the saved state leaf by leaf
/// (<see cref="StateDump.Compare"/>), which is what makes the replay tuple true rather than aspirational.
/// </summary>
public sealed record ReplayResult(string? Refusal, string Dump, ImmutableArray<LoggedCommand> Replayed, long Tick, string? LogFolder = null)
{
    public bool Complete => Refusal is null;

    /// <summary>The first place the replay and the log disagree about a command's fate, or null when they agree throughout.</summary>
    public string? Disagreement(ReplayLogContents log)
    {
        if (Refusal is not null)
            return Refusal;
        foreach (var (entry, replayed) in log.Entries.Where(e => e.Data is not null && e.Why is null).Zip(Replayed))
        {
            if (entry.Tick != replayed.Tick || entry.Sequence != replayed.Sequence)
                return $"the log dispatched '{entry.Type}' at tick {entry.Tick}/{entry.Sequence}, the replay at {replayed.Tick}/{replayed.Sequence}";
            if (!string.Equals(entry.Rejected, replayed.RejectedReason, StringComparison.Ordinal))
            {
                return replayed.RejectedReason is null
                    ? $"'{entry.Type}' at tick {entry.Tick} was refused live ('{entry.Rejected}') and accepted in the replay"
                    : $"'{entry.Type}' at tick {entry.Tick} was accepted live and refused in the replay ('{replayed.RejectedReason}')";
            }
        }
        return log.Entries.Count(e => e.Data is not null) == Replayed.Length
            ? null
            : $"the log holds {log.Entries.Count(e => e.Data is not null)} commands, the replay dispatched {Replayed.Length}";
    }
}

/// <summary>
/// The replay of a session (§5.7): a save's command log fed into a fresh world made from the log's own replay tuple, dispatched at the
/// ticks it records, and stepped one fixed tick at a time exactly as the game stepped. Nothing of the save is read for it - the log is
/// the whole input, which is what makes it a check on the save rather than a restatement of it.
/// </summary>
public static class Replay
{
    /// <summary>
    /// Replays the newest session in <paramref name="logRoot"/> up to <paramref name="toTick"/>. <paramref name="digest"/>, when given,
    /// is the digest the save recorded (<c>command_log_sha256</c>): a log that does not match it is refused, so a truncated or edited log
    /// is caught before a replay is believed. The session writes to <paramref name="options"/>'s profile root, so give it a scratch one.
    /// </summary>
    public static ReplayResult Run(GameOptions options, string logRoot, long toTick, string? digest = null)
    {
        var log = ReplayLog.Read(logRoot);
        if (!log.Complete)
            return Refused(log.Refusal!, toTick);
        if (digest is not null)
        {
            string? actual = ReplayLog.Digest(logRoot, toTick);
            if (!string.Equals(actual, digest, StringComparison.OrdinalIgnoreCase))
            {
                return Refused(actual is null
                    ? $"the save names a command log digest ({digest}) and the log holds no command before its tick"
                    : $"the command log does not match the digest its save recorded: log {actual}, save {digest}", toTick);
            }
        }

        using var session = GameSession.Boot(options);
        try
        {
            var start = log.Start!;
            if (start.ResumedFrom is { } slot)
                session.Load(slot);
            else if (start.Spec is { } spec)
                session.NewGame(spec, start.Seed);
            else
                return Refused("the log names neither a character to make nor a save to load", toTick);
            return Play(session, log, toTick);
        }
        catch (Exception error) when (error is InvalidOperationException or SaveException or KeyNotFoundException)
        {
            return Refused($"the replay could not run: {error.Message}", toTick);
        }
    }

    /// <summary>
    /// Replays a save: its manifest names the tick the world was captured at and the digest of the log that produced it (§5.7).
    /// </summary>
    public static ReplayResult FromSave(GameOptions options, string logRoot, string slot)
    {
        string manifest = Path.Combine(logRoot, slot, SaveFormat.Manifest);
        if (!File.Exists(manifest))
            return Refused($"the save '{slot}' has no manifest to replay", 0);
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(File.ReadAllBytes(manifest));
        }
        catch (Exception error) when (error is System.Text.Json.JsonException or IOException)
        {
            return Refused($"the save '{slot}' has no manifest to replay: {error.Message}", 0);
        }
        long tick = root?["world_tick"]?.GetValue<long>() ?? 0;
        string? digest = root?["command_log_sha256"]?.GetValue<string>();
        if (digest is null)
        {
            // A save names no digest when nothing had been dispatched before its tick - a world saved the moment it was made. Anything
            // else means the save predates the log this folder holds, or the log was never kept: either way it is not this log's save.
            string? actual = ReplayLog.Digest(logRoot, tick);
            if (actual is not null)
                return Refused($"the save '{slot}' names no command log digest, and the log beside it holds commands from before that tick: replay is unavailable", tick);
        }
        return Run(options, logRoot, tick, digest);
    }

    /// <summary>
    /// Dispatches the log as the game dispatched it: every command of a tick submitted together at that tick's boundary, then the boundary's
    /// drain, then one fixed step - the frame loop exactly. A command the running world already logged itself (the story's first quests at
    /// creation) is not dispatched again.
    /// </summary>
    private static ReplayResult Play(GameSession session, ReplayLogContents log, long toTick)
    {
        var simulation = session.Simulation!;
        int already = simulation.CommandLog.Count;
        var commands = new List<(long Tick, GameCommand Command)>();
        foreach (var entry in log.Entries)
        {
            if (entry.Sequence < already)
                continue;
            var (command, why) = ReplayLog.Resolve(entry, simulation.PlayerId);
            if (command is null)
                return Refused($"the log cannot be replayed: {why}", toTick);
            commands.Add((entry.Tick, command));
        }
        int next = 0;
        while (simulation.WorldTick < toTick)
        {
            long tick = simulation.WorldTick;
            for (; next < commands.Count && commands[next].Tick <= tick; next++)
                session.Submit(commands[next].Command);
            simulation.DrainCommands();
            if (simulation.WorldTick >= toTick)
                break;
            simulation.Step();
        }
        return new ReplayResult(null, StateDump.Render(simulation, replayable: true), simulation.CommandLog.ToImmutableArray(),
            simulation.WorldTick, session.SaveFolder);
    }

    private static ReplayResult Refused(string reason, long tick) => new(reason, "", ImmutableArray<LoggedCommand>.Empty, tick);
}
