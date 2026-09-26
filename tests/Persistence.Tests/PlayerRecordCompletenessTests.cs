using System.Collections;
using System.Collections.Immutable;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Text.Json.Serialization;
using MessagePack;
using UNNAMED.Domain;
using UNNAMED.Domain.Companions;
using UNNAMED.Domain.Items;
using UNNAMED.Domain.Progression;
using UNNAMED.Domain.Spatial;
using UNNAMED.M2Probe;
using UNNAMED.Persistence.Sections;
using UNNAMED.World;

namespace UNNAMED.Persistence.Tests;

/// <summary>
/// The Phase-1 technical audit, T-03: that every field of the player is saved was kept by hand, three times over - the record, its
/// digest, and the codec - and the round-trip test's player left most of them empty. Here a sample fills every field; the codec must
/// bring every one of them back; and every field the codec writes must move the digest. A field added to one and not the others fails.
/// </summary>
public class PlayerRecordCompletenessTests
{
    private static readonly MessagePackSerializerOptions Options = MessagePackSerializerOptions.Standard.WithSecurity(MessagePackSecurity.UntrustedData);

    /// <summary>
    /// The historical fixture's player - every schema's fields filled - and more, so no field is left at its default anywhere: a chest
    /// piece worn beside the main hand, a second attribute raised, a stamina pool part spent, a guard's day past the first, a second
    /// companion downed with a trail and an unreachable route of its own, the character in the air, and the fixture's faction ledger.
    /// </summary>
    private static PlayerRecord Full()
    {
        var player = M2Fixtures.Historical.Player();
        var vest = new InventoryEntry(EntityId.Create(EntityKind.Item, 1_700_000_000_003, new byte[] { 9, 9, 9, 9, 9, 9, 9, 9, 9, 3 }), "item.armor.fixture_vest", 1);
        var progression = player.Progression with
        {
            Allocation = player.Progression.Allocation.SetItem(CharacterAttribute.Precision, 2),
            Pools = player.Progression.Pools with { Stamina = 55 },
            Guards = player.Progression.Guards with
            {
                SpeciesToday = player.Progression.Guards.SpeciesToday.SetItem("creature.beast.wolf_grey", new SpeciesDay(2, 3)),
            },
        };
        return new PlayerRecord(player.Id, player.Name, player.XMm, player.YMm, player.ZMm, player.AppearanceSeed, player.Inventory.Add(vest), progression,
                player.FacingMdeg, player.Discoveries, player.Equipment.Add(EquipSlot.Chest, vest.ItemId), player.Currency, player.Effects,
                player.Relationships, player.Conversations, player.Quests, player.Companions.Add(
                    new CompanionRecord("npc.fixture.scout", CompanionOrder.Wait, CompanionCondition.Downed, 151_000, -39_000, 270_000, 0)
                    {
                        DownedTick = 4_990,
                        Trail = ImmutableArray.Create(new TrailMark(151_500, -38_500)),
                        Route = NavRoute.Unreachable(new NavPoint(152_000, -38_000), 4_985, 0x0F1E2D3C4B5A6978, new NavRect(131_000, -59_000, 173_000, -17_000)),
                    }))
            // Room above the last act, so an act's sequence can move alone.
            { Posture = new Posture(Stance.Crouched, Airborne: true, AirMs: 120), Factions = player.Factions with { NextActSeq = player.Factions.NextActSeq + 1 },
                Vitals = player.Vitals };
    }

    [Fact]
    public void TheSample_FillsEveryFieldOfThePlayer()
    {
        var seen = new SortedDictionary<string, bool>(StringComparer.Ordinal);
        Walk(Full(), typeof(PlayerRecord), "player", seen);

        Assert.True(seen.Count > 60, $"only {seen.Count} fields were walked");
        var empty = seen.Where(kv => !kv.Value).Select(kv => kv.Key).ToList();
        Assert.True(empty.Count == 0, "the sample leaves these fields at their defaults - fill them, so the tests below reach them:\n" + string.Join("\n", empty));
    }

    [Fact]
    public void TheCodec_BringsEveryFieldBack()
    {
        var full = Full();
        var decoded = SectionCodec.DecodePlayer(SectionCodec.EncodePlayer(full));

        Assert.Equal(Json(full), Json(decoded));
        Assert.Equal(full.Digest, decoded.Digest);
    }

