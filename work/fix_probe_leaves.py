"""The probe skipped leaf meshes: it only reported nodes that have children, and a rock piece is a MeshInstance3D with none."""
from pathlib import Path

PATH = Path(r'D:\Otherreach_Scratch\ds_fish2_wt\src\Presentation\Greybox\SceneryProbe.cs')
text = PATH.read_text(encoding='utf-8')
OLD = '''            if (child is Node3D piece && piece.GetChildCount() > 0)'''
NEW = '''            // A placed rock is a MeshInstance3D with no children, which an earlier version of this walk skipped entirely - so the one node kind
            // that matters most went unreported. Anything that draws, or holds something that does, is measured.
            if (child is Node3D piece && (piece is MeshInstance3D || piece.GetChildCount() > 0))'''
if OLD in text:
    PATH.write_text(text.replace(OLD, NEW, 1), encoding='utf-8')
    print('leaf meshes now measured')
else:
    print('anchor missing' if 'MeshInstance3D || piece' not in text else 'already fixed')
