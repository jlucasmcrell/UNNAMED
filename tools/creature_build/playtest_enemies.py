"""Derive the three playtest enemies from held ready_new assets; no source files are modified.
Usage: python playtest_enemies.py --source <ready_new> --out <private asset root>
Requires NumPy, SciPy and Pillow. Licensed meshes/textures stay outside Git.
"""
from pathlib import Path
from copy import deepcopy
import argparse, hashlib, io, json, struct
import numpy as np
from PIL import Image
from scipy.spatial.transform import Rotation as R, Slerp

class Glb:
 def __init__(self,path):
  raw=Path(path).read_bytes();n=struct.unpack_from('<I',raw,12)[0]
  self.doc=json.loads(raw[20:20+n]);self.bin=bytearray(raw[28+n:])
 def array(self,index):
  a=self.doc['accessors'][index];v=self.doc['bufferViews'][a['bufferView']]
  dtype={5126:'<f4',5123:'<u2',5125:'<u4',5121:'u1'}[a['componentType']]
  count={'SCALAR':1,'VEC2':2,'VEC3':3,'VEC4':4,'MAT4':16}[a['type']]
  assert not a.get('sparse') and not v.get('byteStride'), 'Expected contiguous converted glTF'
  return np.frombuffer(self.bin,dtype=dtype,count=a['count']*count,offset=v.get('byteOffset',0)+a.get('byteOffset',0)).reshape(-1,count).copy()
 def blob(self,data):
  self.bin+=b'\0'*((-len(self.bin))%4);offset=len(self.bin);self.bin+=data
  self.doc['bufferViews'].append({'buffer':0,'byteOffset':offset,'byteLength':len(data)})
  return len(self.doc['bufferViews'])-1
 def add(self,values,kind='VEC3',component=5126):
  values=np.asarray(values,dtype='<f4' if component==5126 else '<u4')
  a={'bufferView':self.blob(values.tobytes()),'componentType':component,'count':len(values),'type':kind}
  if kind=='SCALAR':a.update(min=[float(values.min())],max=[float(values.max())])
  self.doc['accessors'].append(a);return len(self.doc['accessors'])-1
 def image(self,texture,im):
  stream=io.BytesIO();im.save(stream,format='PNG')
  image=self.doc['textures'][texture]['source'];self.doc['images'][image]={'bufferView':self.blob(stream.getvalue()),'mimeType':'image/png'}
 def texture(self,texture):
  im=self.doc['images'][self.doc['textures'][texture]['source']];v=self.doc['bufferViews'][im['bufferView']]
  return Image.open(io.BytesIO(self.bin[v.get('byteOffset',0):v.get('byteOffset',0)+v['byteLength']])).convert('RGBA')
 def save(self,path):
  self.doc['buffers']=[{'byteLength':len(self.bin)}]
  j=json.dumps(self.doc,separators=(',',':')).encode();j+=b' '*((-len(j))%4);b=bytes(self.bin);b+=b'\0'*((-len(b))%4)
  Path(path).parent.mkdir(parents=True,exist_ok=True)
  Path(path).write_bytes(struct.pack('<III',0x46546c67,2,28+len(j)+len(b))+struct.pack('<II',len(j),0x4e4f534a)+j+struct.pack('<II',len(b),0x004e4942)+b)

def recolour(g,index,colours,range_):
 mat=g.doc['materials'][index];pbr=mat['pbrMetallicRoughness'];tex=pbr['baseColorTexture']['index']
 im=g.texture(tex);a=np.asarray(im).copy();rgb=a[:,:,:3]/255.;lum=rgb@np.array([.2126,.7152,.0722])
 mask=lum>.012;low,high=np.percentile(lum[mask],[3,97]);detail=np.clip((lum-low)/max(.001,high-low),0,1)
 value=range_[0]+detail*(range_[1]-range_[0]);a[:,:,:3]=np.clip(value[:,:,None]*np.array(colours)*255,0,255).astype('uint8')
 g.image(tex,Image.fromarray(a));return mat

