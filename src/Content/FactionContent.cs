// UNNAMED Content - factions and reputation (M7 design §5.13-§5.14)
// Validates and builds config.factions and the faction definitions; lint FAC001.

using System.Collections.Immutable;
using UNNAMED.Domain.Factions;
using UNNAMED.Domain.Quests;
using UNNAMED.Domain.Social;
using UNNAMED.Domain.Spatial;
using UNNAMED.World.Runtime;
using static UNNAMED.Content.CombatContent;

namespace UNNAMED.Content;

/// <summary>
/// The factions (M7): <c>config.factions</c> - the one ladder, the point range and the act log's capacity - and each faction's name,
/// seat, reactions and relations. FAC001 holds content to the rules of §5.8 and §5.14: a reaction only to a single-instance act of a
/// built kind; members standing at their seat, never a companion; a standing condition only on the speaker's own faction; a report only
/// to a faction that reacts; a billet gate only on the trader's own faction. Standing moves only through acts a faction learns of.
/// </summary>
public static class FactionContent
{
    public const string ConfigId = "config.factions";
    public const string Code = "FAC001";

    /// <summary>The largest standing change one reaction may carry.</summary>
    public const int MaxDelta = 250;

    /// <summary>The smallest act log FAC001 allows.</summary>
    public const int MinLogCapacity = 16;

    private static readonly (string Field, string Why)[] RefusedFactionFields =
    {
        ("members", "membership is the NPC's faction_ref"),
        ("player_start_reputation", "one ladder, config.factions; every standing starts at 0"),
        ("reputation_tiers", "one ladder, config.factions; every standing starts at 0"),
        ("laws", "crime is not built; law belongs to places, not to a faction"),
        ("enemy_of", "relations are attitude words; hostility is not a faction field"),
        ("attitude_default", "relations are attitude words; hostility is not a faction field"),
        ("territory", "not built in M7"),
        ("services_gated", "not built in M7"),
        ("joinable", "not built in M7"),
        ("join_requirements", "not built in M7"),
    };

    /// <summary>The subject field each built act kind names.</summary>
    private static readonly ImmutableSortedDictionary<string, (string Field, string Kind)> Subjects =
        new Dictionary<string, (string, string)>
        {
            [ActKinds.CreatureKilled] = ("creature_ref", "creature"),
            [ActKinds.SwitchSet] = ("flag_ref", "world_flag"),
        }.ToImmutableSortedDictionary(StringComparer.Ordinal);

    public static IReadOnlyList<ValidationError> Validate(ContentLoader loader)
    {
        var errors = new List<ValidationError>();
        var factionEnvelopes = loader.GetByKind("faction");
        string? configFile = loader.Definitions.GetValueOrDefault(ConfigId)?.SourceFile;
        if (factionEnvelopes.Count == 0 && configFile is null)
            return errors;

        if (configFile is null)
            errors.Add(Error("a faction exists, so config.factions is required", null));
        else
        {
            try
            {
                ParseConfig(loader);
            }
            catch (Exception e) when (e is FormatException or InvalidCastException or KeyNotFoundException or ArgumentException)
            {
                errors.Add(Error($"{ConfigId}: {e.Message}", configFile));
            }
        }

        var factions = new SortedDictionary<string, FactionDefinition>(StringComparer.Ordinal);
        foreach (var envelope in factionEnvelopes.Values.OrderBy(f => f.Id, StringComparer.Ordinal))
        {
            try
            {
                factions[envelope.Id] = ParseFaction(loader, envelope);
            }
            catch (Exception e) when (e is FormatException or InvalidCastException or KeyNotFoundException or ArgumentException)
            {
                errors.Add(Error($"{envelope.Id}: {e.Message}", envelope.SourceFile));
            }
        }

        var world = new World(loader);
        foreach (var faction in factions.Values)
        {
            string? file = factionEnvelopes[faction.Id].SourceFile;
            foreach (var relation in faction.Relations)
            {
                if (relation.FactionId == faction.Id || !factionEnvelopes.ContainsKey(relation.FactionId))
                    errors.Add(Error($"{faction.Id}: a relation names another existing faction, not '{relation.FactionId}'", file));
            }
            foreach (string problem in SingleInstanceProblems(world, faction))
                errors.Add(Error($"{faction.Id}: {problem}", file));
        }

        foreach (string problem in MemberProblems(loader, world, factions))
            errors.Add(Error(problem, null));
        foreach (string problem in DialogueAndMerchantProblems(world, factions))
            errors.Add(Error(problem, null));
        return errors;
    }

