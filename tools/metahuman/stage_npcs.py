"""Stage finished native NPC exports into the runtime catalog. Never modifies source exports."""
from pathlib import Path as _PipelinePath
import sys as _pipeline_sys
_pipeline_sys.path.insert(0, str(_PipelinePath(__file__).parent))
from pipeline_paths import pipeline_root as _pipeline_root
from pathlib import Path
import json,shutil,hashlib
root=Path((_pipeline_root().as_posix() + ''));runtime=root/'runtime'
prep=json.loads((root/'evidence/npc_characters.json').read_text())
exports=json.loads((root/'evidence/npc_glb_export.json').read_text())
materials=json.loads((root/'evidence/npc_material_groom_export.json').read_text()) if (root/'evidence/npc_material_groom_export.json').exists() else {'characters':[]}
catalog=json.loads((runtime/'catalog.json').read_text())
styles=[
 ('labourer',[.78,.73,.55],False,False),('merchant',[.65,.45,.32],True,False),
 ('scholar',[.63,.66,.68],True,False),('smith',[.61,.54,.43],True,False),
 ('elder',[.57,.61,.63],True,True),('villager',[.69,.73,.54],False,False),
 ('traveller',[.57,.65,.57],True,True),('steward',[.63,.48,.40],True,False),
 ('labourer',[.76,.65,.50],False,False),('elder',[.62,.62,.60],True,True)]
dyes={'oatmeal':[.82,.76,.62],'madder':[.66,.24,.16],'woad':[.26,.34,.52],'walnut':[.42,.28,.16],'saffron':[.80,.62,.20],'charcoal':[.22,.22,.24]}
# DeepSeek npc_variants.gd period-dye palette; each NPC keeps its own approved part combination.
families=['saffron','madder','woad','walnut','charcoal','oatmeal','woad','madder','oatmeal','walnut']
required=['Body_Basecolor.png','Body_Normal.png','Body_SRMF.png','Head_Basecolor.png','Head_Normal.png','Head_SRMF.png','Teeth_Color.png','Teeth_Normal.png','Eyes_Color.png','Eyes_Normal.png','Eyelashes_Color.png']
for i,c in enumerate(prep['characters']):
 id=c['id'];dest=runtime/'npc'/id
 if not (root/'evidence'/('shells_'+id+'.json')).exists() or not (root/'evidence'/('clean_'+id+'.json')).exists() or not any(x['id']==id for x in materials['characters']):continue
 source=root/'exports'/id/'dcc'/('MHC_NPC_'+id)/'Maps'
 if any(not (source/name).exists() for name in required):raise RuntimeError('Missing required native DCC maps '+id)
 (dest/'Maps').mkdir(parents=True,exist_ok=True)
 for name in required:shutil.copy2(source/name,dest/'Maps'/name)
 clean=json.loads((root/'evidence'/('clean_'+id+'.json')).read_text())
 face_roles=next(x['face_surface_roles'] for x in clean['exports'] if x['part']=='head')
 exported=next(x for x in exports['characters'] if x['id']==id)
 parts={x['part']:x for x in exported['exports']}
 archetype,tint,vest,hood=styles[i]
 variant='shirt_'+('vest_' if vest else '')+'pants'+('_hood' if hood else '')+'_shells'
 config=dict(id=id,shells=id,dye_family=families[i],dye_color=dyes[families[i]],sex=c['sex'],body=f'npc/{id}/body.glb',head=f'npc/{id}/head.glb',maps=f'npc/{id}/Maps/',eye_manifest=f'npc/{id}/eyes.json',groom_manifest=f'npc/{id}/grooms.json',tint=tint,vest=vest,hood=hood,hidden_mask=f'masks/{id}_{variant}.png',body_lod=parts['body']['native_lod'],face_lod=parts['head']['native_lod'],body_bones=parts['body']['bone_count'],face_bones=parts['head']['bone_count'],face_pipeline="UE Optimized High (native reduced LOD set)",face_surface_roles=face_roles,max_texture_size=2048,archetype=archetype,preset=c['preset'],age_intent=c['age_intent'])
 catalog['characters'].setdefault(id,{}).update(config)
 catalog['npcs']=[x['id'] for x in prep['characters'] if x['id'] in catalog['characters']]
# Preserve established characters' presentation categories; the simulation/content remain unchanged.
assignments={'npc.ashen_hollow.renn_vale':'10_Walter','npc.ashen_hollow.kera_voss':'04_Sunita','npc.ashen_hollow.sel_arien':'03_Grace','npc.ashen_hollow.tavar_orr':'07_Kelvin','npc.cairnwold.berrin_garth':'08_Dominic','npc.cairnwold.durn_skell':'09_Orlando','npc.cairnwold.mara_ling':'02_Zuri','npc.cairnwold.tam_crowden':'06_Trey'}
catalog['assignments']={k:v for k,v in assignments.items() if v in catalog['npcs']}
if (root/'evidence/ue_npc_measurements.json').exists():shutil.copy2(root/'evidence/ue_npc_measurements.json',runtime/'evidence/ue_npc_measurements.json')
(runtime/'catalog.json').write_text(json.dumps(catalog,indent=2))
print('STAGED_NATIVE_NPCS',catalog['npcs'])
