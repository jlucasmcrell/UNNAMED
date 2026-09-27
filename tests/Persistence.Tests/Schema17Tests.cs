using MessagePack;
using UNNAMED.Domain;
using UNNAMED.M2Probe;
using UNNAMED.Persistence.Sections;
using UNNAMED.World;
using Registry = UNNAMED.EntityRegistry.EntityRegistry;

namespace UNNAMED.Persistence.Tests;

/// <summary>
/// Schema 17 (the owner's ruling on the second M7 E8.5 STOP): a creature's ordinary attack in progress - the tick it began and whom it
/// has landed on - round-trips exactly; an attack no running world can hold is refused with its record, and the rest of the world
/// loads; and the 16 -> 17 step gives an older save no attack in progress, as every older save has always loaded, changing nothing else.
/// </summary>
public class Schema17Tests
{
    private static readonly MessagePackSerializerOptions Options = MessagePackSerializerOptions.Standard;

    private const string Biting = "spawn.fixture.den#2";

    private static readonly EntityId Companion = EntityId.Create(EntityKind.Npc, 1_700_000_000_200, new byte[] { 9, 9, 9, 9, 9, 9, 9, 9, 9, 9 });

    [Theory]
    [InlineData(4_996L, null)]              // in its windup: nothing struck yet
    [InlineData(0L, "character")]           // the first tick; landed on the character
    [InlineData(long.MaxValue, "companion")]
    public void TheAttackInProgress_RoundTripsExactly(long began, string? struck)
    {
        var world = M2Fixtures.Historical.World(new Registry());
        var record = world.Creature(Biting)! with
        {
            AttackTick = began,
            AttackStruck = struck switch { "character" => M2Fixtures.PlayerId, "companion" => Companion, _ => null },
        };
        world.SetCreature(record);
        var snapshot = world.TakeSnapshot();

        var decoded = SectionCodec.DecodeEntitySection(SectionCodec.EncodeEntities(snapshot)).Creatures.Single(c => c.Key == Biting);
        Assert.Equal(record, decoded with { BaselineHash = null });   // the proof is the section's, attached on decode
        Assert.Equal((began, record.AttackStruck), (decoded.AttackTick, decoded.AttackStruck));
    }

    /// <summary>
    /// Nothing is refused that a running world writes, and nothing a running world cannot hold is loaded: an attack landed on something
    /// that cannot be struck, on a creature that is not alive, alongside a stagger (a creature does one thing at a time), or begun before
    /// the first tick. Each such record is rejected alone and said, like any impossible creature state - never repaired into a guess.
    /// </summary>
    [Fact]
    public void AnImpossibleAttack_IsRejectedWithItsRecord_AndTheRestLoads()
    {
        string item = EntityId.Create(EntityKind.Item, 1_700_000_000_300, new byte[] { 3, 3, 3, 3, 3, 3, 3, 3, 3, 3 }).Value;
        var cases = new (string Key, Action<CreatureDto> Damage)[]
        {
            (Biting, c => c.Continuation!.Attack!.Struck = item),
            (Biting, c => c.Continuation!.Attack!.StartTick = -1),
            ("spawn.fixture.den#1", c => c.Continuation!.Attack = new CreatureAttackDto { StartTick = 4_990 }),   // a corpse
            ("spawn.fixture.den#0", c => c.Continuation!.Attack = new CreatureAttackDto { StartTick = 4_999 }),   // stunned
        };
        foreach (var (key, damage) in cases)
        {
            var (profile, store) = Schema15Tests.SavedFixture(dto => damage(dto.Creatures!.Single(c => c.Key == key)));
            using var _ = profile;
            var loaded = Schema15Tests.LoadSaved(store);

            Assert.Empty(loaded.QuarantinedSections);
            var rejected = Assert.Single(loaded.RejectedRecords);
            Assert.Contains("its attack in progress is impossible", rejected.Reason, StringComparison.Ordinal);
            Assert.Contains(key, rejected.Key, StringComparison.Ordinal);
            Assert.Equal(3, loaded.World.TakeSnapshot().Creatures.Length);
        }
    }

