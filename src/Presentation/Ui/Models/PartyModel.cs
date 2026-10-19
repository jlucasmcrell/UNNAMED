// UNNAMED Presentation - the Party screen's cards: who travels with the character, on what terms, under what orders, and how they regard
// them - in words, never an approval number (WP15a, WP15b; SYSTEMS_COMPLETENESS_PLAN §2.15)
// Godot-free: Presentation.Tests links this file and tests the mapping on its own (D-11)

using System.Collections.Immutable;
using UNNAMED.Domain.Companions;
using UNNAMED.Domain.Items;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation.Ui;

/// <summary>A piece a party member wears, as their card lists it: the slot, the piece, and whether it is only loaned.</summary>
public sealed record PartyGearRow(EquipSlot Slot, string SlotWord, string Item, bool Loaned)
{
    public string Whose => Loaned ? "Loaned" : "Theirs";
}

/// <summary>
/// One party member's card: who and what they are to the character (companion, a hireling in a role, an ally for a trip), how they are,
/// the orders they are under, a hireling's contract and wage in words, how they regard the character and whether they go along with what
/// they are asked - words, never numbers - their level, and what they wear.
/// </summary>
public sealed record PartyCard(
    string NpcId,
    string Name,
    PartyMembership Membership,
    string Kind,
    string Doing,
    string Standing,
    bool Downed,
    int Health,
    int MaxHealth,
    CompanionOrder Order,
    CompanionStance Stance,
    PartyTactics Tactics,
    string? Contract,
    string? Wage,
    string? Owed,
    string Regard,
    string Goes,
    WordTone GoesTone,
    string? Level,
    int UnspentPoints,
    ImmutableArray<PartyGearRow> Gear,
    string? Dismiss)
{
    /// <summary>Dismissing them is possible now (a member who is down cannot be sent away).</summary>
    public bool CanDismiss => Dismiss is null;

    /// <summary>A hireling: the contract and wage rows show, and dismissal ends the contract.</summary>
    public bool Hired => Membership == PartyMembership.Hireling;
}

public static class PartyModel
{
    /// <summary>
    /// A member's card from their view, their sheet (null before anything on it changed) and the party's rules. <paramref name="item"/>
    /// names a piece at its quality.
    /// </summary>
    public static PartyCard Card(CompanionView member, PartySheetView? sheet, PartyRules? rules, Func<string, int, string> item)
    {
        bool downed = member.Condition != CompanionCondition.Up;
        var contract = member.Contract;
        var disposition = sheet?.Disposition ?? Disposition.Comply;
        return new PartyCard(
            member.NpcId,
            member.Name,
            member.Membership,
            Kind(member.Membership, contract?.Role),
            Doing(member.Doing),
            member.Standing,
            downed,
            member.Health,
            member.MaxHealth,
            member.Order,
            member.Stance,
            member.Tactics,
            contract is null ? null : ContractWords(contract),
            contract is null ? null : $"{contract.DailyWage} coin a day, paid at midnight; {contract.Purse} paid so far",
            contract is null ? null : OwedWords(contract, rules),
            sheet?.Regard ?? $"{member.Name} has no strong view of you yet.",
            Goes(disposition, member.Name),
            disposition switch { Disposition.Comply => WordTone.Good, Disposition.Hesitate => WordTone.Warning, _ => WordTone.Refusal },
            sheet is null ? null : LevelWords(sheet),
            sheet?.UnspentPoints ?? 0,
            sheet is null ? ImmutableArray<PartyGearRow>.Empty
                : sheet.Gear.OrderBy(g => g.Slot).Select(g => new PartyGearRow(g.Slot, SlotWord(g.Slot), item(g.DefId, g.Quality), g.Loaned)).ToImmutableArray(),
            downed ? $"{member.Name} is down: help them up first" : null);
    }

    /// <summary>What they are to the character: "Companion", "Hireling · guard", "Ally for this trip".</summary>
    public static string Kind(PartyMembership membership, HirelingRole? role) => membership switch
    {
        PartyMembership.Hireling => role is { } r ? $"Hireling · {RoleWord(r).ToLowerInvariant()}" : "Hireling",
        PartyMembership.TemporaryAlly => "Ally for this trip",
        _ => "Companion",
    };

    public static string RoleWord(HirelingRole role) => role switch
    {
        HirelingRole.Guard => "Guard",
        HirelingRole.Porter => "Porter",
        HirelingRole.Gatherer => "Gatherer",
        HirelingRole.Crafter => "Crafter",
        _ => role.ToString(),
    };

    /// <summary>What they are doing, capitalised for a card: the view's own word (following, waiting, fighting, downed).</summary>
    public static string Doing(string doing) => doing.Length == 0 ? doing : char.ToUpperInvariant(doing[0]) + doing[1..];

