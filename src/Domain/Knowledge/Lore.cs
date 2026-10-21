// UNNAMED Domain - lore: what the character has come to know, and the pages a thing is known by (spec 41; MAPS_CARTOGRAPHY_AND_WORLD_KNOWLEDGE.md §3)
// No Godot references - pure C#

using System.Collections.Immutable;
using UNNAMED.Domain;

namespace UNNAMED.Domain.Knowledge;

/// <summary>The journal's headings: what a page can be about (spec 41 §3).</summary>
public enum LoreCategory
{
    Places,
    Peoples,
    Creatures,
    History,
    Faith,
}

public static class LoreCategories
{
    public static string Key(LoreCategory category) => category switch
    {
        LoreCategory.Places => "places",
        LoreCategory.Peoples => "peoples",
        LoreCategory.Creatures => "creatures",
        LoreCategory.History => "history",
        LoreCategory.Faith => "faith",
        _ => throw new ArgumentOutOfRangeException(nameof(category), category, "Unknown lore category"),
    };

    public static LoreCategory? Parse(string key) => key switch
    {
        "places" => LoreCategory.Places,
        "peoples" => LoreCategory.Peoples,
        "creatures" => LoreCategory.Creatures,
        "history" => LoreCategory.History,
        "faith" => LoreCategory.Faith,
        _ => null,
    };

    /// <summary>The journal's order of the headings.</summary>
    public static readonly ImmutableArray<LoreCategory> All =
        ImmutableArray.Create(LoreCategory.Places, LoreCategory.Peoples, LoreCategory.Creatures, LoreCategory.History, LoreCategory.Faith);
}

/// <summary>
/// How a page is learnt (spec 41 §2): a place walked into, an object read, an NPC asked, a quest finished, or a creature met. The kind is
/// the way the world reports it; <see cref="LoreSource.Ref"/> is what it names - a location's ID, a readable's key, the dialogue's ID,
/// a quest's ID, or a creature's definition ID.
/// </summary>
public enum LoreSourceKind
{
    Visited,
    Inspected,
    Told,
    Quest,
    Studied,
}

public static class LoreSources
{
    public static string Key(LoreSourceKind kind) => kind switch
    {
        LoreSourceKind.Visited => "visited",
        LoreSourceKind.Inspected => "inspected",
        LoreSourceKind.Told => "told",
        LoreSourceKind.Quest => "quest",
        LoreSourceKind.Studied => "studied",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown lore source"),
    };

    public static LoreSourceKind? Parse(string key) => key switch
    {
        "visited" => LoreSourceKind.Visited,
        "inspected" => LoreSourceKind.Inspected,
        "told" => LoreSourceKind.Told,
        "quest" => LoreSourceKind.Quest,
        "studied" => LoreSourceKind.Studied,
        _ => null,
    };
}

/// <summary>One way a page comes to the character: how, and what it names.</summary>
public sealed record LoreSource(LoreSourceKind Kind, string Ref);

/// <summary>One page of an entry: its words, and every discovery that opens it.</summary>
public sealed record LorePageDefinition(int Page, string Text, ImmutableArray<LoreSource> Sources);

/// <summary>
/// A lore entry (content kind <c>lore</c>): one thing the character can come to know about, under one heading, in pages that are found one
/// at a time. An entry's page count is what the journal's "Old Late 1/3" counts, so a page nobody has found is still a hint that there is more.
/// </summary>
public sealed record LoreEntryDefinition(string Id, string Title, LoreCategory Category, ImmutableArray<LorePageDefinition> Pages)
{
    public int PageCount => Pages.Length;

    public LorePageDefinition? PageAt(int page) => Pages.FirstOrDefault(p => p.Page == page);
}

/// <summary>
/// A page the character has unlocked (schema 35): which entry and page, what opened it, and when. The page's words are the content's - the
/// record holds only what was found, never a sentence - so lore can be rewritten without rewriting a save.
/// </summary>
public sealed record LorePageRecord(string EntryId, int Page, LoreSourceKind Source, string SourceRef, long Tick)
{
    public bool IsValid => DefinitionId.IsValid(EntryId) && EntryId.StartsWith("lore.", StringComparison.Ordinal)
        && Page >= 1 && Enum.IsDefined(Source) && !string.IsNullOrWhiteSpace(SourceRef) && Tick >= 0;
}

public static class LoreRules
{
    /// <summary>The page a page of prose must be within, in words, so a page fits the reading panel (spec 41 §1: 60-150).</summary>
    public const int MinWords = 40;
    public const int MaxWords = 190;

    /// <summary>
    /// The pages of an entry that this one discovery opens: those naming it as a source and not yet known. A page with several sources opens
    /// at the first of them, which is what makes an entry readable by more than one road.
    /// </summary>
    public static ImmutableArray<LorePageDefinition> Opening(LoreEntryDefinition entry, LoreSourceKind kind, string @ref,
        IEnumerable<LorePageRecord> known)
    {
        var have = known.Where(r => r.EntryId == entry.Id).Select(r => r.Page).ToHashSet();
        return entry.Pages.Where(p => !have.Contains(p.Page) && p.Sources.Any(s => s.Kind == kind && s.Ref == @ref)).ToImmutableArray();
    }

    /// <summary>The entries one discovery opens anything of: what a system walks after an event.</summary>
    public static IEnumerable<LoreEntryDefinition> EntriesOpening(IEnumerable<LoreEntryDefinition> entries, LoreSourceKind kind, string @ref) =>
        entries.Where(e => e.Pages.Any(p => p.Sources.Any(s => s.Kind == kind && s.Ref == @ref)));

    /// <summary>The pages known of one entry, oldest first: what the journal's detail reads.</summary>
    public static ImmutableArray<LorePageRecord> PagesOf(IEnumerable<LorePageRecord> known, string entryId) =>
        known.Where(r => r.EntryId == entryId).OrderBy(r => r.Page).ToImmutableArray();

    public static int WordCount(string text) =>
        text.Split(new[] { ' ', '\t', '\r', '\n', '\u2014' }, StringSplitOptions.RemoveEmptyEntries).Length;
}
