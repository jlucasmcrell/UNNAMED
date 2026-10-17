// UNNAMED Domain - NPC adventurers: who they are, what they own, the goal chain they plan by, and the fixed rule tables their days are
// resolved by (WP13; SYSTEMS_COMPLETENESS_PLAN §2.13 "adventurers"; canon NPC_SIMULATION "Adventurer Agent"; DN2, DN4, DN5)
// No Godot references - pure C#, integer only

using System.Collections.Immutable;
using UNNAMED.Domain.Items;
using UNNAMED.Domain.Progression;

namespace UNNAMED.Domain.Social;

/// <summary>What an adventurer's day is for (the canon chain, NPC_SIMULATION:54: need -> money -> bounty -> buy -> travel -> hunt -> collect).</summary>
public static class AdventurerGoals
{
    /// <summary>Nothing worth the risk today, or injured: they keep to the lodge.</summary>
    public const string Rest = "rest";

    /// <summary>Out after a hunting ground's creatures; <see cref="AdventurerPlan.Ref"/> is the spawn's key.</summary>
    public const string Hunt = "hunt";

    /// <summary>A competitive bounty; <see cref="AdventurerPlan.Ref"/> is the quest.</summary>
    public const string Bounty = "bounty";

    /// <summary>On the road to another settlement; <see cref="AdventurerPlan.Ref"/> is where.</summary>
    public const string Travel = "travel";

    public static readonly ImmutableArray<string> All = ImmutableArray.Create(Rest, Hunt, Bounty, Travel);

    /// <summary>Whether the goal takes them out of their settlement for the day.</summary>
    public static bool Out(string goal) => goal is Hunt or Bounty or Travel;
}

/// <summary>An adventurer's condition: about their business, or injured and resting until a day (DN2's outcome tables set it).</summary>
public enum AdventurerCondition { Active, Injured }

/// <summary>A piece an adventurer owns and wears: a real item (§2.13 "a gear tier as real items") with its instance identity and make.</summary>
public sealed record AdventurerGear(EntityId ItemId, string DefId, int Quality);

/// <summary>
/// The day an adventurer planned at a midnight: the goal, what it names, whom they go with (themselves included, sorted), and - when they
/// asked for company on the LFG channel - the tick they asked and the tick they leave. Resolved at the next midnight.
/// </summary>
public sealed record AdventurerPlan(long Day, string Goal, string? Ref)
{
    public ImmutableArray<string> Group { get; init; } = ImmutableArray<string>.Empty;

    /// <summary>When they post their SettingOut on world chat; 0 when they do not ask for company.</summary>
    public long PostTick { get; init; }

    /// <summary>When they leave the settlement for the day's work.</summary>
    public long DepartTick { get; init; }

    public bool AsksCompany => PostTick > 0;

    public static AdventurerPlan Resting(long day) => new(day, AdventurerGoals.Rest, null);
}

/// <summary>
/// One adventurer (saved, WP13): their slot and generation, name and people, home and where they are, their progression on the character's
/// own rules (<see cref="ProgressionEngine"/>), the gear they own, coin, what they carry to sell, how much risk they accept, their condition,
/// today's plan, and - while the character has them in the party - how (a hireling or a temporary ally): the party then has them, and their
/// own life waits.
/// </summary>
public sealed record AdventurerRecord(string Key, string Slot, int Generation, string Name, string Settlement, string At, CharacterProgression Progression,
    long Coin, int RiskPercent)
{
    public string? PeopleId { get; init; }

    public long ArrivedDay { get; init; }

    public ImmutableSortedDictionary<EquipSlot, AdventurerGear> Gear { get; init; } = ImmutableSortedDictionary<EquipSlot, AdventurerGear>.Empty;

    /// <summary>What they carry to sell or deliver, by item definition.</summary>
    public ImmutableSortedDictionary<string, int> Pack { get; init; } = ImmutableSortedDictionary.Create<string, int>(StringComparer.Ordinal);

    public AdventurerCondition Condition { get; init; } = AdventurerCondition.Active;

    /// <summary>The first day they are well again; 0 when active.</summary>
    public long InjuredUntilDay { get; init; }

    public AdventurerPlan Plan { get; init; } = AdventurerPlan.Resting(0);

    /// <summary>"hireling" or "temporary_ally" while in the character's party; null when living their own life.</summary>
    public string? Party { get; init; }

    /// <summary>How many gear pieces they have bought or commissioned, so each new piece has its own identity.</summary>
    public int Acquired { get; init; }

    public long Kills { get; init; }

    public int BountiesClaimed { get; init; }

    public int Level => Progression.Level;

    public bool Equals(AdventurerRecord? other) =>
        other is not null && Key == other.Key && Slot == other.Slot && Generation == other.Generation && Name == other.Name && Settlement == other.Settlement
        && At == other.At && Progression.Digest == other.Progression.Digest && Coin == other.Coin && RiskPercent == other.RiskPercent && PeopleId == other.PeopleId
        && ArrivedDay == other.ArrivedDay && Gear.SequenceEqual(other.Gear) && Pack.SequenceEqual(other.Pack) && Condition == other.Condition
        && InjuredUntilDay == other.InjuredUntilDay && Plan.Day == other.Plan.Day && Plan.Goal == other.Plan.Goal && Plan.Ref == other.Plan.Ref
        && Plan.Group.SequenceEqual(other.Plan.Group) && Plan.PostTick == other.Plan.PostTick && Plan.DepartTick == other.Plan.DepartTick
        && Party == other.Party && Acquired == other.Acquired && Kills == other.Kills && BountiesClaimed == other.BountiesClaimed;

    public override int GetHashCode() => HashCode.Combine(Key, Generation, Coin, Level);
}

