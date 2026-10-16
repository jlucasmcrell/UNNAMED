using System.Collections.Immutable;
using UNNAMED.Domain.Quests;
using UNNAMED.World.Runtime;

namespace UNNAMED.Application.Tests;

/// <summary>
/// The playtest's closing beat: once the four story quests are done, Renn offers to write the character into his book, and the journal
/// gains a finished "End of the playtest" entry whose summary is the feedback text - words only.
/// </summary>
public class EndOfPlaytestTests
{
    private const string Renn = "npc.ashen_hollow.renn_vale";
    private const string End = "quest.ashen_hollow.end_of_playtest";
    private static readonly (double X, double Z) AtRenn = (48, 126.2);

    private static readonly string[] Story =
    {
        "quest.ashen_hollow.iron_under_ash", "quest.ashen_hollow.three_quiet_stones", "quest.cairnwold.iron_from_the_moor",
        "quest.cairnwold.what_the_bog_kept",
    };

    private static QuestState Done(GameSession session, string id)
    {
        var quest = session.Setup.Quests.Quests[id];
        return new QuestState(id, QuestStatus.Completed, 0, 0, quest.Order[^1].Id,
            quest.Order.Select(o => new ObjectiveState(o.Id, ObjectiveStatus.Satisfied, 0, 0, 0)).ToImmutableArray());
    }

    private static Arena WithDone(GameSession session, IEnumerable<string> done) =>
        Arena.OpenCreatures(session, session.Setup, AtRenn, 0, Array.Empty<(string, double, double, string)>(),
            r => r.WithQuests(done.Select(id => Done(session, id))));

    private static string[] RennsReplies(Arena arena)
    {
        Assert.Null(arena.Submit(new TalkCommand(arena.Player, Renn)));
        if (arena.Simulation.Conversation!.NodeId == "greet")
        {
            Assert.Null(arena.Submit(new ChooseCommand(arena.Player, "leave")));
            Assert.Null(arena.Submit(new TalkCommand(arena.Player, Renn)));
        }
        return arena.Simulation.Conversation!.Replies.Select(r => r.Id).ToArray();
    }

    [Fact]
    public void WithAStoryQuestStillOpen_RennHasNoFarewell()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        foreach (string missing in Story)
            Assert.DoesNotContain("farewell", RennsReplies(WithDone(session, Story.Where(q => q != missing))));
    }

    [Fact]
    public void AllFourDone_RennWritesYouIn_AndTheJournalSaysThePlaytestIsOver_Once()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = WithDone(session, Story);

        Assert.Contains("farewell", RennsReplies(arena));
        Assert.Null(arena.Submit(new ChooseCommand(arena.Player, "farewell")));
        Assert.Equal("farewell", arena.Simulation.Conversation!.NodeId);
        Assert.Null(arena.Submit(new ChooseCommand(arena.Player, "sign")));
        Assert.Null(arena.Submit(new ChooseCommand(arena.Player, "back")));
        arena.Tick(2);

        var end = arena.Simulation.Quests.Single(q => q.Id == End);
        Assert.Equal((QuestStatus.Completed, "End of the playtest"), (end.Status, end.Title));
        Assert.Contains("itch.io", end.Summary);
        Assert.Contains("Thank you", end.Summary);
        Assert.DoesNotContain("://", end.Summary);   // feedback text to read, never a link the game follows
        Assert.DoesNotContain("farewell", arena.Simulation.Conversation!.Replies.Select(r => r.Id));
    }
}