    [Fact]
    public void EveryFieldTheCodecWrites_MovesTheDigest()
    {
        var full = Full();
        byte[] bytes = SectionCodec.EncodePlayer(full);
        var tree = MessagePackSerializer.Deserialize<object>(bytes, Options);
        var unmoved = new List<string>();
        var reached = new SortedDictionary<string, bool>(StringComparer.Ordinal);   // each field, instances folded: did any change decode?

        foreach (var (path, value) in LeavesOf(tree, "player"))
        {
            string field = Regex.Replace(path.ToString()!, @"\[\d+\]", "[]");
            reached.TryAdd(field, false);
            if (value is null)
                continue;   // an absent optional (an objective still open has no end); another instance holds a value
            // Each change is tried here alone, and a string also everywhere it appears: an item's ID is named by its equipment slot too.
            foreach (var (changed, everywhere) in Perturbations(value).Select(c => (c, false)).Concat(value is string ? Perturbations(value).Select(c => (c, true)) : []))
            {
                var copy = MessagePackSerializer.Deserialize<object>(bytes, Options);
                if (everywhere)
                    Replace(copy, value, changed);
                else
                    Set(copy, path, changed);
                PlayerRecord again;
                try
                {
                    again = SectionCodec.DecodePlayer(MessagePackSerializer.Serialize(copy, Options));
                }
                catch (Exception e) when (e is SaveException or ArgumentException or MessagePackSerializationException or FormatException or InvalidOperationException)
                {
                    continue;   // not a valid value there: try another
                }
                reached[field] = true;
                if (again.Digest == full.Digest)
                    unmoved.Add($"{path}: {value} -> {changed}");
                break;
            }
        }

        Assert.True(reached.Count > 60, $"only {reached.Count} encoded fields were perturbed");
        Assert.True(unmoved.Count == 0, "these encoded fields decode without moving the digest:\n" + string.Join("\n", unmoved));
        var untouchable = reached.Where(r => !r.Value).Select(r => r.Key).ToList();
        Assert.True(untouchable.SequenceEqual(Coupled.Keys),
            "no valid change alone was found for these encoded fields (a field that cannot change alone is named in Coupled, with why):\n" +
            string.Join("\n", untouchable));
    }

    /// <summary>
    /// Fields no change alone can reach, because the record's rules tie them to another: each is in the digest (read there by hand).
    /// </summary>
    private static readonly SortedDictionary<string, string> Coupled = new(StringComparer.Ordinal)
    {
        ["player.companions[].condition"] = "up or downed decides the health (0 when downed) and the downed tick (0 when up)",
        ["player.posture.airborne"] = "in the air or not decides the air time (0 on the ground)",
        ["player.companions[].route.status"] = "an active or unreachable route is built by its factory from its corners and goal; none has neither",
        ["player.factions.acts[].cell_key"] = "an act's cell is the cell of where it was done",
        ["player.factions.knowledge[].identity"] = "an act not yet identified moves no standing: its delta is 0 while unidentified",
    };