    /// <summary>What FAC001 reads of the rest of the content, built once per validation: layouts, spawns, NPCs, dialogues, quests, merchants.</summary>
    private sealed class World
    {
        public World(ContentLoader loader)
        {
            var regions = loader.GetByKind("region").Keys.OrderBy(r => r, StringComparer.Ordinal).ToList();
            Layouts = regions.Select(r => Layout(loader, r)).OfType<RegionLayout>().ToList();
            Spawns = regions.SelectMany(r => Spawns(loader, r)).ToList();
            Npcs = Built(() => SocialContent.BuildNpcs(loader));
            Dialogues = Built(() => SocialContent.BuildDialogues(loader));
            Quests = Built(() => QuestContent.BuildQuests(loader));
            Merchants = Built(() => ItemContent.BuildMerchants(loader));
        }

        public List<RegionLayout> Layouts { get; }
        public List<SpawnSite> Spawns { get; }
        public ImmutableSortedDictionary<string, NpcDefinition>? Npcs { get; }
        public ImmutableSortedDictionary<string, DialogueDefinition>? Dialogues { get; }
        public ImmutableSortedDictionary<string, QuestDefinition>? Quests { get; }
        public ImmutableSortedDictionary<string, UNNAMED.Domain.Items.Merchant>? Merchants { get; }

        /// <summary>A build, or null when it fails: the owning lint (SOC, QST, ITM) reports that.</summary>
        private static T? Built<T>(Func<T> build) where T : class
        {
            try
            {
                return build();
            }
            catch (Exception e) when (e is FormatException or InvalidCastException or KeyNotFoundException or ArgumentException)
            {
                return null;
            }
        }
    }

    /// <summary>The factions as the running world uses them; <see cref="FactionSetup.Empty"/> when the pack has none.</summary>
    public static FactionSetup Build(ContentLoader loader)
    {
        if (!loader.Definitions.ContainsKey(ConfigId))
            return FactionSetup.Empty;
        var (ladder, capacity) = ParseConfig(loader);
        var factions = loader.GetByKind("faction").Values.Select(f => ParseFaction(loader, f))
            .ToImmutableSortedDictionary(f => f.Id, f => f, StringComparer.Ordinal);
        return new FactionSetup(factions, ladder, capacity);
    }

    // ── config.factions ─────────────────────────────────────────────────────────

    private static (StandingLadder Ladder, int Capacity) ParseConfig(ContentLoader loader)
    {
        var map = Config(loader, ConfigId);
        if (map.ContainsKey("witness"))
            throw new FormatException("witness is not built: the witnessed channel is M9");
        var rows = Rows(map, "ladder", ConfigId).ToList();
        if (rows.Count != StandingLadder.Keys.Length)
            throw new FormatException($"the ladder holds exactly PROGRESSION §10's {StandingLadder.Keys.Length} tiers, not {rows.Count}");
        var tiers = rows.Select((row, i) =>
        {
            var (key, level) = StandingLadder.Keys[i];
            if (Text(row, "tier") != key || Int(row, "level") != level)
                throw new FormatException($"ladder[{i}] is {key} at level {level} (PROGRESSION §10), not {Text(row, "tier")} at {Int(row, "level")}");
            return new StandingTier(key, level, Int(row, "min"));
        }).ToImmutableArray();
        for (int i = 1; i < tiers.Length; i++)
        {
            if (tiers[i].MinPoints >= tiers[i - 1].MinPoints)
                throw new FormatException($"the ladder's min is strictly descending: {tiers[i].Key} {tiers[i].MinPoints} after {tiers[i - 1].Key} {tiers[i - 1].MinPoints}");
        }
        StandingTier Tier(string key) => tiers.Single(t => t.Key == key);
        if (!(Tier("neutral").MinPoints <= 0 && 0 < Tier("accepted").MinPoints))
            throw new FormatException("a standing of 0 is neutral: neutral.min <= 0 < accepted.min");
        var points = Map(map, "points");
        int min = Int(points, "min"), max = Int(points, "max"), floor = Int(points, "ordinary_floor");
        if (min != FactionLedger.MinPoints || Tier("anathema").MinPoints != FactionLedger.MinPoints)
            throw new FormatException($"points.min and anathema.min are {FactionLedger.MinPoints}");
        if (max != FactionLedger.MaxPoints || Tier("exalted").MinPoints > max)
            throw new FormatException($"points.max is {FactionLedger.MaxPoints}, and exalted.min is at most points.max");
        if (floor != FactionLedger.OrdinaryFloor || Tier("outcast").MinPoints != FactionLedger.OrdinaryFloor)
            throw new FormatException($"points.ordinary_floor and outcast.min are {FactionLedger.OrdinaryFloor}: anathema needs explicit acts");
        int capacity = Int(Map(map, "acts"), "log_capacity");
        if (capacity < MinLogCapacity)
            throw new FormatException($"acts.log_capacity is at least {MinLogCapacity}, not {capacity}");
        return (new StandingLadder(tiers, min, max, floor), capacity);
    }