    /// <summary>A contract in words: "4 of 7 days left" and where they work, if they were set to.</summary>
    public static string ContractWords(HirelingContract contract)
    {
        string days = contract.DaysLeft == 1 ? "the last day" : $"{contract.DaysLeft} of {contract.Days} days left";
        string work = contract.WorkSite is null ? "" : contract.RecipeId is null ? $"; works {Names.Place(contract.WorkSite)}" : $"; makes {Names.Words(contract.RecipeId)} at {Names.Place(contract.WorkSite)}";
        return $"{RoleWord(contract.Role)}, {days}{work}";
    }

    /// <summary>
    /// Wages owed, in words, when any are: how many days unpaid, and how long they wait before going home (the rules' grace). Null when
    /// they are paid up.
    /// </summary>
    public static string? OwedWords(HirelingContract contract, PartyRules? rules)
    {
        if (contract.UnpaidDays <= 0)
            return null;
        string unpaid = contract.UnpaidDays == 1 ? "Unpaid for a day" : $"Unpaid for {contract.UnpaidDays} days";
        int grace = rules?.GraceDays ?? 0;
        int left = grace - contract.UnpaidDays;
        return left > 0 ? $"{unpaid}: they leave if not paid within {(left == 1 ? "a day" : $"{left} days")}. Carry the coin and they are paid at midnight."
            : $"{unpaid}: they leave at midnight unless you carry their wage.";
    }

    /// <summary>
    /// Whether they go along with what they are asked (WP15b's disposition), in a sentence: nothing for a member who complies is said
    /// beyond that they do; a member who hesitates says so and does it; one who refuses will not take orders; one about to leave is leaving.
    /// </summary>
    public static string Goes(Disposition disposition, string name) => disposition switch
    {
        Disposition.Hesitate => $"{name} hesitates at what you ask, but does it.",
        Disposition.Refuse => $"{name} refuses what you ask.",
        Disposition.Leave => $"{name} means to leave you.",
        _ => $"{name} does as you ask.",
    };

    /// <summary>"Level 3 · 40 of 300 XP to 4", with a point to spend said, or "Level 5" alone where the curve gives no next.</summary>
    public static string LevelWords(PartySheetView sheet) =>
        (sheet.ToNextLevelXp > 0 ? $"Level {sheet.Level} · {sheet.LevelProgressXp} of {sheet.ToNextLevelXp} XP to {sheet.Level + 1}" : $"Level {sheet.Level}") +
        (sheet.UnspentPoints switch { 0 => "", 1 => " · a point to spend", var n => $" · {n} points to spend" });

    public static string SlotWord(EquipSlot slot) => EquipSlots.Key(slot).Replace('_', ' ');

    public static string StanceWord(CompanionStance stance) => stance switch
    {
        CompanionStance.Aggressive => "Attack what threatens us",
        CompanionStance.Defensive => "Fight back only",
        CompanionStance.Passive => "Keep out of fights",
        _ => stance.ToString(),
    };

    /// <summary>The standing tactics as toggles: what each says, and whether it is on.</summary>
    public static ImmutableArray<(string Key, string Words, bool On)> Tactics(PartyTactics tactics) => ImmutableArray.Create(
        ("leash", "Stay close to me", tactics.KeepToLeash),
        ("focus", "Focus my target", tactics.FocusTarget),
        ("injured", "Protect the injured", tactics.ProtectInjured),
        ("healing", "Save healing for when it is needed", tactics.HoldHealing));

    /// <summary>The tactics with one of them switched.</summary>
    public static PartyTactics Toggled(PartyTactics tactics, string key) => key switch
    {
        "leash" => tactics with { KeepToLeash = !tactics.KeepToLeash },
        "focus" => tactics with { FocusTarget = !tactics.FocusTarget },
        "injured" => tactics with { ProtectInjured = !tactics.ProtectInjured },
        "healing" => tactics with { HoldHealing = !tactics.HoldHealing },
        _ => tactics,
    };

    /// <summary>The party's head line: how many travel with the character out of how many may.</summary>
    public static string Count(int members, PartyRules? rules) =>
        rules is null ? (members == 1 ? "1 with you" : $"{members} with you") : $"{members} of {rules.MaxMembers} with you";

    /// <summary>A companion recovering at home, in words: "Tavar is recovering at home: back in about 2 days".</summary>
    public static string Recovering(RecoveryView recovery, long now, long ticksPerDay)
    {
        long days = ticksPerDay <= 0 ? 0 : (recovery.UntilTick - now + ticksPerDay - 1) / ticksPerDay;
        return days <= 0 ? $"{recovery.Name} is well again, and at home."
            : $"{recovery.Name} is recovering at home: back in about {(days == 1 ? "a day" : $"{days} days")}.";
    }

    /// <summary>A group the character set out with (an LFG trip, WP16): who leads, what for and where.</summary>
    public static string Group(AllyGroupView group, string leaderName) =>
        $"With {leaderName}: {ChatModel.PurposeKind(group.Purpose)}, at {group.Destination}. No wage: a share of what you find.";
}