    [Fact]
    public void AStruckThatIsNoInstanceId_IsCorrupt()
    {
        var dto = MessagePackSerializer.Deserialize<EntitiesSectionDto>(
            SectionCodec.EncodeEntities(M2Fixtures.Historical.World(new Registry()).TakeSnapshot()), Options);
        dto.Creatures!.Single(c => c.Key == Biting).Continuation!.Attack!.Struck = "the character";
        var bytes = MessagePackSerializer.Serialize(dto, Options);
        Assert.Contains("the character", Assert.Throws<FormatException>(() => SectionCodec.DecodeEntitySection(bytes)).Message);
    }

    /// <summary>
    /// The step alone, on the v16 fixture's own sections: every creature record gains no attack in progress and nothing else of the
    /// entities section changes; the player and cells pass through untouched; applied twice, it writes the same bytes. (The full load is
    /// each fixture's <c>expected.json</c>, where schema 17 adds only <c>"attack": null</c>.)
    /// </summary>
    [Fact]
    public void Schema16To17_GivesNoAttackInProgress_AndChangesNothingElse_TheSameEveryTime()
    {
        string quick = Fixtures.Save(16);
        var sections = new[] { SaveFormat.Player, SaveFormat.Cells, SaveFormat.Entities }
            .ToDictionary(f => f, f => (byte[]?)File.ReadAllBytes(Path.Combine(quick, f)), StringComparer.Ordinal);
        byte[]? first = null;
        for (int run = 0; run < 2; run++)
        {
            var document = new MigrationDocument(16, new System.Text.Json.Nodes.JsonObject { ["schema_version"] = 16 }, sections);
            var report = new MigrationReport("v16");
            new SchemaV16ToV17().Apply(document, new MigrationEnvironment(M2Fixtures.Generator()), report);

            Assert.Equal((17, "schema 16 -> 17:"), ((int)document.Manifest["schema_version"]!, Assert.Single(report.Steps)[..16]));
            byte[] after = document.Sections[SaveFormat.Entities]!;
            var creatures = SectionCodec.DecodeEntitySection(after).Creatures;
            Assert.Equal(3, creatures.Length);
            Assert.All(creatures, c => Assert.Equal((null, null), (c.AttackTick, c.AttackStruck)));

            var old = Map(sections[SaveFormat.Entities]!);
            var now = Map(after);
            Assert.Equal(old.Keys.Cast<string>().Order(), now.Keys.Cast<string>().Order());
            foreach (var key in old.Keys.Where(k => (string)k != "creatures"))
                Assert.Equal(MessagePackSerializer.Serialize(old[key], Options), MessagePackSerializer.Serialize(now[key], Options));
            var oldCreatures = (object[])old["creatures"];
            var newCreatures = (object[])now["creatures"];
            Assert.Equal(oldCreatures.Length, newCreatures.Length);
            for (int i = 0; i < oldCreatures.Length; i++)
            {
                var was = (Dictionary<object, object>)oldCreatures[i];
                var became = (Dictionary<object, object>)newCreatures[i];
                foreach (var key in was.Keys.Where(k => (string)k != "continuation"))
                    Assert.Equal(MessagePackSerializer.Serialize(was[key], Options), MessagePackSerializer.Serialize(became[key], Options));
                var wasContinuation = (Dictionary<object, object>)was["continuation"];
                var continuation = (Dictionary<object, object>)became["continuation"];
                Assert.Equal(wasContinuation.Keys.Cast<string>().Append("attack").Order(), continuation.Keys.Cast<string>().Order());
                foreach (var key in wasContinuation.Keys)
                    Assert.Equal(MessagePackSerializer.Serialize(wasContinuation[key], Options), MessagePackSerializer.Serialize(continuation[key], Options));
                Assert.Null(continuation["attack"]);
            }
            Assert.Same(sections[SaveFormat.Player], document.Sections[SaveFormat.Player]);
            Assert.Same(sections[SaveFormat.Cells], document.Sections[SaveFormat.Cells]);

            first ??= after;
            Assert.Equal(first, after);
        }
    }

    private static Dictionary<object, object> Map(byte[] bytes) => MessagePackSerializer.Deserialize<Dictionary<object, object>>(bytes, Options);
}
