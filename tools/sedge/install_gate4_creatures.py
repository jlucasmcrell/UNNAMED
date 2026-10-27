"""Install the Gate-4 creature art into an offline private primary.
Args: PRIMARY DROWNED_CONVERTED EEL_CONVERTED ACCEPTED_FALLBACK.
Does not modify the accepted HumGen body, rest, clothing or native animation files.
"""
from pathlib import Path
import json,shutil,sys
out,drowned,eel,fallback=map(Path,sys.argv[1:5]);b=json.loads((out/'art_bindings.json').read_text());b.setdefault('creatures',{})
for id,definition,src in [('sedge_drowned','creature.undead.sedge_drowned',drowned),('sedge_eel_serpent','creature.beast.sedge_eel_serpent',eel)]:
 target=out/'rigged'/id;target.mkdir(parents=True,exist_ok=True)
 shutil.copy2(src/(id+'.glb'),target/(id+'_rigged.glb'));shutil.copy2(src/(id+'_meta.json'),target/(id+'_meta.json'));shutil.copytree(src/'animation',out/'animation',dirs_exist_ok=True)
 clips={s:'creature.'+id+'.'+s for s in ['idle','walk','run','hit','death']};clips['attack']='creature.'+id+'.light_1'
 for move in ['light','heavy','rush']:
  for variant in [1,2]:clips['ability.creature.'+id+'_'+move+'_'+str(variant)]='creature.'+id+'.'+move+'_'+str(variant)
 b['creatures'][definition]=dict(model=id,clips=clips,voice='bone_walker_husk' if id=='sedge_drowned' else '',flesh='flesh',blow='claw' if id=='sedge_drowned' else 'bite',steps={'walk':'walk','run':'move_fast'},paces=dict(walk=1.88 if id=='sedge_drowned' else 1.3,run=6.1 if id=='sedge_drowned' else 3.3))
 for f in (out/'animation/clips').glob('anim.creature.'+id+'.*.json'):
  d=json.loads(f.read_text());state=f.stem.split('.')[-1]
  if state.startswith(('light_','heavy_','rush_')):d['category']='combat';f.write_text(json.dumps(d,indent=2))
base=json.loads((fallback/'bindings.json').read_text());aliases={}
for move,states in [('light',['sword_attack_1','sword_attack_2']),('heavy',['sword_attack_3','sword_attack_2']),('rush',['sword_attack_1','sword_attack_3'])]:
 for i,state in enumerate(states):aliases['ability.creature.back_ledger_raider_'+move+'_'+str(i+1)]=state
b['creatures']['creature.humanoid.back_ledger_raider']=dict(model='humgen_pool_03',clips=base['people']['resident.veth.02']['clips'],native_states=aliases,weapon_item='item.weapon.rusted_sword',voice='',flesh='flesh',blow='',steps={'walk':'walk','run':'run'},paces={'walk':1.7,'run':4.4})
(out/'art_bindings.json').write_text(json.dumps(b,indent=2))
credits=json.loads((out/'SEDGE_SOURCE_CREDITS.json').read_text());credits['gate4_sources']=[
 dict(asset='sedge_drowned',author='DM-913',licence='CC BY 4.0',url='https://sketchfab.com/3d-models/cursed-undead-soldier-rig-b2e39de76b834d978adee5716aec814d',motion='Quaternius Universal Animation Library 1, CC0; existing CPU retarget pipeline; native2K maps retained'),
 dict(asset='sedge_eel_serpent',author='KrazyKaijus (pincers60)',licence='CC BY 4.0',url='https://sketchfab.com/3d-models/eel-monster-rigged-7747294b15b348cabd6f6a7c3f741f74',changes='Native anatomy/weights/rest; uniform metric transform; original authored native-rig poses; deterministic olive 4K pigment treatment; no AI inputs'),
 dict(asset='Sedge contacts, residents, workers, punt crew and Back Ledger raider',credit='Exact accepted HumGen cast, native baked motion and published garments; no anatomy/rest edits')]
(out/'SEDGE_SOURCE_CREDITS.json').write_text(json.dumps(credits,indent=2));print(out)
