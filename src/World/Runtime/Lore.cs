// UNNAMED World - lore: the pages a character finds, and what finding one is worth (spec 41; owner finding 41)
// No Godot references - pure C#

using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Domain.Knowledge;
using UNNAMED.Domain.Progression;

namespace UNNAMED.World.Runtime;

/// <summary>
/// Owns: <see cref="StateSlice.Lore"/>. One discovery opens the pages that name it - a landmark walked into, an object read, an NPC asked,
/// a quest finished, a creature met - and each page found is worth the discovery XP the way a landmark is (spec 41 §4). A page already found
/// opens nothing and earns nothing, so the same road walked twice is not a second harvest.
/// </summary>
internal sealed class LoreSystem
{
    /// <summary>What one page is worth: the discovery award a landmark of the smallest kind pays.</summary>
    public const long PageXp = 10;

    private readonly SystemContext _context;
    private readonly SliceOwner _owner;
    private readonly EntityId _player;

    public LoreSystem(SystemContext context, SliceOwner owner, EntityId player)
    {
        _context = context;
        _owner = owner;
        _player = player;
    }

    /// <summary>
    /// The discoveries the simulation reports itself. Read at an object or asked and answered, a page is opened by the command instead,
    /// because the content names the page there: a readable says which, and a reply that tells of a thing says which.
    /// </summary>
    public void Listen(SimulationEvents events)
    {
        events.Listen<LocationDiscovered>(e => _context.Dispatch(new UnlockLore(_player, LoreSourceKind.Visited, e.LocationId)));
        events.Listen<QuestCompleted>(e => _context.Dispatch(new UnlockLore(_player, LoreSourceKind.Quest, e.QuestId)));
        events.Listen<CreatureKilled>(e => _context.Dispatch(new UnlockLore(_player, LoreSourceKind.Studied, e.DefId)));
    }

    /// <summary>A discovery happened: every page naming it, on every entry, that is not yet known.</summary>
    public string? Handle(UnlockLore command, long tick)
    {
        if (command.Actor != _player)
            return $"unknown actor {command.Actor}";
        foreach (var entry in LoreRules.EntriesOpening(_context.Setup.Lore.Values, command.Source, command.SourceRef).ToList())
            foreach (var page in LoreRules.Opening(entry, command.Source, command.SourceRef, _context.State.Lore))
                File(entry, page, command.Source, command.SourceRef, tick);
        return null;
    }

    /// <summary>
    /// A reply told the character of something. Which page that is, the lore content says: the page of the entry whose sources name this
    /// conversation as the voice that tells it, so the words written down and the words spoken cannot drift apart.
    /// </summary>
    public string? Handle(TellLore command, long tick)
    {
        if (command.Actor != _player)
            return $"unknown actor {command.Actor}";
        if (!_context.Setup.Lore.TryGetValue(command.EntryId, out var entry))
            return $"{command.EntryId} is not a thing anyone wrote down";
        var told = entry.Pages.Where(p => p.Sources.Any(s => s.Kind == LoreSourceKind.Told && s.Ref == command.DialogueId)).ToList();
        if (told.Count == 0)
            return $"{command.EntryId} has no page told in {command.DialogueId}";
        foreach (var page in told.Where(p => _context.State.Lore.All(r => r.EntryId != entry.Id || r.Page != p.Page)))
            File(entry, page, LoreSourceKind.Told, command.DialogueId, tick);
        return null;
    }

    /// <summary>
    /// A book: every page of its entry the character has not read, in the order the content wrote them. Read at an object in the world, so
    /// its pages are opened as inspected.
    /// </summary>
    public string? Handle(ReadLore command, long tick)
    {
        if (command.Actor != _player)
            return $"unknown actor {command.Actor}";
        if (!_context.Setup.Lore.TryGetValue(command.EntryId, out var entry))
            return $"{command.EntryId} is not a thing anyone wrote down";
        var unknown = entry.Pages.Where(p => _context.State.Lore.All(r => r.EntryId != entry.Id || r.Page != p.Page)).ToList();
        if (unknown.Count == 0)
            return $"the {entry.Title} is already in your journal";
        foreach (var page in unknown)
            File(entry, page, command.Source, command.SourceRef, tick);
        return null;
    }

    private void File(LoreEntryDefinition entry, LorePageDefinition page, LoreSourceKind source, string sourceRef, long tick)
    {
        _context.State.SetLore(_owner, _context.State.Lore.Add(new LorePageRecord(entry.Id, page.Page, source, sourceRef, tick)));
        _context.Events.Publish(new LorePageUnlocked(entry.Id, page.Page, source, sourceRef, tick));
        _context.Dispatch(new AwardExperience(new XpAward(XpSource.Discovery, PageXp, tick)));
    }
}
