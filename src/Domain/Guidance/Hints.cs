// UNNAMED Domain - guidance: the hints, the moments that call for them, and which one a moment shows
// (SYSTEMS_COMPLETENESS_PLAN_20261017.md §2.10 O1, WP10; PJ07: no modal tutorials, each hint once, guidance Full/Light/Off)
// No Godot references - pure C#

using System.Collections.Immutable;
using System.Text.RegularExpressions;

namespace UNNAMED.Domain.Guidance;

/// <summary>
/// One hint (content kind <c>hint</c>): a line of plain guidance shown once, without stopping anything, when its
/// <see cref="Trigger"/> first comes round. <see cref="Category"/> groups hints for Light guidance, which shows only the first of each.
/// <see cref="Order"/> ranks hints that share a trigger: the next unseen one shows the next time it comes round. The text may name an
/// input action as <c>{key:action}</c>; presentation puts the key the player bound to it there.
/// </summary>
public sealed record HintDefinition(string Id, string Category, string Trigger, string Text, int Order)
{
    /// <summary>
    /// The peoples a hint is for (note 3), by definition id - <c>people.kal</c> and so on. Empty (the default) means everyone: a hint with
    /// one is shown only to a character of that people, so a wing hint never tells a Veth about wings it does not have.
    /// </summary>
    public ImmutableArray<string> People { get; init; } = ImmutableArray<string>.Empty;

    /// <summary>Whether this hint is for a character of <paramref name="peopleId"/>: everyone's hints are, and a people's only theirs.</summary>
    public bool IsFor(string? peopleId) => People.IsDefaultOrEmpty || (peopleId is not null && People.Contains(peopleId));
}

/// <summary>How much the game guides: every hint, the first of each category, or none.</summary>
public enum HintDepth { Every, FirstOfEachCategory, None }

/// <summary>
/// The moments a hint can wait for. Presentation names each as it happens (an event the simulation published, a sheet opened). A
/// trigger may name a moment whose system is still to come - hiring, a price moving - and its hints wait until that system names it.
/// </summary>
public static class HintTriggers
{
    public const string NewGame = "new_game";
    public const string PromptShown = "prompt_shown";
    public const string CreatureEngaged = "creature_engaged";
    public const string HitTaken = "hit_taken";
    public const string HealthLow = "health_low";
    public const string PlayerDowned = "player_downed";
    public const string PlayerDied = "player_died";
    public const string CreatureKilled = "creature_killed";
    public const string ItemPickedUp = "item_picked_up";
    public const string PackFull = "pack_full";
    public const string OverLimit = "over_limit";
    public const string QuestStarted = "quest_started";
    public const string LevelUp = "level_up";
    public const string SkillPracticed = "skill_practiced";
    public const string NodeGathered = "node_gathered";
    public const string StationOpened = "station_opened";
    public const string DarkSpaceEntered = "dark_space_entered";
    public const string RawMeatTaken = "raw_meat_taken";
    public const string ForgeOpened = "forge_opened";
    public const string TradeOpened = "trade_opened";
    public const string StoneTouched = "stone_touched";
    public const string FormulaLearned = "formula_learned";
    public const string CompanionRecruited = "companion_recruited";
    public const string CompanionDowned = "companion_downed";
    public const string AwaySheet = "away_sheet";
    public const string HireOffered = "hire_offered";
    public const string PriceChanged = "price_changed";
    public const string BarOpened = "bar_opened";
    public const string StashOpened = "stash_opened";
    public const string TrainerOpened = "trainer_opened";
    public const string ItemWorn = "item_worn";
    public const string ChatOpened = "chat_opened";
    public const string ReputationOpened = "reputation_opened";

    /// <summary>Named by the stuck rule (1I): no progress for a while, so the guide offers its own help.</summary>
    public const string Stuck = "stuck";

    public static readonly ImmutableSortedSet<string> All = ImmutableSortedSet.Create(StringComparer.Ordinal,
        NewGame, PromptShown, CreatureEngaged, HitTaken, HealthLow, PlayerDowned, PlayerDied, CreatureKilled, ItemPickedUp, OverLimit, PackFull,
        QuestStarted, LevelUp, SkillPracticed, NodeGathered, StationOpened, TradeOpened, StoneTouched, FormulaLearned, CompanionRecruited,
        CompanionDowned, AwaySheet, HireOffered, PriceChanged, BarOpened, StashOpened, TrainerOpened, ItemWorn, ChatOpened, ReputationOpened, Stuck,
        DarkSpaceEntered,RawMeatTaken,ForgeOpened);
}

public static partial class HintRules
{
    /// <summary>The longest a hint may be: one line under the HUD, read at a glance.</summary>
    public const int MaxLength = 160;

    /// <summary>
    /// The hint a moment shows: of the hints waiting on <paramref name="trigger"/> and not yet <paramref name="seen"/>, the first by
    /// order then ID - unless guidance is off, or is Light and a hint of that hint's category has been seen already. Null when none.
    /// </summary>
    public static HintDefinition? Pick(IEnumerable<HintDefinition> hints, string trigger, IReadOnlySet<string> seen, HintDepth depth, string? peopleId = null)
    {
        if (depth == HintDepth.None)
            return null;
        var all = hints as IReadOnlyCollection<HintDefinition> ?? hints.ToList();
        var learned = depth == HintDepth.FirstOfEachCategory
            ? all.Where(h => seen.Contains(h.Id)).Select(h => h.Category).ToHashSet(StringComparer.Ordinal)
            : null;
        return all
            .Where(h => h.Trigger == trigger && !seen.Contains(h.Id) && h.IsFor(peopleId) && learned?.Contains(h.Category) != true)
            .OrderBy(h => h.Order).ThenBy(h => h.Id, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    /// <summary>The input actions a hint's text names, in order (<c>{key:interact}</c> names <c>interact</c>).</summary>
    public static IReadOnlyList<string> Actions(string text) => KeyToken().Matches(text).Select(m => m.Groups[1].Value).ToList();

    /// <summary>The text with each <c>{key:action}</c> replaced by what <paramref name="key"/> says that action is bound to.</summary>
    public static string Words(string text, Func<string, string> key) => KeyToken().Replace(text, m => key(m.Groups[1].Value));

    /// <summary>Why a hint's text cannot be shown, or null: empty, too long, or a brace that is not a well-formed key token.</summary>
    public static string? Problem(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "has no text";
        if (text.Length > MaxLength)
            return $"is {text.Length} characters; a hint is at most {MaxLength}";
        string rest = KeyToken().Replace(text, "");
        return rest.Contains('{') || rest.Contains('}') ? "has a brace that is not a {key:action} token" : null;
    }

    [GeneratedRegex(@"\{key:([a-z0-9_]+)\}")]
    private static partial Regex KeyToken();
}
