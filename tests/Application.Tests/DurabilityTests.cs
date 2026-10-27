using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Domain.Combat;
using UNNAMED.Domain.Crafting;
using UNNAMED.Domain.Items;
using UNNAMED.Domain.Progression;
using UNNAMED.Domain.Spatial;
using UNNAMED.World;
using UNNAMED.World.Runtime;

namespace UNNAMED.Application.Tests;

/// <summary>
/// WP3b over the game's own content (SYSTEMS_COMPLETENESS_PLAN §2.4 I9, I10): wear from the blows the character lands and takes, a worn-out
/// piece counting as crude and never lost, repair at a station (skill-gated, capped at the made quality) and at a trader (for coin, into
/// their purse), the laden band's stamina, and nothing of it moving while the character is away (T-22).
/// </summary>
public class DurabilityTests
{
    private const long Day = 57_600;
    private const string Sword = "item.weapon.rusted_sword";
    private const string Spear = "item.weapon.march_spear";
    private const string Ingot = "item.material.iron_ingot";
    private const string Kera = "npc.ashen_hollow.kera_voss";
    private const string Renn = "npc.ashen_hollow.renn_vale";
    private const string Anvil = "station.forge_anvil";
    private static readonly (double X, double Z) Open = (120, 60);
    private static readonly (double X, double Z) AtKera = (60.3, 140.3);
    private static readonly (double X, double Z) AtAnvil = (57.5, 141.5);
    private static readonly string[] Armour =
    {
        "item.armor.hide_cap", "item.armor.hide_vest", "item.armor.cloak", "item.armor.smith_gloves", "item.armor.wool_trousers", "item.armor.hide_boots",
    };

    private static InventoryEntry Stack(string defId, int count = 1, int quality = Quality.Standard, ItemWear? wear = null) =>
        new(EntityId.NewId(EntityKind.Item), defId, count) { Quality = quality, Wear = wear };

    /// <summary>The character with these stacks besides the kit, the first of each listed piece equipped in its slot, and this much coin.</summary>
    private static PlayerRecord Kit(PlayerRecord r, SimulationSetup setup, long currency, InventoryEntry[] extra, params string[] equip)
    {
        var inventory = r.Inventory.Where(e => !extra.Any(x => x.DefId == e.DefId && equip.Contains(e.DefId))).Concat(extra).ToList();
        var equipment = r.Equipment.Where(kv => !equip.Any(id => setup.Items.Catalog.Get(id).Slot == kv.Key))
            .Concat(equip.Select(id => KeyValuePair.Create(setup.Items.Catalog.Get(id).Slot!.Value, inventory.First(e => e.DefId == id).ItemId)));
        return new PlayerRecord(r.Id, r.Name, r.XMm, r.YMm, r.ZMm, r.AppearanceSeed, inventory, r.Progression, r.FacingMdeg, r.Discoveries,
            equipment, currency, r.Effects, r.Relationships, r.Conversations);
    }

    private static PlayerRecord Skilled(PlayerRecord r, string skill, int level) =>
        r.WithProgression(r.Progression with { Skills = r.Progression.Skills.SetItem(skill, new SkillState(level, 0)) });

    private static Arena At(GameSession session, (double X, double Z) place, Func<PlayerRecord, PlayerRecord> change,
        IEnumerable<(string, double, double, string)>? creatures = null) =>
        Arena.OpenCreatures(session, session.Setup, place, 0, creatures ?? Array.Empty<(string, double, double, string)>(), change);

    private static InventoryEntry Carried(Arena arena, string defId) => arena.Simulation.Player.Inventory.First(e => e.DefId == defId);

    [Fact]
    public void EveryWeaponAndPieceOfArmour_Wears_AndNothingElseDoes()
    {
        using var profile = new TempProfile();
        var catalog = Harness.Boot(profile).Setup.Items.Catalog;
        Assert.All(catalog.Definitions.Values.Where(d => d.Category is "weapon" or "armor"), d =>
        {
            Assert.True(d.DurabilityMax > 0, d.Id);
            Assert.NotNull(d.Repair);
        });
        Assert.All(catalog.Definitions.Values.Where(d => d.Category is not ("weapon" or "armor" or "tool")), d => Assert.Equal(0, d.DurabilityMax));
        // A light carried in the hand burns down and says how it is filled again (note 13).
        Assert.All(catalog.Definitions.Values.Where(d => d.LightRadiusMm > 0), d =>
        {
            Assert.True(d.DurabilityMax > 0, d.Id);
            Assert.NotNull(d.Repair);
        });
    }