def derive_body(source,out,old,new):
 g=Glb(source/old/(old+'.glb'));j=g.doc
 if 'bone_walker' in new:
  recolour(g,0,[.88,.83,.72],[.11,.43])
  mat=recolour(g,1,[1.,.94,.80],[.30,.90]);pbr=mat['pbrMetallicRoughness'];pbr.pop('metallicRoughnessTexture',None);pbr.update(metallicFactor=0,roughnessFactor=.91)
  # Remove the luminous eyeball; the skull retains empty sockets.
  for mesh in j['meshes']:mesh['primitives']=[p for p in mesh['primitives']if p.get('material')!=2]
 elif 'empty_armour' in new:
  # The second primitive is a solid black visor/interior, not plate. Remove it at every LOD.
  head=j['skins'][0]['joints'].index(next(i for i,n in enumerate(j['nodes'])if n.get('name')=='Head'))
  for mesh in j['meshes']:
   mesh['primitives']=[p for p in mesh['primitives']if p.get('material')!=1]
   for p in mesh['primitives']:
    pos=g.array(p['attributes']['POSITION']);weights=g.array(p['attributes']['WEIGHTS_0']);joints=g.array(p['attributes']['JOINTS_0']);tri=g.array(p['indices']).reshape(-1,3)
    c=pos[tri].mean(1);headweight=((joints==head)*weights).sum(1)[tri].mean(1)
    opening=(headweight>.7)&(c[:,2]<-.045)&(abs(c[:,0])<.09)&(c[:,1]>1.70)&(c[:,1]<1.89)
    p['indices']=g.add(tri[~opening].reshape(-1,1),'SCALAR',5125)
  mat=recolour(g,0,[.84,.88,.93],[.12,.60]);pbr=mat['pbrMetallicRoughness'];pbr.update(metallicFactor=.85,roughnessFactor=.9);mat['doubleSided']=True
 else:
  # Uniform scale and body-centred origin. Legs extend beyond the combat body's unchanged 0.5 m radius.
  roots=j['scenes'][j.get('scene',0)]['nodes'];j['nodes'].append({'name':'PlaytestSpiderScale','scale':[2.7]*3,'translation':[-.116,0,.229],'children':roots})
  j['scenes'][j.get('scene',0)]['nodes']=[len(j['nodes'])-1]
 for mesh in j['meshes']:mesh['name']=mesh['name'].replace(old,new)
 for node in j['nodes']:
  if 'name'in node:node['name']=node['name'].replace(old,new)
 g.save(out/'rigged'/new/(new+'_rigged.glb'))
 return g

def base_pose(g):
 pose={i:{k:np.array(n.get(k,{'translation':[0,0,0],'rotation':[0,0,0,1],'scale':[1,1,1]}[k]),dtype=float)for k in ('translation','rotation','scale')}for i,n in enumerate(g.doc['nodes'])}
 for ch in g.doc['animations'][0]['channels']:
  s=g.doc['animations'][0]['samplers'][ch['sampler']];pose[ch['target']['node']][ch['target']['path']]=g.array(s['output'])[0].astype(float)
 return pose

def globals_(g,pose):
 result={};parents={c:i for i,n in enumerate(g.doc['nodes'])for c in n.get('children',[])}
 def at(i):
  if i not in result:
   p=pose[i];m=np.eye(4);m[:3,:3]=R.from_quat(p['rotation']).as_matrix()@np.diag(p['scale']);m[:3,3]=p['translation'];result[i]=at(parents[i])@m if i in parents else m
  return result[i]
 for i in pose:at(i)
 return result,parents

def world_turn(g,pose,node,axis,angle):
 glob,parents=globals_(g,pose);parent=R.from_matrix(glob[parents[node]][:3,:3])if node in parents else R.identity()
 pose[node]['rotation']=(parent.inv()*R.from_rotvec(np.asarray(axis)*np.deg2rad(angle))*parent*R.from_quat(pose[node]['rotation'])).as_quat()

