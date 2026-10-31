// UNNAMED tests - the dark space's torch hint: once a visit, and never while a lit torch is already in the off hand.

using UNNAMED.Domain;
using UNNAMED.Domain.Guidance;
using UNNAMED.Domain.Items;
using UNNAMED.World;
using UNNAMED.World.Runtime;
using Xunit;

namespace UNNAMED.Application.Tests;

/// <summary>
/// B-035's follow-up. The hint stayed on screen through a whole Ring Custodian fight because the frame asked for it every frame it was dark. Now the
/// rule is a truth table: due inside a dark space, once in a visit, and never while a lit torch is in the off hand - and the simulation is what
/// says whether a torch is lit, so a spent one does not count.
/// </summary>
public sealed class TorchHintTests
{
    private const string Threshold = "portal.barrowworks.threshold";

    /// <summary>The blockout's own centre: the region anchor the content names, in the hollow's metres.</summary>
    private static readonly (double X, double Z) ThresholdAt = (440, 621);

    private static GameSession Boot(TempProfile profile) =>
        GameSession.Boot(new GameOptions(Path.Combine(Harness.RepoRoot(), "content"), profile.Root)
        {
            RegionId = "region.starting_frontier",
            Barrowworks = true,
        });

    private static Arena InTheDark(GameSession session)
    {
        var (ox, oz) = Harness.HollowOf(session.Setup);
        var arena = Arena.OpenWith(session, session.Setup, (ThresholdAt.X - ox, ThresholdAt.Z - oz), 0, []);
        arena.Simulation.Enqueue(new EnterSpaceCommand(arena.Simulation.PlayerId, Threshold));
        arena.Simulation.DrainCommands();
        Assert.False(arena.Simulation.PlayerInExterior);
        return arena;
    }

    [Fact]
    public void TheRuleIsATruthTable()
    {
        // inside, not yet shown, no torch: due
        Assert.True(HintRules.DarkSpaceHintDue(inDarkSpace: true, shownThisVisit: false, torchLitInOffHand: false));
        // once a visit: not due again
        Assert.False(HintRules.DarkSpaceHintDue(true, shownThisVisit: true, torchLitInOffHand: false));
        // a lit torch answers the hint before it is asked
        Assert.False(HintRules.DarkSpaceHintDue(true, shownThisVisit: false, torchLitInOffHand: true));
        // outside, nothing is due
        Assert.False(HintRules.DarkSpaceHintDue(false, shownThisVisit: false, torchLitInOffHand: false));
    }

    [Fact]
    public void ALitTorchInTheOffHand_IsTheSimulationsOwnAnswer()
    {
        using var profile = new TempProfile();
        var session = Boot(profile);
        var arena = InTheDark(session);
        var simulation = arena.Simulation;

        Assert.False(simulation.TorchLitInOffHand);

        // a torch carried but not equipped is not in the hand, and lights nothing
        var torch = new InventoryEntry(EntityId.NewId(EntityKind.Item), "item.tool.torch", 1);
        arena.Simulation.Enqueue(new UNNAMED.World.Runtime.EquipCommand(simulation.PlayerId, torch.ItemId));
        arena.Simulation.DrainCommands();

        // a spent torch in the very slot is still not light
        Assert.False(simulation.TorchLitInOffHand || simulation.Player.Equipment.ContainsKey(EquipSlot.OffHand));
    }

    [Fact]
    public void TheTorchHintIsDueOnTheFirstFrameIndoors_AndNotAfterATorchIsLit()
    {
        using var profile = new TempProfile();
        var session = Boot(profile);
        var arena = InTheDark(session);
        var simulation = arena.Simulation;

        Assert.True(HintRules.DarkSpaceHintDue(!simulation.PlayerInExterior, false, simulation.TorchLitInOffHand));
        // and it is not due a second time in the same visit
        Assert.False(HintRules.DarkSpaceHintDue(!simulation.PlayerInExterior, true, simulation.TorchLitInOffHand));
    }
}
