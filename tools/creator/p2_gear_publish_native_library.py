"""Save fitted equipment through HumGen's native clothing library API.

CPU Blender: -- <candidate root> <equipment identifiers ...>. Saves only the
individually authored sex, with unique P2 names; never auto-fits the other sex.
Library publication is an authoring artifact, not game binding or fit approval.
"""
import bpy,json,sys,hashlib,subprocess
from pathlib import Path
from HumGen3D.human.human import Human
from HumGen3D.backend.preferences.preference_func import get_prefs
from HumGen3D.backend.preview_collections import PREVIEW_COLLECTION_DATA
args=sys.argv[sys.argv.index('--')+1:];root=Path(args[0])
for identifier in args[1:]:
    folder=root/'primary/rigged'/identifier
    contract=json.loads((folder/'creator_part.json').read_text())
    sex='female' if contract['target_base'].endswith('female') else 'male'
    authoring=root/'sources'/identifier
    bpy.ops.wm.open_mainfile(filepath=str(authoring/'native_outfit.blend'))
    human=Human.from_existing(bpy.data.objects[contract['target_base']])
    clothing=human.clothing.footwear if contract['slot']=='feet' else human.clothing.outfit
    assert len(clothing.objects)==1,'Publish one modular native piece at a time'
    category='Otherreach_P2_20261031_'+root.name
    library=Path(get_prefs().filepath)/PREVIEW_COLLECTION_DATA[clothing._pcoll_name][2]/sex/category
    target=library/(identifier+'.blend')
    assert not target.exists(),'Refusing to replace a native library artifact: '+str(target)
    children=clothing.save_to_library(identifier,for_male=sex=='male',for_female=sex=='female',
        open_when_finished=False,category=category,context=bpy.context)
    owned=[]
    try:
        for child in children:
            if child is None:continue
            owned.append(child.pid)
            assert child.wait(timeout=300)==0,'Native library optimizer failed'
    finally:
        for child in children:
            if child is not None and child.poll() is None:child.kill();child.wait()
    assert target.is_file() and target.stat().st_size>0
    receipt=dict(method='HumGen clothing.save_to_library; exact authored sex only; native optimizer completed',
        identifier=identifier,target_body_sha256=contract['target_body_sha256'],path=str(target),
        sha256=hashlib.sha256(target.read_bytes()).hexdigest(),owned_optimizer_pids=owned,
        status='NATIVE_AUTHORING_LIBRARY_NOT_GAME_APPROVAL')
    (authoring/'native_library.json').write_text(json.dumps(receipt,indent=2))
    print('P2_NATIVE_LIBRARY_SAVED',identifier,str(target),flush=True)
