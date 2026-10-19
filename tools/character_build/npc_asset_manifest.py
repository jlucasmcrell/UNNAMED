"""Record staged NPC provenance and hashes without publishing licensed asset files."""
import argparse,hashlib,json
from datetime import datetime,timezone
from pathlib import Path

ap=argparse.ArgumentParser(description=__doc__)
ap.add_argument('--asset-root',type=Path,required=True)
ap.add_argument('--integrity-report',type=Path,required=True)
a=ap.parse_args()
recipes=Path(__file__).resolve().parent/'npc_bodies'
records=[]
for proof in json.loads(a.integrity_report.read_text()):
    rid=proof['id'];asset='mh_'+rid;folder=a.asset_root/'rigged'/asset
    recipe=json.loads((recipes/(rid+'.json')).read_text())
    records.append({'id':asset,'native_creator_face':recipe['preset'],'people':recipe['people'],'approved_body':recipe['approved_body'],
        'approved_body_source_sha256':proof['body_sha256'],'integrity':proof,
        'files':{p.relative_to(a.asset_root).as_posix():hashlib.sha256(p.read_bytes()).hexdigest()
                 for p in folder.rglob('*') if p.is_file()},
        'geometry_change':'No body reshaping; native identity face/grooms and optional symmetric wing module only',
        'wardrobe':'Original approved body base layer; no unpublished garments or harness',
        'visual_status':'Paired renders and in-world review tracked in CODEX_PROGRESS.md; data integrity is not visual acceptance'})
manifest={'package':'NPCs and residents: exact approved player body resources + native Creator identities',
    'generated_utc':datetime.now(timezone.utc).isoformat(),'physical_models':records,
    'resident_appearances':48,'named_npcs':8,'body_rule':'OWNER 2026-10-18: anatomy unchanged, whole-model uniform stature only',
    'owner_approval_scope':'Kera body shape and symmetric wings approved; no blanket roster/wardrobe approval inferred',
    'credits':[
        {'asset':'MetaHuman bodies/faces/grooms','author':'Epic Games','source':'UE5.8 in-editor MetaHuman Creator','terms':'Project UE/MetaHuman licence ledger; retain Unreal Engine notices; raw assets stay private; no generative AI'},
        {'asset':'Kal folded wings','author':'WildMesh 3D','source':'Held 100 MODELS / BaldEagle / author Stand_Ground_01-IP pose','terms':'Owner-cleared WildMesh Standard v1.1, as recorded in LICENCE_LEDGER_20261017.md; no raw public redistribution or AI/ML use'},
        {'asset':'Accepted run','author':'VanillaLoop','source':'Free Sample Animation Set, Fab listing 5b737d10-b4cd-4294-8f6b-757b9088039d','terms':'Fab Standard; unchanged clip and its source metadata retained'},
        {'asset':'Other player clips','source':'Unchanged per-clip animation/clips metadata','terms':'Retain individual Fab / Quaternius CC0 / Rokoko source notices'},
        {'asset':'Ondrek scanned stone maps','author':'Poly Haven','source':'lichen_rock','terms':'CC0 1.0'},
    ]}
out=a.asset_root/'manifests/npc_approved_bodies.json';out.parent.mkdir(exist_ok=True)
out.write_text(json.dumps(manifest,indent=2)+'\n')
print('NPC_ASSET_MANIFEST',len(records),out)
