"""Layer the completed delta into an isolated review root, sharing immutable art."""
import json,os,subprocess,sys
from pathlib import Path
baseline=Path('D:/Otherreach_Scratch/codex_f1_siann_wt/work/creator/game/world_fallback')
delta=Path(sys.argv[1]) if len(sys.argv)>1 else Path('D:/gc_delivery/game/primary')
out=Path(sys.argv[2]) if len(sys.argv)>2 else Path('D:/gc_finalreview/world_fallback')
(out/'rigged').mkdir(parents=True,exist_ok=True)
def link(path,target):
    if path.exists():return
    if target.is_dir():
        subprocess.run(['cmd','/c','mklink','/J',str(path),str(target)],check=True,stdout=subprocess.DEVNULL)
    else:os.link(target,path)
for file in baseline.iterdir():
    if file.name not in ('rigged','texture_cache'):link(out/file.name,file)
for folder in (baseline/'rigged').iterdir():
    if not (delta/'rigged'/folder.name).exists():link(out/'rigged'/folder.name,folder)
for folder in (delta/'rigged').iterdir():link(out/'rigged'/folder.name,folder)
entries={}
for root in [baseline,delta]:
    manifest=root/'texture_cache/manifest.json'
    if not manifest.is_file():continue
    for key,value in json.loads(manifest.read_text(encoding='utf-8-sig'))['entries'].items():
        entry=dict(value);entry['dds']=str((manifest.parent/entry['dds']).resolve());entries[key]=entry
(out/'texture_cache').mkdir(exist_ok=True)
(out/'texture_cache/manifest.json').write_text(json.dumps(dict(entries=entries)))
print('ISOLATED_ROLE_REVIEW_READY',len(entries),'signed cache keys',out)
