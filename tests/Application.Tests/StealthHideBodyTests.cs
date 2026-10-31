using UNNAMED.Domain;
using UNNAMED.Domain.Creatures;
using UNNAMED.Domain.Items;
using UNNAMED.Domain.Spatial;
using UNNAMED.World;
using UNNAMED.World.Runtime;

namespace UNNAMED.Application.Tests;

/// <summary>
/// D23 S4's bodies (STEALTH.md section 6.3): carrying a body into cover. The smithy's shadow alcove hides a standing figure whole, so a body laid in
/// it is a body nobody finds - and the mechanic needs no hidden flag for that, only somewhere to put it.
/// </summary>
public class StealthHideBodyTests
{
    /// <summary>The smithy's shadow alcove (content/regions/starting_frontier.yaml): 100 per cent, 1.8 m tall, 2.5 m across.</summary>
    private const double ShadowX = 464, ShadowZ = 737;

    private static Func<PlayerRecord, PlayerRecord> Quiet => r =>
    {
        var inventory = r.Inventory.Where(e => e.DefId is "item.weapon.iron_dagger").ToList();
        if (inventory.Count == 0)
            inventory.Add(Arena.Stack("item.weapon.iron_dagger", 1));
        var dagger = inventory.First(e => e.DefId == "item.weapon.iron_dagger").ItemId;
        return new PlayerRecord(r.Id, r.Name, r.XMm, r.YMm, r.ZMm, r.AppearanceSeed, inventory, r.Progression, r.FacingMdeg,
            r.Discoveries, new[] { KeyValuePair.Create(EquipSlot.MainHand, dagger) }, r.Currency, r.Effects);
    };

    /// <summary>A wolf killed beside the alcove is carried into it; a body already in cover is not carried twice.</summary>
    [Fact]
    public void ABodyIsCarriedIntoCover_AndNotOutOfItTwice()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        // Three metres from the alcove, with the character behind the wolf so the blade is an execution.
        double wolfX = ShadowX, wolfZ = ShadowZ + 3.0;
        double facing = Arena.OpenCreatures(session, session.Setup, (wolfX, wolfZ + 1.5), 0,
            new[] { (Arena.Wolf, wolfX, wolfZ, "pack_hunter") }, Quiet, startTick: 12_000, origin: (0, 0))
            .Creature().Body.FacingMdeg / 1000.0 * Math.PI / 180;
        var arena = Arena.OpenCreatures(session, session.Setup, (wolfX - Math.Sin(facing) * 1.2, wolfZ - Math.Cos(facing) * 1.2),
            (int)Math.Round(facing * 180 / Math.PI), new[] { (Arena.Wolf, wolfX, wolfZ, "pack_hunter") }, Quiet,
            startTick: 12_000, origin: (0, 0));
        arena.Tick(10);
        Assert.Null(arena.Submit(new TakedownCommand(arena.Player)));
        arena.Tick(20);
        var corpse = arena.Simulation.Creatures.Single(c => c.DefId == Arena.Wolf);
        Assert.Equal(CreatureCondition.Corpse, corpse.Condition);
        double fellAtX = corpse.Body.XMm, fellAtZ = corpse.Body.ZMm;

        var hidden = arena.Record<BodyHidden>();
        Assert.Null(arena.Submit(new HideBodyCommand(arena.Player, corpse.Id)));
        var one = Assert.Single(hidden);
        Assert.Equal("cover.smithy_shadow", one.CoverKey);
        Assert.Equal(Arena.Wolf, one.DefId);

        // It lies in the alcove now, not where it fell - and the ground it fell on has no body on it any more.
        var laid = arena.Simulation.Creatures.Single(c => c.DefId == Arena.Wolf).Body;
        Assert.True(Math.Abs(laid.XMm - ShadowX * 1000) < 200 && Math.Abs(laid.ZMm - ShadowZ * 1000) < 200,
            $"the body is in the cover, not where it fell: {laid.XMm / 1000:0.0},{laid.ZMm / 1000:0.0}");
        Assert.True(Math.Abs(laid.ZMm - fellAtZ) > 2_000 || Math.Abs(laid.XMm - fellAtX) > 2_000, "it was carried somewhere else");

        // And a body already in cover is not something to carry again - refused either for being out of sight already, or for being out of reach
        // where it now lies, and both are the truth about a body that has been dealt with.
        string? again = arena.Submit(new HideBodyCommand(arena.Player, corpse.Id));
        Assert.NotNull(again);
        Assert.True(again!.Contains("out of sight") || again.Contains("nothing within reach"), again);
    }
}