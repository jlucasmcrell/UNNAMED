"""Adopt role options only after every exact-base part and mask exists."""
import json,sys
from pathlib import Path
root=Path(__file__).resolve().parents[2]
payload=Path(sys.argv[1])
roles=json.loads((root/'tools/creator/role_garments.json').read_text())
catalogue=root/'src/Presentation/Art/humgen_creator.json'
data=json.loads(catalogue.read_text(encoding='utf-8'))
options=[]
for identifier,entry in roles.items():
    if sys.argv[2:] and identifier not in sys.argv[2:]:continue
    paths={}
    for sex in entry.get('sexes',['f','m']):
        part='humgen_creator_'+sex+'_outfit_'+identifier.replace('.','_')
        folder=payload/'game/primary/rigged'/part
        contract=json.loads((folder/'creator_part.json').read_text())
        assert contract['option']==identifier
        assert (folder/(part+'_rigged.glb')).is_file()
        assert (folder/'body_coverage.png').is_file(),part+' coverage not baked'
        paths[sex]='period/'+identifier
    options.append(dict(id=identifier,label=entry['label'],group='Frontier roles',paths=paths))
data['outfits']=[option for option in data['outfits'] if option['id'] not in roles]+options
catalogue.write_text(json.dumps(data,ensure_ascii=False,indent=1)+'\n',encoding='utf-8')
print('ROLE_CATALOGUE_STAGED',len(options))