/// <summary>An adventurer's slot whose holder died or retired (DN4, DN5): someone new arrives on <see cref="ArrivalDay"/>.</summary>
public sealed record AdventurerVacancy(string Slot, string Settlement, long LeftDay, long ArrivalDay, int NextGeneration);

/// <summary>Every adventurer, keyed with no limit (DN5: a target, not a cap), the slots awaiting an arrival, and the last day settled.</summary>
public sealed record AdventurerState(long Day, ImmutableSortedDictionary<string, AdventurerRecord> Agents, ImmutableSortedDictionary<string, AdventurerVacancy> Vacancies)
{
    public static AdventurerState Empty(long day) => new(day,
        ImmutableSortedDictionary.Create<string, AdventurerRecord>(StringComparer.Ordinal),
        ImmutableSortedDictionary.Create<string, AdventurerVacancy>(StringComparer.Ordinal));

    public void AddTo(CanonicalHasher h)
    {
        h.Add("unnamed.adventurers/v1").Add(Day).Add(Agents.Count);
        foreach (var a in Agents.Values)
        {
            h.Add(a.Key).Add(a.Slot).Add(a.Generation).Add(a.Name).Add(a.PeopleId ?? "-").Add(a.Settlement).Add(a.At).Add(a.Progression.Digest)
                .Add(a.Coin).Add(a.RiskPercent).Add(a.ArrivedDay).Add((int)a.Condition).Add(a.InjuredUntilDay).Add(a.Party ?? "-")
                .Add(a.Acquired).Add(a.Kills).Add(a.BountiesClaimed).Add(a.Gear.Count);
            foreach (var (slot, gear) in a.Gear)
                h.Add((int)slot).Add(gear.ItemId.Value).Add(gear.DefId).Add(gear.Quality);
            h.Add(a.Pack.Count);
            foreach (var (item, count) in a.Pack)
                h.Add(item).Add(count);
            var p = a.Plan;
            h.Add(p.Day).Add(p.Goal).Add(p.Ref ?? "-").Add(p.PostTick).Add(p.DepartTick).Add(p.Group.Length);
            foreach (string member in p.Group)
                h.Add(member);
        }
        h.Add(Vacancies.Count);
        foreach (var v in Vacancies.Values)
            h.Add(v.Slot).Add(v.Settlement).Add(v.LeftDay).Add(v.ArrivalDay).Add(v.NextGeneration);
    }
}

/// <summary>One row of the outcome table (DN2): from a margin of power over danger up to the next row's, the chance in percent of each end.</summary>
public sealed record OutcomeRow(int MarginAtLeast, int DeathPercent, int InjuryPercent, int SuccessPercent);

/// <summary>A settlement adventurers are based at: its market, the merchant who takes commissions, where they lodge, and its hunting grounds.</summary>
public sealed record AdventurerSettlement(string Id, string MarketId, string? SmithMerchantId, long LodgeXMm, long LodgeZMm, int LodgeFacingMdeg,
    ImmutableArray<string> Grounds)
{
    /// <summary>The place they lodge at, whose name words where they wait (a LFG post's anchor); null for none.</summary>
    public string? LocationId { get; init; }
}

/// <summary>A road between two settlements, walked in a day (the Old East Road; no Waystones in the abstract).</summary>
public sealed record AdventurerRoad(string From, string To);

