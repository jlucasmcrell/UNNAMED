"""Native conformed groom cards and eye material parameters for NPC runtime bundles."""
from pathlib import Path as _PipelinePath
import sys as _pipeline_sys
_pipeline_sys.path.insert(0, str(_PipelinePath(__file__).parent))
from pipeline_paths import pipeline_root as _pipeline_root
import unreal,json,traceback,os,re,ast,time
from pathlib import Path
root=Path((_pipeline_root().as_posix() + ''));runtime=root/'runtime';path=root/'evidence/npc_material_groom_export.json'
state=json.loads(path.read_text()) if path.exists() else {'characters':[]}
def save(phase,**kw):state.update(phase=phase,updated=time.time(),**kw);path.write_text(json.dumps(state,indent=2))
def safe(s):return re.sub(r'[^A-Za-z0-9_.-]','_',s)
def export(obj,out,options=None):
 out.parent.mkdir(parents=True,exist_ok=True);t=unreal.AssetExportTask();t.object=obj;t.filename=str(out);t.automated=True;t.prompt=False;t.replace_identical=True
 if options:t.options=options
 if isinstance(obj,unreal.Texture2D):t.exporter=unreal.TextureExporterPNG()
 if not unreal.Exporter.run_asset_export_task(t):raise RuntimeError('Native export failed: '+obj.get_path_name())
 return out.relative_to(runtime).as_posix()
try:
 reference=Path('D:/Otherreach_Scratch/mh_slice_20261019/reference_metahuman-to-glb/5.7/native-glb/stages/02-blender-assemble/tools/import_glb.py')
 tree=ast.parse(reference.read_text());f=next(n for n in tree.body if isinstance(n,ast.FunctionDef) and n.name=='_synth_hair_color');ns={};exec(compile(ast.Module(body=[f],type_ignores=[]),str(reference),'exec'),ns)
 prep=json.loads((root/'evidence/npc_characters.json').read_text());assemblies=json.loads((root/'evidence/npc_assembly.json').read_text());lib=unreal.MaterialEditingLibrary
 start=int(os.environ.get('OTHERREACH_MH_NPC_START','0'));count=int(os.environ.get('OTHERREACH_MH_NPC_COUNT','2'))
 for row in prep['characters'][start:start+count]:
  id=row['id'];save('EXPORTING',current=id);assembly=next(c for c in assemblies['characters'] if c['id']==id);folder=runtime/'npc'/id
  entries=[unreal.load_asset(p) for p in assembly['assets'] if '/Grooms/' in p]
  materials=[]
  for obj in entries:
   if not isinstance(obj,unreal.MaterialInstanceConstant):continue
   record=dict(name=obj.get_name(),asset=obj.get_path_name(),scalars={},vectors={})
   for p in lib.get_scalar_parameter_names(obj):record['scalars'][str(p)]=float(lib.get_material_instance_scalar_parameter_value(obj,p))
   for p in lib.get_vector_parameter_names(obj):
    c=lib.get_material_instance_vector_parameter_value(obj,p);record['vectors'][str(p)]=[float(c.r),float(c.g),float(c.b),float(c.a)]
   materials.append(record)
  cards=[];unrepresented=[]
  for obj in entries:
   if not isinstance(obj,unreal.GroomAsset):continue
   descriptors=list(obj.get_editor_property('hair_groups_cards'));groups={}
   for descriptor in descriptors:
    mesh=descriptor.get_editor_property('imported_mesh')
    if mesh is None:continue
    group=descriptor.get_editor_property('group_index');lod=descriptor.get_editor_property('lod_index')
    groups.setdefault(group,[]).append((lod,descriptor))
   if not groups:unrepresented.append(obj.get_name());continue
   for group,choices in groups.items():
    eligible=[x for x in choices if x[0]>=2];lod,descriptor=min(eligible,key=lambda x:x[0]) if eligible else max(choices,key=lambda x:x[0])
    mesh=descriptor.get_editor_property('imported_mesh');textures=descriptor.get_editor_property('textures')
    if 'LAYOUT2' not in str(textures.get_editor_property('layout')):raise RuntimeError('Unsupported native coverage layout '+obj.get_name())
    coverage=list(textures.get_editor_property('textures'))[1]
    if coverage is None:raise RuntimeError('Native coverage atlas missing '+obj.get_name())
    coverage_path=export(coverage,folder/'grooms'/(safe(coverage.get_name())+'.png'))
    options=unreal.GLTFExportOptions();options.bake_material_inputs=unreal.GLTFMaterialBakeMode.DISABLED;options.export_uniform_scale=.01;options.use_mesh_quantization=False
    glb=export(mesh,folder/'grooms'/(safe(mesh.get_name())+'.glb'),options)
    native=next((m for m in materials if obj.get_name() in m['name'] and m['name'].endswith('_Hair_Cards')),None)
    if native is None:native=next((m for m in materials if obj.get_name() in m['name'] and m['name'].endswith('_Hair')),None)
    if native is None:raise RuntimeError('Native hair colour parameters missing '+obj.get_name())
    cards.append(dict(sex=row['sex'],name=obj.get_name(),glb=glb,coverage=coverage_path,hair_color=ns['_synth_hair_color'](native),native_card_lod=lod,group=group,native_asset=mesh.get_path_name()))
  if not any(c['name'].startswith('Hair_') for c in cards) or not any(c['name'].startswith('Eyebrows_') for c in cards):raise RuntimeError('Required native hair/brows missing '+id)
  folder.mkdir(parents=True,exist_ok=True);(folder/'grooms.json').write_text(json.dumps(dict(cards=cards,unrepresented_groom_assets=unrepresented,source_reference=str(reference)),indent=2))
  face=unreal.load_asset(next(m['path'] for m in assembly['meshes'] if '/Face/' in m['path'] and 'FaceMesh' in m['path']))
  eyes=[]
  for slot in face.materials:
   mi=slot.material_interface
   if mi is None or not any(x in mi.get_name() for x in ['EyeL_Baked','EyeR_Baked']):continue
   record=dict(name='MI_EyeL_Baked' if 'EyeL_Baked' in mi.get_name() else 'MI_EyeR_Baked',textures={},asset=mi.get_path_name())
   for p in lib.get_texture_parameter_names(mi):
    texture=lib.get_material_instance_texture_parameter_value(mi,p)
    if isinstance(texture,unreal.Texture2D):record['textures'][str(p)]={'path':export(texture,folder/'eyes'/(safe(str(p)+'_'+texture.get_name())+'.png'))}
   eyes.append(record)
  if len(eyes)!=2:raise RuntimeError('Native separate left/right eye materials missing '+id)
  (folder/'eyes.json').write_text(json.dumps(dict(characters=[dict(sex=row['sex'],materials=eyes)]),indent=2))
  state['characters']=[c for c in state['characters'] if c['id']!=id]+[dict(id=id,cards=cards,eyes=eyes,unrepresented=unrepresented)]
  save('EXPORTED',current=id)
 save('BATCH_DONE')
except Exception:save('FAILED',error=traceback.format_exc())
finally:
 if os.environ.get('OTHERREACH_MH_NPC_ASSEMBLE')!='1':unreal.SystemLibrary.quit_editor()
