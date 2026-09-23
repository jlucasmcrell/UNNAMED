using UNNAMED.Domain.Progression;
using UNNAMED.SaveTool;
using UNNAMED.World;
using Registry = UNNAMED.EntityRegistry.EntityRegistry;

namespace UNNAMED.Persistence.Tests;

/// <summary>The committed historical fixtures (Fixtures/README.md) and the context they load under.</summary>
internal static class Fixtures
{
    public const string ContentVersion = "0.2.1";

    public static string Root { get; } = FindRoot();

    public static string Save(int schema) => Path.Combine(Root, $"v{schema}", SaveSlots.Quick);

    public static string Expected(int schema) => Path.Combine(Root, $"v{schema}", "expected.json");

    public static string ContentRoot => Path.Combine(Root, "content");

    public static string Profile => Path.Combine(Root, "worldgen_profile.json");

    public static ContentIdentity Content(string? contentRoot = null) =>
        SaveToolApp.ReadContent(contentRoot ?? ContentRoot, ContentVersion, TextWriter.Null)
        ?? throw new InvalidOperationException("The fixture content does not validate");

    public static LoadContext Context(Registry registry, ContentIdentity? content = null) =>
        new(new CellBaselineGenerator(WorldgenProfileFile.Read(Profile)), content ?? Content(), registry);

    /// <summary>A throwaway profile holding a copy of the fixture: tests never touch the committed bytes.</summary>
    public static TempProfile Copy(int schema)
    {
        var profile = new TempProfile();
        CopyDirectory(Save(schema), Path.Combine(profile.Root, SaveSlots.Quick));
        return profile;
    }

    public static void CopyDirectory(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (string file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            string target = Path.Combine(to, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }

    private static string FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "src", "UNNAMED.sln")))
                return Path.Combine(dir.FullName, "tests", "Persistence.Tests", "Fixtures");
        }
        throw new DirectoryNotFoundException("Repository root not found");
    }
}

/// <summary>
/// M2b §11 and §19's CI proof: every schema version that has shipped has a committed fixture written by
/// that version's own writer, and every one loads - and migrates through the commit path - to its
/// expected current state, non-lossily.
/// </summary>
public class HistoricalFixtureTests
{
    public static TheoryData<int> ShippedSchemas()
    {
        var data = new TheoryData<int>();
        for (int schema = SaveFormat.OldestSupportedSchema; schema <= SaveFormat.SchemaVersion; schema++)
            data.Add(schema);
        return data;
    }

    /// <summary>The fixture policy, enforced: a schema bump without its fixture fails CI.</summary>
    [Fact]
    public void EveryShippedSchemaVersion_HasAFixture_AndNoneFromTheFuture()
    {
        var present = Directory.EnumerateDirectories(Fixtures.Root, "v*")
            .Select(d => int.Parse(Path.GetFileName(d)[1..], System.Globalization.CultureInfo.InvariantCulture))
            .OrderBy(v => v)
            .ToList();

        Assert.Equal(Enumerable.Range(SaveFormat.OldestSupportedSchema, SaveFormat.SchemaVersion - SaveFormat.OldestSupportedSchema + 1), present);
        foreach (int schema in present)
        {
            Assert.True(File.Exists(Fixtures.Expected(schema)), $"v{schema} has no expected.json");
            string manifest = File.ReadAllText(Path.Combine(Fixtures.Save(schema), SaveFormat.Manifest));
            Assert.Contains($"\"schema_version\": {schema},", manifest);
        }
    }

