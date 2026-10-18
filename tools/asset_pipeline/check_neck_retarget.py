import argparse,json,sys
from pathlib import Path
import numpy as np
sys.path.insert(0,str(Path(__file__).resolve().parent))
import _retarget_clip as r
parser=argparse.ArgumentParser(description='Check the accepted jog retarget: body/stride and relative cervical/head motion must be unchanged at twelve phases on both bodies.')
parser.add_argument('--source',required=True)
parser.add_argument('--male',required=True)
parser.add_argument('--female',required=True)
parser.add_argument('--out',required=True)
args=parser.parse_args()
src=r.Scene(args.source)
tr=src.tracks(src.clip_names()[0]); duration=src.duration(tr)
mapping=json.loads((Path(__file__).resolve().parent/'retarget_maps/ue5_manny_mh.json').read_text())
report={}
for sex in ('m','f'):
 target=r.Scene(args.male if sex=='m' else args.female)
 old=r.Retarget(src,target,mapping,stance='source'); new=r.Retarget(src,target,mapping,stance='source')
 new.calibrate_neck_pose(tr)
 rows=[]; neck_pitches=[]
 def worlds(rt,t):
  locals_,_,_,_=rt.frame(tr,t,'inplace','ik'); values=target.local()
  for n,v in locals_.items(): values[target.index[n]]=v
  return target.world(values)
 unchanged=('root','pelvis','spine_01','spine_02','spine_03','spine_04','spine_05',
            'clavicle_l','clavicle_r','upperarm_l','upperarm_r','hand_l','hand_r',
            'thigh_l','thigh_r','calf_l','calf_r','foot_l','foot_r')
 for phase in range(12):
  a=worlds(old,duration*phase/12); b=worlds(new,duration*phase/12)
  for bone in unchanged:
   assert np.allclose(a[target.index[bone]],b[target.index[bone]],rtol=0,atol=1e-9),bone
  assert np.allclose(target.pos(a,'neck_01'),target.pos(b,'neck_01'),rtol=0,atol=1e-9)
  hips_motion=target.rot(a,new.hips)@target.rot(new.T0,new.hips).T
  across=hips_motion@new.lat_t_axis; across[1]=0; across/=np.linalg.norm(across)
  correction=r.axis_angle(across,new.neck_pose_radians)
  direction=target.pos(b,'head')-target.pos(b,'neck_01')
  neck_pitches.append(np.arctan2(direction@np.cross(across,[0.,1.,0.]),direction[1]))
  assert np.allclose(correction@target.rot(a,'head'),target.rot(b,'head'),rtol=0,atol=1e-9), 'head did not follow the constant pose correction'
  for parent,child in (('neck_01','neck_02'),('neck_02','head')):
   assert np.allclose(target.rot(a,parent).T@target.rot(a,child),target.rot(b,parent).T@target.rot(b,child),rtol=0,atol=1e-9), 'relative cervical/head motion changed'
  for parent,child in (('neck_01','neck_02'),('neck_02','head')):
   la=np.linalg.norm(target.pos(a,child)-target.pos(a,parent)); lb=np.linalg.norm(target.pos(b,child)-target.pos(b,parent))
   assert abs(la-lb)<1e-9
  rows.append({'phase':phase/12,'head_mm_moved':float(np.linalg.norm(target.pos(a,'head')-target.pos(b,'head'))*1000)})
 mean_pitch=float(np.degrees(np.arctan2(np.sin(neck_pitches).sum(),np.cos(neck_pitches).sum())))
 assert abs(mean_pitch-new.neck_pose['target_bind_deg'])<.001, 'sustained neck pitch was not corrected to the target bind pose'
 report[sex]={'calibration':new.neck_pose,'mean_neck_pitch_deg':mean_pitch,'body_stride_shoulders_unchanged':True,'bone_lengths_unchanged':True,'phases':rows}
 print(sex,json.dumps(new.neck_pose),'PASS unchanged body/stride/shoulders/neck base and cervical lengths',flush=True)
Path(args.out).write_text(json.dumps(report,indent=2))