/// <summary>
/// How adventurers live (<c>config.adventurers</c>; DN2, DN5 - provisional, so content rather than code): how many each settlement keeps
/// (a target refilled by arrivals, never a cap), how they start, the outcome table, the gear ladder they work up, what they buy before going
/// out, the prices they trade at, when they ask for company, injury, retirement and arrivals.
/// </summary>
public sealed record AdventurerRules(
    int TargetPerSettlement,
    int StartLevelMin,
    int StartLevelMax,
    long StartCoinMin,
    long StartCoinMax,
    int RiskMin,
    int RiskMax,
    int ArrivalMinDays,
    int ArrivalMaxDays,
    int InjuryMinDays,
    int InjuryMaxDays,
    ImmutableArray<OutcomeRow> Outcomes,
    ImmutableArray<string> GearLadder)
{
    /// <summary>Power a piece of the ladder owned adds.</summary>
    public int PowerPerGear { get; init; } = 1;

    /// <summary>Power each other member of a group adds.</summary>
    public int PowerPerCompanion { get; init; } = 2;

    /// <summary>The worst margin a fully cautious (0%) adventurer accepts is 0; every this many percent of risk accepts one worse.</summary>
    public int RiskPercentPerMargin { get; init; } = 25;

    /// <summary>Of an item's base value: what the town pays them for it, and what they pay, in permille; a commission costs its inputs at this.</summary>
    public int SellPermille { get; init; } = 500;
    public int BuyPermille { get; init; } = 1000;
    public int CommissionPermille { get; init; } = 1500;

    /// <summary>A consumable they buy one of before each day out, while they can, and carry; null for none.</summary>
    public string? Supply { get; init; }

    /// <summary>The share of days out on which they post a SettingOut, in percent; the game hour they leave; how long before it they post.</summary>
    public int LfgPercent { get; init; } = 50;
    public int DepartHour { get; init; } = 8;
    public int LfgLeadMinutes { get; init; } = 30;

    /// <summary>Coin past which, or days since arriving past which, an adventurer retires (§2.13; NPC_SIMULATION:149-159).</summary>
    public long RetireCoin { get; init; } = 600;
    public int RetireDays { get; init; } = 180;

    /// <summary>A hunt's kill is worth this much of the creature's own XP value, in permille.</summary>
    public int HuntXpPermille { get; init; } = 1000;

    /// <summary>Items in the pack the home market will not take before they carry them to another settlement to sell.</summary>
    public int CarryBeforeTravel { get; init; } = 4;

    /// <summary>The skill every day out practises besides the weapon's (fieldcraft); null for none.</summary>
    public string? FieldSkill { get; init; }

    /// <summary>The first broken rule, or null.</summary>
    public string? Problem()
    {
        if (TargetPerSettlement < 0)
            return "target_per_settlement is never negative";
        if (StartLevelMin < 1 || StartLevelMax < StartLevelMin)
            return "starting levels run from at least 1, the most at least the least";
        if (StartCoinMin < 0 || StartCoinMax < StartCoinMin || RiskMin < 0 || RiskMax > 100 || RiskMax < RiskMin)
            return "starting coin is never negative, risk lies in 0..100, and each most is at least its least";
        if (ArrivalMinDays < 1 || ArrivalMaxDays < ArrivalMinDays || InjuryMinDays < 1 || InjuryMaxDays < InjuryMinDays)
            return "arrivals and injuries last at least a day, within [min, max]";
        if (Outcomes.IsEmpty)
            return "the outcome table has at least one row";
        if (Outcomes.Any(r => r.DeathPercent < 0 || r.InjuryPercent < 0 || r.SuccessPercent < 0 || r.DeathPercent + r.InjuryPercent + r.SuccessPercent > 100))
            return "each outcome row's death, injury and success percents are never negative and sum to at most 100";
        for (int i = 1; i < Outcomes.Length; i++)
        {
            if (Outcomes[i].MarginAtLeast <= Outcomes[i - 1].MarginAtLeast)
                return "the outcome rows rise by margin";
        }
        if (GearLadder.IsEmpty)
            return "the gear ladder names at least the first piece";
        if (PowerPerGear < 0 || PowerPerCompanion < 0 || RiskPercentPerMargin < 1)
            return "power per gear and per companion are never negative, and risk per margin is at least 1";
        if (SellPermille < 0 || BuyPermille < SellPermille || CommissionPermille < 0)
            return "they never buy for less than they sell";
        if (LfgPercent is < 0 or > 100 || DepartHour is < 0 or > 23 || LfgLeadMinutes < 20)
            return "lfg_percent is 0..100, the depart hour 0..23, and a post comes at least 20 game minutes ahead";
        if (RetireCoin < 1 || RetireDays < 1 || CarryBeforeTravel < 1)
            return "retirement and carrying thresholds are at least 1";
        return null;
    }

    /// <summary>The row a margin falls in: the last whose margin it reaches, or the first.</summary>
    public OutcomeRow Row(int margin) => Outcomes.LastOrDefault(r => margin >= r.MarginAtLeast) ?? Outcomes[0];

    /// <summary>The worst margin an adventurer with this much risk accepts.</summary>
    public int WorstMargin(int riskPercent) => -(riskPercent / RiskPercentPerMargin);
}

/// <summary>A hunting ground as adventurers know it: a spawner, the settlement it is worked from, its creature, its level and how many.</summary>
public sealed record AdventurerGround(string SpawnKey, string Settlement, string CreatureId, int CreatureLevel, int Members)
{
    /// <summary>How dangerous it is: the creature's level, and one more for each beyond the first that comes at once.</summary>
    public int Danger => CreatureLevel + Math.Max(0, Members - 1);
}

/// <summary>A competitive bounty as adventurers read the board: where it is posted, what it wants, what it pays.</summary>
public sealed record AdventurerBounty(string QuestId, string Title, string Settlement, string? CreatureId, string? ItemId, int Count, long RewardCoin, long RewardXp);

/// <summary>Everything a midnight reads besides the state: the day, its first tick, the draws, the world's content and what it can say now.</summary>
public sealed record AdventurerDay(long Day, long Tick, long TicksPerHour, Func<string, ulong> Sample)
{
    public required ImmutableSortedDictionary<string, AdventurerSettlement> Settlements { get; init; }
    public required ImmutableArray<AdventurerRoad> Roads { get; init; }
    public required ImmutableSortedDictionary<string, AdventurerGround> Grounds { get; init; }
    public required ImmutableArray<AdventurerBounty> Bounties { get; init; }

    /// <summary>Whether a bounty may be claimed now (WP4: competitive, open or held by the character, and posted).</summary>
    public required Func<string, bool> CanClaim { get; init; }

    public required Func<string, ItemDefinition?> Item { get; init; }

    /// <summary>Whether a settlement's market keeps a good.</summary>
    public required Func<string, string, bool> Keeps { get; init; }

    /// <summary>The inputs of a recipe that makes an item, if any: what a commission consumes.</summary>
    public required Func<string, ImmutableArray<(string ItemId, int Count)>?> Recipe { get; init; }

    /// <summary>What one kill at a ground yields, drawn from its loot table with a draw.</summary>
    public required Func<string, ulong, ImmutableArray<(string ItemId, int Count)>> Loot { get; init; }

    /// <summary>Whether a ground's loot can yield an item at all.</summary>
    public required Func<string, string, bool> Yields { get; init; }

    /// <summary>A newcomer's name from their people's Suggest names, passing over the given set; null with none.</summary>
    public required Func<string, int, IReadOnlySet<string>, (string Name, string? People)?> NameFor { get; init; }

    /// <summary>Whether someone can arrive at a settlement now (spawn suppression: never in sight).</summary>
    public required Func<string, bool> CanArrive { get; init; }

    public required ProgressionRules Progression { get; init; }

    /// <summary>A creature's XP value (its <c>xp_value</c>); none when null.</summary>
    public Func<string, long>? KillXp { get; init; }

    /// <summary>A gear piece's identity: derived from its owner and how many they acquired, so a replay or a load makes the same.</summary>
    public required Func<string, int, EntityId> GearId { get; init; }
}