    [Theory]
    [MemberData(nameof(ShippedSchemas))]
    public void Fixture_LoadsToItsExpectedCurrentState(int schema)
    {
        using var profile = Fixtures.Copy(schema);
        var loaded = new SaveStore(profile.Root).Load(SaveSlots.Quick, Fixtures.Context(new Registry()));

        Assert.True(loaded.IsComplete, string.Join("; ", loaded.Report.Warnings.Concat(loaded.Report.Loss)));
        string rendered = CanonicalState.Render(loaded);
        if (Environment.GetEnvironmentVariable("UNNAMED_WRITE_FIXTURE_EXPECTATIONS") == "1")
            File.WriteAllText(Fixtures.Expected(schema), rendered);
        Assert.Equal(File.ReadAllText(Fixtures.Expected(schema)).Replace("\r\n", "\n"), rendered);

        // Stated independently of expected.json: the fixture world as README.md describes it.
        Assert.Contains(loaded.Player.Inventory, e => e.DefId == "item.potion.minor_healing" && e.Count == 3);   // renamed via _aliases.yaml
        Assert.Contains(loaded.Player.Inventory, e => e.DefId == "item.weapon.iron_sword" && e.Count == 1);
        Assert.Equal(1, loaded.World.GetFlag(CellKey.Parse("r_0_0:c_00_00"), "world.door.cellar_open"));
        Assert.True(loaded.World.IsHarvested(CellKey.Parse("r_0_0:c_00_03"), "node.r_0_0:c_00_03.iron_vein.00"));
        Assert.Equal(1, loaded.World.GetPopulationAlive(CellKey.Parse("r_0_0:c_00_05"), "pop.r_0_0.c_00_05.deer"));
        Assert.Equal(3, loaded.World.GetFlag(CellKey.Parse("r_0_0:c_00_07"), "world.lever.mill_gate"));
        Assert.False(loaded.World.Occupant("r_0_0:c_00_02.pop.r_0_0.c_00_02.wolves.00").Alive);   // tombstoned baseline entity
        var deer = loaded.World.Occupant("r_0_0:c_00_04.pop.r_0_0.c_00_04.deer.01");
        Assert.Equal((1_234, 5_678), (deer.XCm, deer.ZCm));

        // Schema 3's required field: written by v3, derived from the ULID for v1 and v2 (value from Reference/worldgen_v2_reference.py).
        Assert.Equal(0x32599743E39279EFUL, loaded.Player.AppearanceSeed);
        var created = loaded.World.CreatedIn(CellKey.Parse("r_0_0:c_00_06"));
        if (schema >= 3)
            Assert.Equal(("item.weapon.iron_sword", 500, 600), (Assert.Single(created).DefId, created[0].XCm, created[0].ZCm));
        else
            Assert.Empty(created);

        // Schema 4's progression record: written by v4 and renamed on load like the inventory; empty for v1-v3.
        var progression = loaded.Player.Progression;
        if (schema >= 4)
        {
            Assert.Equal(3, progression.Level);
            Assert.True(ProgressionEngine.Knows(progression, "spell.ember.bolt"));   // renamed via _aliases.yaml
            Assert.False(ProgressionEngine.Knows(progression, "spell.ember.firebolt"));
            Assert.Contains("item.potion.minor_healing", progression.ProductionFirsts);
            Assert.Equal(5, ProgressionEngine.SkillLevel(progression, "skill.one_hand_blade"));
        }
        else
        {
            Assert.Equal(CharacterProgression.Empty.Digest, progression.Digest);
        }

        Assert.Equal(SaveFormat.SchemaVersion - schema, loaded.Report.Steps.Count);
        // v4 names the potion twice - held, and first produced - and the report counts each occurrence.
        var aliases = schema >= 4
            ? new[] { "item.potion.healing_draught -> item.potion.minor_healing x2", "spell.ember.firebolt -> spell.ember.bolt" }
            : new[] { "item.potion.healing_draught -> item.potion.minor_healing" };
        Assert.Equal(aliases, loaded.Report.Aliases);
        Assert.Equal(schema >= 3 ? 7 : 6, loaded.Report.CellsMatched);
        Assert.Empty(loaded.Report.Loss);
    }

    [Theory]
    [MemberData(nameof(ShippedSchemas))]
    public void Fixture_MigratesThroughTheCommitPath_AndReloadsToTheSameState(int schema)
    {
        using var profile = Fixtures.Copy(schema);
        var store = new SaveStore(profile.Root);

        var report = store.Migrate(SaveSlots.Quick, Fixtures.Context(new Registry()));
        Assert.Equal(MigrationResult.Ready, report.Result);

        var reloaded = store.Load(SaveSlots.Quick, Fixtures.Context(new Registry()));
        Assert.Equal(MigrationResult.UpToDate, reloaded.Report.Result);
        Assert.Equal(File.ReadAllText(Fixtures.Expected(schema)).Replace("\r\n", "\n"), CanonicalState.Render(reloaded));
        Assert.Equal(SaveFormat.SchemaVersion, reloaded.Manifest.SchemaVersion);
        Assert.Equal(Fixtures.Content().Hash, reloaded.Manifest.ContentHash);

        // The original is kept, byte for byte, when a schema migration displaced it.
        if (schema < SaveFormat.SchemaVersion)
        {
            string original = Assert.Single(store.PreMigrationBackups(SaveSlots.Quick));
            Assert.Equal($"pre_migration_{schema}_quick", Path.GetFileName(original));
            foreach (string file in Directory.EnumerateFiles(Fixtures.Save(schema)))
                Assert.Equal(File.ReadAllBytes(file), File.ReadAllBytes(Path.Combine(original, Path.GetFileName(file))));
        }
    }
}