def ground(g,body,pose):
 # Keep the actual skinned vertices above the floor at authored keys; visual review remains authoritative.
 byname={n.get('name'):pose[i] for i,n in enumerate(g.doc['nodes'])};bp={i:{k:np.array(n.get(k,{'translation':[0,0,0],'rotation':[0,0,0,1],'scale':[1,1,1]}[k]),float)for k in ('translation','rotation','scale')}for i,n in enumerate(body.doc['nodes'])}
 for i,n in enumerate(body.doc['nodes']):
  if n.get('name')in byname:bp[i]=byname[n['name']]
 # The wrapper is a display transform, excluded when grounding animation coordinates.
 for i,n in enumerate(body.doc['nodes']):
  if n.get('name')=='PlaytestSpiderScale':bp[i]['scale']=np.ones(3);bp[i]['translation']=np.zeros(3)
 glob,_=globals_(body,bp);skin=body.doc['skins'][0];ibm=body.array(skin['inverseBindMatrices']).reshape(-1,4,4).transpose(0,2,1);mat=np.array([glob[i]for i in skin['joints']])@ibm
 low=1e9
 for p in body.doc['meshes'][0]['primitives']:
  a=p['attributes'];v=np.c_[body.array(a['POSITION']),np.ones(len(body.array(a['POSITION'])))];w=body.array(a['WEIGHTS_0']);j=body.array(a['JOINTS_0']).astype(int)
  deformed=np.einsum('nv,nvij,nj->ni',w,mat[j],v);low=min(low,float(deformed[:,1].min()))
 root=next(i for i,n in enumerate(g.doc['nodes'])if n.get('name')=='root.x');pose[root]['translation'][1]-=low

def author_spider(g,body,state):
 base=base_pose(g);names={n.get('name'):i for i,n in enumerate(g.doc['nodes'])};glob,parents=globals_(g,base);root=names['root.x'];tail=names['c_tail_00.x']
 times={'idle':[0,.6,1.2,1.8,2.4],'attack':[0,.20,.45,.60,.70,1.0],'hit':[0,.08,.18,.38],'death':[0,.18,.45,.8,1.3]}[state]
 poses=[]
 upper=[i for name,i in names.items()if name and name.startswith('thigh_stretch')];lower=[i for name,i in names.items()if name and name.startswith('leg_stretch')]
 front=sorted(upper,key=lambda i:glob[i][2,3],reverse=True)[:2]
 for t in times:
  p=deepcopy(base)
  if state=='idle':
   world_turn(g,p,tail,[1,0,0],.9*np.sin(t/2.4*np.pi*2))
   for name in ('c_ear_01.l','c_ear_01.r'):world_turn(g,p,names[name],[0,1,0],1.5*np.sin(t/2.4*np.pi*2)*(1 if name.endswith('.l')else -1))
  elif state=='attack':
   wind=np.interp(t,[0,.45,.60,.70,1],[0,1,0,0,0]);strike=np.interp(t,[0,.45,.60,.70,1],[0,0,1,.65,0])
   p[root]['translation']+=np.array([0,-.022*wind+.012*strike,-.045*wind+.14*strike]);world_turn(g,p,root,[1,0,0],-8*wind+9*strike)
   for i in front:world_turn(g,p,i,[1,0,0],-24*wind+8*strike)
   for name in ('c_ear_01.l','c_ear_01.r'):world_turn(g,p,names[name],[0,1,0],(20*wind-12*strike)*(1 if name.endswith('.l')else -1))
  elif state=='hit':
   recoil=np.interp(t,[0,.08,.18,.38],[0,1,.4,0]);p[root]['translation'][2]-=.045*recoil;world_turn(g,p,root,[1,0,0],-9*recoil);world_turn(g,p,tail,[0,0,1],6*recoil)
  else:
   progress=np.interp(t,[0,.18,.45,.8,1.3],[0,.08,.55,.92,1]);world_turn(g,p,root,[0,0,1],13*progress)
   for i in upper+lower:
    radial=glob[i][:3,3]-glob[root][:3,3];radial[1]=0;radial/=max(.001,np.linalg.norm(radial));axis=np.cross(radial,[0,1,0]);axis/=max(.001,np.linalg.norm(axis))
    world_turn(g,p,i,axis,(56 if i in upper else 105)*progress)
   world_turn(g,p,tail,[1,0,0],10*progress);p[root]['translation'][1]-=.13*progress
  ground(g,body,p);poses.append(p)
 return emit_poses(g,state,times,poses)

