"""Bind NPC identities to the same published Average outfit meshes as the player."""
import argparse,json
from pathlib import Path

parser=argparse.ArgumentParser()
parser.add_argument('--manifest',required=True,type=Path)
parser.add_argument('--bindings',required=True,type=Path)
parser.add_argument('--out',required=True,type=Path)
args=parser.parse_args()
fits={model['id']:model['approved_body'] for model in json.loads(args.manifest.read_text())['physical_models']}
document=json.loads(args.bindings.read_text(encoding='utf-8-sig'))
count=0
for key,look in document.get('people',{}).items():
    if look.get('model') not in fits:continue
    fit=fits[look['model']]
    # Everyday clothes share the exact approved body resource. Identity heads,
    # grooms, wings, people's surfaces and whole-model stature remain their own.
    smith=key.endswith('.kera_voss')
    body='vest_trousers_set' if smith else 'linen_trousers_set'
    parts=[body,'sealed_boots']+(['smith_gloves'] if smith else [])
    look['outfit_body']=fit
    look['outfit']=['frontier_'+part+'__'+fit for part in parts]
    count+=1
args.out.parent.mkdir(parents=True,exist_ok=True)
args.out.write_text(json.dumps(document,indent=2)+'\n',encoding='utf-8')
print('SHARED_PLAYER_OUTFITS_FOR_NPC_BINDINGS',count)
