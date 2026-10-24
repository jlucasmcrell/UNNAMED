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
using UNNAMED.Domain.Travel;
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
    /// companion downed with a trail of its own, and the character in the air.
    /// </summary>
    private static PlayerRecord Full()
    {
        var player = M2Fixtures.Historical.Player();
        var vest = new InventoryEntry(Item(12), "item.armor.fixture_vest", 1)
        {
            Bag = 0, Slot = 1,
            // A masterwork with its maker's mark (schema 19).
            Quality = 2,
            Notable = new Domain.Items.NotableRecord("Fixture Vest", Domain.Items.ProvenanceKind.Crafted, "recipe.fixture.vest", 4_200) { Maker = "Aelin", Uses = 2 },
            // Worn a little (schema 24).
            Wear = new Domain.Items.ItemWear(12, 2),
        };
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
                        // Left waiting in an interior space, not their own ground (schema 30).
                        Space = "space.fixture.ring",
                        DownedTick = 4_990,
                        Trail = ImmutableArray.Create(new TrailMark(151_500, -38_500)),
                        // An ally along for a shared goal, told to keep out of fights and to hold healing for the injured (schema 21).
                        Membership = PartyMembership.TemporaryAlly,
                        Stance = CompanionStance.Passive,
                        Tactics = new PartyTactics(KeepToLeash: true, ProtectInjured: false, HoldHealing: true),
                    }))
            {
                Posture = new Posture(Stance.Crouched, Airborne: true, AirMs: 120),
                // Inside an interior space, come in by its portal (schema 30, the Barrowworks).
                SpaceId = "space.fixture.ring",
                SpaceTraversal = "portal.fixture.mouth",
                Waystones = ImmutableArray.Create(new WaystoneRecord("waystone.fixture.cairn", "region.fixture_vale", WaystoneMethod.Woken, 4_400)),
                ReturnPoint = "waystone.fixture.cairn",
                RoadsWalked = ImmutableArray.Create("road.fixture.track#2"),
                Deeds = player.Deeds,
                // Every look choice filled, so each moves the digest (schema 39).
                Identity = player.Identity with { Appearance = player.Identity.Appearance! with { FacialHair = "facehair.beard.goatee", Outfit = "outfit.casual.smart_casual" } },
                // Three acts, the last two told to no one, so a sequence can move alone and a row can name another act (schema 20).
                Factions = new Domain.Factions.FactionLedger(6,
                    player.Factions.Acts.AddRange(new[]
                    {
                        new Domain.Factions.ActRecord(2, Domain.Factions.ActKinds.SwitchSet, "world.lever.mill_gate", "r_0_0:c_00_07", 70_000, 3_000, 4_850),
                        new Domain.Factions.ActRecord(4, Domain.Factions.ActKinds.CreatureKilled, "creature.beast.wolf_grey", "r_0_0:c_00_02", 21_000, 4_000, 4_950),
                    }),
                    player.Factions.Knowledge, player.Factions.Standing.Add(new Domain.Factions.FactionStanding("faction.fixture.wilders", -40))),
                // Someone recovering from a grave wound, and a hireling who fell (schema 21).
                Party = new PartyLedger(ImmutableArray.Create(new RecoveryRecord("npc.fixture.healer", 9_000)), ImmutableArray.Create("npc.fixture.porter")),
                // A slot in use and something cleared off the bar (schema 23).
                QuickBar = ImmutableArray.Create<string?>("item.potion.minor_healing", null, null, "spell.ember.bolt", null, null, null, null, null, null, null, "gearset.set_2"),
                // The weapon family held, and a layout for it (the main bar itself) and for bare hands (schema 34).
                HeldFamily = "skill.one_hand_blade",
                FamilyBars = ImmutableSortedDictionary.Create<string, ImmutableArray<string?>>(StringComparer.Ordinal).Add("skill.one_hand_blade", ImmutableArray.Create<string?>("item.potion.minor_healing", null, null, "spell.ember.bolt", null, null, null, null, null, null, null, "gearset.set_2")).Add("unarmed", QuickBar.Empty.SetItem(0, "item.potion.minor_healing")),
                // The further bars and a gear set that wears two pieces, one of them the vest (schema 32).
                ExtraBars = ImmutableArray.Create(QuickBar.Empty.SetItem(5, "spell.ember.bolt"), QuickBar.Empty, QuickBar.Empty.SetItem(11, "item.potion.minor_healing"), QuickBar.Empty, QuickBar.Empty),
                // The satchel worn in a bag slot, and the vest in the second slot of the backpack (schema 33).
                Bags = player.Bags,
                GearSets = ImmutableArray.Create(new GearSet(2, "Vest and sword", ImmutableArray.Create(
                    new GearSetItem(EquipSlot.MainHand, player.Equipment[EquipSlot.MainHand], "item.weapon.iron_sword"),
                    new GearSetItem(EquipSlot.Chest, vest.ItemId, "item.armor.fixture_vest")))),
                QuickBarCleared = ImmutableArray.Create("item.food.roast_meat"),
                // A party member's sheet in every field: a filled progression, a gift and a loan worn, a notable thing of their own (schema 25).
                PartySheets = ImmutableArray.Create(new PartySheet("npc.fixture.scout", progression)
                {
                    Gear = ImmutableSortedDictionary.Create<EquipSlot, PartyGear>()
                        .Add(EquipSlot.MainHand, new PartyGear(new InventoryEntry(Item(7), "item.weapon.iron_sword", 1) { Quality = 1, Notable = player.Inventory.Single(e => e.Notable is not null).Notable, Wear = new Domain.Items.ItemWear(9, 1) }, Loaned: true))
                        .Add(EquipSlot.Head, new PartyGear(new InventoryEntry(Item(8), "item.armor.fixture_cap", 1) { Quality = -1, Notable = vest.Notable }, Loaned: false)),
                    Belongings = ImmutableArray.Create(new InventoryEntry(Item(9), "item.potion.minor_healing", 2) { Quality = 1 },
                        new InventoryEntry(Item(10), "item.weapon.fixture_knife", 1) { Notable = vest.Notable! with { Familiarity = 1 }, Wear = new Domain.Items.ItemWear(3, 1) },
                        new InventoryEntry(Item(11), "item.weapon.fixture_axe", 1) { Notable = player.Inventory.Single(e => e.Notable is not null).Notable }),
                }),
                // A swing under way that has struck two creatures (schema 27).
                Attack = new AttackRecord("item.weapon.iron_sword", 4_200, ImmutableArray.Create(
                    EntityId.Create(EntityKind.Creature, 1_700_000_000_101, new byte[] { 7, 7, 7, 7, 7, 7, 7, 7, 7, 1 }),
                    EntityId.Create(EntityKind.Creature, 1_700_000_000_102, new byte[] { 7, 7, 7, 7, 7, 7, 7, 7, 7, 2 }))),
                // An experiment that hinted and one that came to nothing (schema 31).
                Findings = ImmutableArray.Create(
                    new Domain.Crafting.Finding(
                        Domain.Crafting.FindingKind.Nothing, "station.fixture_hearth", "hearth", null, ImmutableArray<string>.Empty, 4_600),
                    new Domain.Crafting.Finding(
                        Domain.Crafting.FindingKind.Hint, "station.fixture_hearth", "hearth", "recipe.alchemy.salve_minor", ImmutableArray.Create("item.potion.minor_healing"), 4_800)),
                // A page each of two lore entries, found by the ways the world reports (schema 35).
                Lore = ImmutableArray.Create(
                    new Domain.Knowledge.LorePageRecord("lore.fixture_vale", 1, Domain.Knowledge.LoreSourceKind.Visited, "location.den_mouth", 4_600),
                    new Domain.Knowledge.LorePageRecord("lore.fixture_warden", 1, Domain.Knowledge.LoreSourceKind.Told, "dialogue.fixture.warden_sera", 4_700)),
                Pets = ImmutableArray.Create(
                    new World.PetRecord(EntityId.Create(EntityKind.Summon, 1_700_000_000_004, new byte[] { 9, 9, 9, 9, 9, 9, 9, 9, 9, 4 }),
                        "creature.beast.wolf_grey", "Wolf", Domain.Pets.PetRole.Control, Domain.Pets.PetOrder.WaitHere, Domain.Pets.PetCondition.Wounded,
                        148_750, -41_500, 45_000, 37, 44, ImmutableArray.Create(
                            new Domain.Pets.PetSkill(Domain.Pets.PetSkillKind.Bite, 2, 3),
                            new Domain.Pets.PetSkill(Domain.Pets.PetSkillKind.Lure, 1, 1)))
                    {
                        Space = "space.fixture.stable", TamedFromKey = "spawn.fixture.den#0", WaitXMm = 148_750, WaitZMm = -41_500, LastFedTick = 4_900, Level = 2,
                        WoundedTick = 4_950, RecoversAtTick = 149_000, AdoptedTick = 4_000,
                    }),            };
    }

    private static EntityId Item(byte n) => EntityId.Create(EntityKind.Item, 1_700_000_000_000 + n, new byte[] { 9, 9, 9, 9, 9, 9, 9, 9, 9, n });

    [Fact]
    public void CopyHelpers_PreserveThePlayersSpaceAndFindings()
    {
        var full = Full();
        PlayerRecord[] copies =
        [
            full.WithInventory(full.Inventory), full.WithProgression(full.Progression),
            full.WithDiscoveries(full.Discoveries), full.WithEffects(full.Effects),
            full.WithSocial(full.Relationships, full.Conversations), full.WithQuests(full.Quests),
            full.WithCompanions(full.Companions), full.WithDeeds(full.Deeds), full.WithFactions(full.Factions),
        ];
        foreach (var copy in copies)
        {
            Assert.Equal(full.SpaceId, copy.SpaceId);
            Assert.Equal(full.SpaceTraversal, copy.SpaceTraversal);
            Assert.Equal(full.Findings, copy.Findings);
            Assert.Equal(full.Lore, copy.Lore.ToArray());   // the Lore init re-sorts, so a copy holds a fresh array of the same pages
            Assert.Equal(full.Pets, copy.Pets.ToArray());   // and the Pets init sorts by instance, so the copy holds the same animals
            Assert.Equal(full.ExtraBars, copy.ExtraBars);
            Assert.Equal(full.HeldFamily, copy.HeldFamily);
            Assert.Equal(full.FamilyBars, copy.FamilyBars);
            Assert.Equal(full.GearSets, copy.GearSets);
            Assert.Equal(full.Digest, copy.Digest);
        }
    }

    [Fact]
    public void LoadingThroughDefinitionResolution_PreservesTheReturnPortal()
    {
        using var profile = new TempProfile();
        var store = new SaveStore(profile.Root);
        var player = M2Fixtures.Player("Brannoc").WithSpace("space.fixture.ring", "portal.fixture.mouth");
        var world = M2Fixtures.NewWorld(new UNNAMED.EntityRegistry.EntityRegistry());
        store.Save(M2Fixtures.Slot, SaveDocuments.Capture(world, player, M2Fixtures.Content(), worldTick: 123, playtimeSeconds: 0));
        var loaded = store.Load(M2Fixtures.Slot, M2Fixtures.Context(new UNNAMED.EntityRegistry.EntityRegistry(), M2Fixtures.Content("sha256:" + new string('a', 64))));
        Assert.Equal(player.SpaceId, loaded.Player.SpaceId);
        Assert.Equal(player.SpaceTraversal, loaded.Player.SpaceTraversal);
        Assert.Equal(player.Digest, loaded.Player.Digest);
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
            string.Join("\n", untouchable) + "\nALL: " + string.Join("|", reached.Keys));
    }

    /// <summary>
    /// Fields no change alone can reach, because the record's rules tie them to another: each is in the digest (read there by hand).
    /// </summary>
    private static readonly SortedDictionary<string, string> Coupled = new(StringComparer.Ordinal)
    {
        ["player.companions[].condition"] = "up or downed decides the health (0 when downed) and the downed tick (0 when up)",
        ["player.companions[].contract"] = "a whole contract, present exactly on a hireling (schema 21); each of its fields is perturbed where one is present",
        ["player.inventory[].notable"] = "a whole record, absent on an ordinary stack; each of its fields is perturbed where one is present",
        ["player.inventory[].wear"] = "a whole record, absent on an unworn stack; each of its fields is perturbed where one is present",
        ["player.party_sheets[].belongings[].notable"] = "a whole record, absent on an ordinary stack; each of its fields is perturbed where one is present",
        ["player.party_sheets[].belongings[].wear"] = "a whole record, absent on an unworn stack; each of its fields is perturbed where one is present",
        ["player.party_sheets[].gear[].item.wear"] = "a whole record, absent on an unworn stack; each of its fields is perturbed where one is present",
        ["player.posture.airborne"] = "in the air or not decides the air time (0 on the ground)",
        ["player.pets[].role"] = "one of the four role keys, so no other value decodes alone (schema 36)",
        ["player.pets[].skills[].skill"] = "one of the two skill keys, so no other value decodes alone (schema 36)",
    };

    /// <summary>
    /// The words saves store enums as - every <c>Key</c> of every enum the domain and the world write - and the relationship dimensions:
    /// what a field holding one of them can validly become.
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
        .Concat(UNNAMED.Domain.Factions.ActKinds.Built).Concat(UNNAMED.Domain.Factions.Identities.All).Concat(UNNAMED.Domain.Factions.KnowledgeSources.All)
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

    /// <summary>Where a stack lies in the character's pack (schema 33): a party member's things lie in no bag of the character's, and are saved without it.</summary>
    private static readonly HashSet<string> Placement = new(StringComparer.Ordinal) { "Bag", "Slot", "Locked", "Junk" };

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
        seen[path] = true;   // an optional record (a stack's notable record, schema 19) held by some instance
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                     .Where(p => p.GetIndexParameters().Length == 0 && !Computed.Contains(p.Name) && !(type == typeof(InventoryEntry) && path.Contains("PartySheets") && Placement.Contains(p.Name))))
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
