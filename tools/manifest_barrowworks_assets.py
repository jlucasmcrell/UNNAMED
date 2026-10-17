"""Refresh derived-file checksums and provenance after the CPU export scripts finish."""
import hashlib,json
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
DEPOT=Path(r'D:\Otherreach_Depot\barrowworks_codex')
PRIMARY=Path(r'C:\Otherreach_Recovery\Otherreach_Playtest_RESTORE_20261015b\primary')
records=json.loads((DEPOT/'room_manifest.json').read_text())
for record in json.loads((DEPOT/'props_manifest.json').read_text()):
    asset=record['asset_id'];path=DEPOT/'ready'/asset/(asset+'.glb')
    record['sha256']=hashlib.sha256(path.read_bytes()).hexdigest()
    record['source_urls']=['https://www.fab.com/listings/48148dbe-67c8-40a1-af9b-041fa4b9354e','https://sketchfab.com/3d-models/a10a6011334e4d11a7023d67705ac1d4']
    if 'entrance' in asset:record['source_urls'].append('https://www.fab.com/listings/5d9d932b-9e6e-42d0-8afc-45249e7535a5')
    records.append(record)
record=json.loads((DEPOT/'rigged/bw_codex_custodian/bw_codex_custodian_meta.json').read_text())
record['sha256']=hashlib.sha256((DEPOT/'rigged/bw_codex_custodian/bw_codex_custodian_rigged.glb').read_bytes()).hexdigest();records.append(record)
for asset in ['prop_ph_wooden_crate_01','cw_woodpile_a','prop_bp_beam_mossy','prop_ph_wooden_lantern_01']:
    meta=PRIMARY/'ready'/asset/(asset+'_meta.json')
    records.append({'asset_id':asset,'category':'existing_production_reuse','source_metadata':str(meta),
                    'source_provenance':json.loads(meta.read_text()),'sha256':hashlib.sha256((meta.parent/(asset+'.glb')).read_bytes()).hexdigest(),'shippable':True})
out=ROOT/'docs/BARROWWORKS_ASSET_MANIFEST.json'
out.write_text(json.dumps({'asset_root':str(DEPOT),'raw_sources_committed':False,'assets':records},indent=2)+'\n')
print('Recorded',len(records),'assets')
