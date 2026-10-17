// UNNAMED Content - NPC adventurers from content: the settlements they work from, their hunting grounds, the outcome table, the gear ladder
// and the body they are hired with (WP13)
// No Godot references - pure C#

using System.Collections.Immutable;
using System.Globalization;
using UNNAMED.Domain.Companions;
using UNNAMED.Domain.Social;
using UNNAMED.Domain.Spatial;
using UNNAMED.World.Runtime;
using static UNNAMED.Content.CombatContent;

namespace UNNAMED.Content;

/// <summary>
/// Builds <see cref="AdventurerSetup"/> from <c>config.adventurers</c> and lints it (ADV codes): ADV001 a malformed config; ADV002 a
/// settlement, ground, ladder piece or road naming what content does not define (a market, a spawner, an equippable item, a settlement), or a
/// market whose merchant is not the smith named; ADV003 a lodge a body cannot stand at or reach from the region's spawn. There is no limit on
/// how many adventurers a settlement keeps (DN5: the target is a target). The config is optional: a pack without it has no adventurers.
/// </summary>
public static class AdventurerContent
{
    public const string ConfigId = "config.adventurers";

    public static IReadOnlyList<ValidationError> Validate(ContentLoader loader)
    {
        var errors = new List<ValidationError>();
        if (!loader.Definitions.TryGetValue(ConfigId, out var config))
            return errors;
        AdventurerSetup setup;
        try
        {
            setup = Build(loader)!;
        }
        catch (Exception e) when (e is FormatException or InvalidCastException or KeyNotFoundException or ArgumentException)
        {
            errors.Add(NavigationContent.Error("ADV001", $"{ConfigId}: {e.Message}", config.SourceFile));
            return errors;
        }
        void Problem(string message) => errors.Add(NavigationContent.Error("ADV002", $"{ConfigId}: {message}", config.SourceFile));
        foreach (string piece in setup.Rules.GearLadder)
        {
            if (!loader.Definitions.TryGetValue(piece, out var item) || !item.Id.StartsWith("item.", StringComparison.Ordinal)
                || Read(item.YamlSource) is var map && !map.ContainsKey("slot") && !piece.StartsWith("item.weapon.", StringComparison.Ordinal))
                Problem($"gear_ladder's {piece} is not an item worn in a slot");
        }
        var markets = loader.GetByKind("market");
        foreach (var settlement in setup.Settlements.Values)
        {
            if (!markets.TryGetValue(settlement.MarketId, out var market))
                Problem($"{settlement.Id}'s market {settlement.MarketId} is not defined");
            else if (settlement.SmithMerchantId is { } smith && !(market.YamlSource ?? "").Contains(smith, StringComparison.Ordinal))
                Problem($"{settlement.Id}'s smith {smith} does not sell from {settlement.MarketId}");
            foreach (string ground in settlement.Grounds)
            {
                if (!loader.Definitions.TryGetValue(ground, out var spawn) || !spawn.Id.StartsWith("spawn.", StringComparison.Ordinal))
                    Problem($"{settlement.Id}'s ground {ground} is not a spawner");
            }
        }
        foreach (var road in setup.Roads.Where(r => !setup.Settlements.ContainsKey(r.From) || !setup.Settlements.ContainsKey(r.To) || r.From == r.To))
            Problem($"the road {road.From} - {road.To} joins two different settlements it names");
        if (errors.Count > 0)
            return errors;

        // Each lodge, in each region whose spawners the settlement hunts, can be stood at and reached.
        NavConfig nav;
        try
        {
            nav = NavigationContent.Build(loader);
        }
        catch (FormatException)
        {
            return errors;   // the NAV codes report it
        }
        foreach (var settlement in setup.Settlements.Values)
        {
            var regions = settlement.Grounds.Select(g => Read(loader.Definitions[g].YamlSource).GetValueOrDefault("region_ref") as string).OfType<string>()
                .Distinct(StringComparer.Ordinal).OrderBy(r => r, StringComparer.Ordinal);
            foreach (string regionId in regions)
            {
                RegionLayout layout;
                try
                {
                    layout = WorldContent.BuildLayout(loader, regionId);
                }
                catch (Exception e) when (e is InvalidOperationException or FormatException or KeyNotFoundException or ArgumentException)
                {
                    continue;   // the region's own codes report it
                }
                var grid = NavigationLayout.Build(layout, nav);
                var reach = NavReach.FromSpawn(grid, nav, new NavPoint(layout.Spawn.XMm, layout.Spawn.ZMm));
                var at = new NavPoint(settlement.LodgeXMm, settlement.LodgeZMm);
                if (!NavSearch.Standable(grid, new NavAgent(nav.PersonClass, true), _ => true, at, nav.Classes[nav.PersonClass].RadiusMm)
                    || !reach.Near(at, NpcLifeContent.AnchorReachMm))
                    errors.Add(NavigationContent.Error("ADV003",
                        $"{ConfigId}: {settlement.Id}'s lodge ({settlement.LodgeXMm / 1000.0:0.###}, {settlement.LodgeZMm / 1000.0:0.###}) cannot be stood at, or reached from {regionId}'s spawn",
                        config.SourceFile));
            }
        }
        return errors;
    }

