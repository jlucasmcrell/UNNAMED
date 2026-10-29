"""Flatten constant source armature ancestors, preserving all world joint motion."""
import copy,json,struct
from pathlib import Path
import numpy as np
from scipy.spatial.transform import Rotation
from audit_humgen_transport import Glb

def flatten(source,destination):
 old=Glb(source);doc=copy.deepcopy(old.doc);data=bytearray(old.binary);joints=set(old.joints)
 roots=[i for i in old.joints if old.parents.get(i) not in joints];duration=max(float(old.accessor(s['input'])[-1,0])for s in old.doc['animations'][0]['samplers'])
 def append(index,values):
  values=np.asarray(values,dtype='<f4');data.extend(b'\0'*(-len(data)%4));view=len(doc['bufferViews']);doc['bufferViews'].append(dict(buffer=0,byteOffset=len(data),byteLength=values.nbytes));data.extend(values.tobytes());a=copy.deepcopy(doc['accessors'][index]);a.update(bufferView=view,byteOffset=0);a.pop('min',None);a.pop('max',None);doc['accessors'].append(a);return len(doc['accessors'])-1
 for root in roots:
  parent=old.parents.get(root)
  if parent is None:continue
  old.names['__parent__']=parent;ancestor=old.globals()['__parent__']
  for t in np.linspace(0,duration,5):assert np.max(np.abs(old.globals(t)['__parent__']-ancestor))<1e-5,'Animated armature ancestor must be baked separately'
  scale=np.linalg.norm(ancestor[:3,:3],axis=0);assert np.max(scale)-np.min(scale)<1e-5
  rot=Rotation.from_matrix(ancestor[:3,:3]/scale)
  node=doc['nodes'][root];q=rot*Rotation.from_quat(node.get('rotation',[0,0,0,1]));node['rotation']=q.as_quat().tolist();node['translation']=(ancestor[:3,:3]@np.array(node.get('translation',[0,0,0]))+ancestor[:3,3]).tolist();node['scale']=(scale*np.array(node.get('scale',[1,1,1]))).tolist();assert 'matrix'not in node
  for animation in doc['animations']:
   originals=list(animation['samplers'])
   for c in animation['channels']:
    if c['target']['node']!=root:continue
    sampler=dict(originals[c['sampler']]);v=old.accessor(sampler['output']);kind=c['target']['path']
    if kind=='rotation':v=(rot*Rotation.from_quat(v)).as_quat()
    elif kind=='translation':v=v@ancestor[:3,:3].T+ancestor[:3,3]
    elif kind=='scale':v=v*scale
    else:raise ValueError(kind)
    sampler['output']=append(sampler['output'],v);c['sampler']=len(animation['samplers']);animation['samplers'].append(sampler)
  doc['nodes'][parent]['children'].remove(root)
  for scene in doc['scenes']:scene['nodes'].append(root)
 old.names.pop('__parent__',None);doc['buffers'][0]['byteLength']=len(data);enc=json.dumps(doc,separators=(',',':')).encode();enc+=b' '*(-len(enc)%4);data.extend(b'\0'*(-len(data)%4));destination.parent.mkdir(parents=True,exist_ok=True);destination.write_bytes(struct.pack('<III',0x46546c67,2,28+len(enc)+len(data))+struct.pack('<II',len(enc),0x4e4f534a)+enc+struct.pack('<II',len(data),0x004e4942)+data)
 new=Glb(destination);error=0
 for t in [None,*np.linspace(0,duration,5)]:
  a,b=old.globals(t),new.globals(t);error=max(error,max(float(np.max(np.abs(a[n]-b[n])))for n in a))
 assert error<.0001,(source,error)
 return dict(source=str(source),output=str(destination),world_joint_matrix_error=error)
if __name__=='__main__':
 import sys
 print(json.dumps(flatten(Path(sys.argv[1]),Path(sys.argv[2])),indent=2))