/// <summary>What a midnight did, for the runtime to carry out through its owners' commands and for the away sheet.</summary>
public sealed record AdventurerHappening(string Kind, string Key)
{
    public string? Ref { get; init; }
    public string? Market { get; init; }
    public long Amount { get; init; }
    public long Coin { get; init; }
    public string? Settlement { get; init; }
}

/// <summary>
/// The rules of adventurers' lives, pure (§2.13; DN2). Lives advance one calendar day at a time at midnight: each adventurer's planned day is
/// resolved by the outcome table - never by simulated combat - then each plans the next by the canon goal chain, in key order, so the first
/// to a competitive bounty is the first by key. Arrivals fill the slots whose day has come. Nothing reads the markets', the creatures' or the
/// clock's state: the same days give the same lives however they are split (T-22). What touches other systems comes out as happenings.
/// </summary>
public static class AdventurerEngine
{
    public const string Arrived = "arrived";
    public const string Died = "died";
    public const string Injured = "injured";
    public const string Retired = "retired";
    public const string Claimed = "claimed";
    public const string Lost = "lost";
    public const string Culled = "culled";
    public const string Sold = "sold";
    public const string Bought = "bought";
    public const string Commissioned = "commissioned";
    public const string Consumed = "consumed";
    public const string Travelled = "travelled";
    public const string LevelUp = "level_up";

    /// <summary>A slot's key: its settlement and number.</summary>
    public static string SlotKey(string settlement, int number) => $"{settlement}.slot_{number}";

    /// <summary>An adventurer's key, unique to them: their slot and generation (a fallen hireling's key is never reused).</summary>
    public static string KeyOf(string slot, int generation) => $"npc.adventurer.{slot}_g{generation}";

    /// <summary>
    /// A world's first adventurers: each settlement's slots up to the target, in order, each a newcomer. Or a saved state carried onto what
    /// content defines now: a settlement content no longer has keeps nobody; a slot with no one and no vacancy gets someone.
    /// </summary>
    public static (AdventurerState State, ImmutableArray<AdventurerHappening> Happenings) Reconcile(AdventurerState? saved, AdventurerRules rules, AdventurerDay day)
    {
        var state = saved ?? AdventurerState.Empty(day.Day);
        var agents = state.Agents.Where(a => day.Settlements.ContainsKey(a.Value.Settlement)).ToImmutableSortedDictionary(StringComparer.Ordinal);
        var vacancies = state.Vacancies.Where(v => day.Settlements.ContainsKey(v.Value.Settlement)).ToImmutableSortedDictionary(StringComparer.Ordinal);
        var happenings = ImmutableArray.CreateBuilder<AdventurerHappening>();
        var names = agents.Values.Select(a => a.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var settlement in day.Settlements.Values)
        {
            for (int n = 1; n <= rules.TargetPerSettlement; n++)
            {
                string slot = SlotKey(settlement.Id, n);
                if (agents.Values.Any(a => a.Slot == slot) || vacancies.ContainsKey(slot))
                    continue;
                if (Newcomer(slot, settlement, 0, rules, day, names, saved is null ? 0 : day.Day) is not { } newcomer)
                    continue;
                agents = agents.SetItem(newcomer.Key, newcomer);
                names.Add(newcomer.Name);
                happenings.Add(new AdventurerHappening(Arrived, newcomer.Key) { Settlement = settlement.Id });
            }
        }
        return (state with { Agents = agents, Vacancies = vacancies }, happenings.ToImmutable());
    }

    /// <summary>One midnight: <paramref name="day"/> must be the day after the state's. Every planned day is resolved, arrivals come, and every life plans the new day.</summary>
    public static (AdventurerState State, ImmutableArray<AdventurerHappening> Happenings) Midnight(AdventurerState state, AdventurerRules rules, AdventurerDay day)
    {
        if (day.Day != state.Day + 1)
            throw new ArgumentOutOfRangeException(nameof(day), day.Day, $"Adventurers live one day at a time: they stand at day {state.Day}");
        var happenings = ImmutableArray.CreateBuilder<AdventurerHappening>();
        var agents = state.Agents.ToBuilder();
        var vacancies = state.Vacancies.ToBuilder();
        var claimed = new HashSet<string>(StringComparer.Ordinal);

        // Yesterday's plans come to their end, in key order: the first to a bounty is the first by key.
        foreach (string key in agents.Keys.ToList())
        {
            var a = agents[key];
            if (a.Party is not null)
                continue;
            var resolved = Resolve(a, rules, day, agents, claimed, happenings);
            if (resolved is null)
            {
                agents.Remove(key);
                Vacate(a, rules, day, vacancies);
                continue;
            }
            agents[key] = resolved;
        }

        // Someone well enough off, or long enough at it, retires (§2.13): a role slot in their settlement may take them.
        foreach (string key in agents.Keys.ToList())
        {
            var a = agents[key];
            if (a.Party is not null || a.Condition != AdventurerCondition.Active || (a.Coin < rules.RetireCoin && day.Day - a.ArrivedDay < rules.RetireDays))
                continue;
            agents.Remove(key);
            Vacate(a, rules, day, vacancies);
            happenings.Add(new AdventurerHappening(Retired, key) { Settlement = a.Settlement, Ref = a.Name, Coin = a.Coin });
        }

        // Arrivals (DN4, DN5): a slot whose day has come is taken by someone new, unless they would arrive in sight.
        var names = agents.Values.Select(a => a.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var vacancy in state.Vacancies.Values.Concat(vacancies.Values.Where(v => !state.Vacancies.ContainsKey(v.Slot))).ToList())
        {
            if (!vacancies.ContainsKey(vacancy.Slot) || vacancy.ArrivalDay > day.Day || !day.Settlements.TryGetValue(vacancy.Settlement, out var settlement)
                || !day.CanArrive(vacancy.Settlement))
                continue;
            if (Newcomer(vacancy.Slot, settlement, vacancy.NextGeneration, rules, day, names, day.Day) is not { } newcomer)
                continue;
            vacancies.Remove(vacancy.Slot);
            agents[newcomer.Key] = newcomer;
            names.Add(newcomer.Name);
            happenings.Add(new AdventurerHappening(Arrived, newcomer.Key) { Settlement = settlement.Id });
        }

        // Every life plans the day: in key order, each seeing what those before them took.
        var planned = new Dictionary<string, AdventurerPlan>(StringComparer.Ordinal);
        foreach (string key in agents.Keys.ToList())
        {
            var a = agents[key];
            if (a.Party is not null)
                continue;
            agents[key] = Plan(a, rules, day, claimed, planned, happenings);
        }
        Group(agents, rules, day);
        return (new AdventurerState(day.Day, agents.ToImmutable(), vacancies.ToImmutable()), happenings.ToImmutable());
    }

