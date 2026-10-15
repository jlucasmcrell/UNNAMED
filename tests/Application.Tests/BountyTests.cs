using UNNAMED.Domain;
using UNNAMED.Domain.Items;
using UNNAMED.Domain.Quests;
using UNNAMED.World;
using UNNAMED.World.Runtime;

namespace UNNAMED.Application.Tests;

/// <summary>
/// The standing work (repeatable quests, <c>repeat_policy</c>): taken from its giver, paid on delivery, and posted again a world day
/// after it was done - never sooner.
/// </summary>
public class BountyTests
{
    private const string Renn = "npc.ashen_hollow.renn_vale";
    private const string Timber = "item.material.timber";
    private const string Roof = "quest.bounty.crossing_timber";

    // Where the character stands to speak to Renn (as NpcTests).
    private static readonly (double X, double Z) AtRenn = (48, 126.2);

    private static string? Say(Arena arena, string reply) => arena.Submit(new ChooseCommand(arena.Player, reply));

    private static string[] Replies(Arena arena) => arena.Simulation.Conversation!.Replies.Select(r => r.Id).ToArray();

    private static void ToTheNotices(Arena arena)
    {
        Assert.Null(arena.Submit(new TalkCommand(arena.Player, Renn)));
        if (arena.Simulation.Conversation!.NodeId == "greet")
        {
            Assert.Null(Say(arena, "leave"));
            Assert.Null(arena.Submit(new TalkCommand(arena.Player, Renn)));
        }
        Assert.Null(Say(arena, "notices"));
    }

    [Fact]
    public void RennsTimberWork_IsPaidOnDelivery_AndPostedAgainADayLater()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = Arena.OpenCreatures(session, session.Setup, AtRenn, 0, Array.Empty<(string, double, double, string)>(), r =>
            new PlayerRecord(r.Id, r.Name, r.XMm, r.YMm, r.ZMm, r.AppearanceSeed,
                r.Inventory.Append(new InventoryEntry(EntityId.NewId(EntityKind.Item), Timber, 12)), r.Progression, r.FacingMdeg, r.Discoveries,
                r.Equipment, r.Currency, r.Effects, r.Relationships, r.Conversations));
        var paid = arena.Record<RewardGranted>();
        long coin = arena.Simulation.CaptureRecord().Currency;

        ToTheNotices(arena);
        Assert.Contains("take_timber", Replies(arena));
        Assert.DoesNotContain("give_timber", Replies(arena));
        Assert.Null(Say(arena, "take_timber"));
        Assert.Null(Say(arena, "back"));
        Assert.Null(Say(arena, "notices"));
        Assert.DoesNotContain("take_timber", Replies(arena));   // taken: it is active, not available
        Assert.Null(Say(arena, "give_timber"));
        arena.Tick(2);

        Assert.Equal(QuestStatus.Completed, arena.Simulation.Quests.Single(q => q.Id == Roof).Status);
        Assert.Equal(new[] { "xp", "currency" }, paid.Where(p => p.QuestId == Roof).Select(p => p.Kind));
        Assert.Equal(coin + 8, arena.Simulation.CaptureRecord().Currency);
        Assert.Null(Say(arena, "back"));
        Assert.Null(Say(arena, "notices"));
        Assert.DoesNotContain("take_timber", Replies(arena));   // done today
        Assert.Null(Say(arena, "back"));
        Assert.Null(Say(arena, "leave"));

        arena.Tick((int)session.Setup.Quests.Quests[Roof].RepeatAfterTicks!.Value);
        ToTheNotices(arena);
        Assert.Contains("take_timber", Replies(arena));
        Assert.Null(Say(arena, "take_timber"));
        Assert.Equal(QuestStatus.Active, arena.Simulation.Quests.Single(q => q.Id == Roof).Status);
    }
}