def emit_poses(g,state,times,poses):
 assert np.all(np.diff(times)>0), 'Animation keys must advance in time'
 for pose in poses:
  for values in pose.values():
   assert all(np.isfinite(v).all() for v in values.values()), 'Non-finite rig transform'
   assert abs(np.linalg.norm(values['rotation'])-1)<.002, 'Invalid rig quaternion'
 anim={'name':state,'samplers':[],'channels':[]};input_=g.add(np.array(times)[:,None],'SCALAR')
 for node in g.doc['skins'][0]['joints']:
  for path in ('translation','rotation','scale'):
   values=np.array([p[node][path]for p in poses])
   if path=='rotation':
    for i in range(1,len(values)):
     if np.dot(values[i-1],values[i])<0:values[i]*=-1
   anim['channels'].append({'sampler':len(anim['samplers']),'target':{'node':node,'path':path}})
   anim['samplers'].append({'input':input_,'output':g.add(values,'VEC4'if path=='rotation'else'VEC3'),'interpolation':'LINEAR'})
 g.doc['animations']=[anim];return times[-1]

def author_slam(g):
 base=base_pose(g);names={n.get('name'):i for i,n in enumerate(g.doc['nodes'])};times=[0,.60,1.08,1.44,1.68,2.4];poses=[]
 for t in times:
  p=deepcopy(base);wind=np.interp(t,times,[0,.7,1,0,0,0]);strike=np.interp(t,times,[0,0,0,1,.8,0]);world_turn(g,p,names['Torso'],[1,0,0],-7*wind+18*strike)
  for side,sign in (('L',1),('R',-1)):
   for part in ('Upperarm','Forearm'):
    node=names[part+'.'+side];glob,parents=globals_(g,p);current=R.from_matrix(glob[node][:3,:3]);direction=current.apply([0,1,0])
    target=np.array([sign*.16,.85,.25]if part=='Upperarm'else[-sign*.10,.85,-.1])if wind>0 else np.array([sign*.06,-.4,.8]if part=='Upperarm'else[0,-.8,.4])
    target/=np.linalg.norm(target);delta=R.align_vectors(target[None,:],direction[None,:])[0];mix=max(wind,strike)
    delta=Slerp([0,1],R.concatenate([R.identity(),delta]))([mix])[0];parent=R.from_matrix(glob[parents[node]][:3,:3]);p[node]['rotation']=(parent.inv()*delta*current).as_quat()
    if part=='Upperarm':
     pad=names['Shoulder Pad.'+side];pr=R.from_matrix(glob[parents[pad]][:3,:3]);p[pad]['rotation']=(pr.inv()*delta*pr*R.from_quat(p[pad]['rotation'])).as_quat()
  poses.append(p)
 return emit_poses(g,'attack',times,poses)