    /// <summary>An adventurer's power: their level, the ladder pieces they own, and their group.</summary>
    public static int Power(AdventurerRecord a, AdventurerRules rules) =>
        a.Level + rules.PowerPerGear * a.Gear.Values.Count(g => rules.GearLadder.Contains(g.DefId)) + rules.PowerPerCompanion * Math.Max(0, a.Plan.Group.Length - 1);

    /// <summary>The next piece of the ladder they do not own, or null when they own it all.</summary>
    public static string? NextGear(AdventurerRecord a, AdventurerRules rules, Func<string, ItemDefinition?> item) =>
        rules.GearLadder.FirstOrDefault(id => item(id) is { Slot: { } slot } && (!a.Gear.TryGetValue(slot, out var worn) || Rank(rules, worn.DefId) < Rank(rules, id)));

    private static int Rank(AdventurerRules rules, string defId) => rules.GearLadder.IndexOf(defId);

    /// <summary>
    /// Someone new in a slot: their name from their people's names, a level drawn from the starting band reached by the character's own curve,
    /// the ladder's pieces up to their level, coin and risk drawn from the bands. The progression spends its points on might and endurance.
    /// </summary>
    private static AdventurerRecord? Newcomer(string slot, AdventurerSettlement settlement, int generation, AdventurerRules rules, AdventurerDay day,
        IReadOnlySet<string> taken, long arrivedDay)
    {
        string key = KeyOf(slot, generation);
        if (day.NameFor(key, generation, taken) is not { } named)
            return null;
        ulong draw = day.Sample($"newcomer:{key}");
        int level = rules.StartLevelMin + (int)(draw % (ulong)(rules.StartLevelMax - rules.StartLevelMin + 1));
        long coin = rules.StartCoinMin + (long)((draw >> 16) % (ulong)(rules.StartCoinMax - rules.StartCoinMin + 1));
        int risk = rules.RiskMin + (int)((draw >> 32) % (ulong)(rules.RiskMax - rules.RiskMin + 1));
        var progression = ProgressionEngine.Create(day.Progression);
        long xp = 0;
        for (int l = 1; l < level; l++)
            xp += day.Progression.Curve.ToReach(l + 1);
        if (xp > 0)
            progression = ProgressionEngine.Award(progression, new XpAward(XpSource.QuestObjective, xp, day.Tick), day.Progression).Progression;
        var record = new AdventurerRecord(key, slot, generation, named.Name, settlement.Id, settlement.Id, Spend(progression), coin, risk)
        {
            PeopleId = named.People,
            ArrivedDay = arrivedDay,
        };
        // They come with the ladder's first pieces: one, and one more for every two levels.
        int pieces = Math.Min(rules.GearLadder.Length, 1 + (level - 1) / 2);
        for (int i = 0; i < pieces; i++)
        {
            if (day.Item(rules.GearLadder[i]) is { Slot: { } gearSlot } item)
                record = Wear(record, gearSlot, item.Id, day);
        }
        return record;
    }

    private static AdventurerRecord Wear(AdventurerRecord a, EquipSlot slot, string defId, AdventurerDay day) =>
        a with { Gear = a.Gear.SetItem(slot, new AdventurerGear(day.GearId(a.Key, a.Acquired), defId, 0)), Acquired = a.Acquired + 1 };

    /// <summary>Unspent attribute points go on might and endurance, alternately: an adventurer's way.</summary>
    public static CharacterProgression Spend(CharacterProgression progression)
    {
        while (progression.UnspentAttributePoints > 0)
        {
            var attribute = progression.Allocation.GetValueOrDefault(CharacterAttribute.Might) <= progression.Allocation.GetValueOrDefault(CharacterAttribute.Endurance)
                ? CharacterAttribute.Might
                : CharacterAttribute.Endurance;
            progression = ProgressionEngine.Allocate(progression, new AttributeAllocation(attribute, 1));
        }
        return progression;
    }

    private static void Vacate(AdventurerRecord a, AdventurerRules rules, AdventurerDay day, ImmutableSortedDictionary<string, AdventurerVacancy>.Builder vacancies)
    {
        long wait = rules.ArrivalMinDays + (long)(day.Sample($"arrival:{a.Slot}#{a.Generation + 1}") % (ulong)(rules.ArrivalMaxDays - rules.ArrivalMinDays + 1));
        vacancies[a.Slot] = new AdventurerVacancy(a.Slot, a.Settlement, day.Day, day.Day + wait, a.Generation + 1);
    }

    /// <summary>The adventurer leaves the world for good outside a midnight (a hireling who died in the character's service): their slot waits.</summary>
    public static AdventurerState Remove(AdventurerState state, string key, AdventurerRules rules, AdventurerDay day)
    {
        if (!state.Agents.TryGetValue(key, out var a))
            return state;
        var vacancies = state.Vacancies.ToBuilder();
        Vacate(a, rules, day, vacancies);
        return state with { Agents = state.Agents.Remove(key), Vacancies = vacancies.ToImmutable() };
    }