    // ── a faction ───────────────────────────────────────────────────────────────

    private static FactionDefinition ParseFaction(ContentLoader loader, ContentEnvelope envelope)
    {
        var map = Read(envelope.YamlSource);
        foreach (var (field, why) in RefusedFactionFields)
        {
            if (map.ContainsKey(field))
                throw new FormatException($"{field} is refused: {why}");
        }
        string seat = Text(map, "seat_location_ref");
        if (!loader.GetByKind("location").ContainsKey(seat))
            throw new FormatException($"seat_location_ref '{seat}' is not a location");
        var reactions = (map.ContainsKey("reactions") ? Rows(map, "reactions", envelope.Id) : Enumerable.Empty<Dictionary<object, object>>())
            .Select((row, i) => Reaction(loader, row, $"reactions[{i}]")).ToImmutableArray();
        var twice = reactions.GroupBy(r => (r.Kind, r.Subject)).FirstOrDefault(g => g.Count() > 1);
        if (twice is not null)
            throw new FormatException($"two reactions to {twice.Key.Kind} {twice.Key.Subject}: at most one row per act and subject");
        var relations = (map.ContainsKey("relations") ? Rows(map, "relations", envelope.Id) : Enumerable.Empty<Dictionary<object, object>>())
            .Select((row, i) =>
            {
                string attitude = Text(row, "attitude");
                if (!Attitudes.All.Contains(attitude))
                    throw new FormatException($"relations[{i}]: attitude '{attitude}' is not one of {string.Join(", ", Attitudes.All)}");
                return new Relation(Text(row, "faction_ref"), attitude);
            }).ToImmutableArray();
        if (relations.GroupBy(r => r.FactionId, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1) is { } again)
            throw new FormatException($"two relations to {again.Key}: at most one row per faction");
        return new FactionDefinition(envelope.Id, Text(map, "name"), seat, reactions, relations);
    }

    /// <summary>
    /// An act and its subject as content names them - <c>act</c> and exactly one subject field, the one its kind takes (<c>creature_ref</c>
    /// for <c>creature_killed</c>, <c>flag_ref</c> for <c>switch_set</c>) - in a reaction, an <c>act_done</c> condition or a <c>report_act</c>.
    /// </summary>
    internal static (string Kind, string Subject) ActAndSubject(ContentLoader loader, Dictionary<object, object> row, string at)
    {
        string act = Text(row, "act");
        if (ActKinds.NotBuilt.TryGetValue(act, out string? why))
            throw new FormatException($"{at}: act '{act}' is not built: {why}");
        if (!Subjects.TryGetValue(act, out var subject))
            throw new FormatException($"{at}: act '{act}' is not an act kind ({string.Join(", ", ActKinds.Built)})");
        var named = row.Keys.OfType<string>().Where(k => k.EndsWith("_ref", StringComparison.Ordinal)).ToList();
        if (named.Count != 1 || named[0] != subject.Field)
            throw new FormatException($"{at}: a {act} act names exactly one subject, its {subject.Field}");
        string id = Text(row, subject.Field);
        if (!loader.GetByKind(subject.Kind).ContainsKey(id))
            throw new FormatException($"{at}: {subject.Field} '{id}' is not a {subject.Kind}");
        return (act, id);
    }

