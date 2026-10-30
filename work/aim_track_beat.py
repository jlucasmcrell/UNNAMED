"""Aim the camera for the tracking beat too, or the beat cannot run at all (goal round 18).

Aim() throws for an unknown beat name, so `--shot-beat track` would have died on its first frame. The tracking view looks down at the ground a
few metres ahead, because sign is read at the feet and a level look shows only grass.
"""
from pathlib import Path

path = Path(r'D:\Otherreach_Scratch\ds_fish2_wt\src\Presentation\Main.ActionShots.cs')
code = path.read_text(encoding='utf-8')
old = '''            "mine" => (Mathf.Pi, -0.06f),
            _ => throw new ArgumentException($"--shot-beat wants fish, lock or mine; got '{beat}'"),'''
new = '''            "mine" => (Mathf.Pi, -0.06f),
            // The deer ground at the Charwood spawn (612, 718): looking east along the trees, tilted down so the ground a person reads is in
            // frame rather than the treeline.
            "track" => (Mathf.DegToRad(270f), -0.42f),
            _ => throw new ArgumentException($"--shot-beat wants fish, lock, mine or track; got '{beat}'"),'''
assert old in code
path.write_text(code.replace(old, new, 1), encoding='utf-8')
print('the tracking beat has a camera')
