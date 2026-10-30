"""Update the millpond row and queue a crow standpoint that stands back far enough for the sweep to reach the flock."""
import json
from pathlib import Path

DOC = Path(r'D:\Otherreach_Scratch\ds_fish2_wt\docs\phase_b\FAUNA_CAPTURES.md')
text = DOC.read_text(encoding='utf-8')
OLD = '| `claude_cap_millpond` | did not finish inside the job window | **no result** |'
NEW = ('| `claude_cap_millpond` | banner "MALLARD - Level 1: Even. Passive - can be hunted." over the millpond with '
       '"LANDMARK DISCOVERED: CAIRNWOLD"; the ducks are too small to make out at this range | **good for the banner**, ducks unproven here |')
if OLD in text:
    DOC.write_text(text.replace(OLD, NEW, 1), encoding='utf-8')
    print('millpond row updated')
else:
    print('millpond anchor missing')

JOBS = Path(r'C:\Otherreach_Farm\jobs')
source = JOBS / 'claude_cap_moor_crows.json'
if not source.exists():
    print('source job gone (the runner consumed it); rebuilding from the template job')
    source = JOBS / 'claude_fix_boulders01.json'

job = json.loads(source.read_text(encoding='utf-8'))
args = job['args']
shot = next(i for i, a in enumerate(args) if a == '--shot')
args[shot + 1] = '1046,690,90,-0.05,8'
job['env'].pop('UNNAMED_SCENERY_PROBE', None)
out = JOBS / 'claude_cap_moor_crows2.json'
out.write_text(json.dumps(job, indent=2), encoding='utf-8')
print('queued', out.name, 'shot', args[shot + 1])
