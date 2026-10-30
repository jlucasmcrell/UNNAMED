"""The read's own words go to the HUD, so a player and a capture both see what the ground said (goal round 16)."""
from pathlib import Path

path = Path(r'D:\Otherreach_Scratch\ds_fish2_wt\src\Presentation\Main.Events.cs')
code = path.read_text(encoding='utf-8')
old = '''        _session.Subscribe<FishLost>(e =>
        {
            _hud.Log(e.Reason);
            Tackle().LineOut = false;
        });'''
new = '''        _session.Subscribe<FishLost>(e =>
        {
            _hud.Log(e.Reason);
            Tackle().LineOut = false;
        });
        // Tracking: what the ground said, in the read's own words - the way, how old it is, how many left it, and when the sign refuses to
        // say (cold, stone, water) the reason it refuses.
        _session.Subscribe<SpoorRead>(e =>
        {
            _tracked = e.Readable;
            _hud.Log(e.Text);
        });'''
assert old in code
code = code.replace(old, new, 1)

helper = '''
    /// <summary>Whether the last read of the ground found sign - what a capture waits on.</summary>
    private bool _tracked;
'''
anchor = '    /// <summary>Whether the last read of the ground found sign - what a capture waits on.</summary>'
if anchor not in code:
    marker = '    /// <summary>The rod, line and float the character fishes with, made once and kept on the player\'s body.</summary>'
    assert marker in code
    code = code.replace(marker, helper + '\n' + marker, 1)
path.write_text(code, encoding='utf-8')
print('the HUD says what the ground said')
