using UNNAMED.Domain.Character;
using UNNAMED.Domain.Progression;
using UNNAMED.World;
using UNNAMED.World.Runtime;

namespace UNNAMED.Application.Tests;

/// <summary>
/// WP9: the quick bar (the first of six bars, twelve slots) over the game's own content. What comes to hand is offered a free slot - an item on keys 1 to 3 first, a
/// formula on keys 4 to 8 first - and the player moves, swaps and clears slots through the one command; nothing else writes the bar.
/// </summary>
public class QuickBarTests
{
    private const string Salve = "item.consumable.salve_minor";
    private const string Primer = "item.tome.resonance_primer";
    private const string Bolt = "spell.force.impulse_bolt";
    private const string Ward = "spell.warding.brace_ward";
    private const string Thread = "spell.vital.mending_thread";
    private static readonly (double X, double Z) Ground = (120, 60);

    private static Arena Open(GameSession session, Func<PlayerRecord, PlayerRecord>? change = null) =>
        Arena.OpenCreatures(session, session.Setup, Ground, 0, Array.Empty<(string, double, double, string)>(), change);

    private static PlayerRecord WithPrimer(PlayerRecord r) =>
        new(r.Id, r.Name, r.XMm, r.YMm, r.ZMm, r.AppearanceSeed, r.Inventory.Append(Arena.Stack(Primer, 1)), r.Progression, r.FacingMdeg,
            r.Discoveries, r.Equipment, r.Currency, r.Effects);

    private static void Read(Arena arena)
    {
        var primer = arena.Simulation.Player.Inventory.Single(e => e.DefId == Primer);
        Assert.Null(arena.Submit(new UseItemCommand(arena.Player, primer.ItemId)));
        arena.Tick();
    }

    [Fact]
    public void TheKitsSalve_TakesKeyOne_AndThePrimersFormulas_TakeKeysFourToSix_InTheOrderTheyWereRead()
    {
        using var profile = new TempProfile();
        var arena = Open(Harness.Boot(profile), WithPrimer);
        var assigned = arena.Record<QuickSlotAssigned>();
        arena.Tick();
        Assert.Equal(new string?[] { Salve, null, null, null, null, null, null, null, null, null, null, null }, arena.Simulation.QuickBar);

        Read(arena);

        Assert.Equal(new string?[] { Salve, null, null, Bolt, Ward, Thread, null, null, null, null, null, null }, arena.Simulation.QuickBar);
        Assert.Equal(new[] { (0, Salve), (3, Bolt), (4, Ward), (5, Thread) }, assigned.Select(a => (a.Slot, a.DefinitionId!)));
    }

    [Fact]
    public void ASlotTakesWhatIsPutOnIt_SwapsWithWhereItCameFrom_AndClears()
    {
        using var profile = new TempProfile();
        var arena = Open(Harness.Boot(profile), WithPrimer);
        arena.Tick();
        Read(arena);

        Assert.Null(arena.Submit(new AssignQuickSlotCommand(arena.Player, 0, Ward)));
        Assert.Equal(new string?[] { Ward, null, null, Bolt, Salve, Thread, null, null, null, null, null, null }, arena.Simulation.QuickBar);
        Assert.Null(arena.Submit(new AssignQuickSlotCommand(arena.Player, 7, Bolt)));
        Assert.Equal(new string?[] { Ward, null, null, null, Salve, Thread, null, Bolt, null, null, null, null }, arena.Simulation.QuickBar);
        Assert.Null(arena.Submit(new AssignQuickSlotCommand(arena.Player, 5, null)));
        Assert.Equal(new string?[] { Ward, null, null, null, Salve, null, null, Bolt, null, null, null, null }, arena.Simulation.QuickBar);

        // Cleared, a formula that is still known is not put back while the character plays on.
        arena.Tick(20);
        Assert.DoesNotContain(Thread, arena.Simulation.QuickBar);
    }

    [Fact]
    public void OnlyAUsableItem_OrAFormulaOrTechniqueTheCharacterKnows_GoesOnTheBar()
    {
        using var profile = new TempProfile();
        var arena = Open(Harness.Boot(profile));
        arena.Tick();

        Assert.Equal($"{Bolt} is not known", arena.Submit(new AssignQuickSlotCommand(arena.Player, 3, Bolt)));
        Assert.Equal("item.weapon.rusted_sword has no use", arena.Submit(new AssignQuickSlotCommand(arena.Player, 3, "item.weapon.rusted_sword")));
        Assert.Equal("recipe.smithing.iron_billet cannot go on an action bar", arena.Submit(new AssignQuickSlotCommand(arena.Player, 3, "recipe.smithing.iron_billet")));
        Assert.Equal("there is no slot 13 on action bar 1", arena.Submit(new AssignQuickSlotCommand(arena.Player, 12, Salve)));
        Assert.Equal(new string?[] { Salve, null, null, null, null, null, null, null, null, null, null, null }, arena.Simulation.QuickBar);
    }

