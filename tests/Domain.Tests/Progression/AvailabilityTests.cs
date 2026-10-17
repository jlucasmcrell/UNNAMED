using System.Collections.Immutable;
using UNNAMED.Domain.Combat;
using UNNAMED.Domain.Crafting;
using UNNAMED.Domain.Magic;
using UNNAMED.Domain.Progression;

namespace UNNAMED.Domain.Tests.Progression;

/// <summary>The one availability evaluator (UI progression B, C, D): the word, the first reason in canon's order, the checklist, the risk read by band.</summary>
public class AvailabilityTests
{
    private static readonly MagicConstants Magic = new();

    private static string Name(string id) => id switch
    {
        "skill.one_hand_blade" => "One-hand blade",
        "skill.force" => "Force",
        "skill.smithing" => "Smithing",
        "item.material.iron_billet" => "Iron billet",
        "station.anvil" => "Anvil",
        "effect.bleeding" => "bleeding",
        _ => id,
    };

    private static AvailabilityContext Context(int stamina = 100, int focus = 100, int strain = 0, int tolerance = 40, int health = 100,
        string? held = "skill.one_hand_blade", string? heldName = "Iron sword", int force = 0, int iron = 0, string? station = null, int cooldown = 0) =>
        new(id => id == "skill.force" ? force : 0, stamina, focus, strain, tolerance, health, held, heldName,
            id => id == "item.material.iron_billet" ? iron : 0, station, _ => cooldown, Name);

    private static TechniqueDefinition PowerStrike() =>
        new("ability.blade.power_strike", "skill.one_hand_blade", 1.6, 1.5, 0, 22, 120) { OnHitEffect = "effect.bleeding", OnHitEffectPercent = 50 };

    private static FormulaDefinition Bolt(int complexity = 12) =>
        new("spell.force.bolt", "skill.force", complexity, 10, 20, 12, 6, Targeting.Projectile);

    private static RecipeDefinition Billet() =>
        new("recipe.iron_billet", "station.anvil", "skill.smithing", 5, ImmutableArray.Create(new RecipeInput("item.material.iron_billet", 2)), "item.out", 1, true, 10);

    // ---- techniques ----

    [Fact]
    public void ATechniqueWithTheWeaponStaminaAndNoWait_IsReady()
    {
        var a = AvailabilityRules.Technique(PowerStrike(), Context());
        Assert.Equal(AvailabilityKind.Ready, a.Kind);
        Assert.Equal("Ready", a.Word);
        Assert.Null(a.Reason);
        Assert.All(a.Checklist, line => Assert.True(line.Met));
    }

    [Fact]
    public void AWeaponOfAnotherFamily_NeedsGear_AndSaysWhatIsHeld()
    {
        var a = AvailabilityRules.Technique(PowerStrike(), Context(held: "skill.bow", heldName: "Hunting bow"));
        Assert.Equal(AvailabilityKind.NeedsGear, a.Kind);
        Assert.Equal("Needs gear", a.Word);
        Assert.Equal("Needs a one-hand blade weapon in hand — you hold Hunting bow", a.Reason);
    }

    [Fact]
    public void EmptyHands_NeedGear_InWords()
    {
        var a = AvailabilityRules.Technique(PowerStrike(), Context(held: null, heldName: null));
        Assert.Equal("Needs a one-hand blade weapon in hand — your hands are empty", a.Reason);
    }

    [Fact]
    public void TooLittleStamina_IsShortOf_WithHaveOfNeed()
    {
        var a = AvailabilityRules.Technique(PowerStrike(), Context(stamina: 12));
        Assert.Equal(AvailabilityKind.ShortOf, a.Kind);
        Assert.Equal("Short of Stamina", a.Word);
        Assert.Equal("Short of Stamina: 12 of 22", a.Reason);
    }

    [Fact]
    public void WhenSeveralFail_GearIsNamedFirst_ThenStamina_ThenTheWait_AndTheChecklistListsAll()
    {
        var all = AvailabilityRules.Technique(PowerStrike(), Context(stamina: 1, held: null, heldName: null, cooldown: 4));
        Assert.Equal(AvailabilityKind.NeedsGear, all.Kind);
        Assert.Equal(3, all.Checklist.Count(l => !l.Met));
        var tired = AvailabilityRules.Technique(PowerStrike(), Context(stamina: 1, cooldown: 4));
        Assert.Equal(AvailabilityKind.ShortOf, tired.Kind);
        var waiting = AvailabilityRules.Technique(PowerStrike(), Context(cooldown: 4));
        Assert.Equal(AvailabilityKind.Recovering, waiting.Kind);
        Assert.Equal("Recovering: ready again in 4 s", waiting.Reason);
    }

