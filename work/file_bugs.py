"""File the two P1 bugs the owner asked for: the floating scenery and the Barrowback on the deer ground."""
from pathlib import Path

PATH = Path(r'C:\Otherreach_External_Assets\ROADMAP\BUGS.md')
text = PATH.read_text(encoding='utf-8')

ANCHOR = '| B-020 | P1 | Native Kal creator body clips'

ROWS = (
    '| B-023 | P1 | Boulders hang in the sky above the frontier moor and the Old Late ground | '
    '`claude_motion_deer04\\near.png` and `claude_area_oldlate01\\near.png` (farm capture set): boulders standing well above the treeline, one '
    'clearly airborne over open grass | **Claude - fauna/capture lane, taken directly** | **Root cause found**: `Terrain3DView.Images()` bakes the '
    'imported heightmap from `ground.Carved(x, z, grid.HeightAtMm(...))`, but everything that *stands* on the ground asked `SceneryHeight`, which '
    'returned the plain `ground.Height` - so wherever a water shore lowered the bed, the mesh sits below the field and the piece stands by the cut. '
    '**Fix in this lane**: `Terrain3DView.DrawnHeight(ground, grid, x, z)` is now the single surface both read (imported grid sample + `Carved`, '
    '`Outside()` beyond the region), `SceneryHeight` delegates to it, and `HollowView.DrawnGround` hands dressing that function instead of '
    '`field.Height`. Proof frame and probe output pending on the farm (`claude_fix_boulders01`, `UNNAMED_SCENERY_PROBE=1` names any piece still off '
    'with its delta). Likely the same family as B-021/B-022 at the Steps harbour, though those read as content placement-box and material-path '
    'faults - the shared symptom may have two causes. |\n'
    '| B-024 | P1 | A Barrowback ranges onto the Charwood deer ground, so the wildlife encounter is a fight | '
    '`claude_motion_deer04\\near.png`: a walk to the deer band at (612,718) opened with "BARROWBACK - Level 5: Deadly" and a stampede for 40 damage '
    'at three metres | **Claude - fauna lane** | `spawn.hollow.foldscar_barrowbacks` holds a herd of three at (588,618) and '
    '`content/spawns/old_late/brute.yaml` a single roamer at (650,735); the roamer\'s radius is 0 and it wanders, which puts it on the deer band. '
    'Fix direction: keep the hostile brute north of the road, or give the deer band a berth clear of it, then re-check with a deer-ground frame. |\n'
)

if 'B-023' in text:
    print('rows already filed')
else:
    assert ANCHOR in text, 'anchor row missing'
    PATH.write_text(text.replace(ANCHOR, ROWS + ANCHOR, 1), encoding='utf-8')
    print('filed B-023 and B-024')