    [Fact]
    public void AnItemUsedUp_KeepsItsSlot()
    {
        using var profile = new TempProfile();
        var arena = Open(Harness.Boot(profile));
        arena.Tick();
        var salve = arena.Simulation.Player.Inventory.Single(e => e.DefId == Salve);
        Assert.Null(arena.Submit(new UseItemCommand(arena.Player, salve.ItemId)));
        arena.Tick();

        Assert.DoesNotContain(arena.Simulation.Player.Inventory, e => e.DefId == Salve);
        Assert.Equal(Salve, arena.Simulation.QuickBar[0]);   // none left: the slot keeps it, and the HUD shows it spent
    }

    [Fact]
    public void AnItemTakenOffTheBar_StaysOffWhileCarried_AndIsOfferedASlotAgainWhenNextPickedUp()
    {
        using var profile = new TempProfile();
        var arena = Open(Harness.Boot(profile));
        arena.Tick();
        Assert.Null(arena.Submit(new AssignQuickSlotCommand(arena.Player, 0, null)));
        arena.Tick(5);
        Assert.All(arena.Simulation.QuickBar, slot => Assert.Null(slot));

        string salve = arena.Simulation.Player.Inventory.Single(e => e.DefId == Salve).ItemId.Value;
        Assert.Null(arena.Submit(new MoveItemCommand(arena.Player, salve, ItemPlace.Carried, ItemPlace.Ground, 1)));
        arena.Tick();
        Assert.All(arena.Simulation.QuickBar, slot => Assert.Null(slot));
        Assert.Null(arena.Submit(new MoveItemCommand(arena.Player, salve, ItemPlace.Ground, ItemPlace.Carried, 1)));
        arena.Tick();

        Assert.Equal(Salve, arena.Simulation.QuickBar[0]);
    }

    /// <summary>
    /// The owner's rule for the bar (schema 23): a slot the player cleared stays cleared across a log-off and a log-on, though what it held
    /// is still carried - the bar remembers it was taken off on purpose - until the player puts it back.
    /// </summary>
    [Fact]
    public void ASlotClearedOnPurpose_StaysClearedAcrossALogin_UntilThePlayerPutsItBack()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        session.NewGame(Harness.Named("Wanderer"), seed: 7);
        Harness.Ticks(session, 2);
        Assert.Equal(Salve, session.Simulation!.QuickBar[0]);
        session.Submit(new AssignQuickSlotCommand(session.Simulation.PlayerId, 0, null));
        Harness.Ticks(session, 2);
        Assert.Equal(new[] { Salve }, session.Simulation.CaptureRecord().QuickBarCleared);
        session.Save(Persistence.SaveSlots.Quick);

        var relaunched = Harness.Boot(profile);
        relaunched.Continue();
        Harness.Ticks(relaunched, 5);
        var simulation = relaunched.Simulation!;
        Assert.Contains(simulation.CaptureRecord().Inventory, e => e.DefId == Salve);
        Assert.All(simulation.QuickBar, slot => Assert.Null(slot));

        relaunched.Submit(new AssignQuickSlotCommand(simulation.PlayerId, 2, Salve));
        Harness.Ticks(relaunched, 2);
        Assert.Equal(Salve, simulation.QuickBar[2]);
        Assert.Empty(simulation.CaptureRecord().QuickBarCleared);
    }

    /// <summary>A character from the creator (WP2's opening) starts with the bar's defaults: every usable thing in the kit on keys from 1.</summary>
    [Theory]
    [MemberData(nameof(CharacterCreationTests.Calls), MemberType = typeof(CharacterCreationTests))]
    public void ACharacterFromTheCreator_FindsTheKitOnTheQuickBar_FromKeyOne(string call)
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var simulation = session.NewGame(
            new CharacterSpec("Wenna Lind", Pronouns.She, CharacterSpec.DefaultPeople, "body.veth_f.lean", call) { Start = StartPoint.Road }, seed: 42);
        Harness.Ticks(session, 1);

        var usable = simulation.Player.Inventory.Select(e => e.DefId).Where(session.Setup.Combat.UseEffects.ContainsKey)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        Assert.NotEmpty(usable);
        Assert.Equal(usable.Concat(Enumerable.Repeat<string?>(null, QuickBar.Slots - usable.Count)), simulation.QuickBar);
        Assert.Empty(simulation.CaptureRecord().QuickBarCleared);
    }
}
