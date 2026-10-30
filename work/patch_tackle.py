"""Show the tackle: a rod in the hand whenever a line is being fished, the float out once it lands, and the ring when something takes it.

The figure already plays the cast and reel clips; this gives them something to hold and something to watch.
"""
from pathlib import Path

ROOT = Path(r'D:\Otherreach_Scratch\ds_fish2_wt')

events = ROOT / 'src' / 'Presentation' / 'Main.Events.cs'
code = events.read_text(encoding='utf-8')
old = '''        _session.Subscribe<FishCastStarted>(_ => Notice(NoticeKind.World, "You cast", 2));
        _session.Subscribe<FishingLineOut>(_ => _hud.Log("The line is out"));'''
new = '''        _session.Subscribe<FishCastStarted>(_ =>
        {
            Notice(NoticeKind.World, "You cast", 2);
            Tackle().LineOut = false;
        });
        _session.Subscribe<FishingLineOut>(_ =>
        {
            _hud.Log("The line is out");
            Tackle().LineOut = true;
            Tackle().Biting = false;
        });'''
assert old in code
code = code.replace(old, new, 1)

old = '''        _session.Subscribe<FishCaught>(e =>
        {'''
new = '''        _session.Subscribe<FishCaught>(e =>
        {
            Tackle().LineOut = false;'''
assert old in code
code = code.replace(old, new, 1)

old = '''        _session.Subscribe<FishLost>(e => _hud.Log(e.Reason));'''
new = '''        _session.Subscribe<FishLost>(e =>
        {
            _hud.Log(e.Reason);
            Tackle().LineOut = false;
        });'''
assert old in code
code = code.replace(old, new, 1)

helper = '''
    /// <summary>The rod, line and float the character fishes with, made once and kept on the player's body.</summary>
    private Art.FishingTackle? _tackle;

    private Art.FishingTackle Tackle()
    {
        if (_tackle is not null && GodotObject.IsInstanceValid(_tackle))
            return _tackle;
        _tackle = new Art.FishingTackle { Name = "Tackle" };
        (_avatar as Node3D ?? this).AddChild(_tackle);
        return _tackle;
    }
'''
marker = '    // Fishing: one line at each turn of the loop'
assert marker in code
code = code.replace(marker, helper + marker, 1)
events.write_text(code, encoding='utf-8')
print('the tackle shows with the cast')
