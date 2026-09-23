using UNNAMED.M2Probe;
using UNNAMED.SaveTool;
using Registry = UNNAMED.EntityRegistry.EntityRegistry;

namespace UNNAMED.Persistence.Tests;

/// <summary>
/// M2b §13 and §12.10: <c>save:migrate --dry-run</c> reports the whole migration plan and changes nothing
/// on disk; <c>save:migrate</c> commits through the store.
/// </summary>
public class SaveToolTests
{
    private static (int Code, string Output, string Error) Run(params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        int code = SaveToolApp.Run(args, output, error);
        return (code, output.ToString().Replace("\r\n", "\n"), error.ToString());
    }

    private static string[] DryRun(string save, string? contentRoot = null) => new[]
    {
        "save:migrate", "--dry-run", save, "--content-root", contentRoot ?? Fixtures.ContentRoot,
        "--worldgen", Fixtures.Profile, "--content-version", Fixtures.ContentVersion,
    };

    /// <summary>Every file and directory under a root, with bytes and write times: equality means nothing changed.</summary>
    private static SortedDictionary<string, string> Snapshot(string root)
    {
        var entries = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (string dir in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories))
            entries[Path.GetRelativePath(root, dir) + "/"] = Directory.GetLastWriteTimeUtc(dir).Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture);
        foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            entries[Path.GetRelativePath(root, file)] = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(file)))
                + "@" + File.GetLastWriteTimeUtc(file).Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return entries;
    }

    [Fact]
    public void DryRun_ReportsTheMigrationPlan()
    {
        using var profile = Fixtures.Copy(1);

        var (code, output, _) = Run(DryRun(Path.Combine(profile.Root, SaveSlots.Quick)));

        Assert.Equal(0, code);
        Assert.Contains("Save format: 1 (this build reads 1)", output);
        Assert.Contains($"Schema: 1 -> {SaveFormat.SchemaVersion}", output);
        Assert.Contains($"Content version: 0.1.0 -> {Fixtures.ContentVersion}", output);
        Assert.Contains("Content hash: changed", output);
        Assert.Contains($"Worldgen: 1 -> {World.CellBaselineGenerator.Version}, RNG contract 1 -> 2", output);
        Assert.Contains("Worldgen fingerprint: not recorded (schema 1)", output);
        Assert.Contains("Migration steps:\n- schema 1 -> 2:", output);
        Assert.Contains("Definition aliases:\n- item.potion.healing_draught -> item.potion.minor_healing", output);
        Assert.Contains("Definition removals:\n- none", output);
        Assert.Contains("- 6 baseline hash matches", output);
        Assert.Contains("- 0 baseline mismatches with no transition", output);
        Assert.Contains("Blockers:\n- none", output);
        Assert.Contains("Expected loss:\n- none", output);
        Assert.EndsWith("Result: READY TO MIGRATE\n", output);
    }

    [Fact]
    public void DryRun_ChangesNoPersistentFile_WhetherReadyOrBlocked()
    {
        using var profile = Fixtures.Copy(1);
        string save = Path.Combine(profile.Root, SaveSlots.Quick);
        string pack = Path.Combine(Path.GetTempPath(), "unnamed-pack-" + Guid.NewGuid().ToString("N"));
        try
        {
            Fixtures.CopyDirectory(Fixtures.ContentRoot, pack);
            File.Delete(Path.Combine(pack, "_aliases.yaml"));
            var before = Snapshot(profile.Root);

            Assert.Equal(0, Run(DryRun(save)).Code);
            Assert.Equal(1, Run(DryRun(save, pack)).Code);

            Assert.Equal(before, Snapshot(profile.Root));
        }
        finally
        {
            Directory.Delete(pack, recursive: true);
        }
    }

    /// <summary>A real load of a clean current save records proof in rotation.json; the store's dry run must not.</summary>
    [Fact]
    public void ThePlanThroughTheStore_WritesNothing_NotEvenLoadProof()
    {
        using var profile = new TempProfile();
        var store = new SaveStore(profile.Root);
        store.Save(M2Fixtures.Slot, M2Fixtures.Document(M2Fixtures.OldWorld(new Registry())));
        var before = Snapshot(profile.Root);

        var report = store.PlanMigration(M2Fixtures.Slot, M2Fixtures.Context(new Registry()));

        Assert.Equal(MigrationResult.UpToDate, report.Result);
        Assert.Equal(before, Snapshot(profile.Root));
        Assert.False(File.Exists(Path.Combine(profile.Root, "rotation.json")));
    }

    [Fact]
    public void DryRun_OfABlockedSave_ExitsNonZero_AndNamesTheBlocker()
    {
        using var profile = Fixtures.Copy(2);
        string pack = Path.Combine(Path.GetTempPath(), "unnamed-pack-" + Guid.NewGuid().ToString("N"));
        try
        {
            Fixtures.CopyDirectory(Fixtures.ContentRoot, pack);
            File.Delete(Path.Combine(pack, "_aliases.yaml"));

            var (code, output, _) = Run(DryRun(Path.Combine(profile.Root, SaveSlots.Quick), pack));

            Assert.Equal(1, code);
            Assert.Contains("unresolved definition ID 'item.potion.healing_draught'", output);
            Assert.EndsWith("Result: BLOCKED - this build cannot load the save\n", output);
        }
        finally
        {
            Directory.Delete(pack, recursive: true);
        }
    }

    [Fact]
    public void Migrate_Commits_ThenTheSaveIsUpToDate()
    {
        using var profile = Fixtures.Copy(1);
        string save = Path.Combine(profile.Root, SaveSlots.Quick);

        var migrated = Run("save:migrate", save, "--content-root", Fixtures.ContentRoot, "--worldgen", Fixtures.Profile,
            "--content-version", Fixtures.ContentVersion);

        Assert.Equal(0, migrated.Code);
        Assert.EndsWith("Result: READY TO MIGRATE\n", migrated.Output);
        Assert.EndsWith("Result: UP TO DATE - nothing to migrate\n", Run(DryRun(save)).Output);
        Assert.Null(SaveStore.VerifyIntegrity(Path.Combine(profile.Root, "pre_migration_1_quick")));
    }

    [Fact]
    public void Inspect_ShowsManifestIntegrityAndFiles()
    {
        var (code, output, _) = Run("save:inspect", Fixtures.Save(1));

        Assert.Equal(0, code);
        Assert.Contains("Integrity: ok", output);
        Assert.Contains("\"schema_version\": 1", output);
        Assert.Contains("- sections.sha256", output);
    }

    [Theory]
    [InlineData]
    [InlineData("save:migrate")]
    [InlineData("save:migrate", "--dry-run")]
    [InlineData("save:frobnicate", "x")]
    public void BadArguments_ExitWithUsage(params string[] args)
    {
        var (code, _, error) = Run(args);

        Assert.Equal(2, code);
        Assert.Contains("usage:", error);
    }

    [Fact]
    public void ARealMigration_RequiresTheContentVersionItWillRecord()
    {
        using var profile = Fixtures.Copy(1);
        var (code, _, error) = Run("save:migrate", Path.Combine(profile.Root, SaveSlots.Quick), "--content-root", Fixtures.ContentRoot,
            "--worldgen", Fixtures.Profile);

        Assert.Equal(2, code);
        Assert.Contains("--content-version", error);
    }
}