    [Fact]
    public void BlowsLanded_WearTheWeapon_AndWornOutItCountsAsCrude_ButIsNeverLost()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = At(session, Open, r => Kit(r, session.Setup, 0, new[] { Stack(Sword, wear: new ItemWear(197, Quality.Standard)) }, Sword),
            new[] { (Arena.Wolf, Open.X, Open.Z + 2.0, "pack_hunter") });
        var worn = arena.Record<ItemWornOut>();
        var hits = arena.Record<HitResolved>();
        var sword = Carried(arena, Sword).ItemId;
        Assert.Equal((7, 7), (arena.Simulation.Combat.Weapon.DamageMin, arena.Simulation.Combat.Weapon.DamageMax));

        arena.Fight(arena.Creature(), 600);

        int landed = hits.Count(h => h.Attacker == arena.Player && !h.Dodged);
        Assert.True(landed >= 3, $"{landed} blows landed");
        var entry = arena.Simulation.Player.Inventory.Single(e => e.ItemId == sword);   // still carried, still equipped
        Assert.Equal(sword, arena.Simulation.Player.Equipment[EquipSlot.MainHand]);
        Assert.Equal((Quality.Crude, new ItemWear(200, Quality.Standard)), (entry.Quality, entry.Wear));
        Assert.Equal(sword, Assert.Single(worn).Item);
        Assert.Equal((5, 5), (arena.Simulation.Combat.Weapon.DamageMin, arena.Simulation.Combat.Weapon.DamageMax));   // crude: 2 softer
    }

    [Fact]
    public void ALitTorch_BurnsDown_AndThenGivesNoLight()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        const string Torch = "item.tool.torch";
        var arena = At(session, Open, r => Kit(r, session.Setup, 0, new[] { Stack(Torch, wear: new ItemWear(297, Quality.Standard)) }, Torch));
        var worn = arena.Record<ItemWornOut>();
        var torch = Carried(arena, Torch);
        var definition = session.Setup.Items.Catalog.Find(Torch)!;
        Assert.Equal(torch.ItemId, arena.Simulation.Player.Equipment[EquipSlot.OffHand]);
        // Lit while it has fuel; one point of burn every 20 ticks, so three is enough to spend it.
        Assert.Equal(definition.LightRadiusMm, Durability.LitRadius(definition.LightRadiusMm, definition.DurabilityMax, torch.Wear));

        arena.Tick(3 * session.Setup.Items.Inventory.Durability.LightWearTicks);

        var burned = Carried(arena, Torch);
        Assert.Equal(300, burned.Wear?.Points);
        Assert.Equal(torch.ItemId, arena.Simulation.Player.Equipment[EquipSlot.OffHand]);   // spent, never lost
        Assert.Equal(0, Durability.LitRadius(definition.LightRadiusMm, definition.DurabilityMax, burned.Wear));
        Assert.Equal(torch.ItemId, Assert.Single(worn).Item);
    }

    [Fact]
    public void AWorkingThatStrikes_WearsNoWeapon()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        const string Bolt = "spell.force.impulse_bolt";
        var arena = At(session, Open, r => Skilled(r.WithProgression(r.Progression with
        {
            Known = r.Progression.Known.SetItem(Bolt, new KnownTechnique(LearningSource.Teacher, "npc.test", 0)),
        }), "skill.force", 5), new[] { (Arena.Wolf, Open.X, Open.Z + 15.0, "sentinel") });
        var hits = arena.Record<HitResolved>();
        Assert.Null(arena.Submit(new CastCommand(arena.Player, Bolt)));
        var formula = arena.Simulation.Setup.Magic.Formulas[Bolt];
        arena.Tick(formula.CastTicks + formula.RecoveryTicks + 2);

        Assert.Equal(Bolt, Assert.Single(hits, h => h.Attacker == arena.Player).Source);
        Assert.Null(Carried(arena, Sword).Wear);
    }

    [Fact]
    public void BlowsTaken_WearWhatIsWornWhereTheyLand()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = At(session, Open, r => Kit(r, session.Setup, 0, Armour.Select(a => Stack(a)).ToArray(), Armour),
            new[] { (Arena.Wolf, Open.X, Open.Z + 1.9, "pack_hunter") });
        var hits = arena.Record<HitResolved>();
        // Wound it so it turns on the character, then stand and take its bites.
        Assert.Null(arena.Submit(new AttackCommand(arena.Player)));
        for (int i = 0; i < 600 && hits.Count(h => h.Target == arena.Player && !h.Dodged && !h.Blocked) < 4; i++)
            arena.Tick();

        var taken = hits.Where(h => h.Target == arena.Player && !h.Dodged && !h.Blocked).ToList();
        Assert.NotEmpty(taken);
        int Expected(params BodyRegion[] regions) => taken.Count(h => regions.Contains(h.Region));
        int Wear(string defId) => Carried(arena, defId).Wear?.Points ?? 0;
        Assert.Equal(Expected(BodyRegion.Head), Wear("item.armor.hide_cap"));
        Assert.Equal(Expected(BodyRegion.Torso), Wear("item.armor.hide_vest"));
        Assert.Equal(Expected(BodyRegion.Torso), Wear("item.armor.cloak"));
        Assert.All(new[] { "item.armor.smith_gloves", "item.armor.wool_trousers", "item.armor.hide_boots" },
            id => Assert.Equal(Expected(BodyRegion.Limbs), Wear(id)));
        // The sword is worn by its own blows, not by the bites.
        Assert.Equal(hits.Count(h => h.Attacker == arena.Player && !h.Dodged), Carried(arena, Sword).Wear?.Points ?? 0);
    }

    [Fact]
    public void AHarvest_WearsATool_InTheMainHand_AndNothingElse()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        // Content has no gathering tool yet (an open question): a test pick stands in for one.
        const string Pick = "item.tool.test_pick";
        var pick = new ItemDefinition(Pick, "tool", 1, 2_000, 10, "common", false, false, EquipSlot.MainHand, Requirements.None, null, 0)
        {
            DurabilityMax = 20,
            Repair = new RepairSpec("anvil", "skill.smithing", 0, Ingot),
        };
        var setup = session.Setup with
        {
            Items = session.Setup.Items with { Catalog = new ItemCatalog(session.Setup.Items.Catalog.Definitions.Values.Append(pick)) },
        };
        var node = session.Setup.Layout.Nodes.First();
        var hollow = Harness.HollowOf(setup);
        var place = (node.XMm / 1000.0 - hollow.X, node.ZMm / 1000.0 - hollow.Z + 0.5);
        var sword = Arena.OpenCreatures(session, setup, place, 0, Array.Empty<(string, double, double, string)>(), r => r);
        var picked = Arena.OpenCreatures(session, setup, place, 0, Array.Empty<(string, double, double, string)>(),
            r => Kit(r, setup, 0, new[] { Stack(Pick) }, Pick));

        foreach (var arena in new[] { sword, picked })
        {
            var key = arena.Simulation.Nodes.OrderBy(n => Math.Pow(n.XMm - arena.Simulation.Player.Body.XMm, 2) + Math.Pow(n.ZMm - arena.Simulation.Player.Body.ZMm, 2)).First().Key;
            Assert.Null(arena.Submit(new GatherCommand(arena.Player, key)));
        }

        Assert.Null(Carried(sword, Sword).Wear);
        Assert.Equal(new ItemWear(1, Quality.Standard), Carried(picked, Pick).Wear);
    }

    [Fact]
    public void AtTheAnvil_ARepairSpendsItsMaterial_AndComesBackNoFinerThanItWasMade()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = At(session, AtAnvil, r => Skilled(Kit(r, session.Setup, 0,
            new[] { Stack(Sword, quality: Quality.Crude, wear: new ItemWear(200, Quality.Fine)), Stack(Ingot, 3, Quality.Fine) }, Sword), "skill.smithing", 30));
        var repaired = arena.Record<ItemRepaired>();
        var sword = Carried(arena, Sword).ItemId;

        Assert.Null(arena.Submit(new RepairCommand(arena.Player, sword)));

        var entry = Carried(arena, Sword);
        Assert.Equal(Quality.Fine, entry.Quality);   // a fine ingot in a practised hand: back to its make, and capped there
        Assert.Null(entry.Wear);
        Assert.Equal(2, Carried(arena, Ingot).Count);
        Assert.Equal((sword, Quality.Fine, Anvil, 0L), (repaired.Single().Item, repaired[0].Quality, repaired[0].By, repaired[0].Coin));
        Assert.Equal($"{Sword} needs no repair", arena.Submit(new RepairCommand(arena.Player, sword)));
    }

    [Fact]
    public void ARepair_IsRefused_ForWantOfSkill_Material_OrAStation()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var worn = new ItemWear(50, Quality.Standard);
        var unskilled = At(session, AtAnvil, r => Kit(r, session.Setup, 0, new[] { Stack(Spear, wear: worn), Stack(Ingot) }));
        Assert.Equal($"repairing {Spear} needs skill.smithing 5; you have 0", unskilled.Submit(new RepairCommand(unskilled.Player, Carried(unskilled, Spear).ItemId)));

        var bare = At(session, AtAnvil, r => Kit(r, session.Setup, 0, new[] { Stack(Sword, wear: worn) }, Sword));
        Assert.Equal($"needs 1 {Ingot}", bare.Submit(new RepairCommand(bare.Player, Carried(bare, Sword).ItemId)));

        var away = At(session, Open, r => Kit(r, session.Setup, 0, new[] { Stack(Sword, wear: worn), Stack(Ingot) }, Sword));
        Assert.Equal("no anvil in reach", away.Submit(new RepairCommand(away.Player, Carried(away, Sword).ItemId)));
        Assert.Equal(1, Carried(away, Ingot).Count);   // a refused repair spends nothing
    }

    [Fact]
    public void ATrader_RepairsForCoin_IntoTheirPurse_AndAWornOutPieceComesBackAtMostStandard()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = At(session, AtKera, r => Kit(r, session.Setup, 20,
            new[] { Stack(Sword, quality: Quality.Crude, wear: new ItemWear(200, Quality.Fine)), Stack(Spear, quality: Quality.Fine, wear: new ItemWear(100, Quality.Fine)),
                Stack("item.material.wolf_hide", 3) }, Sword));
        var repaired = arena.Record<ItemRepaired>();
        var sword = Carried(arena, Sword).ItemId;
        var spear = Carried(arena, Spear).ItemId;
        // Her purse starts full, and what would overfill it leaves the town: sell her three hides first, to make room for the quality-priced repair.
        Assert.Null(arena.Submit(new SellCommand(arena.Player, Kera, Carried(arena, "item.material.wolf_hide").ItemId, 3)));
        long purse = arena.Simulation.Wares(Kera)!.Purse!.Value, coin = arena.Simulation.Player.Currency;

        var quote = arena.Simulation.RepairQuote(Kera, sword)!;
        Assert.Equal((8L, Quality.Standard), (quote.Fee, quote.Quality));   // fine as-made value 18 x 40%, rounded up at full wear
        Assert.Null(arena.Submit(new MerchantRepairCommand(arena.Player, Kera, sword)));
        Assert.Equal((Quality.Standard, new ItemWear(0, Quality.Fine)), (Carried(arena, Sword).Quality, Carried(arena, Sword).Wear));
        Assert.Equal(coin - 8, arena.Simulation.Player.Currency);
        Assert.Equal(purse + 8, arena.Simulation.Wares(Kera)!.Purse);
        Assert.Equal((Kera, 8L), (repaired.Single().By, repaired[0].Coin));

        // Not yet worn out, a piece keeps the quality it has.
        Assert.Equal(Quality.Fine, arena.Simulation.RepairQuote(Kera, spear)!.Quality);
        Assert.Null(arena.Submit(new MerchantRepairCommand(arena.Player, Kera, spear)));
        Assert.Equal((Quality.Fine, (ItemWear?)null), (Carried(arena, Spear).Quality, Carried(arena, Spear).Wear));

        Assert.Equal("item.tool.water_flask does not wear",
            arena.Submit(new MerchantRepairCommand(arena.Player, Kera, Carried(arena, "item.tool.water_flask").ItemId)));
        Assert.Null(arena.Simulation.RepairQuote(Renn, sword));
    }

    [Theory]
    [InlineData(Quality.Standard)]
    [InlineData(Quality.Fine)]
    public void ATrader_PaysLessForAWornPiece_AndMendingItToSellItNeverGains(int madeQuality)
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = At(session, AtKera, r => Kit(r, session.Setup, 50, new[] { Stack(Sword, quality: Quality.Crude, wear: new ItemWear(200, madeQuality)) }, Sword));
        var sword = Carried(arena, Sword);
        long whole = arena.Simulation.Offer(Kera, Sword, quality: madeQuality)!.Value, worn = arena.Simulation.Offer(Kera, Sword, sword.Wear, sword.Quality, sword.Notable)!.Value;
        long fee = arena.Simulation.RepairQuote(Kera, sword.ItemId)!.Fee;
        Assert.True(worn < whole);
        Assert.True(whole - worn <= fee, $"mending costs {fee} and raises the offer by {whole - worn}");

        Assert.Null(arena.Submit(new UnequipCommand(arena.Player, EquipSlot.MainHand)));
        long coin = arena.Simulation.Player.Currency;
        Assert.Null(arena.Submit(new SellCommand(arena.Player, Kera, sword.ItemId, 1)));
        Assert.Equal(coin + worn, arena.Simulation.Player.Currency);   // what was offered is what is paid
    }

    [Fact]
    public void ATradersRepair_IsRefused_WithoutTheCoin_OrByOneWhoDoesNotRepair()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = At(session, AtKera, r => Kit(r, session.Setup, 2, new[] { Stack(Sword, quality: Quality.Crude, wear: new ItemWear(200, Quality.Standard)) }, Sword));
        var sword = Carried(arena, Sword).ItemId;
        Assert.Equal("not enough coin: 6 asked, 2 carried", arena.Submit(new MerchantRepairCommand(arena.Player, Kera, sword)));
        Assert.Equal(Quality.Crude, Carried(arena, Sword).Quality);
        Assert.Equal(2L, arena.Simulation.Player.Currency);
        Assert.Equal("Renn Vale does not trade", arena.Submit(new MerchantRepairCommand(arena.Player, Renn, sword)));
    }

    /// <summary>Ingots enough to carry just past the laden band.</summary>
    private static InventoryEntry[] Load(GameSession session, double share)
    {
        var probe = At(session, Open, r => r);
        long target = (long)Math.Ceiling(probe.Simulation.Player.CarryLimitGrams * share) - probe.Simulation.Player.CarriedGrams;
        int ingots = (int)Math.Ceiling(target / 1000.0);
        return Enumerable.Range(0, (ingots + 9) / 10).Select(i => Stack(Ingot, Math.Min(10, ingots - i * 10))).ToArray();
    }

    [Fact]
    public void Laden_ASprintDrainsHalfAgainAsMuch_AndADodgeAQuarterMore()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var light = At(session, Open, r => r);
        var heavy = At(session, Open, r => Kit(r, session.Setup, 0, Load(session, 0.85)));
        Assert.False(light.Simulation.Laden);
        Assert.True(heavy.Simulation.Laden);

        foreach (var arena in new[] { light, heavy })
        {
            arena.Simulation.Enqueue(new MoveCommand(arena.Player, new MoveIntent(1000, 0, Gait.Sprint, 90_000)));
            arena.Tick(40);
        }
        Assert.Equal(light.Simulation.Combat.MaxStamina - 20, light.Simulation.Combat.Stamina);   // 10 a second for 2 s
        Assert.Equal(heavy.Simulation.Combat.MaxStamina - 30, heavy.Simulation.Combat.Stamina);   // 15 a second laden

        foreach (var arena in new[] { light, heavy })
        {
            arena.Simulation.Enqueue(new MoveCommand(arena.Player, MoveIntent.Idle(90_000)));
            arena.Tick();
        }
        int lightBefore = light.Simulation.Combat.Stamina, heavyBefore = heavy.Simulation.Combat.Stamina;
        Assert.Null(light.Submit(new DodgeCommand(light.Player, 1000, 0)));
        Assert.Null(heavy.Submit(new DodgeCommand(heavy.Player, 1000, 0)));
        Assert.Equal(20, lightBefore - light.Simulation.Combat.Stamina);
        Assert.Equal(25, heavyBefore - heavy.Simulation.Combat.Stamina);
    }

    [Fact]
    public void Laden_ASprintBrokenOffEveryFourthTick_StillPaysTheExtraForEverySprintingTick()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var light = At(session, Open, r => r);
        var heavy = At(session, Open, r => Kit(r, session.Setup, 0, Load(session, 0.85)));
        foreach (var arena in new[] { light, heavy })
        {
            for (int i = 0; i < 20; i++)
            {
                arena.Simulation.Enqueue(new MoveCommand(arena.Player, new MoveIntent(1000, 0, Gait.Sprint, 90_000)));
                arena.Tick(3);
                arena.Simulation.Enqueue(new MoveCommand(arena.Player, MoveIntent.Idle(90_000)));
                arena.Tick();
            }
        }
        // 60 sprinting ticks, 3 s: the extra is half of the 30 points a light sprint spends, wherever the stops fall.
        Assert.Equal(15, light.Simulation.Combat.Stamina - heavy.Simulation.Combat.Stamina);
    }

    [Fact]
    public void Laden_ADodgeWithoutItsWholeCost_IsRefused_AndSpendsNothing()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var heavy = At(session, Open, r => Kit(r, session.Setup, 0, Load(session, 0.85))
            .WithProgression(r.Progression with { Pools = r.Progression.Pools with { Stamina = 22 } }));
        Assert.True(heavy.Simulation.Laden);
        Assert.Equal("too tired to dodge", heavy.Submit(new DodgeCommand(heavy.Player, 1000, 0)));
        Assert.Equal(22, heavy.Simulation.Combat.Stamina);
    }

    [Fact]
    public void AWornPiece_KeepsItsWear_OnTheGround_AndInAChest()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var wear = new ItemWear(120, Quality.Fine);
        var arena = At(session, Open, r => Kit(r, session.Setup, 0, new[] { Stack(Spear, quality: Quality.Fine, wear: wear) }));
        var spear = Carried(arena, Spear).ItemId;
        Assert.Null(arena.Submit(new MoveItemCommand(arena.Player, spear.Value, ItemPlace.Carried, ItemPlace.Ground, 1)));
        Assert.Equal(wear, arena.Simulation.WorldItems.Single(i => i.Id == spear).Wear);
        Assert.Null(arena.Submit(new MoveItemCommand(arena.Player, spear.Value, ItemPlace.Ground, ItemPlace.Carried, 1)));
        Assert.Equal((Quality.Fine, wear), (Carried(arena, Spear).Quality, Carried(arena, Spear).Wear));
    }

    /// <summary>
    /// T-22 for wear and the laden band: the character does nothing while away, so nothing they carry wears and no sprint drains - any split
    /// of an absence leaves their gear and pools exactly as they were.
    /// </summary>
    [Fact]
    public void WhileAway_NothingWears_AndTheAdvanceIsChunkInvariant()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        Arena Worn() => At(session, Open, r => Kit(r, session.Setup, 0,
            Load(session, 0.85).Append(Stack(Spear, quality: Quality.Fine, wear: new ItemWear(150, Quality.Fine))).ToArray(), Spear));
        string Gear(Simulation s) => string.Join(";", s.Player.Inventory.OrderBy(e => e.DefId, StringComparer.Ordinal).ThenBy(e => e.Count).Select(e => $"{e.DefId}:{e.Count}:{e.Quality}:{e.Wear}"))
            + $"|{s.Combat.Stamina}|{s.Laden}";

        var once = Worn().Simulation;
        var pieces = Worn().Simulation;
        string before = Gear(once);
        Assert.Equal(before, Gear(pieces));

        once.Advance(10 * Day + 17);
        pieces.Advance(3);
        pieces.Advance(Day);
        pieces.Advance(9 * Day + 14);

        Assert.Equal(before, Gear(once));
        Assert.Equal(Gear(once), Gear(pieces));
        Assert.Equal(once.WorldTick, pieces.WorldTick);
    }
}
