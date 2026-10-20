using System.Diagnostics;

var root = AppContext.BaseDirectory;
var evidence = Path.Combine(root, "evidence");
Directory.CreateDirectory(evidence);
var ownerFlag = @"C:\Otherreach_Recovery\OWNER_PLAYING.flag";
var lockScript = Path.Combine(root, "invoke_gpu_lock.ps1");
bool review = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OTHERREACH_MH_REVIEW_OUT"));
var lease = review ? "codex-mh-slice mh-characters-playtest" : "owner-mh-playtest manual-playtest";
var ownerMarker = "MetaHuman playtest launcher " + Environment.ProcessId;
bool ownerFlagHeld = false;
Process? game = null;
bool held = false;
Task<string>? output = null, errors = null;
void Lock(string action)
{
    for (int attempt = 1; attempt <= 3; attempt++)
    {
        var command = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true };
        foreach (var arg in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", lockScript, action, lease }) command.ArgumentList.Add(arg);
        using var job = Process.Start(command)!;
        job.WaitForExit();
        if (job.ExitCode == 0) return;
        if (attempt < 3) Thread.Sleep(5000);
    }
    throw new Exception("GPU lock " + action + " failed after three attempts");
}
try
{
    if (File.Exists(ownerFlag)) throw new Exception("Owner playing; launch deferred");
    if (!File.Exists(Path.Combine(root, "mh_characters", "catalog.json"))) throw new FileNotFoundException("MetaHuman runtime bundle is missing");
    if (!review)
    {
        using var flag = new FileStream(ownerFlag, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        using var writer = new StreamWriter(flag);
        writer.Write(ownerMarker);
        ownerFlagHeld = true;
    }
    Lock("acquire"); held = true;
    if (review && File.Exists(ownerFlag)) throw new Exception("Owner playing; launch deferred after acquire");
    var start = new ProcessStartInfo(Path.Combine(root, "Otherreach.exe")) {
        WorkingDirectory = root, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true
    };
    start.Environment["OTHERREACH_MH_CHARACTERS_ROOT"] = Path.Combine(root, "mh_characters");
    start.Environment["UNNAMED_SCATTER_RESIDENCY"] = "0";
    start.Environment["UNNAMED_LOD_STIPPLE"] = "0";
    string optionFile = Path.Combine(root, "characters.option");
    string people = File.Exists(optionFile) && File.ReadAllText(optionFile).Trim() == "mpfb" ? "mpfb" : "production";
    string profile = review && Environment.GetEnvironmentVariable("OTHERREACH_MH_REVIEW_PROFILE") is { Length: > 0 } reviewProfile
        ? Path.GetFullPath(reviewProfile) : Path.Combine(root, "saves");
    // Normal launch preserves the owner's client_prefs, including window size and render tier.
    foreach (var arg in new[] { "--", "--region", "region.starting_frontier", "--content-root", Path.Combine(root, "content"),
        "--bindings-overlay", Path.Combine(root, "content", "_art_bindings.json"), "--asset-root", Path.Combine(root, "primary"),
        "--asset-fallback", Path.Combine(root, "fallback"), "--scatter-rules", Path.Combine(root, "content", "_scatter_rules.json"),
        "--profile", profile, "--visual", $"terrain=terrain3d,sky=sky3d,people={people},foldscar=proof,body=none" +
            (start.Environment.TryGetValue("OTHERREACH_MH_REVIEW_HOUR", out var hour) && hour == "12" ? ",hour=12" : "") }) start.ArgumentList.Add(arg);
    game = Process.Start(start)!;
    File.AppendAllText(Path.Combine(evidence, "launcher.log"), $"{DateTimeOffset.Now:o} launcher={Environment.ProcessPath} launcher_pid={Environment.ProcessId} game_pid={game.Id} asset_root={start.Environment["OTHERREACH_MH_CHARACTERS_ROOT"]}\n");
    output = game.StandardOutput.ReadToEndAsync(); errors = game.StandardError.ReadToEndAsync();
    var timer = Stopwatch.StartNew();
    while (!game.WaitForExit(1000))
    {
        if (review && File.Exists(ownerFlag)) throw new Exception("Owner playing; stopping owned review");
        if (review && timer.Elapsed.TotalSeconds > 1080) throw new Exception("GPU review lease time limit");
        if (review && timer.Elapsed.TotalSeconds > 540 && File.Exists(@"C:\Otherreach_Recovery\gpu.queue") && File.ReadAllLines(@"C:\Otherreach_Recovery\gpu.queue").Any(x => !string.IsNullOrWhiteSpace(x))) throw new Exception("Yielding GPU to queued lane");
    }
    Environment.ExitCode = game.ExitCode;
}
catch (Exception e)
{
    File.AppendAllText(Path.Combine(evidence, "launcher.log"), DateTimeOffset.Now + " " + e + Environment.NewLine);
    Environment.ExitCode = 1;
}
finally
{
    if (game is { HasExited: false }) { game.Kill(entireProcessTree: true); game.WaitForExit(); }
    if (output is not null) File.WriteAllText(Path.Combine(evidence, "packaged_run.log"), output.GetAwaiter().GetResult());
    if (errors is not null) File.WriteAllText(Path.Combine(evidence, "packaged_run.stderr"), errors.GetAwaiter().GetResult());
    try { if (held) Lock("release"); }
    finally
    {
        if (ownerFlagHeld && File.Exists(ownerFlag) && File.ReadAllText(ownerFlag) == ownerMarker) File.Delete(ownerFlag);
        game?.Dispose();
    }
}
