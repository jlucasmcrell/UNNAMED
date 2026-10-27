"""Gate A: native preset unchanged; only export detail/retention settings differ."""
import bpy, json, sys, time, hashlib, dataclasses, traceback
from pathlib import Path
from HumGen3D.human.human import Human
from HumGen3D.human.process.settings import ExportSettings, QualitySettings, ScriptSettings
from HumGen3D.human.process.shape_keys import livekey_group
from HumGen3D.backend.preferences.preference_func import get_prefs
args=sys.argv[sys.argv.index('--')+1:]
sex=args[0]; out=Path(args[1]); out.mkdir(parents=True,exist_ok=True)
preset=Path('D:/HumGen Content/models')/sex/('Caucasian presets' if sex=='female' else 'Caucasian Presets')/'Caucasian 2.json'
identifier='humgen_fidelity_'+sex+'_caucasian2'
bpy.ops.wm.read_homefile(use_empty=True)
human=Human.from_preset(str(preset.relative_to(Path('D:/HumGen Content'))))
human.name=identifier
# Do not set age, height, rig, hair, skin, expressions, clothing or any key.
def snapshot():
    return {o.name:{'vertices':len(o.data.vertices),'keys':{k.name:k.value for k in o.data.shape_keys.key_blocks} if o.data.shape_keys else {},'materials':[m.name for m in o.data.materials]} for o in human.objects if o.type=='MESH'}
record={'preset':str(preset),'preset_sha256':hashlib.sha256(preset.read_bytes()).hexdigest(),'before':snapshot(),'images':[], 'materials':{}}
for mat in bpy.data.materials:
    if not mat.use_nodes: continue
    record['materials'][mat.name]={'nodes':[{'name':n.name,'type':n.bl_idname,'group':n.node_tree.name if n.type=='GROUP' else None,'inputs':{s.name:list(s.default_value) if hasattr(s.default_value,'__len__') and not isinstance(s.default_value,str) else s.default_value for s in n.inputs if hasattr(s,'default_value')}} for n in mat.node_tree.nodes], 'links':[[l.from_node.name,l.from_socket.name,l.to_node.name,l.to_socket.name] for l in mat.node_tree.links]}
for im in bpy.data.images:
    record['images'].append({'name':im.name,'filepath':im.filepath,'size':list(im.size),'colorspace':im.colorspace_settings.name})
(out/'source_audit.json').write_text(json.dumps(record,indent=2,default=str))
bpy.ops.wm.save_as_mainfile(filepath=str(out/'native_unchanged.blend'))
settings=ExportSettings.from_recipe('godot')
settings.textures.resolution.update(body=4096,eyes=2048,hair=2048,teeth=2048)
settings.textures.haircap_in_skin=False
settings.shape_keys.body='keep'; settings.shape_keys.face='keep';settings.shape_keys.age='keep'
invalid=[k for k in human.keys.all_livekeys if Path(get_prefs().filepath,k.path).stat().st_size==0]
assert all(k.value==0 and 'trial' in k.name.lower() for k in invalid), 'Missing active or genuine native control'
settings.shape_keys.keep['body']=[k.name for k in human.keys.all_livekeys if livekey_group(k)=='body' and k.path not in {bad.path for bad in invalid}]
record['invalid_installed_trial_files']=[k.path for k in invalid]
settings.lods=[QualitySettings(body='original',clothing='original',eyes='original',teeth='original',haircards='ultra'),QualitySettings(body='lower_face',eyes='medium',teeth='medium',haircards='medium'),QualitySettings(body='quarter',eyes='low',teeth='low',haircards='low'),QualitySettings(body='quarter',eyes='low',teeth='low',haircards='haircap_only')]
settings.scripts.enabled=True
settings.scripts.items=[ScriptSettings(path=str(Path(__file__).with_name('keep_humgen_native_eyes.py')),stage='before_export')]
settings.output.folder=str(out);settings.output.name=identifier
(out/'export_settings.json').write_text(json.dumps(settings.to_dict(),indent=2))
start=time.time()
try:
    result=human.process.run(settings, progress=lambda f: print(f"NATIVE_EXPORT {f:.3f}",flush=True))
    record['after_source']=snapshot();record['source_unchanged']=record['before']==record['after_source']
    record['files']=[str(p) for p in result.files];record['minutes']=(time.time()-start)/60
    (out/'export_receipt.json').write_text(json.dumps(record,indent=2,default=str))
    assert record['source_unchanged'], 'Source preset modified during export'
except Exception:
    (out/'FAILED.txt').write_text(traceback.format_exc());sys.exit(1)
print('FIDELITY_EXPORT_COMPLETE',flush=True)
