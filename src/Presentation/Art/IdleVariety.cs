namespace UNNAMED.Presentation.Art;

/// <summary>Presentation-only idle timing. A person has their own phase and a role-weighted pool of installed clips.</summary>
public sealed class IdleVariety
{
    private readonly Random _random;
    private double _left;
    private string _clip = "idle";
    private string? _previous;
    public double StartPhase { get; }

    public IdleVariety(string identity)
    {
        uint seed = 2166136261;
        foreach (char c in identity) seed = (seed ^ c) * 16777619;
        _random = new Random(unchecked((int)seed));
        StartPhase = _random.NextDouble();
        _left = 1 + _random.NextDouble() * 7;
    }

    public string Step(double delta, string? role, Func<string, bool> has, Func<string, double> length)
    {
        _left -= delta;
        if (_left > 0) return _clip;
        if (_clip != "idle")
        {
            _clip = "idle";
            _left = 3 + _random.NextDouble() * 7;
            return _clip;
        }
        // The legacy idle_look aliases idle. Use it only for bodies without the
        // distinct mocap observation; duplicated labels are not extra variety.
        string looking = has("idle_observe") ? "idle_observe" : "idle_look";
        string[] wanted = role?.Contains("guard", StringComparison.OrdinalIgnoreCase) == true
            ? ["idle_foldarms", "idle_foldarms", looking, "idle_shift", "idle_settle"]
            : ["idle_shift", looking, "idle_settle", "idle_foldarms"];
        var pool = wanted.Where(has).ToArray();
        var different = pool.Where(clip => clip != _previous).ToArray();
        if (different.Length > 0) pool = different;
        _clip = pool.Length == 0 ? "idle" : pool[_random.Next(pool.Length)];
        _previous = _clip;
        _left = _clip == "idle" ? 6 + _random.NextDouble() * 12 : Math.Max(.4, length(_clip));
        return _clip;
    }

    public void Interrupt() { _clip = "idle"; _left = 3 + _random.NextDouble() * 9; }
}
