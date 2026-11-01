using Godot;
using System.Text.Json;
using System.Security.Cryptography;
using UNNAMED.Presentation.Art;

namespace UNNAMED.Presentation;

public partial class Main
{
    private double _qualitySince;
    private string? _qualityReport;
    private bool _qualityStarted;
    private Dictionary<string, string>? _qualityPackageFiles;
    private Task<Dictionary<string, string>>? _qualityPackageHashTask;
    private readonly Dictionary<string, (float Blink, float Jaw)> _qualityFacePeaks = new(StringComparer.Ordinal);

    // Opt-in package-gate telemetry. This observes the normal camera, populated world,
    // selected settings and ordinary animation. It never changes any of them.
    private void ObserveCharacterQuality(double delta)
    {
        if (!_qualityStarted)
        {
            _qualityStarted = true;
            string directory = OS.GetEnvironment("OTHERREACH_CHARACTER_QUALITY_OUT");
            if (directory.Length == 0) return;
            Directory.CreateDirectory(directory);
            _qualityReport = Path.Combine(directory, "character_quality.jsonl");
            if (File.Exists(_qualityReport)) throw new IOException("Quality evidence already exists: " + _qualityReport);
            string package = Home();
            if (OS.HasFeature("template")) _qualityPackageHashTask = Task.Run(() => CharacterQualityPackageFiles(package));
            else _qualityPackageFiles = new Dictionary<string, string>();
        }
        if (_qualityReport is null) return;
        if (_npcs is not null)
        {
            var figures = _npcs.GetChildren().OfType<SkinnedFigure>();
            if (_avatar is SkinnedFigure player) figures = figures.Append(player);
            foreach (var figure in figures)
            {
                var previous = _qualityFacePeaks.GetValueOrDefault(figure.QualityActorId);
                var current = figure.QualityFaceWeights;
                _qualityFacePeaks[figure.QualityActorId] = (Math.Max(previous.Blink, current.Blink), Math.Max(previous.Jaw, current.Jaw));
            }
        }
        if (_creatures is not null)
            foreach (var creature in _creatures.FindChildren("*", nameof(SkinnedCreature), true, false).OfType<SkinnedCreature>().Where(c => c.NativeHumGenQuality))
            {
                var previous = _qualityFacePeaks.GetValueOrDefault(creature.QualityActorId);
                var current = creature.QualityFaceWeights;
                _qualityFacePeaks[creature.QualityActorId] = (Math.Max(previous.Blink, current.Blink), Math.Max(previous.Jaw, current.Jaw));
            }
        _qualitySince += delta;
        if (_qualitySince < .25) return;
        _qualitySince = 0;
        if (CharacterQualitySnapshot() is { } sample)
            File.AppendAllText(_qualityReport, JsonSerializer.Serialize(sample) + System.Environment.NewLine);
        _qualityFacePeaks.Clear();
    }

