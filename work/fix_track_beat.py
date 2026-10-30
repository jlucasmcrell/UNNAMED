"""The beat reads the player's body, and Done takes its sentence (goal round 17)."""
from pathlib import Path

path = Path(r'D:\Otherreach_Scratch\ds_fish2_wt\src\Presentation\Main.ActionShots.cs')
code = path.read_text(encoding='utf-8')
code = code.replace('simulation.Player.Position.X / 1000.0', 'simulation.Player.Body.XMm / 1000.0')
code = code.replace('simulation.Player.Position.Z / 1000.0', 'simulation.Player.Body.ZMm / 1000.0')
code = code.replace('''                    Done();
                }
                return;''', '''                    Done($"read {_moments.Count} sign(s) and walked the trail");
                }
                return;''')
path.write_text(code, encoding='utf-8')
print('the beat reads the body position')
