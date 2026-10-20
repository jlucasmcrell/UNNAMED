"""Creator preset + native Body Params only. Separate from the approved player assets."""
from pathlib import Path as _PipelinePath
import sys as _pipeline_sys
_pipeline_sys.path.insert(0, str(_PipelinePath(__file__).parent))
from pipeline_paths import pipeline_root as _pipeline_root
import unreal,json,traceback
from pathlib import Path
root=Path((_pipeline_root().as_posix() + ''))
specs=[
 ('01_Aoi','masculine','Aoi','young',159,-.35,-.15,'LowPonytail'),
 ('02_Zuri','feminine','Zuri','young',173,.1,.4,'UpdoBraids'),
 ('03_Grace','feminine','Grace','middle',163,.55,-.1,'LowPonytail'),
 ('04_Sunita','feminine','Sunita','middle',167,.2,.15,'PulledBack'),
 ('05_Etta','feminine','Etta','elder',156,.4,-.25,'Updo'),
 ('06_Trey','masculine','Trey','young',181,-.3,.45,'Casual'),
 ('07_Kelvin','masculine','Kelvin','young',187,.25,.55,'Coil'),
 ('08_Dominic','masculine','Dominic','middle',176,.6,.25,'Messy'),
 ('09_Orlando','masculine','Orlando','middle',172,-.15,-.1,'RecedeMessy'),
 ('10_Walter','masculine','Walter','elder',175,-.25,-.2,'HairLoss'),
]
state={'status':'PREPARING','characters':[],'rules':'native Creator params; no manual body/skeleton edits','age_labels':'selection intent; native preset appearance must be reviewed'}
path=root/'evidence/npc_characters.json'
def save():path.write_text(json.dumps(state,indent=2))
try:
 sub=unreal.get_editor_subsystem(unreal.MetaHumanCharacterEditorSubsystem)
 for id,sex,preset,age,height,fat,muscle,hair in specs:
  target='/Game/MHPlaytest/NPC/'+id+'/MHC_NPC_'+id
  character=unreal.load_asset(target) if unreal.EditorAssetLibrary.does_asset_exist(target) else unreal.EditorAssetLibrary.duplicate_asset('/MetaHumanCharacter/Optional/Presets/'+preset,target)
  if not character or character.fixed_body_type:raise RuntimeError('Missing parametric character '+id)
  if not sub.try_add_object_to_edit(character):raise RuntimeError('Cannot edit '+id)
  try:
   constraints=list(sub.get_body_constraints(character))
   desired={'Height':height,'Fat':fat,'Muscularity':muscle}
   for c in constraints:
    name=str(c.name)
    if name in desired:
     if not c.min_measurement<=desired[name]<=c.max_measurement:raise ValueError((id,name,desired[name],str(c)))
     c.is_active=True;c.target_measurement=desired[name]
   sub.set_body_constraints(character,constraints);sub.commit_body_state(character)
   skin=character.skin_settings;res=skin.desired_texture_sources_resolutions
   for field in ['face_albedo','face_normal','face_cavity','face_animated_maps','body_albedo','body_normal','body_cavity','body_masks']:res.set_editor_property(field,unreal.RequestTextureResolution.RES4K)
   skin.desired_texture_sources_resolutions=res;sub.commit_skin_settings(character,skin)
   hair_item=unreal.load_asset('/MetaHumanCharacter/Optional/Grooms/Bindings/Hair/WI_Hair_S_'+hair)
   if not hair_item:raise RuntimeError('Missing native groom '+hair)
   key=character.internal_collection.try_add_item_from_wardrobe_item('Hair',hair_item)
   character.internal_collection.default_instance.set_single_slot_selection('Hair',key)
   if not unreal.EditorAssetLibrary.save_loaded_asset(character,False):raise RuntimeError('Save failed '+id)
   actual={str(c.name):float(c.target_measurement) for c in sub.get_body_constraints(character)}
   state['characters'].append({'id':id,'sex':sex,'preset':preset,'age_intent':age,'asset':target,'hair':hair_item.get_path_name(),'body_params':actual,'skin':str(character.skin_settings),'selections':str(character.internal_collection.default_instance.get_slot_selection_data())})
   save()
  finally:sub.remove_object_to_edit(character)
 sk=unreal.get_editor_subsystem(unreal.SkeletalMeshEditorSubsystem)
 state['source_lods']=[]
 for sex in ['Feminine','Masculine']:
  for part in ['Head','Body']:
   mesh=unreal.load_asset('/Game/MHSlice/Final/'+sex+'/Source/MHC_Slice_'+sex+'_'+part)
   state['source_lods'].append(dict(sex=sex,part=part,count=sk.get_lod_count(mesh)))
 state['status']='PREPARED' 
except Exception:state.update(status='FAILED',error=traceback.format_exc())
save()
unreal.SystemLibrary.quit_editor()
