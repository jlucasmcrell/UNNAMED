using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Domain.Pets;
using UNNAMED.Domain.Spatial;
using UNNAMED.World;
using UNNAMED.World.Runtime;

namespace UNNAMED.Application.Tests;

/// <summary>
/// The road's own pack, met the way a captured run meets it: a wolf of the wayside pack offered food at arm's length and held to the channel.
/// Written because a captured run stopped dead at about this step - the engine's frame loop stopped - so the question is whether the world can
/// be driven into a state that does not tick. A test is where that question gets an answer without a window.
/// </summary>
public class TamingOnTheRoadTests
{
    private const string Wolf = "creature.beast.wolf_grey";
    private const string Meat = "item.consumable.roast_meat";

    [Fact]
    public void TheRoadsPack_CanBeOfferedFood_AndTheWorldGoesOnTicking()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        // The region's own wayside pack (content/spawns/road/wayside_pack.yaml: two roamers and a stray at (770, 692), level band 3), and the
        // character set down beside them rather than walked - reaching a moving pack is the capture harness's problem, not the world's.
        var arena = Arena.OpenCreatures(session, session.Setup, (768.0, 690.0), 90, Array.Empty<(string, double, double, string)>(),
            r => r.WithInventory(r.Inventory.Add(Arena.Stack(Meat, 3))), keepSpawns: true);

        var wolves = arena.Simulation.Creatures.Where(c => c.DefId == Wolf && c.Alive).ToList();
        Assert.NotEmpty(wolves);
        arena.Tick(2);

        var nearest = arena.Simulation.Creatures
            .Where(c => c.DefId == Wolf && c.Alive)
            .OrderBy(c => Math.Sqrt(Math.Pow(c.Body.XMm - arena.Simulation.Player.Body.XMm, 2) + Math.Pow(c.Body.ZMm - arena.Simulation.Player.Body.ZMm, 2)))
            .First();
        arena.Submit(new TameCommand(arena.Player, nearest.Id, Meat));

        // The channel runs out with the world ticking under it: whatever the roll says, the world is still a world afterwards, and the tick
        // count has really moved - a world that stops ticking is what the capture showed and this is the test that would catch it.
        arena.Tick((int)(arena.Simulation.PetRules.Tame.ChannelTicks + 200));
        Assert.True(arena.Simulation.WorldTick > 200, $"the world stopped ticking at {arena.Simulation.WorldTick}");
        Assert.NotEmpty(arena.Simulation.Creatures.Where(c => c.DefId == Wolf));
        Assert.InRange(arena.Simulation.Pets.Length, 0, 1);
    }
}
