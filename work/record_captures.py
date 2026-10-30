"""Record the seven captures from the claude_cap_* set, judged one by one."""
from pathlib import Path

PATH = Path(r'D:\Otherreach_Scratch\ds_fish2_wt\docs\phase_b\FAUNA_CAPTURES.md')
text = PATH.read_text(encoding='utf-8')

SECTION = '''
## The `claude_cap_*` set (2026-10-30 15:10-15:20, `claude_min` on the farm)

Seven standpoints from `work\\fauna_capture_all.ps1`, judged frame by frame:

| job | what the frame shows | verdict |
|---|---|---|
| `claude_cap_elk_band` | banner "UPLAND ELK - Level 3: Hard. Neutral - can be hunted." with the animal and its pale antlers on the ground at the left; no sky rock in view | **good** - the elk's best frame so far; the prone pose is B-025 |
| `claude_cap_oldlate_hares` | banner "TIMBER HIND - Level 1: Even."; **four deer standing in the grass**, confirmed on a 3x crop (`oldlate_deer_right.png`); a Barrowback at the left edge | **good** for deer, hare not in frame |
| `claude_cap_charwood_deer` | three Timber Hind rows in the target list, no deer in shot; a large boulder hangs in the sky at the upper right | partial - deer present by target rows only; the rock is B-023 |
| `claude_cap_moor_crows` | the camera stands inside the player's own head, so the flock cannot be seen; the frame is the player's scalp and a Frost Hare banner | **unusable standpoint** - crows still not captured |
| `claude_cap_sedge_ducks` | the sedge renders as a bare grey plane - no ground detail, no water, no ducks - on this build and asset root | **unusable** - the duck proof stands on `claude_fauna_sedge01` instead |
| `claude_cap_field_deer` | the stand lands at the gate itself: "Animated Armour", "Gate Warden", "Gate Tender" targets and the industrial architecture, no deer | **wrong standpoint** - the deer field is not at (852,768) |
| `claude_cap_millpond` | did not finish inside the job window | **no result** |

**What the set did establish**: the elk photographed with its banner and body (`claude_cap_elk_band`), and deer standing in numbers at Old Late (four animals on
the crop). **What it did not**: crows, the field deer, the sedge, and anything at the millpond.
'''

if 'claude_cap_*` set' in text:
    print('already recorded')
else:
    PATH.write_text(text.rstrip() + '\n' + SECTION, encoding='utf-8')
    print('capture set recorded')
