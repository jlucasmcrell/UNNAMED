"""Rename the probe's env var to the OTHERREACH_ prefix the farm runner accepts, and repair both queued job files."""
import json
from pathlib import Path

PROBE = Path(r'D:\Otherreach_Scratch\ds_fish2_wt\src\Presentation\Greybox\SceneryProbe.cs')
text = PROBE.read_text(encoding='utf-8')
text = text.replace('OS.GetEnvironment("UNNAMED_SCENERY_PROBE")', 'OS.GetEnvironment("OTHERREACH_SCENERY_PROBE")')
text = text.replace('Set UNNAMED_SCENERY_PROBE=1', 'Set OTHERREACH_SCENERY_PROBE=1')
PROBE.write_text(text, encoding='utf-8')
print('probe env is OTHERREACH_SCENERY_PROBE now')

JOBS = Path(r'C:\Otherreach_Farm\jobs')
BASE_ENV = {'OTHERREACH_CHARACTER_ROOT': '{BUILD}/game/humgen_characters', 'OTHERREACH_LIGHTING_FAST': '1'}

boulders = json.loads((JOBS / 'claude_fix_boulders01.json').read_text(encoding='utf-8'))
boulders['record'] = False
boulders['env'] = dict(BASE_ENV, OTHERREACH_SCENERY_PROBE='1')
(JOBS / 'claude_fix_boulders01.json').write_text(json.dumps(boulders, indent=2), encoding='utf-8')

video = json.loads((JOBS / 'claude_proof_elkvideo01.json').read_text(encoding='utf-8').replace('"timeout_s", 900', '"timeout_s": 900'))
args = video['args']
args[args.index('--shot') + 1] = '1024,644,90,-0.08,10'
if '--fixed-fps' not in args:
    at = args.index('--shot') + 2
    args[at:at] = ['--fixed-fps', '60', '--capture-unfocused']
video['record'] = True
video['env'] = dict(BASE_ENV)
(JOBS / 'claude_proof_elkvideo01.json').write_text(json.dumps(video, indent=2), encoding='utf-8')

for name in ('claude_fix_boulders01', 'claude_proof_elkvideo01'):
    job = json.loads((JOBS / f'{name}.json').read_text(encoding='utf-8'))
    print(name, 'ok; record', job['record'], '; env', sorted(job['env']))
