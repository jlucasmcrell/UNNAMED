// UNNAMED Content - chatter from content: the corpus, the templates, and the lint that keeps both sayable (WP16)
// No Godot references - pure C#

using System.Collections.Immutable;
using System.Globalization;
using System.Text.RegularExpressions;
using UNNAMED.World.Runtime;
using static UNNAMED.Content.CombatContent;

namespace UNNAMED.Content;

/// <summary>The chatter schema (<c>content/chatter/_schema.yaml</c>): the tags, slots, key formats and limits every chatter line is held to.</summary>
public sealed record ChatterSchema(
    ImmutableSortedSet<string> SpeakerTypes,
    ImmutableSortedDictionary<string, string> Places,
    ImmutableArray<TimeBand> TimeBands,
    ImmutableSortedSet<string> Weather,
    ImmutableSortedSet<string> Events,
    ImmutableSortedDictionary<string, ChatterSlot> Slots,
    ImmutableSortedDictionary<string, ChatterUse> Uses,
    Regex CorpusKey,
    Regex TemplateKey,
    int MaxChars,
    double NearDuplicateJaccard,
    int CoverageMinLines);

/// <summary>A placeholder slot: whether a corpus line may use it, and the events one of which a line using it must wait on.</summary>
public sealed record ChatterSlot(string Name, bool Corpus, ImmutableArray<string> RequiresEvent);

/// <summary>A template use: the channel its lines are said on and the slots its fact supplies.</summary>
public sealed record ChatterUse(string Name, ChatChannel Channel, ImmutableSortedSet<string> Slots);

/// <summary>A speaker x place x time-band cell of the corpus with fewer lines that can always be said than the schema asks for.</summary>
public sealed record ChatterCoverageGap(string SpeakerType, string Place, string TimeBand, int Lines);

/// <summary>
/// Builds chat from content and lints it (CHT001-CHT012). A chatter file is a definition of kind <c>chatter</c> holding a list of lines;
/// its meta files - <c>_schema.yaml</c>, <c>_glossary.yaml</c>, <c>_banned.yaml</c>, <c>_provenance.yaml</c> - sit beside it. The lint:
/// <list type="bullet">
/// <item>CHT001 the meta files and <c>config.chat</c> read; CHT002 each file and line is well formed and every key is unique;</item>
/// <item>CHT003 key formats; CHT004 tags the schema names; CHT005 placeholders - known, allowed, their events waited on;</item>
/// <item>CHT006 at most <see cref="ChatterSchema.MaxChars"/> characters; CHT007 no raw IDs; CHT008 proper nouns only from the glossary;</item>
/// <item>CHT009 no banned term (modern slang, brands, explicit words, interface words, the named NPCs' signature tics);</item>
/// <item>CHT010 no near-duplicates; CHT011 every corpus line <c>ai_generated: true</c> from a batch in the provenance file, every
/// template hand-written; CHT012 the glossary's references exist.</item>
/// </list>
/// Coverage of the speaker x place x time-band cells is a report (<see cref="Coverage"/>), not an error: the corpus comes in batches.
/// </summary>
public static partial class ChatterContent
{
    public const string Directory = "chatter";

    private static readonly string[] Roles = { "corpus", "templates" };

    [GeneratedRegex(@"\{([^{}]*)\}")]
    private static partial Regex Placeholder();

    [GeneratedRegex(@"\b[a-z][a-z0-9]*_[a-z0-9_]+\b")]
    private static partial Regex SnakeCase();

    [GeneratedRegex(@"\b(?:itm|npc|crt|bld|cnt|qst|crp|anc|smn|plt|wev|chr)_[0-9A-Za-z]{6,}\b")]
    private static partial Regex InstanceId();

    [GeneratedRegex(@"[A-Za-z][A-Za-z'’-]*")]
    private static partial Regex Word();

    [GeneratedRegex(@"^[0-9]{4}-[0-9]{2}-[0-9]{2}$")]
    private static partial Regex Date();

    [GeneratedRegex(@"^sha256:[0-9a-f]{64}$")]
    private static partial Regex PromptHash();

