"""Shared native HumGen export for individually fitted P2 equipment candidates.

The caller authors the real garment on the unchanged current body and runs
HumGen custom-clothing setup. This function exports that outfit with the
existing Godot recipe, native controls and unchanged compatible rests.
"""
import bpy,json,sys,hashlib,shutil
import numpy as np
from pathlib import Path
from HumGen3D.human.process.settings import ExportSettings,QualitySettings,ScriptSettings
from HumGen3D.human.process.shape_keys import livekey_group
from HumGen3D.backend.preferences.preference_func import get_prefs

def export_candidate(human,row,work,creator,sex,source,solver,adjusted,fit_audit=None,processed=None):
    base='humgen_creator_'+sex
    identifier=row['art_id']+'__'+base
    candidate=work/row.get('native_candidate_root','armour_native')
    folder=candidate/'primary/rigged'/identifier
    folder.mkdir(parents=True,exist_ok=True)
    tools=Path(__file__).resolve().parents[1]/'animation'
    sys.path.insert(0,str(tools))
    from audit_humgen_transport import Glb
    authoring=candidate/'sources'/identifier;authoring.mkdir(parents=True,exist_ok=True)
    if processed is None:
        # Save the native custom-content working outfit before the normal export.
        for image in bpy.data.images:
            if image.has_data and image.source in ('FILE','GENERATED'):image.pack()
        bpy.ops.wm.save_as_mainfile(filepath=str(authoring/'native_outfit.blend'),compress=True)
        settings=ExportSettings.from_recipe('godot')
        settings.output.format='in_file';settings.output.name=identifier
        settings.textures.enabled=False
        settings.shape_keys.body=settings.shape_keys.face=settings.shape_keys.age='keep'
        settings.shape_keys.lod0_only=False
        valid=[k for k in human.keys.all_livekeys if livekey_group(k)=='body' and Path(get_prefs().filepath,k.path).stat().st_size]
        settings.shape_keys.keep['body']=[name for k in valid for name in (k.name,f'{k.category[0]}_{{{k.subcategory}}}_{k.name}' if k.subcategory else f'{k.category[0]}_{k.name}')]
        settings.lods=[QualitySettings(body='original',clothing='original',eyes='original',teeth='original',haircards='ultra'),QualitySettings(body='lower_face',clothing='original'),QualitySettings(body='quarter',clothing='original'),QualitySettings(body='quarter',clothing='original')]
        settings.scripts.enabled=True
        hook='keep_humgen_headwear_keys.py' if row['slot']=='head' else 'keep_humgen_clothing_keys.py'
        hooks=authoring/'export_hooks';hooks.mkdir(exist_ok=True)
        for name in {hook,'keep_humgen_clothing_keys.py'}:shutil.copy2(tools/name,hooks/name)
        settings.scripts.items=[ScriptSettings(path=str(hooks/hook),stage='before_meshes')]
        result=human.process.run(settings,progress=lambda f:print('P2_EXPORT',identifier,round(f,3),flush=True))
        copies=result.humans
        for copy in copies:copy.location=(0,0,0)
        copies[0].process.merge_levels(copies[1:]);copy=copies[0]
        processed=dict(copy=copy,settings=settings,authoring=authoring,headwear=row['slot']=='head')
    else:
        assert processed['headwear']==(row['slot']=='head'),'Cannot reuse an export with another control policy'
        copy,settings=processed['copy'],processed['settings']
        shutil.copy2(processed['authoring']/'native_outfit.blend',authoring/'native_outfit.blend')
        hooks=authoring/'export_hooks'
        shutil.copytree(processed['authoring']/'export_hooks',hooks,dirs_exist_ok=True)
    meshes=list(copy.clothing.footwear.objects if row['slot']=='feet' else copy.clothing.outfit.objects)
    assert len(meshes)==4,('Expected one native equipment piece/pair per LOD',len(meshes))
    if row['slot']=='head':
        assert all(any(k.name.startswith('f_') for k in mesh.data.shape_keys.key_blocks) for mesh in meshes),'Native face proportion controls missing on headwear'
    bpy.ops.object.select_all(action='DESELECT');copy.objects.rig.select_set(True)
    for mesh in meshes:mesh.select_set(True)
    bpy.context.view_layer.objects.active=copy.objects.rig;bpy.context.view_layer.update()
    destination=folder/(identifier+'_rigged.glb')
    bpy.ops.export_scene.gltf(filepath=str(destination),export_format='GLB',use_selection=True,export_animations=False,export_morph=True,export_skins=True,export_yup=True)
    target_path=creator/'game/primary/rigged'/base/(base+'_rigged.glb')
    target,exported=Glb(target_path),Glb(destination)
    assert target.names.keys()==exported.names.keys(),'Native joint vocabulary changed'
    a,b=target.globals(),exported.globals()
    rest_error=max(float(np.max(np.abs(a[n]-b[n]))) for n in a)
    assert rest_error<.0001,('Native rest changed',rest_error)
    identity=json.loads((target_path.parent/'identity_weights.json').read_text())
    contract=dict(kind='equipment',slot=row['slot'],item=row['item_id'],review_id=row['review_id'],target_base=base,target_body_sha256=identity['target_body_sha256'],source=str(source),source_sha256=hashlib.sha256(source.read_bytes()).hexdigest(),native_meshes=[m.name for m in meshes],status='NATIVE_REVIEW_CANDIDATE_NOT_BOUND',clearance_m=.0035,clearance_vertices_adjusted=adjusted,native_rest_error=rest_error,lod_geometry='Original native source geometry retained at all four levels; no reduction claimed',solver=solver)
    if fit_audit:contract['fit_audit']=fit_audit
    contract['export_hook_sha256']={p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in hooks.glob('*.py')}
    (folder/'creator_part.json').write_text(json.dumps(contract,indent=2))
    (folder/'native_materials.json').write_text('{}')
    layer={'legs':1,'chest':2,'feet':3,'hands':4,'head':5,'cloak':6}[row['slot']]
    (folder/(identifier+'_garment.json')).write_text(json.dumps(dict(layer=layer,hides_mask=0,hide_regions=[],item=row['item_id'],target_body_sha256=identity['target_body_sha256']),indent=2))
    (authoring/'export_settings.json').write_text(json.dumps(settings.to_dict(),indent=2))
    print('P2_NATIVE_GARMENT_DONE',identifier,rest_error,flush=True)
    return processed
