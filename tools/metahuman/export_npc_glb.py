"""Native lower-LOD GLTF export; no decimation, skeleton edits or morph processing."""
from pathlib import Path as _PipelinePath
import sys as _pipeline_sys
_pipeline_sys.path.insert(0, str(_PipelinePath(__file__).parent))
from pipeline_paths import pipeline_root as _pipeline_root
import unreal,json,traceback,os,struct,time
from pathlib import Path
root=Path((_pipeline_root().as_posix() + ''));path=root/'evidence/npc_glb_export.json'
state=json.loads(path.read_text()) if path.exists() else {'characters':[]}
def save(phase,**kw):state.update(phase=phase,updated=time.time(),**kw);path.write_text(json.dumps(state,indent=2))
try:
 prep=json.loads((root/'evidence/npc_characters.json').read_text())
 sources=json.loads((root/'evidence/npc_cloud_export.json').read_text())
 assemblies=json.loads((root/'evidence/npc_assembly.json').read_text())
 sub=unreal.get_editor_subsystem(unreal.SkeletalMeshEditorSubsystem)
 start=int(os.environ.get('OTHERREACH_MH_NPC_START','0'));count=int(os.environ.get('OTHERREACH_MH_NPC_COUNT','2'))
 selected=prep['characters'][start:start+count]
 # Bring already exported characters up to the same native LOD candidate evidence.
 completed={c['id'] for c in assemblies['characters']}
 for row in prep['characters']:
  previous=next((c for c in state['characters'] if c['id']==row['id']),None)
  if row not in selected and row['id'] in completed and previous and not any(x['part']=='head_candidate_1' for x in previous['exports']):selected.append(row)
 for row in selected:
  id=row['id'];save('EXPORTING',current=id)
  source=next(c for c in sources['characters'] if c['id']==id)
  assembly=next(c for c in assemblies['characters'] if c['id']==id)
  body=next(unreal.load_asset(p) for p in source['geometry'] if unreal.load_asset(p).get_name().endswith('_Body'))
  candidates=[unreal.load_asset(m['path']) for m in assembly['meshes'] if '/Face/' in m['path'] and 'FaceMesh' in m['path']]
  if len(candidates)!=1:raise RuntimeError(('Expected native assembled semantic face',id,[x['path'] for x in assembly['meshes']]))
  head=candidates[0];exports=[]
  for part,mesh,lod in [('body',body,1),('head',head,0),('head_candidate_1',head,1),('head_candidate_2',head,2)]:
   available=sub.get_lod_count(mesh)
   if available<=lod:raise RuntimeError(('Requested native LOD unavailable',id,part,lod,available))
   out=root/'glb/epic'/id;out.mkdir(parents=True,exist_ok=True)
   filename=out/(part+'.glb')
   options=unreal.GLTFExportOptions();options.bake_material_inputs=unreal.GLTFMaterialBakeMode.DISABLED
   options.export_vertex_skin_weights=True;options.export_morph_targets=False;options.export_uniform_scale=.01
   options.default_level_of_detail=lod;options.export_preview_mesh=False;options.use_mesh_quantization=False
   task=unreal.AssetExportTask();task.object=mesh;task.filename=str(filename);task.options=options;task.automated=True;task.prompt=False;task.replace_identical=True
   if not unreal.Exporter.run_asset_export_task(task):raise RuntimeError('Native GLTF export failed '+id+' '+part)
   raw=filename.read_bytes();length=struct.unpack_from('<I',raw,12)[0];gltf=json.loads(raw[20:20+length])
   component=unreal.new_object(unreal.SkeletalMeshComponent);component.set_skeletal_mesh_asset(mesh)
   names=[component.get_bone_name(i) for i in range(component.get_num_bones())]
   native_bones=[dict(name=str(n),parent=str(component.get_parent_bone(n))) for n in names]
   exports.append(dict(part=part,path=str(filename),asset=mesh.get_path_name(),native_lod=lod,native_pipeline='UE Optimized High' if mesh==head else 'Geometry Export',available_lods=available,bones=native_bones,bone_count=len(names),skin_joints=[len(s['joints']) for s in gltf.get('skins',[])],materials=[m.get('name','') for m in gltf.get('materials',[])]))
  state['characters']=[c for c in state['characters'] if c['id']!=id]+[dict(id=id,exports=exports)]
  save('EXPORTED',current=id)
 save('BATCH_DONE')
 if os.environ.get('OTHERREACH_MH_NPC_ASSEMBLE')=='1':
  import runpy
  runpy.run_path(str(root/'scripts/measure_npc_sources.py'),run_name='__main__')
  runpy.run_path(str(root/'scripts/probe_etta_lods.py'),run_name='__main__')
except Exception:save('FAILED',error=traceback.format_exc())
finally:
 if os.environ.get('OTHERREACH_MH_NPC_ASSEMBLE')!='1':unreal.SystemLibrary.quit_editor()