    private static Reaction Reaction(ContentLoader loader, Dictionary<object, object> row, string at)
    {
        var (act, id) = ActAndSubject(loader, row, at);
        if (row.ContainsKey("anathema"))
            throw new FormatException($"{at}: anathema is refused: it needs explicit acts, and none exist yet");
        int delta = Int(row, "delta");
        if (delta == 0 || Math.Abs(delta) > MaxDelta)
            throw new FormatException($"{at}: delta is non-zero and at most {MaxDelta} either way, not {delta}");
        return new Reaction(act, id, delta);
    }

    /// <summary>
    /// FAC-R5: a reaction only to a single-instance act. A creature whose every spawner places it once (<c>respawn: none</c>), and a
    /// flag that some switch sets and no dialogue or quest reward writes: a repeatable subject would need a repeat rule (M9).
    /// </summary>
    private static IEnumerable<string> SingleInstanceProblems(World world, FactionDefinition faction)
    {
        foreach (var reaction in faction.Reactions)
        {
            if (reaction.Kind == ActKinds.CreatureKilled)
            {
                var respawning = world.Spawns
                    .Where(s => s.RespawnTicks > 0 && s.Members.Any(m => m.CreatureId == reaction.Subject))
                    .Select(s => s.Key).ToList();
                if (respawning.Count > 0)
                    yield return $"a reaction to killing {reaction.Subject} needs every spawner to place it once (respawn: none); " +
                        $"{string.Join(", ", respawning)} brings it back";
            }
            else if (reaction.Kind == ActKinds.SwitchSet)
            {
                bool switched = world.Layouts.Any(l => l.Switches.Any(s => s.FlagId == reaction.Subject));
                if (!switched)
                    yield return $"a reaction to setting {reaction.Subject} needs a switch that sets it";
                if (FlagWriters(world, reaction.Subject).FirstOrDefault() is { } writer)
                    yield return $"a reaction to setting {reaction.Subject} needs a flag only its switch sets; {writer} writes it too";
            }
        }
    }

    private static IEnumerable<string> FlagWriters(World world, string flagId)
    {
        foreach (var dialogue in world.Dialogues?.Values ?? Enumerable.Empty<DialogueDefinition>())
        {
            if (dialogue.Nodes.Values.SelectMany(n => n.Choices).SelectMany(c => c.Consequences).OfType<SetWorldFlagConsequence>().Any(f => f.FlagId == flagId))
                yield return dialogue.Id;
        }
        foreach (var quest in world.Quests?.Values ?? Enumerable.Empty<QuestDefinition>())
        {
            if (quest.Rewards.OfType<WorldFlagReward>().Any(r => r.FlagId == flagId))
                yield return quest.Id;
        }
    }

    // ── members ─────────────────────────────────────────────────────────────────

    /// <summary>FAC-M1 and FAC-M2: a member stands inside their faction's seat, in every region that places them, and never travels with the character.</summary>
    private static IEnumerable<string> MemberProblems(ContentLoader loader, World world, IReadOnlyDictionary<string, FactionDefinition> factions)
    {
        var layouts = world.Layouts;
        foreach (var npc in loader.GetByKind("npc").Values.OrderBy(n => n.Id, StringComparer.Ordinal))
        {
            var map = Read(npc.YamlSource);
            if (map.GetValueOrDefault("faction_ref") is not string factionId)
                continue;
            if (!factions.TryGetValue(factionId, out var faction))
            {
                yield return $"{npc.Id}: faction_ref '{factionId}' is not a faction";
                continue;
            }
            if (map.ContainsKey("companion"))
            {
                yield return $"{npc.Id}: a companion belongs to no faction (a report told on the road would reach the seat at once)";
                continue;
            }
            var placed = layouts.Where(l => l.Npcs.Any(s => s.NpcId == npc.Id)).ToList();
            if (placed.Count == 0)
                yield return $"{npc.Id}: a member of {factionId} is placed in no region";
            foreach (var layout in placed)
            {
                var site = layout.Npcs.First(s => s.NpcId == npc.Id);
                var seat = layout.Locations.FirstOrDefault(l => l.Id == faction.SeatLocationId);
                if (seat is null)
                {
                    yield return $"{npc.Id}: {faction.Id}'s seat {faction.SeatLocationId} is not in {layout.Id}, where {npc.Id} stands";
                    continue;
                }
                long dx = site.XMm - seat.XMm, dz = site.ZMm - seat.ZMm;
                if (dx * dx + dz * dz > seat.DiscoveryRadiusMm * seat.DiscoveryRadiusMm)
                    yield return $"{npc.Id}: stands {Math.Sqrt(dx * dx + dz * dz) / 1000.0:0.0} m from {faction.SeatLocationId}, outside its " +
                        $"{seat.DiscoveryRadiusMm / 1000.0:0.#} m: a member stands inside the seat";
            }
        }
    }

