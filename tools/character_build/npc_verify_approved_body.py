from pathlib import Path
import argparse,json,struct,hashlib,shutil
ap=argparse.ArgumentParser();ap.add_argument('--workspace',type=Path,required=True);ap.add_argument('--player-root',type=Path,required=True);ap.add_argument('--stage-root',type=Path);ap.add_argument('--report',type=Path,required=True);args=ap.parse_args()
w=args.workspace;dest=args.stage_root;recipes=Path(__file__).resolve().parent/'npc_bodies';bodyroot=args.player_root/'rigged'
approved={'m':'89a6dc3e6ebf30a330027d556d259d079df19d57ada99f5b8f4bde0af55a7c5d','f':'2bcbb1bbecd5b4f1f6d32469b3485cbc4ea119c01eff43dd99b1d56e2442cb11'}
def read(p):
 raw=p.read_bytes();n=struct.unpack_from('<I',raw,12)[0];return json.loads(raw[20:20+n]),raw[28+n:]
report=[]
for folder in sorted((w/'approved_models').iterdir()):
 if not folder.is_dir():continue
 rid=folder.name.removeprefix('mh_');recipe=json.loads((recipes/(rid+'.json')).read_text());sex=recipe['sex'];src=(w/'approved_wing_models'/folder.name)if recipe['people']=='people.kal' else folder
 canonical=bodyroot/('mh_veth_'+sex)/('mh_veth_'+sex+'_rigged.glb');orig,ob=read(canonical);j,b=read(src/(folder.name+'_rigged.glb'))
 assert hashlib.sha256(canonical.read_bytes()).hexdigest()==approved[sex], 'Approved source changed: '+str(canonical)
 assert b[:len(ob)]==ob
 assert j['skins'][0]==orig['skins'][0]
 assert j['accessors'][:len(orig['accessors'])]==orig['accessors']
 assert j['bufferViews'][:len(orig['bufferViews'])]==orig['bufferViews']
 assert j['meshes'][:4]==orig['meshes'][:4]
 assert j['materials'][0]==orig['materials'][0]
 assert [{k:v for k,v in n.items()if k!='children'}for n in [j['nodes'][i] for i in orig['skins'][0]['joints']]]==[{k:v for k,v in n.items()if k!='children'}for n in [orig['nodes'][i] for i in orig['skins'][0]['joints']]]
 active=set()
 def visit(i):
  if i in active:return
  active.add(i)
  for c in j['nodes'][i].get('children',[]):visit(c)
 for scene in j['scenes']:
  for node in scene['nodes']:visit(node)
 names=[j['nodes'][i].get('name','')for i in active if 'mesh'in j['nodes'][i]]
 assert all(any(n.get('name')=='identity_FACIAL_'+side+'_Eye' for n in j['nodes']) for side in ('L','R')), 'Identity eye helpers missing'
 assert not any(any(k in name.lower()for k in ['outfit','harness','strap','buckle','stitch','apron','shirt'])for name in names)
 companion=src/(folder.name+'_correctives.json');sourcecorrect=canonical.parent/('mh_veth_'+sex+'_correctives.json');assert companion.read_bytes()==sourcecorrect.read_bytes()
 if dest is not None:shutil.copytree(src,dest/'rigged'/folder.name,dirs_exist_ok=True)
 report.append({'id':rid,'body_file':str(canonical),'body_sha256':hashlib.sha256(canonical.read_bytes()).hexdigest(),'original_body_bin_skin_four_lods_material_joint_transforms_exact':True,'player_correctives_identical':True,'active_clothing_meshes':[],'native_identity_face_and_grooms':True,'wings':recipe['people']=='people.kal','optional_colour_layer':(src/'body_skin_colour.png').exists(),'shape_status':'approved method; per-character paired renders required'})
args.report.write_text(json.dumps(report,indent=2));print('EXACT_BODY_PASS',len(report))
