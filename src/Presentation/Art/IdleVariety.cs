namespace UNNAMED.Presentation.Art;

/// <summary>Presentation-only idle timing. A person has their own phase and a role-weighted pool of installed clips.</summary>
public sealed class IdleVariety
{
    private readonly Random _random;
    private double _left;
    private string _clip = "idle";
    public double StartPhase { get; }

    public IdleVariety(string identity)
    {
        uint seed = 2166136261;
        foreach (char c in identity) seed = (seed ^ c) * 16777619;
        _random = new Random(unchecked((int)seed));
        StartPhase = _random.NextDouble();
        _left = 3 + _random.NextDouble() * 12;
    }

    public string Step(double delta, string? role, Func<string, bool> has, Func<string, double> length)
    {
        _left -= delta;
        if (_left > 0) return _clip;
        if (_clip != "idle")
        {
            _clip = "idle";
            _left = 6 + _random.NextDouble() * 12;
            return _clip;
        }
        string[] wanted = role?.Contains("guard", StringComparison.OrdinalIgnoreCase) == true
            ? ["idle_foldarms", "idle_foldarms", "idle_look", "idle_shift"]
            : ["idle_shift", "idle_look", "idle_look", "idle_foldarms", "idle_breathe", "idle_nervous"];
        var pool = wanted.Where(has).ToArray();
        _clip = pool.Length == 0 ? "idle" : pool[_random.Next(pool.Length)];
        _left = _clip == "idle" ? 6 + _random.NextDouble() * 12 : Math.Max(.4, length(_clip));
        return _clip;
    }

    public void Interrupt() { _clip = "idle"; _left = 3 + _random.NextDouble() * 9; }
}
