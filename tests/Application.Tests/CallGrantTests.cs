using UNNAMED.Domain.Character;
using UNNAMED.Domain.Progression;
using UNNAMED.Domain.Social;

namespace UNNAMED.Application.Tests;

/// <summary>
/// D14: a Call is a life, not a list of numbers. A new game started from the creator with a Call begins with the Call's two skills at 15, its kit,
/// its contact - and its starting techniques, learned with the starting-package source. Its contact can also recognise it: the Call's own line is
/// a choice on the contact's conversation, gated by the Call.
/// </summary>
public class CallGrantTests
{
    // Three Calls end to end, as the brief asks: a fighter, a scholar and a worker.
    public static readonly TheoryData<string> Calls = new() { "call.blade_for_hire", "call.scholar", "call.quarry_hand" };

    [Theory]
    [MemberData(nameof(Calls))]
    public void ANewGame_FromTheCreator_GrantsTheCallsTechniques_Skills_Kit_Contact_AndRecognition(string callId)
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var call = session.Characters.Calls[callId];
        var spec = new CharacterSpec("Wenna Lind", Pronouns.She, "people.veth", "body.veth_f.lean", callId);

        var simulation = session.NewGame(spec, seed: 42);
        var progression = simulation.CaptureRecord().Progression;

        // 1. Its starting techniques, learned as the starting package.
        Assert.NotEmpty(call.Package.Techniques);
        foreach (string technique in call.Package.Techniques)
        {
            Assert.True(progression.Known.TryGetValue(technique, out var known), $"{callId} did not learn {technique}");
            Assert.Equal(LearningSource.StartingPackage, known.Source);
        }

        // 2. Its two skills at 15.
        Assert.Equal(2, call.Package.Skills.Count);
        foreach (var (skill, level) in call.Package.Skills)
        {
            Assert.Equal(15, level);
            Assert.Equal(15, progression.Skills.TryGetValue(skill, out var state) ? state.Level : -1);
        }

        // 3. Its kit, in hand.
        var carried = simulation.Player.Inventory.Select(e => e.DefId).ToHashSet(StringComparer.Ordinal);
        foreach (var item in call.Kit)
            Assert.Contains(item.ItemId, carried);

        // 4. Its contact, and the recognition line that contact can say to this Call and no other.
        Assert.NotNull(call.Contact);
        Assert.NotNull(call.Recognition);
        var npc = session.Setup.Social.Npcs[call.Contact.NpcId];
        Assert.Equal(call.Recognition!.NpcId, call.Contact.NpcId);
        var dialogue = session.Setup.Social.Dialogues[npc.DialogueId!];
        var lines = dialogue.Nodes.Values.SelectMany(n => n.Choices)
            .Where(c => c.Conditions.OfType<CallCondition>().Any(x => x.CallId == callId))
            .ToArray();
        Assert.True(lines.Length > 0, $"{callId}: no choice on {npc.DialogueId} is gated by the Call");
        Assert.Contains(lines, c => c.Text == call.Recognition.Line);
    }
}
