using MessagePack;
using UNNAMED.M2Probe;
using UNNAMED.Persistence.Sections;
using UNNAMED.World;
using Registry = UNNAMED.EntityRegistry.EntityRegistry;

namespace UNNAMED.Persistence.Tests;

/// <summary>
/// Schema 16 (the owner's ruling on the M7 E8.5 STOP): the character's vitals - the pauses after a blow, an exertion and a working, and
/// the part-points each pool has accrued - round-trip exactly, edges included; a value no running world can hold is refused; a
/// schema-16 player without them is corrupt, not defaulted; and the 15 -> 16 step gives an older save a character at rest, as every
/// older save has always loaded, changing nothing else.
/// </summary>
public class Schema16Tests
{
    private static readonly MessagePackSerializerOptions Options = MessagePackSerializerOptions.Standard;

    private static PlayerRecord With(VitalsClock vitals) => M2Fixtures.Historical.Player() with { Vitals = vitals };

    private static string Refused(Action<PlayerDto> damage)
    {
        var dto = MessagePackSerializer.Deserialize<PlayerDto>(SectionCodec.EncodePlayer(M2Fixtures.Historical.Player()), Options);
        damage(dto);
        var bytes = MessagePackSerializer.Serialize(dto, Options);
        return Assert.Throws<FormatException>(() => SectionCodec.DecodePlayer(bytes)).Message;
    }

    [Theory]
    [InlineData(VitalsClock.Never, VitalsClock.Never, VitalsClock.Never, 0, 0, 0, 0, 0)]   // at rest
    [InlineData(0L, 0L, 0L, 999, 999, 999, 999, 999)]                                      // the first tick; a thousandth short of every point
    [InlineData(4_990L, VitalsClock.Never, 12_345_678L, 1, 0, 500, 998, 1)]                // mixed
    [InlineData(long.MaxValue, 0L, VitalsClock.Never, 0, 1, 0, 0, 999)]
    public void TheVitals_RoundTripExactly_EdgesIncluded(long combat, long exertion, long cast, int health, int stamina, int focus, int strain, int sprint)
    {
        var vitals = new VitalsClock(combat, exertion, cast, health, stamina, focus, strain, sprint);
        var player = With(vitals);
        var decoded = SectionCodec.DecodePlayer(SectionCodec.EncodePlayer(player));
        Assert.Equal(vitals, decoded.Vitals);
        Assert.Equal(player.Digest, decoded.Digest);
    }

    [Fact]
    public void AValueNoWorldHolds_IsRefused_AndSoIsAPlayerWithoutVitals()
    {
        Assert.Contains("vitals", Refused(dto => dto.Vitals = null));
        Assert.Contains("health_milli 1000", Refused(dto => dto.Vitals!.HealthMilli = 1_000));
        Assert.Contains("stamina_milli -1", Refused(dto => dto.Vitals!.StaminaMilli = -1));
        Assert.Contains("focus_milli", Refused(dto => dto.Vitals!.FocusMilli = 5_000));
        Assert.Contains("strain_milli", Refused(dto => dto.Vitals!.StrainMilli = -999));
        Assert.Contains("sprint_milli", Refused(dto => dto.Vitals!.SprintMilli = 1_000));
        Assert.Contains("last_combat_tick -1 ", Refused(dto => dto.Vitals!.LastCombatTick = -1));
        Assert.Contains("last_exertion_tick", Refused(dto => dto.Vitals!.LastExertionTick = VitalsClock.Never - 1));
        Assert.Contains("last_cast_tick", Refused(dto => dto.Vitals!.LastCastTick = -2));
        Assert.Throws<ArgumentException>(() => M2Fixtures.Historical.Player() with { Vitals = new VitalsClock(0, 0, 0, 1_000, 0, 0, 0, 0) });
    }

    /// <summary>
    /// The step alone, on the v15 fixture's own sections: the player gains a character at rest and nothing else of it changes; the world
    /// sections pass through untouched; applied twice, it writes the same bytes. (The full load - the chain, then the definition-ID pass
    /// - is each fixture's <c>expected.json</c>, where schema 16 adds only the rested vitals.)
    /// </summary>
    [Fact]
    public void Schema15To16_GivesACharacterAtRest_AndChangesNothingElse_TheSameEveryTime()
    {
        string quick = Fixtures.Save(15);
        var sections = new[] { SaveFormat.Player, SaveFormat.Cells, SaveFormat.Entities }
            .ToDictionary(f => f, f => (byte[]?)File.ReadAllBytes(Path.Combine(quick, f)), StringComparer.Ordinal);
        byte[]? first = null;
        for (int run = 0; run < 2; run++)
        {
            var document = new MigrationDocument(15, new System.Text.Json.Nodes.JsonObject { ["schema_version"] = 15 }, sections);
            var report = new MigrationReport("v15");
            new SchemaV15ToV16().Apply(document, new MigrationEnvironment(M2Fixtures.Generator()), report);

            Assert.Equal((16, "schema 15 -> 16:"), ((int)document.Manifest["schema_version"]!, Assert.Single(report.Steps)[..16]));
            byte[] after = document.Sections[SaveFormat.Player]!;
            Assert.Equal(VitalsClock.Rested, SectionCodec.DecodePlayer(after).Vitals);
            var old = MessagePackSerializer.Deserialize<Dictionary<object, object>>(sections[SaveFormat.Player]!, Options);
            var now = MessagePackSerializer.Deserialize<Dictionary<object, object>>(after, Options);
            Assert.Equal(old.Keys.Cast<string>().Order(), now.Keys.Cast<string>().Where(k => k != "vitals").Order());
            foreach (var key in old.Keys)
                Assert.Equal(MessagePackSerializer.Serialize(old[key], Options), MessagePackSerializer.Serialize(now[key], Options));
            Assert.Same(sections[SaveFormat.Cells], document.Sections[SaveFormat.Cells]);
            Assert.Same(sections[SaveFormat.Entities], document.Sections[SaveFormat.Entities]);

            first ??= after;
            Assert.Equal(first, after);
        }
    }
}
