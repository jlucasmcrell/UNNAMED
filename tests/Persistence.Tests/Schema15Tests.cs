using System.Collections.Immutable;
using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using MessagePack;
using UNNAMED.Domain.Factions;
using UNNAMED.Domain.Progression;
using UNNAMED.Domain.Spatial;
using UNNAMED.Domain;
using UNNAMED.M2Probe;
using UNNAMED.Persistence.Sections;
using UNNAMED.World;
using Registry = UNNAMED.EntityRegistry.EntityRegistry;

namespace UNNAMED.Persistence.Tests;

/// <summary>
/// Schema 15 (M7 design §7): the 14 -> 15 step's defaults; every new field required on decode - "corrupt, not defaulted" - for the
/// faction ledger, companion and errand routes, placed pieces, the structure sequence and NPC errands; the definition-ID pass over M7's
/// records (§7.10); and the guards no hand-listed copy drops a snapshot property (G11), no <c>With*</c> drops an init property (F-E4),
/// routes and ledgers compare by value after a decode (G28), and a captured save document holds nothing mutable (G29).
/// </summary>
public class Schema15Tests
{
    private static readonly MessagePackSerializerOptions Options = MessagePackSerializerOptions.Standard;

    private static PlayerDto PlayerDto() =>
        MessagePackSerializer.Deserialize<PlayerDto>(SectionCodec.EncodePlayer(M2Fixtures.Historical.Player()), Options);

    private static EntitiesSectionDto EntitiesDto() =>
        MessagePackSerializer.Deserialize<EntitiesSectionDto>(SectionCodec.EncodeEntities(M2Fixtures.Historical.World(new Registry()).TakeSnapshot()), Options);

    private static string PlayerRefused(Action<PlayerDto> damage)
    {
        var dto = PlayerDto();
        damage(dto);
        var bytes = MessagePackSerializer.Serialize(dto, Options);
        var e = Assert.ThrowsAny<Exception>(() => SectionCodec.DecodePlayer(bytes));
        Assert.True(e is FormatException or ArgumentException, $"{e.GetType().Name}: {e.Message}");
        return e.Message;
    }

    private static string EntitiesRefused(Action<EntitiesSectionDto> damage)
    {
        var dto = EntitiesDto();
        damage(dto);
        var bytes = MessagePackSerializer.Serialize(dto, Options);
        return Assert.Throws<FormatException>(() => SectionCodec.DecodeEntitySection(bytes)).Message;
    }

    // ── quarantine and row rejection (§7.13): each hash-valid but damaged section loads without crashing past the loader (L-04) ──

    /// <summary>The v15 fixture's player and world, saved; <paramref name="damage"/> then rewrites the entities section with a valid hash.</summary>
    internal static (TempProfile Profile, SaveStore Store) SavedFixture(Action<EntitiesSectionDto>? damage = null)
    {
        var profile = new TempProfile();
        var store = new SaveStore(profile.Root);
        store.Save(M2Fixtures.Slot, SaveDocuments.Capture(M2Fixtures.Historical.World(new Registry()), M2Fixtures.Historical.Player(),
            M2Fixtures.Content(), 5_000, 60));
        if (damage is not null)
        {
            string slot = store.SlotPath(M2Fixtures.Slot);
            var dto = MessagePackSerializer.Deserialize<EntitiesSectionDto>(File.ReadAllBytes(Path.Combine(slot, SaveFormat.Entities)), Options);
            damage(dto);
            Rewrite(slot, SaveFormat.Entities, MessagePackSerializer.Serialize(dto, Options));
        }
        return (profile, store);
    }

    /// <summary>Replace a section and its line in <c>sections.sha256</c>, so the damage passes the integrity check.</summary>
    private static void Rewrite(string slot, string file, byte[] bytes)
    {
        File.WriteAllBytes(Path.Combine(slot, file), bytes);
        string hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
        string root = Path.Combine(slot, SaveFormat.IntegrityRoot);
        var lines = File.ReadAllLines(root).Select(l => l.EndsWith("  " + file, StringComparison.Ordinal) ? $"{hash}  {file}" : l);
        File.WriteAllText(root, string.Join("\n", lines) + "\n");
    }

    internal static LoadResult LoadSaved(SaveStore store) => store.Load(M2Fixtures.Slot, M2Fixtures.Context(new Registry()));

    private static string PieceKey(long ordinal, EntityId owner) => M2Fixtures.Historical.PieceId(ordinal, owner).Value;