    [GeneratedRegex(@"^[a-z0-9_]+$")]
    private static partial Regex BatchId();

    public static IReadOnlyList<ValidationError> Validate(ContentLoader loader) => Lint(loader).Errors;

    /// <summary>The game's chat: the corpus and templates, the places and time bands, the tuning, and the names params may use.</summary>
    public static ChatSetup Build(ContentLoader loader)
    {
        var rules = BuildRules(loader);
        if (!HasChatter(loader))
            return ChatSetup.Empty with { Rules = rules };
        var schema = ReadSchema(loader);
        var lines = ReadLines(loader, schema, new List<ValidationError>());
        return new ChatSetup(lines.Where(l => !l.Line.IsTemplate).Select(l => l.Line).ToImmutableArray(),
            lines.Where(l => l.Line.IsTemplate).Select(l => l.Line).ToImmutableArray(), rules)
        {
            Places = schema.Places,
            TimeBands = schema.TimeBands,
            Names = WorldContent.DisplayNames(loader).ToImmutableSortedDictionary(StringComparer.Ordinal),
        };
    }

    /// <summary>How chat is tuned (<c>config.chat</c>); a pack without it has the defaults.</summary>
    public static ChatRules BuildRules(ContentLoader loader)
    {
        if (!loader.Definitions.ContainsKey("config.chat"))
            return ChatRules.Default;
        var map = Config(loader, "config.chat");
        return new ChatRules(Mm(map, "local_radius_m"), Int(map, "local_chance_percent"), Int(map, "world_chance_percent"),
            Int(map, "world_lines_per_hour"), Int(map, "speaker_repeat_hours"), Int(map, "region_repeat_hours"), Int(map, "recent_event_hours"),
            Int(map, "event_weight"), Int(map, "history_messages"), Mm(map, "join_radius_m"), Int(map, "offer_lookback_hours")).Validated();
    }

