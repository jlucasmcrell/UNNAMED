"""Build a corrected source supplement over the complete retained native library."""
import argparse,json,shutil
from pathlib import Path

def prepare(job):
 report=json.loads((job/'extra_sources.json').read_text());assert report['status']=='DONE' and not report['failures']
 sources=[r for r in report['sources']if r.get('source_import_version')==3];assert len(sources)==632
 ids={r['id']for r in sources};canonical={}
 for actor in ('humgen_creator_female','humgen_creator_male'):
  full=job/('retarget_'+actor+'.json');base=job/('retarget_base_'+actor+'.json');previous=json.loads(base.read_text())
  if not previous.get('source_frames_repair_baseline'):
   current=json.loads(full.read_text());assert current['status']=='DONE' and len(current['clips'])>4000
   assert previous['body_sha256']==current['body_sha256'];backup=base.with_suffix('.before_source_frames.json')
   if not backup.exists():shutil.copy2(base,backup)
   clips={c['state']:c for c in previous['clips']};clips.update({c['state']:c for c in current['clips']});previous=current|dict(clips=list(clips.values()),source_frames_repair_baseline=True);base.write_text(json.dumps(previous,indent=2))
  for clip in previous['clips']:
   provider,name=clip['source'].split('/',1)
   if provider in ids and clip['state'].startswith('owned_'+provider+'_'):
    canonical[provider]=(clip['state'],name)
 assert len(canonical)==len(ids),(len(canonical),len(ids))
 selection=json.loads((job/'selection.json').read_text())
 selection={k:v for k,v in selection.items()if not(v[0]in ids and k.startswith('owned_'))}
 for source in sources:
  takes=[a for a in source['assets']if a['kind']=='animation'];assert len(takes)==1,source['id']
  state,alias=canonical[source['id']];takes[0]['name']=alias;selection[state]=[source['id'],alias]
  for key,value in list(selection.items()):
   if value[0]==source['id']:selection[key]=[source['id'],alias]
 subset={k:v for k,v in selection.items()if v[0]in ids}
 (job/'source_frames_extra.json').write_text(json.dumps(dict(status='DONE',sources=sources,failures=[]),indent=2));(job/'source_frames_selection.json').write_text(json.dumps(subset,indent=2));(job/'selection.json').write_text(json.dumps(selection,indent=2));print('Corrected source supplement:',len(sources),'groups /',len(subset),'states; complete native baselines retained')
if __name__=='__main__':
 p=argparse.ArgumentParser();p.add_argument('job',type=Path);a=p.parse_args();prepare(a.job)
