// UNNAMED SaveTool - save:inspect and save:migrate (M2b §13)
// No Godot references - pure C#

using System.Text.Json;
using System.Text.Json.Serialization;
using UNNAMED.Content;
using UNNAMED.Persistence;
using UNNAMED.World;
using Registry = UNNAMED.EntityRegistry.EntityRegistry;

namespace UNNAMED.SaveTool;

/// <summary>
/// <c>save:migrate [--dry-run] &lt;save&gt;</c> and <c>save:inspect &lt;save&gt;</c>. The dry run loads and
/// analyzes a save exactly as the game would and reports the migration plan; it writes nothing.
/// Exit codes: 0 up to date or ready, 1 blocked, 2 bad arguments.
/// </summary>
public static class SaveToolApp
{
    public const string Usage =
        "usage:\n" +
        "  save:inspect <save-dir>\n" +
        "  save:migrate --dry-run <save-dir> --content-root <dir> --worldgen <profile.json> [--content-version <v>]\n" +
        "  save:migrate <save-dir> --content-root <dir> --worldgen <profile.json> --content-version <v>";

    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        try
        {
            return args.FirstOrDefault() switch
            {
                "save:inspect" when args.Length == 2 => Inspect(args[1], output),
                "save:migrate" => Migrate(args[1..], output, error),
                _ => Fail(error, Usage),
            };
        }
        catch (ArgumentException e)
        {
            return Fail(error, e.Message + "\n" + Usage);
        }
    }

    private static int Migrate(string[] args, TextWriter output, TextWriter error)
    {
        bool dryRun = args.Contains("--dry-run");
        var options = Options(args.Where(a => a != "--dry-run").ToArray(), out var positional);
        if (positional.Count != 1)
            return Fail(error, Usage);
        string save = positional[0];
        if (!Directory.Exists(save))
            return Fail(error, $"No save directory at '{save}'");

        string contentRoot = Required(options, "--content-root");
        string worldgen = Required(options, "--worldgen");
        string? version = options.GetValueOrDefault("--content-version");
        if (!dryRun && version is null)
            return Fail(error, "save:migrate needs --content-version: the migrated save records the content it was migrated to");

        var content = ReadContent(contentRoot, version ?? "unversioned", error);
        if (content is null)
            return 1;
        var context = new LoadContext(new CellBaselineGenerator(WorldgenProfileFile.Read(worldgen)), content, new Registry());

        MigrationReport report;
        if (dryRun)
        {
            report = SaveStore.PlanMigrationAt(save, context);
        }
        else
        {
            string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(save));
            try
            {
                report = new SaveStore(Path.GetDirectoryName(full)!).Migrate(Path.GetFileName(full), context);
            }
            catch (SaveCompatibilityException e)
            {
                report = e.Report;
            }
        }
        output.Write(report.ToText());
        return report.Result == MigrationResult.Blocked ? 1 : 0;
    }

    private static int Inspect(string save, TextWriter output)
    {
        var manifest = JsonSerializer.Deserialize<JsonElement>(File.ReadAllBytes(Path.Combine(save, SaveFormat.Manifest)));
        output.WriteLine($"Save: {Path.GetFullPath(save)}");
        output.WriteLine($"Integrity: {SaveStore.VerifyIntegrity(save) ?? "ok"}");
        output.WriteLine(JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
        output.WriteLine("Files:");
        foreach (string file in Directory.EnumerateFiles(save).OrderBy(f => f, StringComparer.Ordinal))
            output.WriteLine($"- {Path.GetFileName(file)} ({new FileInfo(file).Length} bytes)");
        return 0;
    }

    /// <summary>The running content's identity, through the real content loader (DATA_MODEL.md §2.1).</summary>
    public static ContentIdentity? ReadContent(string contentRoot, string version, TextWriter error)
    {
        var loader = new ContentLoader();
        if (!loader.LoadAll(contentRoot) || loader.HasErrors)
        {
            error.WriteLine($"Content at '{contentRoot}' does not validate:");
            foreach (var e in loader.Errors)
                error.WriteLine("  " + e);
            return null;
        }
        return new ContentIdentity(version, loader.ComputeContentHash(), loader.Definitions.Keys,
            loader.Aliases, loader.Removed, loader.Discarded);
    }

    private static Dictionary<string, string> Options(string[] args, out List<string> positional)
    {
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        positional = new List<string>();
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i].StartsWith("--", StringComparison.Ordinal))
            {
                if (i + 1 >= args.Length)
                    throw new ArgumentException($"{args[i]} needs a value");
                options[args[i]] = args[++i];
            }
            else
            {
                positional.Add(args[i]);
            }
        }
        return options;
    }

    private static string Required(Dictionary<string, string> options, string name) =>
        options.TryGetValue(name, out string? value) ? value : throw new ArgumentException($"{name} is required");

    private static int Fail(TextWriter error, string message)
    {
        error.WriteLine(message);
        return 2;
    }
}

/// <summary>
/// A generation profile on disk. The game has no world configuration yet, so the tool takes the
/// placement data a save was generated with as a file (the historical fixtures carry theirs).
/// </summary>
public static class WorldgenProfileFile
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    public static GenerationProfile Read(string path)
    {
        var file = JsonSerializer.Deserialize<ProfileFile>(File.ReadAllBytes(path), Json)
                   ?? throw new ArgumentException($"'{path}' is empty");
        return new GenerationProfile(
            file.Nodes.Select(n => new NodeRule(n.Name, n.DefId, n.MinPerCell, n.MaxPerCell)),
            file.Populations.Select(p => new PopulationRule(p.Name, p.FamilyDefId, p.Target, p.Min, p.Max)),
            new TerrainRule(file.Terrain.BaseHeightMm, file.Terrain.AmplitudeMm, file.Terrain.SamplesPerAxis));
    }

    private sealed record ProfileFile(NodeFile[] Nodes, PopulationFile[] Populations, TerrainFile Terrain);

    private sealed record NodeFile(string Name, string DefId, int MinPerCell, int MaxPerCell);

    private sealed record PopulationFile(string Name, string FamilyDefId, int Target, int Min, int Max);

    private sealed record TerrainFile(int BaseHeightMm, int AmplitudeMm, int SamplesPerAxis);
}
