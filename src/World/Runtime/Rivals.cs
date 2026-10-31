// UNNAMED World - named adventurer rivals who race the character for competitive bounties (R12, CMP-122, CMP-33; SOCIAL 33)
// No Godot references - pure C#

using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Domain.Quests;
using UNNAMED.Domain.Social;

namespace UNNAMED.World.Runtime;

/// <summary>
/// A rival said something about the race (R12): which rival, over which posting, what the moment was, and the line in their people's voice.
/// Presentation shows it as a notice; nothing about the world changes because they spoke.
/// </summary>
public sealed record RivalTaunted(string NpcId, string Name, string QuestId, RivalMoment Moment, string Line, long Tick);

/// <summary>How one rival stands with the character: the tally, and what the tally has made of them (R12).</summary>
public sealed record RivalView(string NpcId, string Name, string People, int Wins, int Losses, RivalRegard Regard)
{
    /// <summary>How many postings the two have both gone after, one of them finishing each.</summary>
    public int Contests => Wins + Losses;
}

/// <summary>
/// Owns the rival race (R12): who is working the board, when each expects to finish what they took on, what they remember, and what they say.
///
/// The rivals are ordinary named NPCs carrying a <see cref="RivalProfile"/>, so a rival claims a posting through the same
/// <see cref="ClaimBountyCommand"/> everyone else uses: the race is scheduling, memory and voice, not a second rulebook.
///
/// Both memories live in world flags, so a save reproduces them without a state slice or a schema bump:
/// <c>rival.&lt;npc&gt;.&lt;quest&gt;.at</c> is the tick a rival expects to finish a posting (0 when it has not been worked out yet, which
/// also frees it to be worked out again once a posting is reposted), and <c>rival.&lt;npc&gt;.wins</c>, <c>.losses</c>, <c>.last_tick</c>
/// are the tally. Nothing is random: the schedule comes from the posting's own work, the rival's pace and the world seed, so two runs of the
/// same save race the same way.
/// </summary>
internal sealed class RivalSystem
{
    private readonly SystemContext _context;
    private readonly CellKey _flagCell;
    private readonly EntityId _player;
    private readonly Action<GameCommand> _enqueue;

    public RivalSystem(SystemContext context, CellKey flagCell, EntityId player, Action<GameCommand> enqueue)
    {
        _context = context;
        _flagCell = flagCell;
        _player = player;
        _enqueue = enqueue;
    }

    private RuntimeState State => _context.State;

    /// <summary>Every rival in the pack with an instance standing somewhere: who they are is definition data, what they remember is saved.</summary>
    private ImmutableArray<(string NpcId, string Name, EntityId Instance, RivalProfile Profile)> Rivals() =>
        State.Npcs.Values
            .Where(n => n.Definition.Rival is not null)
            .Select(n => (n.Definition.Id, n.Definition.Name, n.InstanceId, n.Definition.Rival!))
            .OrderBy(r => r.Item1, StringComparer.Ordinal)
            .ToImmutableArray();

    /// <summary>The rivals as the presentation names them: who they are and what they have made of the character so far.</summary>
    public ImmutableArray<RivalView> Views() => Rivals()
        .Select(r => new RivalView(r.NpcId, r.Name, r.Profile.People,
            Wins: (int)Flag(r.NpcId + ".wins"), Losses: (int)Flag(r.NpcId + ".losses"), Regard: Memory(r.NpcId).Regard))
        .ToImmutableArray();

    /// <summary>What one rival remembers of the races: read from the flags, so it survives a save.</summary>
    public RivalMemory Memory(string npcId) =>
        new(Wins: (int)Flag(npcId + ".wins"), Losses: (int)Flag(npcId + ".losses"), LastTick: Flag(npcId + ".last_tick"), LastQuestId: null);

    /// <summary>
    /// The race, once a tick. A rival that has not yet worked out a live posting does so and writes the tick down; a rival whose tick has come
    /// claims the posting through the ordinary claim command. A posting the character can no longer claim is not one to race for.
    /// </summary>
    public void Tick(long tick)
    {
        foreach (var (npcId, name, instance, profile) in Rivals())
        {
            foreach (var quest in _context.Setup.Quests.Quests.Values)
            {
                if (!quest.Competitive || !profile.Wants(quest.Id))
                    continue;
                if (!QuestRules.CanClaim(quest, State.Quests.GetValueOrDefault(quest.Id), tick))
                    continue;

                string key = npcId + "." + quest.Id + ".at";
                long at = Flag(key);
                if (at == 0)
                {
                    _context.Dispatch(new SetWorldFlag(_flagCell, "world.rival." + key,
                        RivalRules.AttemptTick(tick, WorkTicks(quest), profile, Seed(npcId, quest.Id))));
                    continue;
                }
                if (at > tick)
                    continue;

                // The tick has come: the rival goes for it through the ordinary claim path, and the claim event settles the race.
                _context.Dispatch(new SetWorldFlag(_flagCell, "world.rival." + key, 0));
                _enqueue(new ClaimBountyCommand(instance, quest.Id));
            }
        }
    }

    /// <summary>
    /// The posting was claimed: by the character, and every rival that wanted it remembers a loss and says so; by a rival, and that rival
    /// remembers the win and says so. Either way the race is settled by the claim itself, not by who asked for it.
    /// </summary>
    public void Claimed(string questId, string claimantId, long tick)
    {
        bool byCharacter = claimantId == _player.Value;
        foreach (var (npcId, name, instance, profile) in Rivals())
        {
            if (!profile.Wants(questId))
                continue;
            if (!byCharacter && instance.Value != claimantId)
                continue;
            Record(npcId, won: !byCharacter, questId, tick);
            Say(profile, npcId, name, questId, byCharacter ? RivalMoment.Bested : RivalMoment.Beaten, Memory(npcId), tick);
        }
    }

    private void Record(string npcId, bool won, string questId, long tick)
    {
        var after = RivalRules.After(Memory(npcId), won ? RivalOutcome.RivalFirst : RivalOutcome.PlayerFirst, questId, tick);
        _context.Dispatch(new SetWorldFlag(_flagCell, "world.rival." + npcId + ".wins", after.Wins));
        _context.Dispatch(new SetWorldFlag(_flagCell, "world.rival." + npcId + ".losses", after.Losses));
        _context.Dispatch(new SetWorldFlag(_flagCell, "world.rival." + npcId + ".last_tick", after.LastTick));
    }

    private void Say(RivalProfile profile, string npcId, string name, string questId, RivalMoment moment, RivalMemory memory, long tick)
    {
        if (RivalRules.TauntFor(profile, moment, memory) is { } line)
            _context.Events.Publish(new RivalTaunted(npcId, name, questId, moment, line, tick));
    }

    private long Flag(string suffix) => State.World.GetFlag(_flagCell, "world.rival." + suffix);

    /// <summary>How long a posting's work takes a capable adventurer: one unit per objective, at a fixed cost each.</summary>
    private static long WorkTicks(QuestDefinition quest) =>
        RivalRules.ShortestRaceTicks + Math.Max(1, quest.Objectives.Count) * 120;

    /// <summary>A rival's seed for one posting: the world's seed mixed with both names, so a posting races the same way every time.</summary>
    private ulong Seed(string npcId, string questId)
    {
        ulong h = State.World.WorldSeed;
        foreach (char c in npcId + "|" + questId)
            h = (h ^ c) * 1099511628211UL;
        return h;
    }
}