    /// <summary>Every lint finding, and the coverage gaps of the corpus.</summary>
    public static (IReadOnlyList<ValidationError> Errors, IReadOnlyList<ChatterCoverageGap> Gaps) Lint(ContentLoader loader)
    {
        var errors = new List<ValidationError>();
        try
        {
            BuildRules(loader);
        }
        catch (Exception e) when (e is FormatException or InvalidCastException or KeyNotFoundException or ArgumentException)
        {
            errors.Add(Error("CHT001", null, $"config.chat: {e.Message}"));
        }
        if (!HasChatter(loader))
            return (errors, Array.Empty<ChatterCoverageGap>());

        ChatterSchema schema;
        Glossary glossary;
        ImmutableSortedDictionary<string, ImmutableArray<string>> banned;
        ImmutableSortedSet<string> batches;
        try
        {
            schema = ReadSchema(loader);
            glossary = ReadGlossary(loader);
            banned = ReadBanned(loader);
            batches = ReadProvenance(loader);
        }
        catch (Exception e) when (e is FormatException or InvalidCastException or KeyNotFoundException or ArgumentException or IOException
                                      or YamlDotNet.Core.YamlException)
        {
            errors.Add(Error("CHT001", null, $"chatter meta files: {e.Message}"));
            return (errors, Array.Empty<ChatterCoverageGap>());
        }

        foreach (var (term, reference) in glossary.References)
        {
            if (!loader.Definitions.TryGetValue(reference, out var definition))
                errors.Add(Error("CHT012", null, $"the glossary's '{term}' names {reference}, which content does not define"));
            else if (Read(definition.YamlSource).GetValueOrDefault("name") is not string name || !name.Contains(term, StringComparison.Ordinal))
                errors.Add(Error("CHT012", null, $"the glossary's '{term}' is not in the name of {reference}"));
        }

        var lines = ReadLines(loader, schema, errors);
        var idPrefixes = ContentKindRegistry.Kinds.Values.Select(k => k.IdPrefix).Distinct().OrderBy(p => p, StringComparer.Ordinal).ToList();
        var rawId = new Regex(@"\b(?:" + string.Join("|", idPrefixes.Select(Regex.Escape)) + @")\.[a-z0-9_]", RegexOptions.CultureInvariant);
        var bannedPatterns = banned.SelectMany(c => c.Value.Select(term => (Category: c.Key, Term: term,
            Pattern: new Regex(@"(?<![A-Za-z])" + string.Join(@"[\s-]+", term.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(Regex.Escape)) + @"(?![A-Za-z])",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)))).ToList();

        foreach (var (line, definition) in lines)
        {
            string where = $"{definition.Id} {line.Key}";
            CheckTags(line, schema, where, definition, errors);
            CheckPlaceholders(line, schema, where, definition, errors);
            if (line.Text.Length > schema.MaxChars)
                errors.Add(Error("CHT006", definition, $"{where} is {line.Text.Length} characters; a line is at most {schema.MaxChars}"));
            string words = Placeholder().Replace(line.Text, " ");
            if (rawId.Match(words) is { Success: true } id1)
                errors.Add(Error("CHT007", definition, $"{where} says '{id1.Value}…', a raw content ID"));
            else if (SnakeCase().Match(words) is { Success: true } id2)
                errors.Add(Error("CHT007", definition, $"{where} says '{id2.Value}', which reads as an ID, not a word"));
            else if (InstanceId().Match(words) is { Success: true } id3)
                errors.Add(Error("CHT007", definition, $"{where} says '{id3.Value}', an instance ID"));
            foreach (string noun in ProperNouns(words).Where(n => !glossary.Allows(n)).Distinct(StringComparer.Ordinal))
                errors.Add(Error("CHT008", definition, $"{where} names '{noun}', which is not in the glossary (_glossary.yaml)"));
            foreach (var hit in bannedPatterns.Where(b => b.Pattern.IsMatch(words)))
                errors.Add(Error("CHT009", definition, $"{where} says '{hit.Term}' ({hit.Category.Replace('_', ' ')})"));
            if (!line.IsTemplate && (!line.AiGenerated || line.Batch is null || !batches.Contains(line.Batch)))
                errors.Add(Error("CHT011", definition, line.AiGenerated
                    ? $"{where} comes from batch '{line.Batch}', which _provenance.yaml does not record"
                    : $"{where} is a corpus line, so it is ai_generated: true from a recorded batch"));
            if (line.IsTemplate && line.AiGenerated)
                errors.Add(Error("CHT011", definition, $"{where} is a template; templates are hand-written (ai_generated: false)"));
        }

        foreach (var group in new[] { lines.Where(l => !l.Line.IsTemplate).ToList(), lines.Where(l => l.Line.IsTemplate).ToList() })
        {
            foreach (var (a, b, jaccard) in NearDuplicates(group.Select(l => l.Line).ToList(), schema.NearDuplicateJaccard))
                errors.Add(Error("CHT010", group.First(l => l.Line.Key == b.Key).Definition,
                    $"{b.Key} is a near-duplicate of {a.Key} (word-pair overlap {jaccard.ToString("0.00", CultureInfo.InvariantCulture)})"));
        }

        return (errors, Coverage(lines.Select(l => l.Line).Where(l => !l.IsTemplate), schema));
    }

    /// <summary>
    /// The corpus's thin cells: for each speaker type, place and time band, how many lines that ask for no event can be said there (any
    /// weather), where that is fewer than <see cref="ChatterSchema.CoverageMinLines"/>.
    /// </summary>
    public static IReadOnlyList<ChatterCoverageGap> Coverage(IEnumerable<ChatLine> corpus, ChatterSchema schema)
    {
        var always = corpus.Where(l => l.Events.IsEmpty).ToList();
        var gaps = new List<ChatterCoverageGap>();
        foreach (string speaker in schema.SpeakerTypes)
            foreach (string place in schema.Places.Keys)
                foreach (var band in schema.TimeBands)
                {
                    int count = always.Count(l => Fits(l.Speakers, speaker) && Fits(l.Places, place) && Fits(l.Times, band.Id));
                    if (count < schema.CoverageMinLines)
                        gaps.Add(new ChatterCoverageGap(speaker, place, band.Id, count));
                }
        return gaps;
    }

