"""Prepare faithful scene-unit, upright-reference transports for Epic glTF import."""
import argparse,json,shutil
from pathlib import Path
import numpy as np
from audit_humgen_transport import Glb
from normalize_source_skin_units import normalize
from flatten_source_armature import flatten

def prepare(inventories,output):
 rows=[];report=[];output.mkdir(parents=True,exist_ok=True)
 for inventory in inventories:
  for original in json.loads(inventory.read_text()):
   if original['status']!='SOURCE_EXPORTED_NOT_RETARGETED':continue
   row=dict(original);source=Path(row['path']);stage=output/'units'/(row['id']+'.glb');dest=output/(row['id']+'.glb');receipt=dest.with_suffix('.json')
   if receipt.exists() and dest.exists():
    prior=json.loads(receipt.read_text());rows.append(prior['row']);report.append(prior['verification']);continue
   units=normalize(source,stage)
   if units['status']=='ALREADY_SCENE_UNITS':shutil.copy2(source,stage)
   verification=flatten(stage,dest);g=Glb(source);rates=[]
   for a in g.doc.get('animations',[]):
    for sampler in a['samplers']:
     times=g.accessor(sampler['input'])[:,0]
     if len(times)>2 and times[-1]>times[0]:rates.append(float(1/np.median(np.diff(times))))
   fps=int(round(max(rates))) if rates else 30
   assert fps>0 and fps<=1000,(source,fps)
   row.update(path=str(dest.resolve()),original_transport_path=str(source),source_sample_rate=fps,source_import_version=3)
   verification.update(units=units,source_sample_rate=fps)
   receipt.write_text(json.dumps(dict(row=row,verification=verification),indent=2));rows.append(row);report.append(verification)
   (output/'inventory.json').write_text(json.dumps(rows,indent=2));print('SOURCE_FRAME_READY',len(rows),row['id'],fps,flush=True)
 (output/'inventory.json').write_text(json.dumps(rows,indent=2));(output/'verification.json').write_text(json.dumps(report,indent=2));print('DONE',len(rows),flush=True)
if __name__=='__main__':
 p=argparse.ArgumentParser();p.add_argument('output',type=Path);p.add_argument('inventories',nargs='+',type=Path);a=p.parse_args();prepare(a.inventories,a.output)
