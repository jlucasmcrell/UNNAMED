"""Apply the room builder's tinted-glass material to existing GLB exports without reassembling geometry."""
import hashlib,json,struct
from pathlib import Path
DEPOT=Path(r'D:\Otherreach_Depot\barrowworks_codex')
manifest=json.loads((DEPOT/'room_manifest.json').read_text())
for record in manifest:
 for output in record.get('outputs',[]):
  path=DEPOT/'ready'/record['asset_id']/output['file'];data=path.read_bytes()
  size,kind=struct.unpack_from('<II',data,12);doc=json.loads(data[20:20+size]);changed=False
  for material in doc.get('materials',[]):
   if material.get('name','').startswith('amber_glass'):
    material['alphaMode']='BLEND';material['doubleSided']=True
    material.setdefault('pbrMetallicRoughness',{})['baseColorFactor']=[.28,.19,.09,.42];changed=True
  for material in doc.get('materials',[]):
   name=material.get('name','')
   pbr=material.setdefault('pbrMetallicRoughness',{})
   if any(key in name for key in ('bw_codex_relief','bw_codex_floor','bw_codex_plain_slab','bw_codex_pillar','Poly_Haven_rock')):
    pbr.pop('metallicRoughnessTexture',None);pbr['metallicFactor']=0;pbr['roughnessFactor']=.95;changed=True
   if 'wooden_lantern' in name:
    pbr['baseColorFactor']=[.035,.024,.009,.65] if 'glass' in name else [.42,.35,.25,1]
    pbr.pop('metallicRoughnessTexture',None);pbr['metallicFactor']=0;pbr['roughnessFactor']=.4 if 'glass' in name else .95
    if 'glass' in name:
     material['alphaMode']='BLEND';material['doubleSided']=True
     # A dim amber diffuser beneath the separate reflective glass pane caps near-field bulb irradiance.
     material.setdefault('extensions',{})['KHR_materials_unlit']={}
     used=doc.setdefault('extensionsUsed',[])
     if 'KHR_materials_unlit' not in used:used.append('KHR_materials_unlit')
     material['alphaMode']='OPAQUE';pbr['baseColorFactor']=[.24,.11,.025,1]
    material.pop('emissiveTexture',None);material['emissiveFactor']=[0,0,0];changed=True
  timber=next((m for m in doc.get('materials',[]) if 'prop_bp_beam_mossy_0' in m.get('name','')),None)
  if timber:
   for material in doc['materials']:
    if material.get('name','').startswith(('sorting_bin_','sorting_table_','ledger_shelf_')):
     source=timber.get('pbrMetallicRoughness',{})
     if 'baseColorTexture' in source:material['pbrMetallicRoughness']['baseColorTexture']=source['baseColorTexture'];changed=True
  if changed:
   chunk=json.dumps(doc,separators=(',',':')).encode();chunk+=b' '*((-len(chunk))%4)
   rest=data[20+size:];path.write_bytes(struct.pack('<III',0x46546c67,2,20+len(chunk)+len(rest))+struct.pack('<II',len(chunk),kind)+chunk+rest)
  output['sha256']=hashlib.sha256(path.read_bytes()).hexdigest();output['bytes']=path.stat().st_size
 if record.get('outputs'):
  meta=DEPOT/'ready'/record['asset_id']/(record['asset_id']+'_meta.json');meta.write_text(json.dumps(record,indent=2)+'\n')
(DEPOT/'room_manifest.json').write_text(json.dumps(manifest,indent=2)+'\n')
print('Room glass and checksums updated')
