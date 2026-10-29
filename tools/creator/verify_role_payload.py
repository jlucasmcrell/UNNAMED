"""Independent file-level exact-body/rest and full-LOD checks for the role delta."""
import hashlib,json,sys
from pathlib import Path
import numpy as np
wt=Path(__file__).resolve().parents[2]
sys.path.insert(0,str(wt/'tools/animation'))
from audit_humgen_transport import Glb
rows=[]
for folder in sorted(Path('D:/gc_delivery/game/primary/rigged').iterdir()):
    contract=json.loads((folder/'creator_part.json').read_text())
    target=Path('D:/Otherreach_Scratch/codex_f1_siann_wt/work/creator/game/primary/rigged')/contract['target_base']/(contract['target_base']+'_rigged.glb')
    body=Glb(target);part_file=folder/(folder.name+'_rigged.glb');part=Glb(part_file)
    assert hashlib.sha256(target.read_bytes()).hexdigest().lower()==contract['target_body_sha256'].lower(),folder.name+' target changed'
    assert body.names.keys()==part.names.keys(),folder.name+' bone vocabulary differs'
    a,b=body.globals(),part.globals()
    error=max(float(np.max(np.abs(a[name]-b[name]))) for name in a)
    assert error<.0001,(folder.name,error)
    meshes=part.doc['meshes']
    assert not any('_Body_LOD' in mesh.get('name','') for mesh in meshes)
    assert all(any('_LOD'+str(level) in mesh.get('name','') for mesh in meshes) for level in range(4))
    assert all(any(name.startswith('b_') for name in mesh.get('extras',{}).get('targetNames',[]))for mesh in meshes),folder.name+' body controls missing'
    assert (folder/'body_coverage.png').is_file()
    rows.append(dict(id=folder.name,body_sha256=contract['target_body_sha256'],garment_sha256=hashlib.sha256(part_file.read_bytes()).hexdigest(),max_global_rest_error=error,meshes=len(meshes)))
assert len(rows)==15,len(rows)
Path('D:/gc_delivery/NATIVE_ROLE_GUARD.json').write_text(json.dumps(dict(status='PASS',rows=rows,scope='File-level native body/rest/morph/LOD contract; visual/gameplay clipping judged separately'),indent=2))
print('NATIVE_ROLE_GUARD_PASS',len(rows))
