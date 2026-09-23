using System.Diagnostics;

namespace UNNAMED.Persistence.Tests;

/// <summary>A throwaway profile directory, removed when the test ends.</summary>
internal sealed class TempProfile : IDisposable
{
    public TempProfile() =>
        Root = Path.Combine(Path.GetTempPath(), "unnamed-saves-" + Guid.NewGuid().ToString("N"));

    public string Root { get; }

    public void Dispose()
    {
        if (Directory.Exists(Root))
            Directory.Delete(Root, recursive: true);
    }
}

internal static class Cultures
{
    /// <summary>Run an action with the thread's culture set, as if on a machine configured that way.</summary>
    public static void Run(string culture, Action action)
    {
        var (previous, previousUi) = (System.Globalization.CultureInfo.CurrentCulture, System.Globalization.CultureInfo.CurrentUICulture);
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo(culture);
            System.Globalization.CultureInfo.CurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo(culture);
            action();
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
            System.Globalization.CultureInfo.CurrentUICulture = previousUi;
        }
    }
}

internal static class Tamper
{
    /// <summary>Corrupt one byte of a file in place, as disk or transfer damage would.</summary>
    public static void FlipByte(string path, int index = 5)
    {
        byte[] bytes = File.ReadAllBytes(path);
        bytes[Math.Min(index, bytes.Length - 1)] ^= 0xFF;
        File.WriteAllBytes(path, bytes);
    }
}

/// <summary>Runs the M2 probe as a separate OS process.</summary>
internal static class Probe
{
    public static (int ExitCode, string Output, string Error) Run(params string[] args)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add(ProbeAssembly());
        foreach (string arg in args)
            start.ArgumentList.Add(arg);

        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the probe");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(120_000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"The probe did not finish: {string.Join(' ', args)}");
        }
        return (process.ExitCode, output.Result.Trim(), error.Result.Trim());
    }

    // The probe's own build output (bin/<configuration>/net8.0), which carries its runtimeconfig.
    private static string ProbeAssembly()
    {
        var testBin = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
        string configuration = testBin.Parent!.Name;   // .../bin/<configuration>/net8.0
        for (var dir = testBin; dir is not null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, "tests", "M2.Probe", "bin", configuration, "net8.0", "M2.Probe.dll");
            if (File.Exists(candidate))
                return candidate;
        }
        throw new FileNotFoundException("M2.Probe.dll not found; build the solution first");
    }
}
