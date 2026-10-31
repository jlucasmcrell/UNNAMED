// UNNAMED Domain - named adventurer rivals who race the character for competitive bounties (R12, CMP-122, CMP-33; SOCIAL 33)
// No Godot references - pure C#

using System.Collections.Immutable;

namespace UNNAMED.Domain.Social;

/// <summary>
/// One named adventurer who works the same board the character does (R12). They are ordinary NPCs with a <c>rival:</c> block: the
/// profile is what makes them race, and every field is definition data, so nothing about who they are is saved.
/// </summary>
public sealed record RivalProfile(
    string NpcId,
    string People,
    string Call,
    int PacePermille,
    int SkillPermille,
    ImmutableArray<string> Bounties,
    RivalTaunts Taunts)
{
    /// <summary>Whether this rival works a given posting: their own list, or none listed and so any competitive bounty.</summary>
    public bool Wants(string questId) => Bounties.IsEmpty || Bounties.Contains(questId);
}

/// <summary>
/// What the rival says, in their own people's voice: on first meeting, when they take a bounty from under the character, and when
/// the character beats them to it. Each list is drawn from in order, so the same rival never says the same thing twice running.
/// </summary>
public sealed record RivalTaunts(ImmutableArray<string> Met, ImmutableArray<string> Beaten, ImmutableArray<string> Bested)
{
    public static RivalTaunts Empty { get; } = new(ImmutableArray<string>.Empty, ImmutableArray<string>.Empty, ImmutableArray<string>.Empty);
}

/// <summary>How the races have gone: what the rival remembers about the character, and what they will say next because of it.</summary>
public sealed record RivalMemory(int Wins, int Losses, long LastTick, string? LastQuestId)
{
    public static RivalMemory None { get; } = new(0, 0, 0, null);

    /// <summary>How many times the two have been after the same posting and one of them finished it.</summary>
    public int Contests => Wins + Losses;

    /// <summary>
    /// What the rival thinks of the character now: a plain tally rather than a scale. Nobody who has been beaten twice by the same
    /// person keeps treating them as a stranger.
    /// </summary>
    public RivalRegard Regard => Losses >= 2 ? RivalRegard.Respectful
        : Wins >= 2 ? RivalRegard.Contemptuous
        : Contests == 0 ? RivalRegard.Stranger
        : RivalRegard.Wary;
}

public enum RivalRegard { Stranger, Wary, Respectful, Contemptuous }

/// <summary>Which of the three taunt lists a moment draws from.</summary>
public enum RivalMoment { Met, Beaten, Bested }

public static class RivalRules
{
    /// <summary>The shortest a race can be, in ticks: nobody hears of a posting and finishes it inside ten seconds.</summary>
    public const int ShortestRaceTicks = 200;

    /// <summary>
    /// The tick a rival finishes a posting, worked out from the posting's own work and the rival's pace: <paramref name="workTicks"/> is how
    /// long the work takes a capable adventurer, and a rival at 800 per mille is a fifth slower than that. The small offset keeps two
    /// rivals on the same posting from ever finishing on the same tick, which is decided by <paramref name="seed"/> rather than by luck.
    /// </summary>
    public static long AttemptTick(long postedTick, long workTicks, RivalProfile rival, ulong seed)
    {
        long pace = Math.Clamp(rival.PacePermille, 250, 2000);
        long ticks = (long)Math.Ceiling(workTicks * 1000.0 / pace);
        long offset = (long)(seed % 11);
        return postedTick + Math.Max(ShortestRaceTicks, ticks) + offset;
    }

    /// <summary>Who finished the posting: the character, or the rival.</summary>
    public static RivalOutcome Decide(long attemptTick, long? playerFinishedTick) =>
        playerFinishedTick is { } done && done <= attemptTick ? RivalOutcome.PlayerFirst : RivalOutcome.RivalFirst;

    /// <summary>The memory after a posting: a win for the rival, or a loss, and the posting that did it.</summary>
    public static RivalMemory After(RivalMemory memory, RivalOutcome outcome, string questId, long tick) => outcome switch
    {
        RivalOutcome.RivalFirst => memory with { Wins = memory.Wins + 1, LastTick = tick, LastQuestId = questId },
        RivalOutcome.PlayerFirst => memory with { Losses = memory.Losses + 1, LastTick = tick, LastQuestId = questId },
        _ => memory,
    };

    /// <summary>
    /// The line the rival says at a moment, drawn from that moment's list and rotated by how many contests there have been, so the same
    /// rival does not repeat themselves while the player is listening. Null when the list is empty: a rival with nothing to say stays quiet.
    /// </summary>
    public static string? TauntFor(RivalProfile rival, RivalMoment moment, RivalMemory memory)
    {
        var list = moment switch
        {
            RivalMoment.Met => rival.Taunts.Met,
            RivalMoment.Beaten => rival.Taunts.Beaten,
            _ => rival.Taunts.Bested,
        };
        return list.IsEmpty ? null : list[Math.Abs(memory.Contests) % list.Length];
    }
}

/// <summary>Who finished a contested posting first.</summary>
public enum RivalOutcome { RivalFirst, PlayerFirst }