    // ── resolving a day ─────────────────────────────────────────────────────

    /// <summary>A planned day comes to its end (DN2: by the table, never by a fight). Null: they died.</summary>
    private static AdventurerRecord? Resolve(AdventurerRecord a, AdventurerRules rules, AdventurerDay day,
        ImmutableSortedDictionary<string, AdventurerRecord>.Builder agents, HashSet<string> claimed, ImmutableArray<AdventurerHappening>.Builder happenings)
    {
        if (a.Condition == AdventurerCondition.Injured && a.InjuredUntilDay <= day.Day)
            a = a with { Condition = AdventurerCondition.Active, InjuredUntilDay = 0 };
        var plan = a.Plan;
        if (plan.Day != day.Day - 1)
            return a with { Plan = AdventurerPlan.Resting(day.Day) };
        switch (plan.Goal)
        {
            case AdventurerGoals.Travel when plan.Ref is { } to && day.Settlements.ContainsKey(to):
                happenings.Add(new AdventurerHappening(Travelled, a.Key) { Ref = to, Settlement = a.At });
                return a with { At = to };
            case AdventurerGoals.Bounty when plan.Ref is { } questId && day.Bounties.FirstOrDefault(b => b.QuestId == questId) is { } bounty:
                return bounty.CreatureId is null ? Deliver(a, bounty, rules, day, claimed, happenings) : Fight(a, rules, day, Ground(bounty, day), bounty, claimed, happenings);
            case AdventurerGoals.Hunt when plan.Ref is { } ground && day.Grounds.TryGetValue(ground, out var g):
                return Fight(a, rules, day, g, null, claimed, happenings);
            default:
                return a;
        }
    }

    /// <summary>The ground a kill bounty is worked at: the first of its settlement's grounds holding its creature.</summary>
    private static AdventurerGround? Ground(AdventurerBounty bounty, AdventurerDay day) =>
        day.Settlements.TryGetValue(bounty.Settlement, out var s)
            ? s.Grounds.Select(k => day.Grounds.GetValueOrDefault(k)).FirstOrDefault(g => g is not null && g.CreatureId == bounty.CreatureId)
            : null;

    private static AdventurerRecord Deliver(AdventurerRecord a, AdventurerBounty bounty, AdventurerRules rules, AdventurerDay day, HashSet<string> claimed,
        ImmutableArray<AdventurerHappening>.Builder happenings)
    {
        if (bounty.ItemId is not { } item || a.Pack.GetValueOrDefault(item) < bounty.Count)
            return a;
        if (claimed.Contains(bounty.QuestId) || !day.CanClaim(bounty.QuestId))
        {
            happenings.Add(new AdventurerHappening(Lost, a.Key) { Ref = bounty.QuestId });
            return a;
        }
        claimed.Add(bounty.QuestId);
        a = Take(a, item, bounty.Count);
        return Reward(a, bounty, day, happenings);
    }

    private static AdventurerRecord Reward(AdventurerRecord a, AdventurerBounty bounty, AdventurerDay day, ImmutableArray<AdventurerHappening>.Builder happenings)
    {
        happenings.Add(new AdventurerHappening(Claimed, a.Key) { Ref = bounty.QuestId, Coin = bounty.RewardCoin, Settlement = bounty.Settlement });
        a = a with { Coin = a.Coin + bounty.RewardCoin, BountiesClaimed = a.BountiesClaimed + 1 };
        return Gain(a, new XpAward(XpSource.QuestObjective, bounty.RewardXp, day.Tick), day, happenings);
    }

    /// <summary>
    /// A day out against a ground's creatures, resolved by the outcome table on the margin of power over danger: death, injury, success or an
    /// empty day. Success culls creatures (they come back by their spawner's timer), gains the kills' XP on the character's own guards, practises
    /// the weapon's skill and fieldcraft, fills the pack from the loot tables, and - for a kill bounty - claims it if no one has first.
    /// </summary>
    private static AdventurerRecord? Fight(AdventurerRecord a, AdventurerRules rules, AdventurerDay day, AdventurerGround? ground, AdventurerBounty? bounty,
        HashSet<string> claimed, ImmutableArray<AdventurerHappening>.Builder happenings)
    {
        if (ground is null)
            return a;
        int margin = Power(a, rules) - ground.Danger;
        var row = rules.Row(margin);
        int roll = (int)(day.Sample($"outcome:{a.Key}@{day.Day}") % 100);
        if (roll < row.DeathPercent)
        {
            happenings.Add(new AdventurerHappening(Died, a.Key) { Ref = ground.SpawnKey, Settlement = a.Settlement });
            return null;
        }
        if (roll < row.DeathPercent + row.InjuryPercent)
        {
            long days = rules.InjuryMinDays + (long)((day.Sample($"injury:{a.Key}@{day.Day}") % (ulong)(rules.InjuryMaxDays - rules.InjuryMinDays + 1)));
            happenings.Add(new AdventurerHappening(Injured, a.Key) { Ref = ground.SpawnKey, Amount = days });
            // Hurt, they use what they carried to mend.
            if (rules.Supply is { } supply && a.Pack.GetValueOrDefault(supply) > 0)
                a = Take(a, supply, 1);
            return a with { Condition = AdventurerCondition.Injured, InjuredUntilDay = day.Day + days };
        }
        if (roll >= row.DeathPercent + row.InjuryPercent + row.SuccessPercent)
            return a;

        int kills = bounty is { } b ? Math.Min(ground.Members, Math.Max(1, b.Count)) : Math.Min(ground.Members, 1 + a.Level / 3);
        happenings.Add(new AdventurerHappening(Culled, a.Key) { Ref = ground.SpawnKey, Amount = kills });
        var progression = a.Progression;
        string? weaponSkill = a.Gear.TryGetValue(EquipSlot.MainHand, out var weapon) ? day.Item(weapon.DefId)?.Weapon?.SkillId : null;
        for (int k = 0; k < kills; k++)
        {
            foreach (var (item, count) in day.Loot(ground.SpawnKey, day.Sample($"loot:{a.Key}@{day.Day}#{k}")))
                a = Put(a, item, count);
            a = a with { Kills = a.Kills + 1 };
            long xp = (day.KillXp?.Invoke(ground.CreatureId) ?? 0) * rules.HuntXpPermille / 1000;
            a = Gain(a, new XpAward(XpSource.Combat, xp, day.Tick) { Kill = new KillContext(ground.CreatureId, ground.CreatureLevel, ground.SpawnKey) }, day, happenings);
            if (weaponSkill is not null)
                a = a with { Progression = ProgressionEngine.Practice(a.Progression, new SkillPractice(weaponSkill, ground.CreatureLevel, PracticeOutcome.Success, day.Tick), day.Progression).Progression };
        }
        if (rules.FieldSkill is { } field)
            a = a with { Progression = ProgressionEngine.Practice(a.Progression, new SkillPractice(field, ground.Danger, PracticeOutcome.Success, day.Tick), day.Progression).Progression };
        if (bounty is not null)
        {
            if (claimed.Contains(bounty.QuestId) || !day.CanClaim(bounty.QuestId))
                happenings.Add(new AdventurerHappening(Lost, a.Key) { Ref = bounty.QuestId });
            else
            {
                claimed.Add(bounty.QuestId);
                a = Reward(a, bounty, day, happenings);
            }
        }
        return a;
    }