    /// <summary>
    /// The words saves store enums as - every <c>Key</c> of every enum the domain and the world write - the relationship dimensions, and
    /// the faction ledger's act kinds, sources and identities: what a field holding one of them can validly become.
    /// </summary>
    private static readonly string[] Words = typeof(DiscoveryMethods).Assembly.GetTypes().Concat(typeof(ProgressionKeys).Assembly.GetTypes())
        .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.Name == "Key" && m.ReturnType == typeof(string) && m.GetParameters() is [{ ParameterType.IsEnum: true }]))
        .SelectMany(m => Enum.GetValues(m.GetParameters()[0].ParameterType).Cast<object>().Select(v =>
        {
            try
            {
                return m.Invoke(null, new[] { v }) as string;
            }
            catch (TargetInvocationException)
            {
                return null;
            }
        }))
        .OfType<string>()
        .Concat(UNNAMED.Domain.Social.Relationships.Dimensions)
        .Concat(UNNAMED.Domain.Factions.ActKinds.Built).Concat(UNNAMED.Domain.Factions.KnowledgeSources.All).Concat(UNNAMED.Domain.Factions.Identities.All)
        .Distinct(StringComparer.Ordinal)
        .ToArray();

    private static void Replace(object? node, object from, object to)
    {
        switch (node)
        {
            case IDictionary<object, object> map:
                foreach (var key in map.Keys.ToList())
                {
                    if (Equals(map[key], from))
                        map[key] = to;
                    else
                        Replace(map[key], from, to);
                }
                break;
            case object[] array:
                for (int i = 0; i < array.Length; i++)
                {
                    if (Equals(array[i], from))
                        array[i] = to;
                    else
                        Replace(array[i], from, to);
                }
                break;
        }
    }

    // ── the record's fields ─────────────────────────────────────────────────

    private static readonly HashSet<string> Computed = new(StringComparer.Ordinal) { "Digest", "EqualityContract" };

    /// <summary>Every leaf field under a value, by path, and whether any instance of it in the sample holds something other than its default.</summary>
    private static void Walk(object? value, Type type, string path, IDictionary<string, bool> seen)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (IsLeaf(type))
        {
            seen[path] = seen.TryGetValue(path, out bool any) && any || !IsDefault(value, type);
            return;
        }
        if (value is null)
        {
            seen.TryAdd(path, false);
            return;
        }
        if (value is IEnumerable items and not string)
        {
            bool none = true;
            foreach (object? item in items)
            {
                none = false;
                if (item is not null && item.GetType().IsGenericType && item.GetType().GetGenericTypeDefinition() == typeof(KeyValuePair<,>))
                {
                    var pair = item.GetType();
                    Walk(pair.GetProperty("Key")!.GetValue(item), pair.GetGenericArguments()[0], path + "{key}", seen);
                    Walk(pair.GetProperty("Value")!.GetValue(item), pair.GetGenericArguments()[1], path + "{value}", seen);
                }
                else
                {
                    Walk(item, item?.GetType() ?? typeof(object), path + "[]", seen);
                }
            }
            seen[path + "[]"] = seen.TryGetValue(path + "[]", out bool held) && held || !none;   // some instance of the list holds something
            return;
        }
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                     .Where(p => p.GetIndexParameters().Length == 0 && !Computed.Contains(p.Name)))
            Walk(property.GetValue(value), property.PropertyType, $"{path}.{property.Name}", seen);
    }

    private static bool IsLeaf(Type type) =>
        type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal) || type == typeof(EntityId);

    private static bool IsDefault(object? value, Type type) => value is null || (type.IsValueType && value.Equals(Activator.CreateInstance(type)))
        || (value is string s && s.Length == 0);

    private static string Json(PlayerRecord player)
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        options.Converters.Add(new JsonStringEnumConverter());
        return JsonSerializer.Serialize(player, options);
    }

    // ── the encoded form ────────────────────────────────────────────────────

    /// <summary>Every leaf of a decoded MessagePack tree - maps, arrays and values - by its path of keys and indices.</summary>
    private static IEnumerable<(IReadOnlyList<object> Path, object? Value)> LeavesOf(object? node, string root)
    {
        var path = new List<object>();
        return Walk(node);

        IEnumerable<(IReadOnlyList<object>, object?)> Walk(object? at)
        {
            switch (at)
            {
                case IDictionary<object, object> map:
                    foreach (var key in map.Keys.ToList())
                    {
                        path.Add(key);
                        foreach (var leaf in Walk(map[key]))
                            yield return leaf;
                        path.RemoveAt(path.Count - 1);
                    }
                    break;
                case object[] array:
                    for (int i = 0; i < array.Length; i++)
                    {
                        path.Add(i);
                        foreach (var leaf in Walk(array[i]))
                            yield return leaf;
                        path.RemoveAt(path.Count - 1);
                    }
                    break;
                default:
                    yield return (new PathList(root, path), at);
                    break;
            }
        }
    }

    /// <summary>A path through the tree that prints as <c>player.inventory[1].count</c>.</summary>
    private sealed class PathList : List<object>
    {
        private readonly string _root;

        public PathList(string root, IEnumerable<object> steps) : base(steps) => _root = root;

        public override string ToString() => _root + string.Concat(this.Select(s => s is int i ? $"[{i}]" : $".{s}"));
    }

    private static void Set(object tree, IReadOnlyList<object> path, object value)
    {
        object at = tree;
        for (int i = 0; i < path.Count - 1; i++)
            at = at is IDictionary<object, object> map ? map[path[i]] : ((object[])at)[(int)path[i]];
        if (at is IDictionary<object, object> last)
            last[path[^1]] = value;
        else
            ((object[])at)[(int)path[^1]] = value;
    }

    /// <summary>Other values of the same kind, the likeliest to be valid first.</summary>
    private static IEnumerable<object> Perturbations(object value)
    {
        switch (value)
        {
            case bool b:
                yield return !b;
                break;
            case string s:
                yield return s + "x";
                // Another character, not the same one in the other case: an ID's ULID reads the same whatever its case.
                if (s.Length > 0)
                    yield return s[..^1] + (char.IsDigit(s[^1]) ? (s[^1] == '9' ? '8' : '9') : char.ToLowerInvariant(s[^1]) == 'b'
                        ? (char.IsUpper(s[^1]) ? 'C' : 'c')
                        : (char.IsUpper(s[^1]) ? 'B' : 'b'));
                yield return "x" + s;
                foreach (string word in Words.Where(w => w != s))
                    yield return word;
                break;
            case double d:
                yield return d + 1;
                yield return d - 1;
                break;
            case float f:
                yield return f + 1;
                yield return f - 1;
                break;
            case byte[] raw when raw.Length > 0:
                var flipped = (byte[])raw.Clone();
                flipped[^1] ^= 1;
                yield return flipped;
                break;
            case ulong u:
                yield return u + 1;
                if (u > 0)
                    yield return u - 1;
                break;
            case sbyte or byte or short or ushort or int or uint or long:
                long n = Convert.ToInt64(value);
                yield return n + 1;
                yield return n - 1;
                yield return n + 1_000;
                break;
        }
    }
}
