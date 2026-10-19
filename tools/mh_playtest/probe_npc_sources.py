import unreal,json,traceback
from pathlib import Path
root=Path('D:/Otherreach_Scratch/mh_playtest_20261019')
result={}
try:
    sub=unreal.get_editor_subsystem(unreal.MetaHumanCharacterEditorSubsystem)
    sk=unreal.get_editor_subsystem(unreal.SkeletalMeshEditorSubsystem)
    result['skeletal_lod_api']=[x for x in dir(sk) if 'lod' in x or 'bone' in x]
    result['export_options']=[x for x in dir(unreal.GLTFExportOptions()) if not x.startswith('_')]
    result['presets']=[]
    for name in ['Aoi','Zuri','Grace','Sunita','Etta','Trey','Kelvin','Dominic','Orlando','Walter']:
        character=unreal.load_asset('/MetaHumanCharacter/Optional/Presets/'+name)
        if not character:raise RuntimeError('Missing native preset '+name)
        row={'preset':name,'fixed_body':bool(character.fixed_body_type),'skin':str(character.skin_settings)}
        if not sub.try_add_object_to_edit(character):raise RuntimeError('Cannot inspect '+name)
        try:
            row['body']=[{'name':str(c.name),'value':c.target_measurement,'min':c.min_measurement,'max':c.max_measurement} for c in sub.get_body_constraints(character)]
            instance=character.get_editor_property('character_instance') if 'character_instance' in dir(character) else None
            row['instance']=str(instance)
        finally:sub.remove_object_to_edit(character)
        result['presets'].append(row)
    result['meshes']=[]
    for sex in ['Feminine','Masculine']:
        for part in ['Head','Body']:
            mesh=unreal.load_asset('/Game/MHSlice/Final/'+sex+'/Source/MHC_Slice_'+sex+'_'+part)
            result['meshes'].append({'sex':sex,'part':part,'lod_count':sk.get_lod_count(mesh)})
    result['status']='DONE'
except Exception:result.update(status='FAILED',error=traceback.format_exc())
(root/'evidence/npc_source_probe.json').write_text(json.dumps(result,indent=2))
unreal.SystemLibrary.quit_editor()
