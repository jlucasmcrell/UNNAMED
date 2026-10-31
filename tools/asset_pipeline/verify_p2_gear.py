"""Verify prepared gear without loading Godot or modifying the source art.

Usage: python verify_p2_gear.py <manifest.json> <private-art-root> [--partial]
"""
import hashlib,io,json,sys
from pathlib import Path
from PIL import Image
from _glb_truth_check import read_glb,world_bounds

manifest,root=Path(sys.argv[1]),Path(sys.argv[2]);partial='--partial' in sys.argv
rows=json.loads(manifest.read_text());results=[]
for row in rows:
    id=row['art_id'];file=root/'ready'/id/(id+'.glb')
    receipt=file.parent/(id+('_meta.json' if row['item_id'].startswith('item.weapon.') else '.json'))
    if partial and not receipt.exists():continue
    assert file.exists(),str(file)
    data,binary=read_glb(file)
    assert data.get('meshes') and data.get('materials'),id+': no surface/material'
    assert not data.get('skins'),id+': static source unexpectedly carries an old body skin'
    for mesh in data['meshes']:
        for primitive in mesh['primitives']:
            assert {'POSITION','NORMAL','TEXCOORD_0'} <= primitive['attributes'].keys(),id+': missing surface attributes'
            assert 'material' in primitive,id+': unmaterialed primitive'
    maps=[]
    for image in data.get('images',[]):
        assert 'bufferView' in image,id+': external image dependency'
        view=data['bufferViews'][image['bufferView']];start=view.get('byteOffset',0)
        with Image.open(io.BytesIO(binary[start:start+view['byteLength']])) as native:maps.append(list(native.size))
    assert maps or row.get('material_source')=='authored_pbr_factors',id+': no native image maps or authored-factor source receipt'
    if row['item_id'].startswith('item.weapon.'):
        names={n.get('name'):n for n in data.get('nodes',[])}
        assert 'SOCK_grip_primary' in names,id+': no grip'
        socket=names['SOCK_grip_primary'];assert 'rotation' in socket,id+': no grip frame'
        for lod in (1,2):
            assert (file.parent/(id+f'_lod{lod}.glb')).exists(),id+': missing LOD'
    results.append(dict(item_id=row['item_id'],asset_id=id,sha256=hashlib.sha256(file.read_bytes()).hexdigest(),bounds=world_bounds(data),image_sizes=maps))
assert results,'No gear prepared'
if not partial:
    assert sum(r['item_id'].startswith('item.weapon.') for r in results)>=15
    assert sum(r['item_id'].startswith('item.armor.') for r in results)>=20
(root/'static_source_verification.json').write_text(json.dumps(dict(status='PASS_STATIC_SOURCES_ONLY_NOT_WORN_FITS',count=len(results),assets=results),indent=2))
print('PASS',len(results),'static source models; no worn-fit claim')