    [Fact]
    public void AQuarantinedEntitiesSection_LosesStructuresErrandsAndChestsTogether_AndSaysSo()
    {
        var (profile, store) = SavedFixture(dto => dto.Pieces![0].InstanceId = "not an id");
        using var _ = profile;
        var loaded = LoadSaved(store);

        Assert.Equal(new[] { "entities" }, loaded.QuarantinedSections);
        Assert.False(loaded.IsComplete);
        Assert.Empty(loaded.World.Pieces);
        Assert.Equal(0, loaded.World.StructureSequence);
        Assert.Null(loaded.World.NpcErrand("npc.fixture.smith"));
        Assert.DoesNotContain(loaded.World.TakeSnapshot().Containers, c => c.Key.StartsWith("container.pce_", StringComparison.Ordinal));
        Assert.Empty(loaded.World.TakeSnapshot().Creatures);
        Assert.Contains(loaded.Report.Loss.Concat(loaded.Report.Warnings), m => m.Contains("entities", StringComparison.Ordinal));
        Assert.Equal(M2Fixtures.Historical.Player().Digest, loaded.Player.Digest);   // the player is whole
    }

    [Fact]
    public void AnInvalidPieceRow_IsRejectedAlone()
    {
        string foreign = PieceKey(9, M2Fixtures.Historical.ForeignOwner);
        var cases = new (Action<PieceDto> Damage, string Reason)[]
        {
            (p => p.Rotation = 4, "rotation 4 is not a quarter turn 0-3"),
            (p => p.Health = 0, "health 0 is below 1"),
            (p => p.HostCell = "r_0_0:c_00_05", "host cell r_0_0:c_00_05 is not the cell of its anchor"),
            (p => p.InstanceId = M2Fixtures.Historical.PieceId(10, M2Fixtures.Historical.ForeignOwner).Value, "ordinal 10 is outside 1..9"),
            (p => p.InstanceId = PieceKey(1, M2Fixtures.PlayerId), "appears twice"),
        };
        foreach (var (damage, reason) in cases)
        {
            var (profile, store) = SavedFixture(dto => damage(dto.Pieces!.Single(p => p.InstanceId == foreign)));
            using var _ = profile;
            var loaded = LoadSaved(store);

            Assert.Empty(loaded.QuarantinedSections);
            var rejected = Assert.Single(loaded.RejectedRecords);
            Assert.Contains(reason, rejected.Reason, StringComparison.Ordinal);
            Assert.Contains(loaded.Report.Warnings, w => w.StartsWith("invalid entities record '", StringComparison.Ordinal) && w.Contains(reason, StringComparison.Ordinal));
            Assert.Equal(5, loaded.World.Pieces.Count);   // the other five, the chest and the errand stand
            Assert.NotNull(loaded.World.NpcErrand("npc.fixture.smith"));
            Assert.Contains(loaded.World.TakeSnapshot().Containers, c => c.Key.StartsWith("container.pce_", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void AnInvalidErrandRow_IsRejectedAlone()
    {
        string pad = PieceKey(1, M2Fixtures.PlayerId);
        var cases = new (Action<NpcErrandDto> Damage, string Reason)[]
        {
            (e => { e.Phase = "at_work"; e.PieceId = PieceKey(4, M2Fixtures.PlayerId); }, "is not placed"),
            (e => { e.Phase = "at_work"; e.PieceId = PieceKey(9, M2Fixtures.Historical.ForeignOwner); }, "is not the owner of"),
            (e => e.PieceId = pad, "does not match its piece and owner"),
            (e => e.FacingMdeg = 360_000, "its facing or stuck count is out of range"),
            (e => e.StuckTicks = -1, "its facing or stuck count is out of range"),
        };
        foreach (var (damage, reason) in cases)
        {
            var (profile, store) = SavedFixture(dto => damage(Assert.Single(dto.NpcErrands!)));
            using var _ = profile;
            var loaded = LoadSaved(store);

            Assert.Empty(loaded.QuarantinedSections);
            var rejected = Assert.Single(loaded.RejectedRecords);
            Assert.Contains(reason, rejected.Reason, StringComparison.Ordinal);
            Assert.Null(loaded.World.NpcErrand("npc.fixture.smith"));
            Assert.Equal(6, loaded.World.Pieces.Count);
        }

        // A second worker at one piece: the second is rejected, the first stays.
        var (twoProfile, twoStore) = SavedFixture(dto =>
        {
            var first = dto.NpcErrands![0];
            first.Phase = "at_work";
            first.PieceId = pad;
            var second = MessagePackSerializer.Deserialize<NpcErrandDto>(MessagePackSerializer.Serialize(first, Options), Options);
            second.NpcId = "npc.fixture.warden_sera";
            dto.NpcErrands = new[] { first, second };
        });
        using var two = twoProfile;
        var both = LoadSaved(twoStore);
        Assert.Contains("already has a worker", Assert.Single(both.RejectedRecords).Reason, StringComparison.Ordinal);
        Assert.NotNull(both.World.NpcErrand("npc.fixture.smith"));
    }

    [Fact]
    public void APieceChestWithoutItsPiece_IsRejected()
    {
        string chest = PieceKey(7, M2Fixtures.PlayerId);
        var (profile, store) = SavedFixture(dto => dto.Pieces = dto.Pieces!.Where(p => p.InstanceId != chest).ToArray());
        using var _ = profile;
        var loaded = LoadSaved(store);

        var rejected = Assert.Single(loaded.RejectedRecords);
        Assert.Equal(("entities", "container." + chest.ToLowerInvariant(), "its chest is gone"), (rejected.Section, rejected.Key, rejected.Reason));
        Assert.Equal(5, loaded.World.Pieces.Count);
    }

    [Fact]
    public void APieceChestWithTheWrongIdentity_IsRejected()
    {
        string chest = "container." + PieceKey(7, M2Fixtures.PlayerId).ToLowerInvariant();
        var wrong = EntityId.Derived(EntityKind.Container, 7, "unnamed.piece-container/v1", PieceKey(1, M2Fixtures.PlayerId));
        var (profile, store) = SavedFixture(dto => dto.Containers!.Single(c => c.Key == chest).InstanceId = wrong.Value);
        using var _ = profile;
        var loaded = LoadSaved(store);

        var rejected = Assert.Single(loaded.RejectedRecords);
        Assert.Equal((chest, "not its chest's identity"), (rejected.Key, rejected.Reason));
        Assert.Equal(6, loaded.World.Pieces.Count);
        Assert.Null(loaded.World.Container(chest));
    }

    [Fact]
    public void ACellsQuarantine_LeavesPiecesAndErrandsWhole()
    {
        var (profile, store) = SavedFixture();
        using var _ = profile;
        Tamper.FlipByte(Path.Combine(store.SlotPath(M2Fixtures.Slot), SaveFormat.Cells));
        var loaded = LoadSaved(store);

        Assert.Equal(new[] { "cells" }, loaded.QuarantinedSections);
        Assert.Empty(loaded.RejectedRecords);
        Assert.Equal(6, loaded.World.Pieces.Count);
        Assert.Equal(9, loaded.World.StructureSequence);
        Assert.True(loaded.World.Piece(M2Fixtures.Historical.PieceId(3, M2Fixtures.PlayerId))!.DoorOpen);
        Assert.NotNull(loaded.World.NpcErrand("npc.fixture.smith"));
        Assert.Equal(0, loaded.World.GetFlag(CellKey.Parse("r_0_0:c_00_00"), "world.door.cellar_open"));   // an authored door's flag is lost, as ever
    }

    [Fact]
    public void Schema14To15_GivesNothingBuiltNoLedgerAndNoRoutes()
    {
        using var profile = Fixtures.Copy(14);
        var store = new SaveStore(profile.Root);
        var report = store.Migrate(SaveSlots.Quick, Fixtures.Context(new Registry()));

        Assert.Equal(new[] { "schema 14 -> 15:", "schema 15 -> 16:", "schema 16 -> 17:" }, report.Steps.Select(s => s[..16]));
        string slot = store.SlotPath(SaveSlots.Quick);
        var entities = SectionCodec.DecodeEntitySection(File.ReadAllBytes(Path.Combine(slot, SaveFormat.Entities)));
        var player = SectionCodec.DecodePlayer(File.ReadAllBytes(Path.Combine(slot, SaveFormat.Player)));

        // Asserted empty.
        Assert.Empty(entities.Pieces);
        Assert.Empty(entities.NpcErrands);
        Assert.Equal(0, entities.StructureSequence);
        Assert.Equal(FactionLedger.Empty, player.Factions);
        Assert.Equal(NavRoute.None, Assert.Single(player.Companions).Route);

        // Asserted kept: everything schema 14 held.
        var warden = player.Companions[0];
        Assert.Equal(3, warden.Trail.Length);
        Assert.Equal(Domain.Spatial.Stance.Crouched, player.Posture.Stance);
        Assert.Equal(2, player.Quests.Length);
        Assert.Contains(entities.Containers, c => c.Key == "corpse.fixture_den.m1_g0");
        var den = entities.Creatures.Single(c => c.Key == "spawn.fixture.den#0");
        Assert.Equal((5_060L, 5_020L, (long?)4_995, 40), (den.NextChargeTick, den.StaggerImmuneUntil, den.StaggeredTick, den.StaggerLastsTicks));
        Assert.Equal(2, entities.Noises.Length);

        // Asserted not reconstructed: a wolf corpse and a set lever are no act.
        Assert.Contains(entities.Creatures, c => c.Condition == CreatureCondition.Corpse);
        Assert.Empty(player.Factions.Acts);
    }

    [Fact]
    public void ASchema15PlayerWithoutFactions_IsCorrupt_NotDefaulted()
    {
        Assert.Contains("factions", PlayerRefused(dto => dto.Factions = null));
        Assert.Contains("identity", PlayerRefused(dto => dto.Factions!.Knowledge[0].Identity = "suspected"));
        PlayerRefused(dto => dto.Factions!.Acts = dto.Factions.Acts.Reverse().ToArray());
        PlayerRefused(dto => dto.Factions!.Acts[0].CellKey = "r_0_0:c_00_03");
        PlayerRefused(dto => dto.Factions!.Standing[0].Points = 0);
        Assert.Contains("kind", PlayerRefused(dto => dto.Factions!.Acts[0].Kind = "piece_placed"));
    }

    [Fact]
    public void ASchema15CompanionWithoutARoute_IsCorrupt_NotDefaulted()
    {
        Assert.Contains("route", PlayerRefused(dto => dto.Companions![0].Route = null));
        Assert.Contains("wandering", PlayerRefused(dto => dto.Companions![0].Route!.Status = "wandering"));
        PlayerRefused(dto => dto.Companions![0].Route!.CornersMm = dto.Companions[0].Route!.CornersMm[..^1]);
        PlayerRefused(dto => dto.Companions![0].Route!.CornersMm = Enumerable.Range(0, 66).Select(n => (long)n).ToArray());
        PlayerRefused(dto => dto.Companions![0].Route!.WatchMm = new long[3]);
        PlayerRefused(dto => dto.Companions![0].Route!.CornersMm = Array.Empty<long>());
    }

    [Fact]
    public void ASchema15EntitiesSectionWithoutPiecesSeqOrErrands_IsCorrupt_NotDefaulted()
    {
        Assert.Contains("pieces", EntitiesRefused(dto => dto.Pieces = null));
        Assert.Contains("structure_seq", EntitiesRefused(dto => dto.StructureSeq = null));
        Assert.Contains("npc_errands", EntitiesRefused(dto => dto.NpcErrands = null));
        Assert.Contains("structure_seq", EntitiesRefused(dto => dto.StructureSeq = -1));
        EntitiesRefused(dto => dto.Pieces![0].Owner = "not an id");
        EntitiesRefused(dto => dto.NpcErrands![0].Phase = "idling");
    }

    [Fact]
    public void AnErrandWithoutARoute_IsCorrupt_NotDefaulted()
    {
        Assert.Contains("route", EntitiesRefused(dto => dto.NpcErrands![0].Route = null));
        EntitiesRefused(dto =>
        {
            dto.NpcErrands![0].Route!.Status = "unreachable";
            dto.NpcErrands[0].Route!.Partial = false;
        });
    }

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    private static string Of(object? value) => JsonSerializer.Serialize(value, Json);

    // G11
    [Fact]
    public void EveryDeltaSnapshotProperty_SurvivesDecodeTheDefinitionPassAndTheRebase()
    {
        var world = M2Fixtures.Historical.World(new Registry());
        var snapshot = world.TakeSnapshot();
        var properties = typeof(DeltaSnapshot).GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.Name != "EqualityContract").ToList();
        foreach (var property in properties)
        {
            object? value = property.GetValue(snapshot);
            bool filled = value switch { IEnumerable items => items.Cast<object>().Any(), long n => n != 0, _ => value is not null };
            Assert.True(filled, $"DeltaSnapshot.{property.Name} is empty in the fixture: extend the builder, so this guard reaches it");
        }

        void Same(DeltaSnapshot expected, DeltaSnapshot actual, string after)
        {
            foreach (var property in properties)
                Assert.True(Of(property.GetValue(expected)) == Of(property.GetValue(actual)), $"{after} changed DeltaSnapshot.{property.Name}");
        }

        // The decode.
        var decoded = SectionCodec.DecodeEntitySection(SectionCodec.EncodeEntities(snapshot)) with { Cells = SectionCodec.DecodeCells(SectionCodec.EncodeCells(snapshot)) };
        Same(snapshot, decoded, "the decode");

        // The definition-ID pass, under content whose hash differs and which defines every ID the save names.
        var player = M2Fixtures.Historical.Player();
        var named = NamedIds(SectionCodec.EncodePlayer(player)).Concat(NamedIds(SectionCodec.EncodeEntities(snapshot))).Concat(NamedIds(SectionCodec.EncodeCells(snapshot)));
        var content = new ContentIdentity("g11", "sha256:" + string.Concat(Enumerable.Repeat("ef", 32)), named);
        var report = new MigrationReport("g11");
        var (passedPlayer, passed) = SaveLoader.ResolveDefinitions(player, decoded, content, report);
        Assert.Empty(report.Blockers);
        Assert.Empty(report.Loss);
        Same(snapshot, passed, "the definition-ID pass");
        Assert.Equal(player.Digest, passedPlayer.Digest);

        // The rebase, over every host cell, with the fixture generator on both sides.
        var generator = M2Fixtures.Generator();
        var hosts = M2Fixtures.TenCells.Select(c => c.ToString()).ToHashSet(StringComparer.Ordinal);
        var rebased = SemanticRebase.Apply(new BaselineTransition("g11", generator.Fingerprint, generator.Fingerprint), hosts, passed,
            cell => generator.Generate(M2Fixtures.Seed, cell), new MigrationReport("g11"));
        Same(snapshot, rebased, "the rebase");
    }

    /// <summary>Every string in an encoded section that is shaped like a definition ID.</summary>
    private static IEnumerable<string> NamedIds(byte[] section)
    {
        var found = new SortedSet<string>(StringComparer.Ordinal);
        void Walk(object? node)
        {
            switch (node)
            {
                case string s when DefinitionId.IsValid(s):
                    found.Add(s);
                    break;
                case IDictionary<object, object> map:
                    foreach (var (_, value) in map)
                        Walk(value);
                    break;
                case object[] items:
                    foreach (var item in items)
                        Walk(item);
                    break;
            }
        }
        Walk(MessagePackSerializer.Deserialize<object>(section, MessagePackSerializerOptions.Standard));
        return found;
    }

    // F-E4
    [Fact]
    public void EveryPlayerRecordInitProperty_SurvivesEveryWithMethod()
    {
        using var profile = Fixtures.Copy(15);
        var loaded = new SaveStore(profile.Root).Load(SaveSlots.Quick, Fixtures.Context(new Registry()));
        foreach (var player in new[] { M2Fixtures.Historical.Player(), loaded.Player })
        {
            Assert.NotEqual(Posture.Grounded, player.Posture);
            Assert.NotEqual(FactionLedger.Empty, player.Factions);
            var withs = typeof(PlayerRecord).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => m.Name.StartsWith("With", StringComparison.Ordinal)).ToList();
            Assert.True(withs.Count >= 8, $"only {withs.Count} With* methods");
            foreach (var method in withs)
            {
                var arguments = method.GetParameters()
                    .Select(p => typeof(PlayerRecord).GetProperty(char.ToUpperInvariant(p.Name![0]) + p.Name[1..])!.GetValue(player))
                    .ToArray();
                var after = (PlayerRecord)method.Invoke(player, arguments)!;
                foreach (var property in typeof(PlayerRecord).GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.Name != "EqualityContract"))
                {
                    // By value: a record's synthesized equality compares a nested array (a conversation's lines) by reference.
                    Assert.True(Of(property.GetValue(player)) == Of(property.GetValue(after)), $"{method.Name} dropped {property.Name}");
                }
            }
        }
    }

    // G28
    [Fact]
    public void NavRoute_AndFactionLedger_EqualByValue_AfterADecode()
    {
        var player = M2Fixtures.Historical.Player();
        var decoded = SectionCodec.DecodePlayer(SectionCodec.EncodePlayer(player));
        Assert.Equal(player.Factions, decoded.Factions);
        Assert.NotSame(player.Factions, decoded.Factions);
        Assert.Equal(player.Factions.GetHashCode(), decoded.Factions.GetHashCode());
        Assert.Equal(player.Companions[0].Route, decoded.Companions[0].Route);
        Assert.Equal(player.Companions[0].Route.GetHashCode(), decoded.Companions[0].Route.GetHashCode());

        var snapshot = M2Fixtures.Historical.World(new Registry()).TakeSnapshot();
        var entities = SectionCodec.DecodeEntitySection(SectionCodec.EncodeEntities(snapshot));
        Assert.Equal(snapshot.NpcErrands[0], entities.NpcErrands[0]);   // composes through the route
        Assert.Equal<PieceRecord>(snapshot.Pieces, entities.Pieces);

        // And a different corner is a different route.
        var route = player.Companions[0].Route;
        var moved = NavRoute.Active(new NavPoint(route.GoalXMm, route.GoalZMm), route.Corners.SetItem(0, new NavPoint(1, 1)), route.PlannedTick, route.Stamp,
            route.Watch, route.Partial);
        Assert.NotEqual(route, moved);
    }

    // G29
    [Fact]
    public void SaveDocument_IsDeeplyImmutable()
    {
        var offenders = new List<string>();
        var seen = new HashSet<Type>();
        void Walk(Type type, string path)
        {
            type = Nullable.GetUnderlyingType(type) ?? type;
            if (type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal) || type == typeof(DateTimeOffset) || !seen.Add(type))
                return;
            if (type.IsArray || (type.IsGenericType && type.GetGenericTypeDefinition() is var d
                    && (d == typeof(List<>) || d == typeof(Dictionary<,>) || d == typeof(HashSet<>) || d == typeof(SortedDictionary<,>)
                        || d == typeof(SortedSet<>) || d == typeof(Queue<>) || d == typeof(Stack<>) || d == typeof(IList<>) || d == typeof(ICollection<>)
                        || d == typeof(IDictionary<,>))))
            {
                offenders.Add($"{path}: {type.Name} is a mutable collection");
                return;
            }
            if (type.IsGenericType && type.Namespace == "System.Collections.Immutable")
            {
                foreach (var argument in type.GetGenericArguments())
                    Walk(argument, $"{path}<{argument.Name}>");
                return;
            }
            if (type.Namespace?.StartsWith("UNNAMED", StringComparison.Ordinal) != true)
                return;
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.Name != "EqualityContract"))
            {
                var setter = property.SetMethod;
                bool initOnly = setter is not null && setter.ReturnParameter.GetRequiredCustomModifiers().Contains(typeof(IsExternalInit));
                if (setter is { IsPublic: true } && !initOnly)
                    offenders.Add($"{path}.{property.Name} has a public setter");
                Walk(property.PropertyType, $"{path}.{property.Name}");
            }
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance).Where(f => !f.IsInitOnly))
                offenders.Add($"{path}.{field.Name} is a writable field");
        }
        Walk(typeof(SaveDocument), "SaveDocument");
        Assert.Contains(typeof(PieceRecord), seen);
        Assert.Contains(typeof(NpcErrandRecord), seen);
        Assert.Contains(typeof(FactionLedger), seen);
        Assert.Contains(typeof(NavRoute), seen);
        Assert.True(offenders.Count == 0, "A captured save document can change after capture:\n" + string.Join("\n", offenders));
    }

    private static readonly string OtherHash = "sha256:" + string.Concat(Enumerable.Repeat("cd", 32));

    /// <summary>Every definition ID the fixture player and world name, as the writer named them: all current, unless changed below.</summary>
    private static readonly string[] Named =
    {
        "config.base_speeds", "container.fixture_chest", "corpse.fixture_den.m1_g0", "creature.beast.ash_hound", "creature.beast.deer",
        "creature.beast.wolf_grey", "dialogue.fixture.warden", "effect.bleeding", "effect.weakness", "faction.fixture.delvers",
        "faction.fixture.keepers", "item.book.alchemy_primer", "item.material.timber", "item.potion.healing_draught", "item.weapon.iron_sword",
        "location.wolf_den", "npc.fixture.smith", "npc.fixture.warden", "piece.fixture.chest", "piece.fixture.door", "piece.fixture.doorway",
        "piece.fixture.old_wall", "piece.fixture.pad", "quest.fixture.cull", "quest.fixture.errand", "quest.fixture.rescue",
        "recipe.alchemy.salve_minor", "skill.athletics", "skill.one_hand_blade", "spell.ember.firebolt", "world.door.cellar_open",
        "world.lever.mill_gate",
    };

    private static ContentIdentity Content(IEnumerable<string>? remove = null, IReadOnlyDictionary<string, string>? aliases = null)
    {
        var removed = (remove ?? Array.Empty<string>()).ToHashSet(StringComparer.Ordinal);
        var ids = Named.Where(id => !removed.Contains(id) && !(aliases?.ContainsKey(id) ?? false)).Concat(aliases?.Values ?? Array.Empty<string>());
        return new ContentIdentity("m7-pass", OtherHash, ids, aliases, discarded: removed);
    }

    private static (PlayerRecord Player, DeltaSnapshot Delta, MigrationReport Report) Pass(ContentIdentity content,
        PlayerRecord? player = null, DeltaSnapshot? delta = null)
    {
        var report = new MigrationReport("test");
        var (p, d) = SaveLoader.ResolveDefinitions(player ?? M2Fixtures.Historical.Player(), delta ?? M2Fixtures.Historical.World(new Registry()).TakeSnapshot(),
            content, report);
        return (p, d, report);
    }

    private static readonly EntityId Aelin = M2Fixtures.PlayerId;

    [Fact]
    public void PiecesAndErrands_GoThroughTheDefinitionPass_RenamesRemovalsSpillsAndMerges()
    {
        var (_, delta, report) = Pass(Content(aliases: new Dictionary<string, string> { ["piece.fixture.old_wall"] = "piece.fixture.wall" }));
        Assert.Empty(report.Blockers);
        Assert.Equal("piece.fixture.wall", delta.Pieces.Single(p => p.InstanceId == M2Fixtures.Historical.PieceId(5, Aelin)).DefId);
        Assert.Equal(9, delta.StructureSequence);
        Assert.Contains("piece.fixture.old_wall -> piece.fixture.wall", report.Aliases);

        // The chest's definition removed: the piece goes as a loss, its three timber land on the ground with their own identity.
        var before = M2Fixtures.Historical.World(new Registry()).TakeSnapshot();
        var chest = before.Containers.Single(c => c.Key.StartsWith("container.pce_", StringComparison.Ordinal));
        (_, delta, report) = Pass(Content(remove: new[] { "piece.fixture.chest" }), delta: before);
        Assert.Empty(report.Blockers);
        Assert.DoesNotContain(delta.Pieces, p => p.DefId == "piece.fixture.chest");
        Assert.DoesNotContain(delta.Containers, c => c.Key == chest.Key);
        var spilled = delta.Created.Single(c => c.InstanceId == chest.Items[0].ItemId);
        Assert.Equal(("item.material.timber", 3, "r_0_0:c_00_04", 4_950, 9_950), (spilled.DefId, spilled.Count, spilled.HostCell, spilled.XCm, spilled.ZCm));
        Assert.Contains(report.Loss, l => l.Contains("piece.fixture.chest", StringComparison.Ordinal));
        Assert.Contains(report.Warnings, w => w.Contains("1 stacks from the chest of piece", StringComparison.Ordinal));
        Assert.Equal(delta.Created.OrderBy(c => c.InstanceId.Value, StringComparer.Ordinal).Select(c => c.InstanceId), delta.Created.Select(c => c.InstanceId));

        // A station removed under an NPC at work: they walk home, keeping their pose and whom they worked for.
        var station = new PieceRecord(M2Fixtures.Historical.PieceId(10, Aelin), "piece.fixture.pad", "r_0_0:c_00_04", 52_500, 499_500, 0, Aelin, 200,
            before.Pieces[0].BaselineHash);
        var working = new NpcErrandRecord("npc.fixture.smith", "r_0_0:c_00_00", NpcErrandPhase.AtWork, station.InstanceId, Aelin, 52_500, 499_250, 0,
            before.NpcErrands[0].BaselineHash) { Route = NavRoute.None, StuckTicks = 0 };
        var withStation = before with { Pieces = before.Pieces.Add(station), StructureSequence = 10, NpcErrands = ImmutableArray.Create(working) };
        (_, delta, report) = Pass(Content(remove: new[] { "piece.fixture.pad" }), delta: withStation);
        var home = Assert.Single(delta.NpcErrands);
        Assert.Equal((NpcErrandPhase.ToHome, (EntityId?)null, NavRoute.None, 0, (EntityId?)Aelin, 52_500L),
            (home.Phase, home.PieceId, home.Route, home.StuckTicks, home.WorkOwner, home.XMm));
        Assert.Contains(report.Warnings, w => w.Contains("they walk home", StringComparison.Ordinal));

        // Two NPCs merged into one: the first, by the original ID, keeps the errand.
        var second = before.NpcErrands[0] with { NpcId = "npc.fixture.warden" };
        var two = before with { NpcErrands = ImmutableArray.Create(before.NpcErrands[0], second) };
        (_, delta, report) = Pass(Content(aliases: new Dictionary<string, string> { ["npc.fixture.warden"] = "npc.fixture.smith" }), delta: two);
        Assert.Equal(before.NpcErrands[0] with { NpcId = "npc.fixture.smith" }, Assert.Single(delta.NpcErrands));
        Assert.Contains(report.Loss, l => l.Contains("merged into npc.fixture.smith", StringComparison.Ordinal));
    }

    [Fact]
    public void TheFactionLedger_GoesThroughTheDefinitionPass_RenamesMergesAndRemovals()
    {
        var (player, _, report) = Pass(Content(aliases: new Dictionary<string, string>
        {
            ["faction.fixture.delvers"] = "faction.fixture.diggers", ["npc.fixture.warden"] = "npc.fixture.warden_sera",
        }));
        Assert.Empty(report.Blockers);
        Assert.Equal(new[] { "faction.fixture.diggers", "faction.fixture.keepers" }, player.Factions.Knowledge.Select(k => k.Knower));
        Assert.Equal("npc.fixture.warden_sera", player.Factions.Knowledge[1].Via);
        Assert.Equal(new[] { ("faction.fixture.diggers", -100), ("faction.fixture.keepers", 100) }, player.Factions.Standing.Select(s => (s.FactionId, s.Points)));

        // The wolf removed: its act goes with what the factions knew of it; standing stays; the sequence never goes back.
        (player, _, report) = Pass(Content(remove: new[] { "creature.beast.wolf_grey" }));
        Assert.Empty(report.Blockers);
        Assert.Equal(new long[] { 2 }, player.Factions.Acts.Select(a => a.Seq));
        Assert.Empty(player.Factions.Knowledge);
        Assert.Equal(2, player.Factions.Standing.Length);
        Assert.Equal(3, player.Factions.NextActSeq);

        // A removed faction takes its rows; a removed reporter leaves the knowledge standing without them, with a warning.
        (player, _, report) = Pass(Content(remove: new[] { "faction.fixture.keepers", "npc.fixture.smith" }));
        Assert.Empty(report.Blockers);
        var row = Assert.Single(player.Factions.Knowledge);
        Assert.Equal(("faction.fixture.delvers", (string?)null), (row.Knower, row.Via));
        Assert.Equal("faction.fixture.delvers", Assert.Single(player.Factions.Standing).FactionId);
        Assert.Contains(report.Warnings, w => w.Contains("its reporter 'npc.fixture.smith' was removed; the knowledge stays", StringComparison.Ordinal));

        // Two factions merged: knowledge of one act is one row, the identified and then earlier one; standing is summed and clamped.
        var ledger = new FactionLedger(2, ImmutableArray.Create(new ActRecord(1, ActKinds.CreatureKilled, "creature.beast.wolf_grey", "r_0_0:c_00_02", 20_000, 250_000, 4_100)),
            ImmutableArray.Create(
                new FactionKnowledge("faction.fixture.delvers", 1, Identities.Identified, KnowledgeSources.Reported, "npc.fixture.smith", 4_300, -600),
                new FactionKnowledge("faction.fixture.keepers", 1, Identities.Identified, KnowledgeSources.Reported, "npc.fixture.smith", 4_150, -600)),
            ImmutableArray.Create(new FactionStanding("faction.fixture.delvers", -600), new FactionStanding("faction.fixture.keepers", -600)));
        (player, _, report) = Pass(Content(aliases: new Dictionary<string, string> { ["faction.fixture.delvers"] = "faction.fixture.keepers" }),
            player: M2Fixtures.Historical.Player() with { Factions = ledger });
        Assert.Empty(report.Blockers);
        Assert.Equal(4_150, Assert.Single(player.Factions.Knowledge).Tick);
        Assert.Equal(FactionLedger.OrdinaryFloor, Assert.Single(player.Factions.Standing).Points);
    }

    [Fact]
    public void TwoFactionsMergedWithOppositeStanding_LoadNeutral_WithAWarning()
    {
        var (player, _, report) = Pass(Content(aliases: new Dictionary<string, string> { ["faction.fixture.delvers"] = "faction.fixture.keepers" }));
        Assert.Empty(report.Blockers);
        Assert.Empty(player.Factions.Standing);
        Assert.Contains("standing with faction.fixture.delvers and faction.fixture.keepers merged to neutral", report.Warnings);
    }

    [Fact]
    public void AnUnmappedVia_IsABlocker()
    {
        var content = new ContentIdentity("m7-pass", OtherHash, Named.Where(id => id != "npc.fixture.smith"));
        var (_, _, report) = Pass(content);
        Assert.Contains(report.Blockers, b => b.Contains("'npc.fixture.smith'", StringComparison.Ordinal)
            && b.Contains("player faction knowledge of act 1", StringComparison.Ordinal));
    }

    [Fact]
    public void ASpilledChestItemWithARenamedDefinition_LandsRenamed()
    {
        var before = M2Fixtures.Historical.World(new Registry()).TakeSnapshot();
        var chest = before.Containers.Single(c => c.Key.StartsWith("container.pce_", StringComparison.Ordinal));
        var content = Content(remove: new[] { "piece.fixture.chest" }, aliases: new Dictionary<string, string> { ["item.material.timber"] = "item.material.plank" });
        var (_, delta, report) = Pass(content, delta: before);
        Assert.Empty(report.Blockers);
        var spilled = delta.Created.Single(c => c.InstanceId == chest.Items[0].ItemId);
        Assert.Equal("item.material.plank", spilled.DefId);
    }

    [Fact]
    public void ADefinitionPassThatBreaksARecord_IsABlocker_NotACrash()
    {
        // An alias that turns a faction into an NPC: the rebuilt ledger holds standing with something that is not a faction.
        var aliases = new Dictionary<string, string> { ["faction.fixture.keepers"] = "npc.fixture.smith" };
        using var profile = new TempProfile();
        var store = new SaveStore(profile.Root);
        store.Save(SaveSlots.Quick, SaveDocuments.Capture(M2Fixtures.Historical.World(new Registry()), M2Fixtures.Historical.Player(),
            M2Fixtures.Content(), 5_000, 60));
        var context = new LoadContext(M2Fixtures.Generator(), Content(aliases: aliases), new Registry());

        var refused = Assert.Throws<SaveCompatibilityException>(() => store.Load(SaveSlots.Quick, context));
        Assert.Contains(refused.Report.Blockers, b => b.StartsWith("the definition-ID pass could not rebuild the save", StringComparison.Ordinal));
        var plan = store.PlanMigration(SaveSlots.Quick, context with { Registry = new Registry() });
        Assert.Contains(plan.Blockers, b => b.StartsWith("the definition-ID pass could not rebuild the save", StringComparison.Ordinal));
    }
}
