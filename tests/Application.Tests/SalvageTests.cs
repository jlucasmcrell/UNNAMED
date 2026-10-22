using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Domain.Crafting;
using UNNAMED.Domain.Progression;
using UNNAMED.World;
using UNNAMED.Persistence;
using Registry = UNNAMED.EntityRegistry.EntityRegistry;
using UNNAMED.World.Runtime;

namespace UNNAMED.Application.Tests;

public class SalvageTests
{
    private const string Gloves = "item.armor.smith_gloves";
    private static InventoryEntry Stack(bool locked = false) => new(EntityId.NewId(EntityKind.Item), Gloves, 1) { Locked = locked };
    private static Arena Open(GameSession session, InventoryEntry item, int skill = 0) => Arena.OpenCreatures(session, session.Setup,
        (28, 44.1), 0, Array.Empty<(string, double, double, string)>(), r =>
        new PlayerRecord(r.Id, r.Name, r.XMm, r.YMm, r.ZMm, r.AppearanceSeed, r.Inventory.Add(item),
            r.Progression with { Skills = r.Progression.Skills.SetItem(SalvageRules.SkillId, new SkillState(skill, 0)) },
            r.FacingMdeg, r.Discoveries, r.Equipment, r.Currency, r.Effects));

    [Fact]
    public void PreviewIsStable_AndNormalCommandConsumesOneAndRecoversExactlyItsPreview()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile); var item = Stack(); var arena = Open(session, item);
        var first = arena.Simulation.Salvage(item.ItemId);
        Assert.Null(first.Refusal);
        arena.Tick();
        Assert.Equal(first.Materials.ToArray(), arena.Simulation.Salvage(item.ItemId).Materials.ToArray());
        var before = arena.Simulation.Player.Inventory.GroupBy(e => e.DefId).ToDictionary(g => g.Key, g => g.Sum(e => e.Count));
        new SaveStore(profile.Root).Save(SaveSlots.Manual("salvage_preview"), SaveDocuments.Capture(arena.Simulation.World,
            arena.Simulation.CaptureRecord(), session.Content, arena.Simulation.WorldTick, 0));
        var loaded = new SaveStore(profile.Root).Load(SaveSlots.Manual("salvage_preview"), new LoadContext(session.Generator, session.Content, new Registry()));
        var resumed = Arena.Resume(session.Setup, loaded);
        Assert.Equal(first.Materials.ToArray(), resumed.Simulation.Salvage(item.ItemId).Materials.ToArray());
        var practice = arena.Record<SkillPracticed>();
        var events = arena.Record<ItemSalvaged>();
        Assert.Null(arena.Submit(new SalvageCommand(arena.Player, item.ItemId))); arena.Tick();
        Assert.DoesNotContain(arena.Simulation.Player.Inventory, e => e.ItemId == item.ItemId);
        Assert.Single(events);
        Assert.Contains(practice, p => p.SkillId == SalvageRules.SkillId && p.Xp > 0);
        foreach (var material in first.Materials)
            Assert.Equal(before.GetValueOrDefault(material.ItemId) + material.Count,
                arena.Simulation.Player.Inventory.Where(e => e.DefId == material.ItemId).Sum(e => e.Count));
        Assert.Contains(SalvageRules.SkillId, arena.Simulation.Player.Progression.Skills.Keys);
    }

    [Fact]
    public void LockedGearIsProtected_AndSkillImprovesRecoveryWithoutGuaranteeingSingleUnitReturns()
    {
        using var profile = new TempProfile(); var session = Harness.Boot(profile); var item = Stack(true); var arena = Open(session, item);
        Assert.Contains("Unlock", arena.Simulation.Salvage(item.ItemId).Refusal);
        Assert.NotNull(arena.Submit(new SalvageCommand(arena.Player, item.ItemId)));
        Assert.Contains(arena.Simulation.Player.Inventory, e => e.ItemId == item.ItemId);
        Assert.Null(SalvageRules.Plan(session.Setup.Items.Catalog.Get("item.weapon.haldas_bow"), session.Setup.Crafting.Recipes.Values, session.Setup.Items.Catalog));
        var keepsake = new InventoryEntry(EntityId.NewId(EntityKind.Item), "item.weapon.haldas_bow", 1);
        var rewardArena = Open(session, keepsake);
        Assert.Contains("Keepsakes", rewardArena.Simulation.Salvage(keepsake.ItemId).Refusal);
        Assert.NotNull(rewardArena.Submit(new SalvageCommand(rewardArena.Player, keepsake.ItemId)));
        Assert.Contains(rewardArena.Simulation.Player.Inventory, e => e.ItemId == keepsake.ItemId);
        var definition = session.Setup.Items.Catalog.Get(Gloves);
        Assert.True(SalvageRules.RecoveryPercent(25, definition, null, session.Setup.Crafting.Constants)
            > SalvageRules.RecoveryPercent(0, definition, null, session.Setup.Crafting.Constants));
        Assert.True(SalvageRules.RecoveryPercent(0, definition, new UNNAMED.Domain.Items.ItemWear(definition.DurabilityMax, 0), session.Setup.Crafting.Constants)
            < SalvageRules.RecoveryPercent(0, definition, null, session.Setup.Crafting.Constants));
        var plan = new SalvagePlan(null, 1, ImmutableArray.Create(new RecipeInput("hide", 1)), 1);
        Assert.Empty(SalvageRules.Recover(plan, 25, _ => .9));
        Assert.Equal(1, Assert.Single(SalvageRules.Recover(plan with { RecipeId = "recipe.test" }, 25, _ => .9)).Count);
        Assert.Equal(1, Assert.Single(SalvageRules.Recover(plan, 25, _ => .1)).Count);
    }
    [Fact]
    public void AllRecoveredStacksMustFit_BeforeAnyItemOrSkillIsChanged()
    {
        using var profile = new TempProfile(); var session = Harness.Boot(profile); var item = Stack();
        var recipe = session.Setup.Crafting.Recipes.Values.First(r => r.OutputItemId == Gloves) with
        { Inputs = ImmutableArray.Create(new RecipeInput("item.material.iron_ore", 20), new RecipeInput("item.material.small_hide", 20)) };
        var setup = session.Setup with { Crafting = session.Setup.Crafting with { Recipes = session.Setup.Crafting.Recipes.SetItem(recipe.Id, recipe) } };
        var arena = Arena.OpenCreatures(session, setup, (28, 44.1), 0, Array.Empty<(string,double,double,string)>(), r =>
            new PlayerRecord(r.Id,r.Name,r.XMm,r.YMm,r.ZMm,r.AppearanceSeed,
                Enumerable.Range(0,23).Select(_ => new InventoryEntry(EntityId.NewId(EntityKind.Item), "item.tool.water_flask", 1)).Append(item),
                r.Progression,r.FacingMdeg,r.Discoveries));
        var preview = arena.Simulation.Salvage(item.ItemId);
        Assert.Null(preview.Refusal); Assert.Equal(2, preview.Materials.Length);
        var inventory = arena.Simulation.Player.Inventory.ToArray(); var skills = arena.Simulation.Player.Progression.Skills;
        Assert.NotNull(arena.Submit(new SalvageCommand(arena.Player,item.ItemId)));
        Assert.Equal(inventory, arena.Simulation.Player.Inventory.ToArray());
        Assert.Equal(skills, arena.Simulation.Player.Progression.Skills);
    }

}