    // ── dialogue and merchants (R1, R3, R4, R5) ─────────────────────────────────

    private static IEnumerable<string> DialogueAndMerchantProblems(World world, IReadOnlyDictionary<string, FactionDefinition> factions)
    {
        if (world.Npcs is not { } npcs || world.Dialogues is not { } dialogues)
            yield break;   // SOC reports it

        foreach (var dialogue in dialogues.Values)
        {
            var memberships = dialogue.Participants.Select(p => npcs.GetValueOrDefault(p)?.FactionId).Distinct().ToList();
            string? shared = memberships.Count == 1 ? memberships[0] : null;
            foreach (var (node, choice) in dialogue.Nodes.Values.SelectMany(n => n.Choices.Select(c => (n.Id, c))))
            {
                string at = $"{dialogue.Id} node {node} reply {choice.Id}";
                foreach (var standing in choice.Conditions.OfType<ReputationCondition>())
                {
                    if (standing.FactionId != shared)
                        yield return $"{at}: a reputation condition names only the faction every participant belongs to, not {standing.FactionId}";
                }
                foreach (var done in choice.Conditions.OfType<ActDoneCondition>())
                {
                    if (!choice.Consequences.OfType<ReportActConsequence>().Any(r => r.Kind == done.Kind && r.Subject == done.Subject))
                        yield return $"{at}: act_done {done.Kind} {done.Subject} stands only beside a report_act of the same act";
                }
                foreach (var report in choice.Consequences.OfType<ReportActConsequence>())
                {
                    if (shared is null || !factions.TryGetValue(shared, out var told))
                        yield return $"{at}: a report goes to one faction, which every participant belongs to";
                    else if (!told.Reactions.Any(r => r.Kind == report.Kind && r.Subject == report.Subject))
                        yield return $"{at}: {shared} has no reaction to {report.Kind} {report.Subject}, so a report to it cannot be written";
                }
            }
        }

        if (world.Merchants is not { } merchants)
            yield break;   // ITM reports it
        foreach (var merchant in merchants.Values)
        {
            var traders = npcs.Values.Where(n => n.MerchantId == merchant.Id).ToList();
            foreach (var row in merchant.Stock.Where(s => s.Requires is not null))
            {
                string gate = row.Requires!.FactionId;
                if (!factions.ContainsKey(gate))
                    yield return $"{merchant.Id}: {row.ItemId}'s requires names '{gate}', which is not a faction";
                else if (traders.Count == 0 || traders.Any(t => t.FactionId != gate))
                    yield return $"{merchant.Id}: {row.ItemId} is gated by {gate}, the faction of every NPC who trades from it";
                if (merchant.Stock.Count(s => s.ItemId == row.ItemId) != 1)
                    yield return $"{merchant.Id}: a gated item appears in exactly one stock row, and {row.ItemId} does not";
            }
        }
    }

    // ── shared ──────────────────────────────────────────────────────────────────

    private static ImmutableArray<SpawnSite> Spawns(ContentLoader loader, string regionId)
    {
        try
        {
            return CombatContent.BuildSpawns(loader, regionId);
        }
        catch (Exception e) when (e is FormatException or InvalidCastException or KeyNotFoundException or ArgumentException)
        {
            return ImmutableArray<SpawnSite>.Empty;   // CMB reports it
        }
    }

    private static RegionLayout? Layout(ContentLoader loader, string regionId)
    {
        try
        {
            return WorldContent.BuildLayout(loader, regionId);
        }
        catch (InvalidOperationException)
        {
            return null;   // the WLD codes report it
        }
    }

    private static ValidationError Error(string message, string? file) => new()
    {
        SeverityLevel = ValidationError.Severity.Error,
        Code = Code,
        Message = message,
        FilePath = file ?? string.Empty,
    };
}
