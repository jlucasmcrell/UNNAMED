// UNNAMED Domain - standing a duty construct down without a fight (CMB-43; EPIC 537 #12, 22; ENEMY_LIST 15)
// No Godot references - pure C#

using System.Collections.Immutable;

namespace UNNAMED.Domain.Combat;

/// <summary>
/// The three ways canon lets a doorkeeper be answered (CMB-43): saying the right word back to it, proving a right by showing what the
/// court gave you, or following the ritual in the order the court keeps. A construct that answers none of them must be fought.
/// </summary>
public enum StandDownRoute
{
    /// <summary>Talk: say the thing the machine is waiting to hear.</summary>
    Word,

    /// <summary>A right: show the token the court issued to whoever may pass.</summary>
    Token,

    /// <summary>Ritual: do the steps in the order the court keeps them.</summary>
    Rite,
}

/// <summary>One step of a court's ritual: what it is called and what a player sees asked. Order is the whole point of a ritual.</summary>
public sealed record DutyRite(string Id, string Asked);

/// <summary>What the construct says at the three moments that matter, in its own voice: called to stand and answer, accepting, refusing.</summary>
public sealed record DutyVoice(string Called, string Accepted, string Refused)
{
    public static DutyVoice Silent { get; } = new("It stands.", "It stands down.", "It does not answer.");
}

/// <summary>
/// A duty construct's terms (<c>duty:</c> in content): the routes it answers, the ritual in order, the token that is a right, the words
/// that are a password, and its voice. A construct with no terms is kill-only, which is every construct that content does not give them.
/// </summary>
public sealed record DutyTerms(
    ImmutableArray<StandDownRoute> Routes,
    ImmutableArray<DutyRite> Rites,
    string? TokenId,
    ImmutableArray<string> Words,
    DutyVoice Voice)
{
    public static DutyTerms None { get; } = new(ImmutableArray<StandDownRoute>.Empty, ImmutableArray<DutyRite>.Empty, null,
        ImmutableArray<string>.Empty, DutyVoice.Silent);

    /// <summary>Whether this construct can be stood down at all: a kill-only construct is never talked to.</summary>
    public bool AnswersAny => !Routes.IsEmpty;
}

/// <summary>What the character does to stand a construct down: one route, its argument, and how far through the ritual they are.</summary>
public sealed record StandDownAttempt(StandDownRoute Route, string? Argument, int RiteStep, bool CarriesToken);

/// <summary>The verdict, and on a refusal the line the construct answers with (which is the reason, in its own voice).</summary>
public sealed record StandDownResult(StandDownVerdict Verdict, string Said)
{
    public bool Accepted => Verdict == StandDownVerdict.Accepted;
}

public enum StandDownVerdict { Refused, Accepted }

public static class StandDownRules
{
    /// <summary>
    /// Whether the construct stands down. The order of the checks is the order canon argues them: a construct that answers nothing is
    /// never offered the choice; then the route must be one it keeps; then the argument must be the right one. A ritual step is answered
    /// only at its own place in the order, because a ritual is a sequence and not a set.
    /// </summary>
    public static StandDownResult Answers(DutyTerms terms, StandDownAttempt attempt)
    {
        if (!terms.AnswersAny)
            return new StandDownResult(StandDownVerdict.Refused, terms.Voice.Refused);

        if (!terms.Routes.Contains(attempt.Route))
            return new StandDownResult(StandDownVerdict.Refused, terms.Voice.Refused);

        switch (attempt.Route)
        {
            case StandDownRoute.Word:
                return string.IsNullOrWhiteSpace(attempt.Argument) || !terms.Words.Contains(attempt.Argument)
                    ? new StandDownResult(StandDownVerdict.Refused, terms.Voice.Refused)
                    : new StandDownResult(StandDownVerdict.Accepted, terms.Voice.Accepted);

            case StandDownRoute.Token:
                return terms.TokenId is null || !attempt.CarriesToken
                    ? new StandDownResult(StandDownVerdict.Refused, terms.Voice.Refused)
                    : new StandDownResult(StandDownVerdict.Accepted, terms.Voice.Accepted);

            default:
                // The ritual is answered step by step, in order, and the last step is the one that stands it down.
                int step = Math.Clamp(attempt.RiteStep, 0, terms.Rites.Length);
                bool inOrder = attempt.Argument is null || terms.Rites[Math.Min(step, Math.Max(0, terms.Rites.Length - 1))].Id == attempt.Argument;
                if (!inOrder)
                    return new StandDownResult(StandDownVerdict.Refused, terms.Voice.Refused);
                bool complete = step >= terms.Rites.Length;
                return complete
                    ? new StandDownResult(StandDownVerdict.Accepted, terms.Voice.Accepted)
                    : new StandDownResult(StandDownVerdict.Refused, terms.Rites[step].Asked);
        }
    }

    /// <summary>
    /// What standing down is worth against killing it (owner Q1: both legal, different rewards). A stood-down construct leaves its loot
    /// where it is - the court keeps it - and pays in knowledge instead; the numbers are the rules, not the content's.
    /// </summary>
    public static (int Xp, bool Loot, bool Knowledge) Reward(StandDownVerdict verdict, int xpValue) => verdict switch
    {
        StandDownVerdict.Accepted => (xpValue / 2, false, true),
        _ => (xpValue, true, false),
    };
}