    /// <summary>Level XP on the character's rules: a quest's, or a kill's at the creature's value through the anti-farm guards.</summary>
    private static AdventurerRecord Gain(AdventurerRecord a, XpAward award, AdventurerDay day, ImmutableArray<AdventurerHappening>.Builder happenings)
    {
        if (award.Amount <= 0)
            return a;
        int before = a.Level;
        var result = ProgressionEngine.Award(a.Progression, award, day.Progression);
        a = a with { Progression = Spend(result.Progression) };
        if (a.Level > before)
            happenings.Add(new AdventurerHappening(LevelUp, a.Key) { Amount = a.Level });
        return a;
    }

    // ── planning a day ──────────────────────────────────────────────────────

    /// <summary>
    /// The canon goal chain (NPC_SIMULATION:54), deterministic: injured, rest; otherwise deliver what a bounty here wants if they carry it,
    /// sell what this market keeps, buy the next ladder piece when they can (or commission it from the smith), buy the day's supply - then a
    /// bounty here within their risk, or a hunt for what a bounty wants, or the best ground within their risk; with nothing here, or a pack the
    /// market will not take, the road to another settlement. A day out may ask for company.
    /// </summary>
    private static AdventurerRecord Plan(AdventurerRecord a, AdventurerRules rules, AdventurerDay day, HashSet<string> claimed,
        Dictionary<string, AdventurerPlan> planned, ImmutableArray<AdventurerHappening>.Builder happenings)
    {
        if (a.Condition == AdventurerCondition.Injured || !day.Settlements.TryGetValue(a.At, out var town))
            return a with { Plan = AdventurerPlan.Resting(day.Day) };
        var here = day.Bounties.Where(b => b.Settlement == town.Id && !claimed.Contains(b.QuestId) && !planned.Values.Any(p => p.Ref == b.QuestId)
                                           && day.CanClaim(b.QuestId)).ToList();

        // A bounty they can deliver now.
        if (here.FirstOrDefault(b => b.ItemId is { } item && b.CreatureId is null && a.Pack.GetValueOrDefault(item) >= b.Count) is { } deliverable)
            return Schedule(a, new AdventurerPlan(day.Day, AdventurerGoals.Bounty, deliverable.QuestId), rules, day, planned, ask: false);

        // Sell what the market here keeps, and what no market keeps (the town takes it, its stock untouched), keeping what a bounty here
        // wants; what another settlement's market keeps is carried there.
        var wanted = here.Where(b => b.ItemId is not null).Select(b => b.ItemId!).ToHashSet(StringComparer.Ordinal);
        foreach (var (item, count) in a.Pack.ToList())
        {
            bool keptHere = day.Keeps(town.Id, item);
            if (wanted.Contains(item) || item == rules.Supply || (!keptHere && KeptElsewhere(day, town.Id, item)) || day.Item(item) is not { } definition)
                continue;
            long coin = definition.ValueBase * rules.SellPermille * count / 1000;
            a = Take(a, item, count) with { Coin = a.Coin + coin };
            happenings.Add(new AdventurerHappening(Sold, a.Key) { Ref = item, Market = keptHere ? town.MarketId : null, Amount = count, Coin = coin });
        }

        // Buy, or commission, the next piece of the ladder.
        if (NextGear(a, rules, day.Item) is { } next && day.Item(next) is { Slot: { } slot } piece)
        {
            if (day.Keeps(town.Id, next))
            {
                long price = piece.ValueBase * rules.BuyPermille / 1000;
                if (a.Coin >= price)
                {
                    a = Wear(a with { Coin = a.Coin - price }, slot, next, day);
                    happenings.Add(new AdventurerHappening(Bought, a.Key) { Ref = next, Market = town.MarketId, Amount = 1, Coin = price });
                }
            }
            else if (town.SmithMerchantId is { } smith && day.Recipe(next) is { } inputs && inputs.All(i => day.Keeps(town.Id, i.ItemId)))
            {
                long price = inputs.Sum(i => (day.Item(i.ItemId)?.ValueBase ?? 0) * i.Count) * rules.CommissionPermille / 1000;
                if (a.Coin >= price)
                {
                    a = Wear(a with { Coin = a.Coin - price }, slot, next, day);
                    happenings.Add(new AdventurerHappening(Commissioned, a.Key) { Ref = next, Market = town.MarketId, Coin = price, Settlement = smith });
                    foreach (var (item, count) in inputs)
                        happenings.Add(new AdventurerHappening(Consumed, a.Key) { Ref = item, Market = town.MarketId, Amount = count });
                }
            }
        }

        // What they could do here, within their risk.
        int worst = rules.WorstMargin(a.RiskPercent);
        int power = Power(a, rules);
        AdventurerPlan? choice = null;
        foreach (var bounty in here.Where(b => b.CreatureId is not null).OrderByDescending(b => b.RewardCoin).ThenBy(b => b.QuestId, StringComparer.Ordinal))
        {
            if (Ground(bounty, day) is { } g && power - g.Danger >= worst)
            {
                choice = new AdventurerPlan(day.Day, AdventurerGoals.Bounty, bounty.QuestId);
                break;
            }
        }
        var grounds = town.Grounds.Select(k => day.Grounds.GetValueOrDefault(k)).OfType<AdventurerGround>().Where(g => power - g.Danger >= worst).ToList();
        if (choice is null && here.Where(b => b.CreatureId is null && b.ItemId is not null).OrderByDescending(b => b.RewardCoin).ThenBy(b => b.QuestId, StringComparer.Ordinal)
                .SelectMany(b => grounds.Where(g => day.Yields(g.SpawnKey, b.ItemId!))).FirstOrDefault() is { } collect)
            choice = new AdventurerPlan(day.Day, AdventurerGoals.Hunt, collect.SpawnKey);
        if (choice is null && grounds.OrderByDescending(g => g.Danger).ThenBy(g => g.SpawnKey, StringComparer.Ordinal).FirstOrDefault() is { } best)
            choice = new AdventurerPlan(day.Day, AdventurerGoals.Hunt, best.SpawnKey);

        // Nothing here, or carrying what nobody here buys: the road.
        int unsold = a.Pack.Where(kv => kv.Key != rules.Supply && !wanted.Contains(kv.Key)).Sum(kv => kv.Value);
        if (choice is null || unsold >= rules.CarryBeforeTravel)
        {
            var road = day.Roads.Where(r => r.From == town.Id || r.To == town.Id).OrderBy(r => r.From == town.Id ? r.To : r.From, StringComparer.Ordinal).FirstOrDefault();
            if (road is not null)
                choice = new AdventurerPlan(day.Day, AdventurerGoals.Travel, road.From == town.Id ? road.To : road.From);
        }
        if (choice is null)
            return a with { Plan = AdventurerPlan.Resting(day.Day) };

        // The day's supply, bought before going out.
        if (choice.Goal != AdventurerGoals.Travel && rules.Supply is { } supply && a.Pack.GetValueOrDefault(supply) == 0 && day.Keeps(town.Id, supply)
            && day.Item(supply) is { } consumable && a.Coin >= consumable.ValueBase * rules.BuyPermille / 1000)
        {
            long price = consumable.ValueBase * rules.BuyPermille / 1000;
            a = Put(a with { Coin = a.Coin - price }, supply, 1);
            happenings.Add(new AdventurerHappening(Bought, a.Key) { Ref = supply, Market = town.MarketId, Amount = 1, Coin = price });
        }
        return Schedule(a, choice, rules, day, planned, ask: choice.Goal != AdventurerGoals.Travel);
    }

