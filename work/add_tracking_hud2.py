"""The read says its own piece: the way, the age, how many, and the reason it refuses (goal round 16)."""
from pathlib import Path

path = Path(r'D:\Otherreach_Scratch\ds_fish2_wt\src\Presentation\Main.Events.cs')
code = path.read_text(encoding='utf-8')
old = '''        _session.Subscribe<SpoorRead>(e =>
        {
            _tracked = e.Readable;
            _hud.Log(e.Text);
        });'''
new = '''        _session.Subscribe<SpoorRead>(e =>
        {
            _tracked = e.Readable;
            _hud.Log(e.Readable
                ? $"The ground says: {e.Count} went {Compass(e.BearingMdeg)}, {e.AgeTicks / 20.0:0.#} s ago{(e.Uncertain ? " - and it is not certain" : "")}"
                : e.Reason);
        });'''
assert old in code
code = code.replace(old, new, 1)

helper = '''
    /// <summary>A bearing in degrees (0 = north, 90 = east) in the eight words a walker uses.</summary>
    private static string Compass(int bearingMdeg)
    {
        string[] points = { "north", "north-east", "east", "south-east", "south", "south-west", "west", "north-west" };
        int octant = (int)Math.Round(((bearingMdeg % 360 + 360) % 360) / 45.0) % 8;
        return points[octant];
    }
'''
anchor = "    /// <summary>Whether the last read of the ground found sign - what a capture waits on.</summary>"
assert anchor in code
code = code.replace(anchor, helper + '\n' + anchor, 1)
path.write_text(code, encoding='utf-8')
print('the read is spoken')
