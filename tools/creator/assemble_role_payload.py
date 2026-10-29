"""Stage a small immutable role-outfit delta; existing world/body art stays external."""
import hashlib,json,os,shutil,sys
from pathlib import Path
wt=Path(__file__).resolve().parents[2]
roles=json.loads((wt/'tools/creator/role_garments.json').read_text())
out=Path('D:/gc_delivery/game/primary/rigged');out.mkdir(parents=True,exist_ok=True)
rows=[];missing=[]
for option,recipe in roles.items():
    for sex in recipe.get('sexes',['f','m']):
        identifier='humgen_creator_'+sex+'_outfit_'+option.replace('.','_')
        root=Path('D:/gc_hunter02' if option.endswith('leathers') else 'D:/gc_roles02' if sex=='f' else 'D:/gc_roles_m')
        source=root/'game/primary/rigged'/identifier
        if not (source/'creator_part.json').is_file():missing.append(identifier);continue
        target=out/identifier;target.mkdir(exist_ok=True)
        for file in source.iterdir():
            if not file.is_file():continue
            destination=target/file.name
            if file.suffix=='.glb':
                if not destination.exists():os.link(file,destination)
                assert hashlib.sha256(destination.read_bytes()).digest()==hashlib.sha256(file.read_bytes()).digest()
            else:shutil.copy2(file,destination)
        rows.append(dict(id=identifier,option=option,sex=sex,source=str(source),body_target=json.loads((target/'creator_part.json').read_text())['target_body_sha256'],sha256=hashlib.sha256((target/(identifier+'_rigged.glb')).read_bytes()).hexdigest()))
Path('D:/gc_delivery/STAGING.json').write_text(json.dumps(dict(ready=not missing,rows=rows,missing=missing),indent=2))
Path('D:/gc_delivery/game/fallback').mkdir(parents=True,exist_ok=True)
print('ROLE_PAYLOAD',len(rows),'ready;',len(missing),'pending')
if missing and '--partial' not in sys.argv:raise SystemExit(1)
