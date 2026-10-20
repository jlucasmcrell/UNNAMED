from pathlib import Path as _PipelinePath
import sys as _pipeline_sys
_pipeline_sys.path.insert(0, str(_PipelinePath(__file__).parent))
from pipeline_paths import pipeline_root as _pipeline_root
import unreal,json,traceback,struct
from pathlib import Path
r=Path((_pipeline_root().as_posix() + ''));result={'status':'STARTING','exports':[]};p=r/'evidence/npc_body_lod0_export.json'
try:
 src=json.loads((r/'evidence/npc_cloud_export.json').read_text())
 for row in src['characters']:
  mesh=next(unreal.load_asset(x)for x in row['geometry'] if unreal.load_asset(x).get_name().endswith('_Body'))
  target=r/'glb/epic'/row['id']/'body_lod0.glb'
  options=unreal.GLTFExportOptions();options.bake_material_inputs=unreal.GLTFMaterialBakeMode.DISABLED;options.export_vertex_skin_weights=True;options.export_morph_targets=False;options.export_uniform_scale=.01;options.default_level_of_detail=0;options.export_preview_mesh=False;options.use_mesh_quantization=False
  task=unreal.AssetExportTask();task.object=mesh;task.filename=str(target);task.options=options;task.automated=True;task.prompt=False;task.replace_identical=True
  assert unreal.Exporter.run_asset_export_task(task)
  result['exports'].append({'id':row['id'],'path':str(target),'asset':mesh.get_path_name(),'native_lod':0});p.write_text(json.dumps(result,indent=2))
 result['status']='EXPORTED'
except Exception:result.update(status='FAILED',error=traceback.format_exc())
finally:p.write_text(json.dumps(result,indent=2));unreal.SystemLibrary.quit_editor()
