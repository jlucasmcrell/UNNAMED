namespace UNNAMED.Application.Tests.Acceptance;

/// <summary>
/// The playtest acceptance checklist's automated steps: each step id (<c>1.5</c> is the checklist's §1.5) gets one line, <c>[§1.5] PASS reason</c> or
/// <c>[§1.5] FAIL reason</c>, written to <see cref="Path"/> after every run of the walkthrough. A step that throws is a FAIL with the exception's first
/// line as its reason; the walkthrough carries on, so one broken step does not hide the rest.
/// </summary>
internal sealed class AcceptanceReport
{
    /// <summary>Where the report is written: <c>UNNAMED_ACCEPTANCE_REPORT</c>, or <c>acceptance_report.txt</c> beside the test assembly.</summary>
    public static string Path => System.Environment.GetEnvironmentVariable("UNNAMED_ACCEPTANCE_REPORT") is { Length: > 0 } set
        ? set
        : System.IO.Path.Combine(AppContext.BaseDirectory, "acceptance_report.txt");

    private static readonly object Gate = new();
    private static readonly SortedDictionary<string, string> Lines = new(Comparer<string>.Create(Compare));

    private static int Compare(string a, string b)
    {
        var pa = a.Split('.').Select(Number).ToArray();
        var pb = b.Split('.').Select(Number).ToArray();
        int order = pa[0] != pb[0] ? pa[0].CompareTo(pb[0]) : pa[1].CompareTo(pb[1]);
        return order != 0 ? order : string.CompareOrdinal(a, b);
    }

    private static int Number(string part) => int.Parse(new string(part.TakeWhile(char.IsDigit).ToArray()));

    /// <summary>Steps known to be broken by a game bug, not by the walkthrough: they report FAIL but do not fail the test (bug id in the reason).</summary>
    public static readonly Dictionary<string, string> KnownBugs = new();

    public static IReadOnlyList<string> Failures
    {
        get
        {
            lock (Gate)
                return Lines.Where(l => l.Value.StartsWith("FAIL", StringComparison.Ordinal) && !KnownBugs.ContainsKey(l.Key)).Select(l => $"[§{l.Key}] {l.Value}").ToList();
        }
    }

    /// <summary>Run a step. <paramref name="check"/> asserts and returns what it saw, as the PASS line's reason.</summary>
    public static void Step(string id, Func<string> check)
    {
        string line;
        try
        {
            line = "PASS " + check();
        }
        catch (Exception e)
        {
            line = "FAIL " + First(e.Message);
        }
        lock (Gate)
            Lines[id] = line;
    }

    /// <summary>Record a step as failed without running anything (an earlier step it depends on did not hold).</summary>
    public static void Blocked(string id, string why)
    {
        lock (Gate)
            Lines[id] = "FAIL blocked: " + why;
    }

    public static bool Passed(string id)
    {
        lock (Gate)
            return Lines.TryGetValue(id, out var line) && line.StartsWith("PASS", StringComparison.Ordinal);
    }

    public static void Write()
    {
        lock (Gate)
        {
            var text = string.Join(System.Environment.NewLine, Lines.Select(l => $"[§{l.Key}] {l.Value}{(KnownBugs.TryGetValue(l.Key, out var bug) ? " (" + bug + ")" : "")}")) + System.Environment.NewLine;
            File.WriteAllText(Path, text);
        }
    }

    private static string First(string message)
    {
        string line = message.Split('\n')[0].Trim();
        return line.Length > 240 ? line[..240] + "..." : line;
    }
}
