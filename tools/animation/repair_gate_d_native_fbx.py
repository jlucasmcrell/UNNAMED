"""Use vendor-native FBX rigs for the 61 Rokoko Unreal takes; no source rebasing."""
import json,os,traceback
from pathlib import Path
import unreal
job=Path(os.environ['OTHERREACH_HUMGEN_ANIMATION_JOB']);path=job/'extra_sources.json';report=json.loads(path.read_text());assert report['status']=='DONE' and not report['failures'];repairs=[]
try:
 for source in report['sources']:
  original=source.get('original','')
  if not Path(original).stem.lower().endswith('_ue') or source.get('native_original_fbx'):continue
  assert Path(original).is_file() and source.get('source_import_version')==3,source
  folder='/Game/GateDOriginalFbxSources/'+source['id'];task=unreal.AssetImportTask();task.filename=original;task.destination_path=folder;task.automated=True;task.save=True;task.replace_existing=True
  options=unreal.FbxImportUI();options.import_as_skeletal=True;options.import_animations=True;options.import_materials=False;options.import_textures=False;options.mesh_type_to_import=unreal.FBXImportType.FBXIT_SKELETAL_MESH
  options.anim_sequence_import_data.set_editor_property('use_default_sample_rate', False);options.anim_sequence_import_data.set_editor_property('custom_sample_rate', source['source_sample_rate']);task.options=options;unreal.AssetToolsHelpers.get_asset_tools().import_asset_tasks([task]);assets=[]
  alias=next(a['name']for a in source['assets']if a['kind']=='animation')
  for name in unreal.EditorAssetLibrary.list_assets(folder,True,False):
   asset=unreal.load_asset(name)
   if isinstance(asset,unreal.SkeletalMesh):assets.append(dict(kind='mesh',asset=asset.get_path_name()))
   if isinstance(asset,unreal.AnimSequence):
    model=asset.get_editor_property('data_model_interface');fps=model.get_frame_rate();assert fps.numerator/fps.denominator>=source['source_sample_rate']-.01
    assets.append(dict(kind='animation',asset=asset.get_path_name(),name=alias,imported_fps=[fps.numerator,fps.denominator],keys=model.get_number_of_keys()))
  assert sum(a['kind']=='animation'for a in assets)==1 and any(a['kind']=='mesh'for a in assets),source['id']
  source.update(assets=assets,native_original_fbx=True,import_path=original);repairs.append(source['id']);temporary=path.with_suffix('.pending.json');temporary.write_text(json.dumps(report,indent=2));temporary.replace(path)
 (job/'native_fbx_repair.json').write_text(json.dumps(dict(status='DONE',repaired=repairs),indent=2))
except Exception:(job/'native_fbx_repair.json').write_text(json.dumps(dict(status='FAILED',repaired=repairs,error=traceback.format_exc()),indent=2));raise
finally:unreal.SystemLibrary.quit_editor()