    [Fact]
    public void ATechniqueSummary_SaysWhatItDoesAndCosts() =>
        Assert.Equal("One-hand blade · 22 Stamina · a harder blow · a longer wind-up · may leave bleeding · 6 s between uses",
            AvailabilityRules.TechniqueSummary(PowerStrike(), Name));

    // ---- formulas ----

    [Fact]
    public void AFormula_WithTheFocus_IsReady_EvenBelowItsComplexity()
    {
        var a = AvailabilityRules.Formula(Bolt(), Magic, Context(focus: 10, force: 3));
        Assert.Equal(AvailabilityKind.Ready, a.Kind);
        Assert.False(a.Checklist[1].Met);
    }

    [Fact]
    public void AFormula_ShortOfFocus_SaysSo()
    {
        var a = AvailabilityRules.Formula(Bolt(), Magic, Context(focus: 8));
        Assert.Equal("Short of Focus", a.Word);
        Assert.Equal("Short of Focus: 8 of 10", a.Reason);
    }

    [Fact]
    public void PastTolerance_IsACaution_NeverARefusal()
    {
        var a = AvailabilityRules.Formula(Bolt(), Magic, Context(strain: 30, tolerance: 40, force: 12));
        Assert.True(a.Ready);
        Assert.StartsWith("Past your Strain tolerance now", a.Caution);
    }

    // ---- the risk read by band ----

    [Theory]
    [InlineData(17, "steady")]
    [InlineData(16, "uncertain")]
    [InlineData(12, "uncertain")]
    [InlineData(11, "beyond you")]
    public void TheWord_IsSteadyFiveOver_UncertainWithinFour_BeyondYouBelow(int skill, string word) =>
        Assert.Equal(word, AvailabilityRules.Risk(Bolt(12), Magic, Context(force: skill)).Word);

    [Fact]
    public void BelowFifteen_ItIsWordsOnly()
    {
        var read = AvailabilityRules.Risk(Bolt(14), Magic, Context(force: 5));
        Assert.Equal("beyond you", read.Word);
        Assert.Empty(read.Lines);
    }

    [Fact]
    public void FromFifteen_ItSaysWhatSlips_ButNotTheChance()
    {
        var read = AvailabilityRules.Risk(Bolt(20), Magic, Context(force: 16));
        Assert.Equal("beyond you", read.Word);
        Assert.Single(read.Lines);
        Assert.Contains("fizzles", read.Lines[0]);
    }

    [Fact]
    public void FromThirty_ItGivesTheSlipChance_AndFromFortyFive_TheSeverity()
    {
        var thirty = AvailabilityRules.Risk(Bolt(34), Magic, Context(force: 31));
        Assert.Contains(thirty.Lines, l => l.StartsWith("Slip chance "));
        Assert.DoesNotContain(thirty.Lines, l => l.StartsWith("Strain "));
        var fortyFive = AvailabilityRules.Risk(Bolt(34), Magic, Context(force: 46));
        Assert.Contains(fortyFive.Lines, l => l.StartsWith("Strain ") && l.Contains(" of 40: "));
        Assert.Contains(fortyFive.Lines, l => l == "It will not slip at your skill.");
    }

    [Fact]
    public void ABacklash_IsToldInEveryBand_AndCouldKillWhenItExceedsHealth()
    {
        var low = AvailabilityRules.Risk(Bolt(), Magic, Context(force: 2, strain: 39, tolerance: 40, health: 100));
        Assert.Single(low.Lines);
        Assert.StartsWith("Past your Strain tolerance this would cost", low.Lines[0]);
        var deadly = AvailabilityRules.Risk(Bolt(), Magic, Context(force: 2, strain: 39, tolerance: 40, health: 3));
        Assert.EndsWith("this could kill you.", deadly.Lines[0]);
    }

    // ---- recipes ----

    [Fact]
    public void ARecipe_ShortOfAnInput_NamesThatSupply()
    {
        var a = AvailabilityRules.Recipe(Billet(), Context(iron: 1, station: "station.anvil"));
        Assert.Equal(AvailabilityKind.ShortOf, a.Kind);
        Assert.Equal("Short of Iron billet", a.Word);
        Assert.Equal("Short of Iron billet: 1 of 2", a.Reason);
    }

    [Fact]
    public void ARecipe_WithTheInputs_ButNoStationNear_NeedsTheSituation()
    {
        var a = AvailabilityRules.Recipe(Billet(), Context(iron: 2));
        Assert.Equal(AvailabilityKind.NeedsSituation, a.Kind);
        Assert.Equal("Needs an anvil within reach", a.Reason);
    }

    [Fact]
    public void ARecipe_WithEverything_IsReady()
    {
        var a = AvailabilityRules.Recipe(Billet(), Context(iron: 2, station: "station.anvil"));
        Assert.True(a.Ready);
        Assert.All(a.Checklist, line => Assert.True(line.Met));
    }
}
