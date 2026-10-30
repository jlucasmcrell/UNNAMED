"""Report why a cast was refused, from the state the beat can see (goal round 10).

GrantItem is internal to the world, so the beat cannot hand itself tackle; what it can do is say what it had when the cast failed - the bank,
the rod, a bait, and whether something else was already running - which is what the next run needs to be diagnosable in one lease instead of
two.
"""
from pathlib import Path

path = Path(r'D:\Otherreach_Scratch\ds_fish2_wt\src\Presentation\Main.ActionShots.cs')
code = path.read_text(encoding='utf-8')

start = code.index('                // The pack has to carry what the config names: a rod to cast and a bait to shorten the wait this beat waits on.')
end = code.index('                Press("fish");', start)
replacement = '''                // What the cast needs is the config's: say what the pack had when it failed, so one run explains itself.
                string rod = simulation.Setup.Fishing.RodItemId ?? "";
                bool hasRod = rod.Length == 0 || simulation.Player.Inventory.Any(e => e.DefId == rod);
                string baits = string.Join(",", simulation.Setup.Fishing.Baits.Keys
                    .Where(id => simulation.Player.Inventory.Any(e => e.DefId == id)));
                _moments.Add($"- before the cast: bank ok, rod {(hasRod ? "carried" : "MISSING")}, baits [{baits}], "
                    + $"activity {simulation.Activity?.Kind.ToString() ?? "none"}");
'''
code = code[:start] + replacement + code[end:]
path.write_text(code, encoding='utf-8')
print('the beat reports what it had')