    private object? CharacterQualitySnapshot(string? screenshot = null)
    {
        if (_session?.Simulation is not { } simulation || _camera is null || _npcs is null || _creatures is null
            || GetViewport().GetCamera3D() is not { } camera) return null;
        if (_qualityPackageHashTask is { } hashes)
        {
            if (!hashes.IsCompleted) return null;
            _qualityPackageFiles = hashes.GetAwaiter().GetResult();
            _qualityPackageHashTask = null;
        }
        if (_creator?.Visible == true || _title?.Visible == true || _firstLaunch?.Visible == true
            || _veil?.Visible == true || (_menu is not null && Screen is not null)) return null;
        var figures = _npcs.FindChildren("*", "", true, false).OfType<SkinnedFigure>().ToList();
        if (_avatar is SkinnedFigure player) figures.Add(player);
        var viewport = GetViewport();
        return new
        {
            schema = 1, packaged = OS.HasFeature("template"), executable = OS.GetExecutablePath(),
            ordinaryLaunch = CharacterQualityOrdinaryLaunch(),
            launchArguments = OS.GetCmdlineUserArgs(), engineArguments = OS.GetCmdlineArgs(),
            packageFiles = _qualityPackageFiles,
            wallSeconds = Time.GetTicksMsec() / 1000.0, frame = Engine.GetProcessFrames(),
            fps = Engine.GetFramesPerSecond(), tier = VisualOptions.Tier,
            normalCamera = camera == _camera.Camera || camera == _talkCamera,
            cameraMode = camera == _talkCamera ? "dialogue" : camera == _camera.Camera ? "gameplay" : "other",
            cameraFov = camera.Fov, cameraAspect = camera.KeepAspect.ToString(),
            viewportWidth = (int)viewport.GetVisibleRect().Size.X, viewportHeight = (int)viewport.GetVisibleRect().Size.Y,
            taa = viewport.UseTaa, msaa = viewport.Msaa3D.ToString(),
            renderer = RenderingServer.GetCurrentRenderingMethod(),
            renderScale = viewport.Scaling3DScale, mipBias = viewport.TextureMipmapBias,
            region = simulation.ActiveLayout.SpaceId, npcs = simulation.Npcs.Count(n => n.Space == simulation.ActiveLayout.SpaceId),
            expectedActors = simulation.Npcs.Where(n => n.Space == simulation.ActiveLayout.SpaceId).Select(n => n.Id).Append("player").Distinct().ToArray(),
            conversation = simulation.Conversation?.NpcId,
            screenshot = screenshot is null ? null : new
            {
                file = Path.GetFileName(screenshot),
                sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(screenshot))).ToLowerInvariant(),
            },
            figures = figures.Select(figure => figure.QualitySnapshot(camera, _qualityFacePeaks.GetValueOrDefault(figure.QualityActorId)))
                .Concat(_creatures.FindChildren("*", nameof(SkinnedCreature), true, false).OfType<SkinnedCreature>()
                    .Where(c => c.NativeHumGenQuality).Select(c => c.QualitySnapshot(camera, _qualityFacePeaks.GetValueOrDefault(c.QualityActorId)))).ToArray(),
            fallbacks = _art.Coverage.Entries.Where(entry => entry.Fallbacks > 0).ToArray(),
            artProblems = _art.Problems,
        };
    }

    private bool CharacterQualityOrdinaryLaunch()
    {
        if (_scripted || DisplayServer.GetName() == "headless") return false;
        string[] arguments = OS.GetCmdlineUserArgs();
        // These are the two shipped launchers: direct EXE/PLAY, or PLAY Ultra 4K.
        string[] ultra = Ui.Models.PlaytestLaunchDefaults.Resolve([], Home(), true);
        ultra[^1] = "tier=ultra," + ultra[^1];
        if (arguments.Length != 0 && !arguments.SequenceEqual(ultra, StringComparer.OrdinalIgnoreCase)) return false;
        string[] forbidden = ["--headless", "--editor", "--path", "--script", "--fixed-fps", "--write-movie", "--disable-render-loop"];
        if (OS.GetCmdlineArgs().Any(argument => forbidden.Contains(argument, StringComparer.Ordinal))) return false;
        foreach (System.Collections.DictionaryEntry variable in System.Environment.GetEnvironmentVariables())
        {
            string name = (string)variable.Key;
            if (!name.StartsWith("OTHERREACH_", StringComparison.OrdinalIgnoreCase)
                && !name.StartsWith("UNNAMED_", StringComparison.OrdinalIgnoreCase)
                && !name.StartsWith("BARROWWORKS_", StringComparison.OrdinalIgnoreCase)) continue;
            if (name.Equals("OTHERREACH_CHARACTER_QUALITY_OUT", StringComparison.OrdinalIgnoreCase)) continue;
            if ((name.Equals("UNNAMED_SCATTER_RESIDENCY", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("UNNAMED_LOD_STIPPLE", StringComparison.OrdinalIgnoreCase)) && variable.Value?.ToString() == "0") continue;
            if (!string.IsNullOrEmpty(variable.Value?.ToString())) return false;
        }
        return true;
    }

    private static Dictionary<string, string> CharacterQualityPackageFiles(string root)
    {
        var files = new List<string> { Path.Combine(root, "Otherreach.exe"), Path.Combine(root, "VERSION.txt"),
            Path.Combine(root, "primary", "manifests", "character_quality_contract.json") };
        files.AddRange(Directory.EnumerateFiles(Path.Combine(root, "primary", "manifests"), "*", SearchOption.AllDirectories));
        files.AddRange(Directory.EnumerateFiles(root, "*.pck"));
        foreach (string directory in Directory.EnumerateDirectories(root, "data_*").Append(Path.Combine(root, "content")))
            if (Directory.Exists(directory)) files.AddRange(Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories));
        var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string file in files.Distinct())
        {
            using var stream = File.OpenRead(file);
            hashes[Path.GetRelativePath(root, file).Replace('\\', '/')] = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        }
        return hashes;
    }

    private void RecordQualityScreenshot(string file)
    {
        if (_qualityReport is null || CharacterQualitySnapshot(file) is not { } sample) return;
        string json = JsonSerializer.Serialize(sample);
        File.WriteAllText(file + ".quality.json", json);
        File.AppendAllText(_qualityReport, json + System.Environment.NewLine);
    }
}