def main():
 ap=argparse.ArgumentParser(description=__doc__);ap.add_argument('--source',type=Path,required=True);ap.add_argument('--out',type=Path,required=True);args=ap.parse_args();out=args.out;out.mkdir(parents=True,exist_ok=True)
 pairs=[('creature_cursed_undead_soldier','creature_playtest_bone_walker'),('creature_armored_guard_knight','creature_playtest_empty_armour'),('creature_nephila_spider','creature_playtest_cave_spider')];manifest=[]
 for old,new in pairs:
  body=derive_body(args.source,out,old,new);meta=json.loads((args.source/old/(old+'_meta.json')).read_text());meta['asset_id']=new;meta['derived_from']=old;meta['source_model_sha256']=hashlib.sha256((args.source/old/(old+'.glb')).read_bytes()).hexdigest()
  if 'undead'in old:meta['source_url']='https://www.fab.com/listings/cf236a92-88d9-4f0d-af10-d4f878560b39'
  if 'knight'in old:meta['author']='DM-913'
  if 'spider'in old:meta['author']='huangyuanqiang'
  if 'seller credit needed'in meta['licence']:meta['licence']='CC BY 4.0 (held acquisition record); credit '+meta['author']
  states={}
  for state in ('idle','walk','run','attack','hit','death'):
   clipdir=args.source/old/'animation/ready/creatures';source=clipdir/('anim.creature.'+old+'.'+state+'.glb')
   authored='spider'in old and state not in ('walk','run')
   if not source.exists():source=clipdir/('anim.creature.'+old+'.walk.glb')
   if 'knight'in old and state=='attack':source=clipdir/('anim.creature.'+old+'.idle.glb')
   clip=Glb(source);record=json.loads((args.source/old/'animation/clips'/('anim.creature.'+old+'.'+('walk'if authored or not (args.source/old/'animation/clips'/('anim.creature.'+old+'.'+state+'.json')).exists()else state)+'.json')).read_text())
   if 'knight'in old and state=='attack':
    duration=author_slam(clip);record['source']={'kind':'authored two-handed overhead gauntlet slam from the held UAL idle pose','author':'OTHERREACH / Codex under owner direction','base_animation_author':'Quaternius','base_animation_license':'CC0 1.0','base_model_author':'DM-913','base_model_url':meta['source_url'],'license':meta['licence']}
   elif authored:
    duration=author_spider(clip,body,state);record.pop('authored_speed_m_s',None);record.pop('retime_factor',None)
    record['source']={'kind':'authored keyframes on the existing Nephila rig; no generative tools','author':'OTHERREACH / Codex under owner direction','base_model_author':'huangyuanqiang','base_model_url':meta['source_url'],'license':meta['licence'],'action':state}
   else:
    duration=record['duration_s']
    if 'spider'in old:
     if state=='run':
      factor=5.5/(.9*2.7)
      for s in clip.doc['animations'][0]['samplers']:s['input']=clip.add(clip.array(s['input'])/factor,'SCALAR')
      duration/=factor;record['retime_factor']=record.get('retime_factor',1)/factor;record['playback_factor_from_converted_walk']=factor
     record['authored_speed_m_s']=5.5 if state=='run'else .9*2.7
   if 'spider'in old and not authored:record['source'].update(author=meta['author'],license=meta['licence'],url=meta['source_url'])
   record['category']='locomotion'if state in ('walk','run')else'idle'if state=='idle'else'combat'
   record['tags']=[t for t in record.get('tags',[])if t!='looping']+(['looping']if state in ('idle','walk','run')else[])
   record.update(animation_id='anim.creature.'+new+'.'+state,duration_s=duration,loop=state in ('idle','walk','run'),root_motion=False,clip_type='move'if state in ('walk','run')else'attack'if state=='attack'else state)
   clip.save(out/'animation/ready/creatures'/('anim.creature.'+new+'.'+state+'.glb'))
   target=out/'animation/clips'/('anim.creature.'+new+'.'+state+'.json');target.parent.mkdir(parents=True,exist_ok=True);target.write_text(json.dumps(record,indent=2)+'\n');states[state]=record
  meta['source_missing_clips']=meta.get('missing_clips',{});meta['missing_clips']={};meta['source_clips']=meta.pop('clips',{});meta['clips']=states
  if 'spider'in old:
   meta['spec']['leg_count']=8;meta['source_transform']=meta.pop('transform');meta['transform']={'uniform_scale_from_conversion':2.7,'body_centred_translation_m':[-.116,0,.229],'rest_dimensions_m':{key:round(value*2.7,4)for key,value in meta['source_transform']['dimensions_m'].items()}}
  meta['game_integration']='candidate; native visual review required';target=out/'manifests'/(new+'.json');target.parent.mkdir(parents=True,exist_ok=True);target.write_text(json.dumps(meta,indent=2)+'\n');manifest.append(new)
  print('ENEMY_BUILT',new,flush=True)
 files=list((out/'rigged').glob('creature_playtest_*/*glb'))+list((out/'animation/ready/creatures').glob('anim.creature.creature_playtest_*.glb'))+list((out/'animation/clips').glob('anim.creature.creature_playtest_*.json'))+list((out/'manifests').glob('creature_playtest_*.json'))
 assert len(files)==42, 'Expected three models, eighteen clips/records and three provenance records'
 (out/'manifests/playtest_enemies.json').write_text(json.dumps({'models':manifest,'body_radii_m':dict(zip(manifest,[.35,.45,.5])),'files':{p.relative_to(out).as_posix():hashlib.sha256(p.read_bytes()).hexdigest()for p in sorted(files)},'note':'Gameplay radii unchanged; spider uniformly 2.7x, body-centred origin; no generative assets'},indent=2)+'\n')
if __name__=='__main__':main()
