"""Refresh derived-file checksums and provenance after the CPU export scripts finish."""
import hashlib,json
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
DEPOT=Path(r'D:\Otherreach_Depot\barrowworks_codex')
PRIMARY=Path(r'C:\Otherreach_Recovery\Otherreach_Playtest_RESTORE_20261015b\primary')
records=json.loads((DEPOT/'room_manifest.json').read_text())
boiler=json.loads((DEPOT/'ready/bw_codex_boiler/bw_codex_boiler_meta.json').read_text())
for record in records:
    if record['asset_id']=='bw_codex_room_06':record['boiler_source']=boiler
for record in json.loads((DEPOT/'props_manifest.json').read_text()):
    asset=record['asset_id'];path=DEPOT/'ready'/asset/(asset+'.glb')
    record['sha256']=hashlib.sha256(path.read_bytes()).hexdigest()
    record['source_urls']=['https://www.fab.com/listings/48148dbe-67c8-40a1-af9b-041fa4b9354e','https://sketchfab.com/3d-models/a10a6011334e4d11a7023d67705ac1d4']
    if 'entrance' in asset:
        record['source_urls'].append('https://www.fab.com/listings/5d9d932b-9e6e-42d0-8afc-45249e7535a5')
        record['berm_surface_source']=json.loads((PRIMARY/'materials/cw_rock_ground/cw_rock_ground_provenance.json').read_text())
    if 'measure' in asset:record['boiler_source']=boiler
    record['rough_wood_source']=json.loads((PRIMARY/'materials/cw_timber_beam/cw_timber_beam_provenance.json').read_text())
    record['first_party_surface']='Worn raster writing from existing region done_text, manually drawn gauges; conventional geometry.'
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

# Native caches are opt-in, derived outside the primary package. Textures are shared external resources.
scenes=[]
textures=[]
for path in sorted((DEPOT/'barrowworks_native').rglob('*.scn')):
    meta=json.loads(Path(str(path)+'.source.json').read_text())
    source=Path(meta['source']);stamp=Path(str(path)+'.sha256').read_text()
    assert stamp==meta['stamp'] and stamp.startswith('v4 ') and stamp.endswith(hashlib.sha256(source.read_bytes()).hexdigest().upper()),source
    scenes.append({'file':str(path),'bytes':path.stat().st_size,'sha256':hashlib.sha256(path.read_bytes()).hexdigest(),
                   'source':str(source),'stamp':stamp})
for path in sorted((DEPOT/'barrowworks_native').rglob('*.dds.res')):
    source=PRIMARY/'texture_cache'/path.name[:-4]
    assert source.exists(),source
    textures.append({'file':str(path),'bytes':path.stat().st_size,'sha256':hashlib.sha256(path.read_bytes()).hexdigest(),
                     'source_dds':str(source),'source_sha256':hashlib.sha256(source.read_bytes()).hexdigest()})
(ROOT/'docs/BARROWWORKS_SCENE_CACHE.json').write_text(json.dumps({'raw_sources_committed':False,'outputs':scenes,
    'shared_textures':textures,'relocation':'External texture paths are absolute; rebuild the cache after moving the asset depot.'},indent=2)+'\n')
# Terrain cache is deliberately separate from the shared primary texture-cache junction.
terrain=[]
for path in sorted((DEPOT/'barrowworks_terrain/texture_cache').glob('*.dds')):
    terrain.append({'file':str(path),'bytes':path.stat().st_size,'sha256':hashlib.sha256(path.read_bytes()).hexdigest(),
                    'size_px':[4096,4096],'format':'BC7 RGBA, full mip chain'})
assert len(terrain)==18,'Rebuild all nine packed terrain pairs before publishing a cache manifest'
(ROOT/'docs/BARROWWORKS_TERRAIN_CACHE.json').write_text(json.dumps({'cache_root':str(DEPOT/'barrowworks_terrain/texture_cache'),
    'licence':'CC0 1.0; existing Poly Haven packed terrain maps','raw_sources_committed':False,'outputs':terrain},indent=2)+'\n')
print('Recorded',len(scenes),'native scenes,',len(textures),'shared textures and',len(terrain),'packed terrain maps')