    private static AdventurerRecord Schedule(AdventurerRecord a, AdventurerPlan plan, AdventurerRules rules, AdventurerDay day,
        Dictionary<string, AdventurerPlan> planned, bool ask)
    {
        long depart = day.Tick + rules.DepartHour * day.TicksPerHour;
        plan = plan with { DepartTick = depart, Group = ImmutableArray.Create(a.Key) };
        if (ask && (int)(day.Sample($"lfg:{a.Key}@{day.Day}") % 100) < rules.LfgPercent)
            plan = plan with { PostTick = Math.Max(day.Tick + 1, depart - rules.LfgLeadMinutes * day.TicksPerHour / 60) };
        if (plan.Goal == AdventurerGoals.Bounty)
            planned[a.Key] = plan;
        return a with { Plan = plan };
    }

    /// <summary>Temporary group-ups among adventurers (NPC_SIMULATION parties): those out after the same ground from the same settlement go together.</summary>
    private static void Group(ImmutableSortedDictionary<string, AdventurerRecord>.Builder agents, AdventurerRules rules, AdventurerDay day)
    {
        foreach (var group in agents.Values.Where(a => a.Party is null && a.Plan.Day == day.Day && a.Plan.Goal == AdventurerGoals.Hunt)
                     .GroupBy(a => (a.At, a.Plan.Ref)).Where(g => g.Count() > 1).ToList())
        {
            var members = group.Select(a => a.Key).OrderBy(k => k, StringComparer.Ordinal).ToImmutableArray();
            foreach (var a in group)
                agents[a.Key] = a with { Plan = a.Plan with { Group = members } };
        }
    }

    private static bool KeptElsewhere(AdventurerDay day, string here, string item) =>
        day.Settlements.Keys.Any(s => s != here && day.Keeps(s, item));

    private static AdventurerRecord Put(AdventurerRecord a, string item, int count) =>
        count <= 0 ? a : a with { Pack = a.Pack.SetItem(item, a.Pack.GetValueOrDefault(item) + count) };

    private static AdventurerRecord Take(AdventurerRecord a, string item, int count)
    {
        int left = a.Pack.GetValueOrDefault(item) - count;
        return a with { Pack = left > 0 ? a.Pack.SetItem(item, left) : a.Pack.Remove(item) };
    }
}
