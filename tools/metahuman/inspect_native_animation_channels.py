"""Read-only native animation channel diagnostic; no transforms are rewritten."""
from pathlib import Path as _PipelinePath
import sys as _pipeline_sys
_pipeline_sys.path.insert(0, str(_PipelinePath(__file__).parent))
from pipeline_paths import pipeline_root as _pipeline_root
import json,struct,sys
from pathlib import Path
import numpy as np
root=Path((_pipeline_root().as_posix() + ''))
for stage in sys.argv[1:] or ['epic','clean']:
 path=root/'animations'/stage/'ada/idle.glb';raw=path.read_bytes();n=struct.unpack_from('<I',raw,12)[0];g=json.loads(raw[20:20+n]);binary=raw[28+n:]
 def read(i):
  a=g['accessors'][i];v=g['bufferViews'][a['bufferView']];w={'SCALAR':1,'VEC3':3,'VEC4':4}[a['type']]
  return np.ndarray((a['count'],w),np.dtype('<f4'),binary,offset=v.get('byteOffset',0)+a.get('byteOffset',0),strides=(v.get('byteStride',4*w),4))
 print(stage)
 for name in ['root','pelvis','spine_01','thigh_l']:
  node=next(i for i,n in enumerate(g['nodes']) if n.get('name')==name)
  print(name,'node',g['nodes'][node].get('translation'),g['nodes'][node].get('rotation'),g['nodes'][node].get('scale'))
  for channel in g['animations'][0]['channels']:
   if channel['target']['node']==node:
    values=read(g['animations'][0]['samplers'][channel['sampler']]['output'])
    print(channel['target']['path'],'mid',values[len(values)//2].tolist())