    /// <summary><c>config.adventurers</c>, or null for a pack without one.</summary>
    public static AdventurerSetup? Build(ContentLoader loader)
    {
        if (!loader.Definitions.ContainsKey(ConfigId))
            return null;
        var map = Config(loader, ConfigId);
        (long, long) Band(string key)
        {
            var band = List(map, key);
            if (band.Count != 2)
                throw new FormatException($"{key} is [least, most]");
            return (IntOf(band, 0, key), IntOf(band, 1, key));
        }
        var (levelMin, levelMax) = Band("starting_level");
        var (coinMin, coinMax) = Band("starting_coin");
        var (riskMin, riskMax) = Band("risk_percent");
        var (arrivalMin, arrivalMax) = Band("arrival_days");
        var (injuryMin, injuryMax) = Band("injury_days");
        var outcomes = Rows(map, "outcomes", ConfigId)
            .Select(r => new OutcomeRow(Int(r, "margin_at_least"), Int(r, "death"), Int(r, "injury"), Int(r, "success"))).ToImmutableArray();
        var ladder = List(map, "gear_ladder").Select(p => p as string ?? throw new FormatException("gear_ladder lists item IDs")).ToImmutableArray();
        var retire = Map(map, "retire");
        var lfg = Map(map, "lfg");
        var rules = new AdventurerRules(Int(map, "target_per_settlement"), (int)levelMin, (int)levelMax, coinMin, coinMax, (int)riskMin, (int)riskMax,
            (int)arrivalMin, (int)arrivalMax, (int)injuryMin, (int)injuryMax, outcomes, ladder)
        {
            PowerPerGear = Int(map, "power_per_gear"),
            PowerPerCompanion = Int(map, "power_per_companion"),
            SellPermille = Int(map, "sell_permille"),
            BuyPermille = Int(map, "buy_permille"),
            CommissionPermille = Int(map, "commission_permille"),
            Supply = map.GetValueOrDefault("supply_item_ref") as string,
            FieldSkill = map.GetValueOrDefault("field_skill_ref") as string,
            HuntXpPermille = Int(map, "hunt_xp_permille"),
            CarryBeforeTravel = Int(map, "carry_before_travel"),
            RetireCoin = Int(retire, "coin"),
            RetireDays = Int(retire, "days"),
            LfgPercent = Int(lfg, "percent"),
            DepartHour = Int(lfg, "depart_hour"),
            LfgLeadMinutes = Int(lfg, "lead_minutes"),
        };
        if (rules.Problem() is { } problem)
            throw new FormatException(problem);

        var settlements = Rows(map, "settlements", ConfigId).Select(row =>
        {
            string id = Text(row, "id");
            var lodge = Map(row, "lodge");
            var at = List(lodge, "position_m");
            if (at.Count != 2)
                throw new FormatException($"{id}'s lodge position_m is [x, z]");
            long Metres(int i) => (long)Math.Round(double.Parse(at[i] as string ?? throw new FormatException($"{id}'s lodge position_m holds numbers"),
                NumberStyles.Float, CultureInfo.InvariantCulture) * 1000, MidpointRounding.AwayFromZero);
            int facing = (int)Math.Round(Number(lodge, "facing_deg") * 1000, MidpointRounding.AwayFromZero);
            var grounds = (row.GetValueOrDefault("grounds") as List<object> ?? new List<object>())
                .Select(g => g as string ?? throw new FormatException($"{id}'s grounds list spawner IDs")).ToImmutableArray();
            return new AdventurerSettlement(id, Text(row, "market_ref"), row.GetValueOrDefault("smith_merchant_ref") as string, Metres(0), Metres(1),
                ((facing % 360_000) + 360_000) % 360_000, grounds) { LocationId = row.GetValueOrDefault("location_ref") as string };
        }).ToList();
        if (settlements.Select(s => s.Id).Distinct(StringComparer.Ordinal).Count() != settlements.Count)
            throw new FormatException("each settlement is named once");
        var roads = Rows(map, "roads", ConfigId).Select(r => new AdventurerRoad(Text(r, "from"), Text(r, "to"))).ToImmutableArray();

        var hire = Map(map, "hire");
        var roles = List(hire, "roles").Select(r => PartyKeys.ParseRole(r as string ?? "") ?? throw new FormatException($"hire role '{r}' is not guard, porter, gatherer or crafter"))
            .Distinct().ToImmutableArray();
        var body = new CompanionProfile(Int(hire, "health"), Text(hire, "weapon_item_ref"), ImmutableSortedDictionary<Domain.Combat.BodyRegion, int>.Empty);
        if (body.MaxHealth < 1 || roles.IsEmpty)
            throw new FormatException("a hired adventurer has some health and at least one role");
        return new AdventurerSetup(rules, settlements.ToImmutableSortedDictionary(s => s.Id, s => s, StringComparer.Ordinal), roads,
            new HirelingProfile(roles, body))
        {
            Names = WorldContent.DisplayNames(loader).ToImmutableSortedDictionary(StringComparer.Ordinal),
        };
    }
}
