// UNNAMED Presentation - what the encounter sounds like: a stinger on the start, on every phase, and on the end (CMB-42; EPIC 530, 534 #5 #7)
// Pure: no Godot, so the rule is tested directly

using System.Collections.Immutable;

namespace UNNAMED.Presentation.Audio;

/// <summary>The three moments a boss fight is marked by (CMB-42).</summary>
public enum EncounterMoment
{
    /// <summary>The fight is joined: the stinger that says something has decided to kill the character.</summary>
    Start,

    /// <summary>The encounter crossed into another phase (CMB-38's machine): the deeper the phase, the harder the cue.</summary>
    Phase,

    /// <summary>It is over, one way or the other.</summary>
    End,
}

/// <summary>
/// The rule for encounter stingers (CMB-42 audio half): which owned sound marks each moment. A cue is a list of ids in the order to try, because
/// the set is delivered in families and a family may not be in the workspace; the first one the bank actually has is played, and nothing is
/// ever invented. Every id here comes from the owned set - the discovery stinger the score already plays, and the per-archetype creature
/// families the Phase-1 audio contract names - and the score's music bus ducks while any of them sounds.
/// </summary>
public static class EncounterStinger
{
    /// <summary>The owned stinger recording the score already uses for a discovery, reused as the fallback for every encounter moment.</summary>
    public const string OwnedStinger = MusicChoice.Discovery;

    /// <summary>How far the music bus is pulled down under a stinger (the score's own duck).</summary>
    public const float DuckGain = 0.35f;

    /// <summary>Seconds the music takes to duck, and to come back: fast in, unhurried out.</summary>
    public const float DuckAttackSeconds = 0.08f;

    public const float DuckReleaseSeconds = 0.9f;

    /// <summary>
    /// The ids to try for one moment, best first. Start and end are marked with the owned stinger, since the set carries no encounter stinger of
    /// its own; a phase change is marked by the archetype's own voice - alert for the first step, attack for later ones - with the stinger behind
    /// it for the last phase, which is the one that changes the fight.
    /// </summary>
    public static ImmutableArray<string> Cues(EncounterMoment moment, string archetype, int phaseIndex, int phaseCount)
    {
        string voice = Creature(archetype);
        bool lastPhase = phaseIndex > 0 && phaseIndex >= phaseCount - 1;

        return moment switch
        {
            EncounterMoment.Start => ImmutableArray.Create(OwnedStinger, $"sfx.creature.{voice}.alert.01", $"sfx.creature.{voice}.alert.02"),
            EncounterMoment.Phase => lastPhase
                ? ImmutableArray.Create(OwnedStinger, $"sfx.creature.{voice}.attack.01", $"sfx.creature.{voice}.attack.02")
                : ImmutableArray.Create($"sfx.creature.{voice}.alert.01", OwnedStinger, $"sfx.creature.{voice}.attack.01"),
            _ => ImmutableArray.Create(OwnedStinger, $"sfx.creature.{voice}.death.01", $"sfx.creature.{voice}.death.02"),
        };
    }

    /// <summary>
    /// The audio contract's own name for a creature family (PHASE1_AUDIO_EVENT_CONTRACT.md §5). A family the contract does not name falls back to
    /// the animated armour's set, which every construct shares - a wrong voice is worse than a shared one, so nothing is guessed at from a name.
    /// </summary>
    public static string Creature(string archetype) => archetype switch
    {
        "construct" => "animated_armour",
        "beast" => "bristleback_boar",
        "undead" => "bone_walker_husk",
        "spider" => "cave_hunting_spider",
        "hound" => "ash_ember_hound",
        "animated_armour" or "bristleback_boar" or "bone_walker_husk" or "cave_hunting_spider" or "ash_ember_hound" => archetype,
        _ => "animated_armour",
    };

    /// <summary>
    /// Whether a moment is worth a cue at all: a fight with no phases still starts and ends, but only a real crossing of a phase is marked by the
    /// phase cue, so a definition with one phase does not fire it on every tick.
    /// </summary>
    public static bool Wants(EncounterMoment moment, int phaseIndex, int phaseCount) =>
        moment != EncounterMoment.Phase || (phaseIndex > 0 && phaseIndex < Math.Max(1, phaseCount));

    /// <summary>
    /// The music bus's gain <paramref name="elapsed"/> seconds after a stinger began, ducking to <see cref="DuckGain"/> over the attack and coming
    /// back over the release once the stinger's own length has run. Deterministic, so the duck is a rule and not a taste.
    /// </summary>
    public static float DuckGainAt(float elapsed, double stingerSeconds)
    {
        float t = Math.Max(0f, elapsed);
        if (t < DuckAttackSeconds)
            return 1f + (DuckGain - 1f) * (t / DuckAttackSeconds);
        float held = (float)Math.Max(0, stingerSeconds);
        float releaseStart = DuckAttackSeconds + held;
        if (t <= releaseStart)
            return DuckGain;
        float back = Math.Min(1f, (t - releaseStart) / DuckReleaseSeconds);
        return DuckGain + (1f - DuckGain) * back;
    }
}