    /// <summary>Pairs of lines whose word-pair (bigram) sets overlap at or above the threshold, Jaccard; a line of one word compares its word.</summary>
    public static IEnumerable<(ChatLine A, ChatLine B, double Jaccard)> NearDuplicates(IReadOnlyList<ChatLine> lines, double threshold)
    {
        var grams = lines.Select(l => Bigrams(l.Text)).ToList();
        for (int i = 0; i < lines.Count; i++)
        {
            for (int j = i + 1; j < lines.Count; j++)
            {
                int small = Math.Min(grams[i].Count, grams[j].Count), large = Math.Max(grams[i].Count, grams[j].Count);
                if (large == 0 || (double)small / large < threshold)
                    continue;   // the overlap can be no larger than the smaller set
                int shared = grams[i].Count(grams[j].Contains);
                double jaccard = (double)shared / (grams[i].Count + grams[j].Count - shared);
                if (jaccard >= threshold)
                    yield return (lines[i], lines[j], jaccard);
            }
        }
    }

    private static HashSet<string> Bigrams(string text)
    {
        var words = Word().Matches(Placeholder().Replace(text.ToLowerInvariant(), " slot ")).Select(m => m.Value.Trim('\'', '’', '-')).Where(w => w.Length > 0).ToList();
        return words.Count < 2 ? words.ToHashSet(StringComparer.Ordinal) : words.Zip(words.Skip(1), (a, b) => a + " " + b).ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>The capitalised words of a text that do not begin a sentence: what a proper noun would be.</summary>
    public static IEnumerable<string> ProperNouns(string text)
    {
        bool sentenceStart = true;
        int last = 0;
        foreach (Match match in Word().Matches(text))
        {
            string between = text[last..match.Index];
            if (between.IndexOfAny(new[] { '.', '!', '?', ':', ';', '"', '“', '—' }) >= 0 || between.Contains(" - ", StringComparison.Ordinal))
                sentenceStart = true;
            last = match.Index + match.Length;
            string word = match.Value.TrimEnd('\'', '’', '-');
            bool capital = char.IsUpper(word[0]);
            if (capital && !sentenceStart && !IsPronounI(word))
                yield return Possessive(word);
            sentenceStart = false;
        }
    }

    private static bool IsPronounI(string word) => word is "I" || (word.Length > 2 && word[0] == 'I' && (word[1] == '\'' || word[1] == '’'));

    private static string Possessive(string word) =>
        word.EndsWith("'s", StringComparison.Ordinal) || word.EndsWith("’s", StringComparison.Ordinal) ? word[..^2] : word;

    private static bool Fits(ImmutableArray<string> tags, string value) => tags.IsEmpty || tags.Contains(value);

    // ---- reading ----

    private static bool HasChatter(ContentLoader loader) =>
        loader.GetByKind("chatter").Count > 0 || (loader.ContentRootPath is { } root && System.IO.Directory.Exists(Path.Combine(root, Directory)));

    private static Dictionary<object, object> Meta(ContentLoader loader, string file)
    {
        string path = Path.Combine(loader.ContentRootPath ?? ".", Directory, file);
        if (!File.Exists(path))
            throw new FormatException($"{Directory}/{file} is missing");
        return Read(File.ReadAllText(path));
    }

    public static ChatterSchema ReadSchema(ContentLoader loader) => ReadSchema(Meta(loader, "_schema.yaml"));

    public static ChatterSchema ReadSchema(Dictionary<object, object> map)
    {
        var limits = Map(map, "limits");
        var keys = Map(map, "line_key");
        var bands = Map(map, "time_bands").Select(b =>
        {
            var hours = b.Value as List<object> ?? throw new FormatException($"time band {b.Key} is [from, to]");
            return new TimeBand((string)b.Key, IntOf(hours, 0, $"time band {b.Key}"), IntOf(hours, 1, $"time band {b.Key}"));
        }).ToImmutableArray();
        for (int hour = 0; hour < 24; hour++)
        {
            int held = bands.Count(b => b.Contains(hour));
            if (held != 1)
                throw new FormatException($"hour {hour} falls in {held} time bands; every hour is in exactly one");
        }
        var events = Map(map, "events").Keys.Cast<string>().ToImmutableSortedSet(StringComparer.Ordinal);
        var slots = Map(map, "placeholders").Select(p =>
        {
            var slot = p.Value as Dictionary<object, object> ?? throw new FormatException($"placeholder {p.Key} is a map");
            var requires = slot.GetValueOrDefault("requires_event") switch
            {
                string one => ImmutableArray.Create(one),
                List<object> many => many.Cast<string>().ToImmutableArray(),
                _ => ImmutableArray<string>.Empty,
            };
            foreach (string e in requires.Where(e => !events.Contains(e)))
                throw new FormatException($"placeholder {p.Key} requires '{e}', which is not an event");
            return new ChatterSlot((string)p.Key, Flag(slot, "corpus"), requires);
        }).ToImmutableSortedDictionary(s => s.Name, s => s, StringComparer.Ordinal);
        var uses = Map(map, "templates").Select(t =>
        {
            var use = t.Value as Dictionary<object, object> ?? throw new FormatException($"template use {t.Key} is a map");
            var useSlots = List(use, "slots").Cast<string>().ToImmutableSortedSet(StringComparer.Ordinal);
            foreach (string s in useSlots.Where(s => !slots.ContainsKey(s)))
                throw new FormatException($"template use {t.Key} supplies '{s}', which is not a placeholder");
            return new ChatterUse((string)t.Key, ParseChannel(Text(use, "channel")) ?? throw new FormatException($"template use {t.Key} names no channel"), useSlots);
        }).ToImmutableSortedDictionary(u => u.Name, u => u, StringComparer.Ordinal);
        return new ChatterSchema(
            Map(map, "speaker_types").Keys.Cast<string>().ToImmutableSortedSet(StringComparer.Ordinal),
            Map(map, "places").ToImmutableSortedDictionary(p => (string)p.Key, p => (string)p.Value, StringComparer.Ordinal),
            bands,
            List(map, "weather").Cast<string>().ToImmutableSortedSet(StringComparer.Ordinal),
            events,
            slots,
            uses,
            new Regex(Text(keys, "corpus"), RegexOptions.CultureInvariant),
            new Regex(Text(keys, "template"), RegexOptions.CultureInvariant),
            Int(limits, "max_chars"),
            Number(limits, "near_duplicate_jaccard"),
            Int(limits, "coverage_min_lines"));
    }

    private sealed record Glossary(ImmutableHashSet<string> Words, ImmutableArray<(string Term, string Reference)> References)
    {
        public bool Allows(string word) => Words.Contains(word);
    }

    private static Glossary ReadGlossary(ContentLoader loader)
    {
        var words = new HashSet<string>(StringComparer.Ordinal);
        var references = new List<(string, string)>();
        foreach (var entry in List(Meta(loader, "_glossary.yaml"), "terms"))
        {
            var term = entry as Dictionary<object, object> ?? throw new FormatException("a glossary term is a map with a term");
            string text = Text(term, "term");
            foreach (Match word in Word().Matches(text))
                words.Add(Possessive(word.Value));
            if (term.GetValueOrDefault("ref") is string reference)
                references.Add((text, reference));
        }
        return new Glossary(words.ToImmutableHashSet(StringComparer.Ordinal), references.ToImmutableArray());
    }

    private static ImmutableSortedDictionary<string, ImmutableArray<string>> ReadBanned(ContentLoader loader) =>
        Map(Meta(loader, "_banned.yaml"), "categories").ToImmutableSortedDictionary(c => (string)c.Key,
            c => (c.Value as List<object> ?? new List<object>()).Cast<string>().Where(t => t.Trim().Length > 0).ToImmutableArray(), StringComparer.Ordinal);

    private static ImmutableSortedSet<string> ReadProvenance(ContentLoader loader)
    {
        var map = Meta(loader, "_provenance.yaml");
        var ids = new List<string>();
        foreach (var entry in map.GetValueOrDefault("batches") as List<object> ?? new List<object>())
        {
            var batch = entry as Dictionary<object, object> ?? throw new FormatException("a provenance batch is a map");
            string id = Text(batch, "id");
            if (!BatchId().IsMatch(id))
                throw new FormatException($"batch id '{id}' is lower-case letters, digits and underscores");
            Text(batch, "model");
            if (!Date().IsMatch(Text(batch, "date")))
                throw new FormatException($"batch {id}'s date is YYYY-MM-DD");
            if (!PromptHash().IsMatch(Text(batch, "prompt_hash")))
                throw new FormatException($"batch {id}'s prompt_hash is sha256:<64 hex digits>");
            if (ids.Contains(id))
                throw new FormatException($"batch {id} is recorded twice");
            ids.Add(id);
        }
        return ids.ToImmutableSortedSet(StringComparer.Ordinal);
    }

    /// <summary>Every line of every chatter file, with its file; a malformed line is reported (CHT002, CHT003) and left out.</summary>
    private static List<(ChatLine Line, ContentEnvelope Definition)> ReadLines(ContentLoader loader, ChatterSchema schema, List<ValidationError> errors)
    {
        var lines = new List<(ChatLine, ContentEnvelope)>();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var definition in loader.GetByKind("chatter").Values.OrderBy(d => d.Id, StringComparer.Ordinal))
        {
            var map = Read(definition.YamlSource);
            string? role = map.GetValueOrDefault("role") as string;
            if (role is null || !Roles.Contains(role))
            {
                errors.Add(Error("CHT002", definition, $"{definition.Id} role is corpus or templates"));
                continue;
            }
            string? batch = map.GetValueOrDefault("batch") as string;
            if (role == "corpus" && batch is null)
            {
                errors.Add(Error("CHT002", definition, $"{definition.Id} is corpus, so it names the batch its lines came from"));
                continue;
            }
            if (map.GetValueOrDefault("lines") is not List<object> entries)
            {
                errors.Add(Error("CHT002", definition, $"{definition.Id} holds a list of lines"));
                continue;
            }
            foreach (var entry in entries)
            {
                if (entry is not Dictionary<object, object> row || row.GetValueOrDefault("key") is not string key || row.GetValueOrDefault("text") is not string text)
                {
                    errors.Add(Error("CHT002", definition, $"{definition.Id}: every line has a key and a text"));
                    continue;
                }
                if (!keys.Add(key))
                {
                    errors.Add(Error("CHT002", definition, $"{definition.Id}: line key {key} is used twice"));
                    continue;
                }
                bool template = role == "templates";
                string? use = row.GetValueOrDefault("use") as string;
                if (template ? !schema.TemplateKey.IsMatch(key) : !schema.CorpusKey.IsMatch(key))
                {
                    errors.Add(Error("CHT003", definition, $"{definition.Id}: {key} is not a {(template ? "template" : "corpus")} key ({(template ? schema.TemplateKey : schema.CorpusKey)})"));
                    continue;
                }
                if (!template && key.Split('.')[1] != batch)
                {
                    errors.Add(Error("CHT003", definition, $"{definition.Id}: {key} is not of the file's batch {batch}"));
                    continue;
                }
                ChatChannel channel;
                if (template)
                {
                    if (use is null || !schema.Uses.TryGetValue(use, out var known))
                    {
                        errors.Add(Error("CHT004", definition, $"{definition.Id}: {key} uses '{use}', which is not a template use the schema names"));
                        continue;
                    }
                    if (!key.StartsWith(use + ".", StringComparison.Ordinal))
                    {
                        errors.Add(Error("CHT003", definition, $"{definition.Id}: {key} is a {use} template, so its key begins {use}."));
                        continue;
                    }
                    channel = known.Channel;
                }
                else
                {
                    string written = row.GetValueOrDefault("channel") as string ?? "local";
                    if (ParseChannel(written) is not { } parsed || parsed is not (ChatChannel.Local or ChatChannel.World))
                    {
                        errors.Add(Error("CHT004", definition, $"{definition.Id}: {key} is said on '{written}'; a corpus line is local or world"));
                        continue;
                    }
                    channel = parsed;
                }
                lines.Add((new ChatLine(key, text.Trim(), channel)
                {
                    Speakers = Tags(row, "speakers"),
                    Places = Tags(row, "places"),
                    Times = Tags(row, "times"),
                    Weather = Tags(row, "weather"),
                    Events = Tags(row, "events"),
                    Use = template ? use : null,
                    AiGenerated = row.GetValueOrDefault("ai_generated") is string flag && flag.Equals("true", StringComparison.OrdinalIgnoreCase),
                    Batch = template ? null : batch,
                }, definition));
            }
        }
        return lines;
    }

