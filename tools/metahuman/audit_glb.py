"""Read-only GLB cost/rig audit. Counts exported joints separately from actually weighted joints."""
import json,struct,sys
from pathlib import Path
import numpy as np
DTYPES={5120:'i1',5121:'u1',5122:'<i2',5123:'<u2',5125:'<u4',5126:'<f4'}
WIDTH={'SCALAR':1,'VEC2':2,'VEC3':3,'VEC4':4,'MAT4':16}
def audit(path):
 raw=Path(path).read_bytes();n,t=struct.unpack_from('<II',raw,12);g=json.loads(raw[20:20+n]);p=20+n;n,t=struct.unpack_from('<II',raw,p);binary=raw[p+8:p+8+n]
 def accessor(i):
  a=g['accessors'][i];v=g['bufferViews'][a['bufferView']];dt=np.dtype(DTYPES[a['componentType']]);w=WIDTH[a['type']];stride=v.get('byteStride',dt.itemsize*w)
  result=np.ndarray((a['count'],w),dt,buffer=binary,offset=v.get('byteOffset',0)+a.get('byteOffset',0),strides=(stride,dt.itemsize)).copy()
  if a.get('normalized') and a['componentType']!=5126:result=result.astype(np.float64)/np.iinfo(dt).max
  return result
 triangles=vertices=0;weighted=set();max_weights=0;bad_sums=0;eight=0
 for mesh in g.get('meshes',[]):
  for primitive in mesh['primitives']:
   attrs=primitive['attributes'];v=len(accessor(attrs['POSITION']));vertices+=v
   triangles+=(len(accessor(primitive['indices'])) if 'indices' in primitive else v)//3
   if 'JOINTS_0' not in attrs:continue
   joints=np.concatenate([accessor(attrs[k]) for k in sorted(attrs) if k.startswith('JOINTS_')],axis=1)
   weights=np.concatenate([accessor(attrs[k]) for k in sorted(attrs) if k.startswith('WEIGHTS_')],axis=1)
   active=weights>1e-7;weighted.update(int(x) for x in joints[active]);counts=active.sum(axis=1)
   max_weights=max(max_weights,int(counts.max()));eight+=int((counts==8).sum());bad_sums+=int((abs(weights.sum(axis=1)-1)>0.0002).sum())
 return dict(path=str(path),triangles=triangles,vertices=vertices,skin_joint_counts=[len(s['joints']) for s in g.get('skins',[])],weighted_joints=len(weighted),max_influences=max_weights,eight_weight_vertices=eight,invalid_weight_sums=bad_sums,zero_count_accessors=sum(a.get('count',0)==0 for a in g['accessors']),materials=[m.get('name','') for m in g.get('materials',[])])
if __name__=='__main__':
 for path in sys.argv[1:]:print(json.dumps(audit(path),indent=2))
