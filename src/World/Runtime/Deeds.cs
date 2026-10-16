// UNNAMED World - what the character has done: the deed tallies dialogue's deed condition reads (E-7)
// No Godot references - pure C#

using UNNAMED.Domain.Quests;
using UNNAMED.Domain.Social;

namespace UNNAMED.World.Runtime;

/// <summary>
/// Owns: <see cref="StateSlice.Deeds"/> - how many times the character has killed, crafted, harvested or delivered each thing, and
/// when last (saved with the player, schema 16). Every deed recorded for the quests is tallied here too, whether or not a quest was waiting for it.
/// </summary>
internal sealed class DeedSystem
{
    private readonly SystemContext _context;
    private readonly SliceOwner _owner;

    public DeedSystem(SystemContext context, SliceOwner owner)
    {
        _context = context;
        _owner = owner;
    }

    private RuntimeState State => _context.State;

    public string? Handle(RecordDeed command)
    {
        var deed = command.Deed;
        if (deed.Count <= 0)
            return null;
        string key = DeedKeys.TallyKey(deed.Kind, deed.Subject, deed.NpcId);
        int before = State.Deeds.TryGetValue(key, out var tally) ? tally.Count : 0;
        State.SetDeed(_owner, new DeedTally(deed.Kind, deed.Subject, deed.NpcId, (int)Math.Min(int.MaxValue, (long)before + deed.Count), deed.Tick));
        return null;
    }

    /// <summary>How many times the character has done this deed: to any NPC when <paramref name="npcId"/> is null.</summary>
    public static int Count(RuntimeState state, DeedKind kind, string subjectId, string? npcId) =>
        npcId is not null
            ? state.Deeds.TryGetValue(DeedKeys.TallyKey(kind, subjectId, npcId), out var tally) ? tally.Count : 0
            : (int)Math.Min(int.MaxValue, state.Deeds.Values.Where(t => t.Kind == kind && t.SubjectId == subjectId).Sum(t => (long)t.Count));
}