    private static void CheckTags(ChatLine line, ChatterSchema schema, string where, ContentEnvelope definition, List<ValidationError> errors)
    {
        void Each(ImmutableArray<string> tags, IReadOnlyCollection<string> known, string what)
        {
            foreach (string tag in tags.Where(t => !known.Contains(t)))
                errors.Add(Error("CHT004", definition, $"{where} is tagged {what} '{tag}', which the schema does not name"));
        }

        Each(line.Speakers, schema.SpeakerTypes, "speaker");
        Each(line.Places, schema.Places.Keys.ToList(), "place");
        Each(line.Times, schema.TimeBands.Select(b => b.Id).ToList(), "time");
        Each(line.Weather, schema.Weather, "weather");
        Each(line.Events, schema.Events, "event");
        if (line.IsTemplate && (line.Speakers.Length + line.Places.Length + line.Times.Length + line.Weather.Length + line.Events.Length) > 0)
            errors.Add(Error("CHT004", definition, $"{where} is a template: it is said for its fact, so it carries no tags"));
    }

    private static void CheckPlaceholders(ChatLine line, ChatterSchema schema, string where, ContentEnvelope definition, List<ValidationError> errors)
    {
        string bare = Placeholder().Replace(line.Text, string.Empty);
        if (bare.Contains('{') || bare.Contains('}'))
            errors.Add(Error("CHT005", definition, $"{where} has a brace that is not a {{slot}}"));
        foreach (Match match in Placeholder().Matches(line.Text))
        {
            string name = match.Groups[1].Value;
            if (!schema.Slots.TryGetValue(name, out var slot))
            {
                errors.Add(Error("CHT005", definition, $"{where} uses {{{name}}}, which is not a placeholder the schema names"));
                continue;
            }
            if (line.IsTemplate)
            {
                if (!schema.Uses[line.Use!].Slots.Contains(name))
                    errors.Add(Error("CHT005", definition, $"{where} uses {{{name}}}, which a {line.Use} fact does not supply"));
                continue;
            }
            if (!slot.Corpus)
                errors.Add(Error("CHT005", definition, $"{where} uses {{{name}}}, which only templates may use"));
            else if (!slot.RequiresEvent.IsEmpty && !line.Events.Any(slot.RequiresEvent.Contains))
                errors.Add(Error("CHT005", definition, $"{where} uses {{{name}}}, so it waits on {string.Join(" or ", slot.RequiresEvent)}"));
        }
    }

    private static ImmutableArray<string> Tags(Dictionary<object, object> row, string key) => row.GetValueOrDefault(key) switch
    {
        List<object> list => list.Select(t => t as string ?? string.Empty).ToImmutableArray(),
        string one when one.Length > 0 => ImmutableArray.Create(one),
        _ => ImmutableArray<string>.Empty,
    };

    private static bool Flag(Dictionary<object, object> map, string key) =>
        map.GetValueOrDefault(key) is string flag && flag.Equals("true", StringComparison.OrdinalIgnoreCase);

    private static ChatChannel? ParseChannel(string channel) => channel switch
    {
        "world" => ChatChannel.World,
        "local" => ChatChannel.Local,
        "party" => ChatChannel.Party,
        "system" => ChatChannel.System,
        _ => null,
    };

    private static ValidationError Error(string code, ContentEnvelope? definition, string message) => new()
    {
        SeverityLevel = ValidationError.Severity.Error,
        Code = code,
        Message = message,
        FilePath = definition?.SourceFile ?? string.Empty,
        DefinitionId = definition?.Id,
    };
}
