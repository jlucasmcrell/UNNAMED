"""Remove W23/W24 real-world branding using only nearby pixels from the same source maps.

Non-generative local clone paint: native dimensions, UVs, geometry and other pixels remain unchanged.
Usage: python p2_gear_remove_branding.py <private-art-root>
"""
from pathlib import Path
from PIL import Image,ImageDraw,ImageFilter,ImageChops
import hashlib,io,json,struct,sys
import cv2
import numpy as np

def read(path):
 raw=path.read_bytes();n=struct.unpack_from('<I',raw,12)[0];d=json.loads(raw[20:20+n]);return d,raw[28+n:]
def write(path,d,binary,replacements):
 result=bytearray()
 for i,v in enumerate(d['bufferViews']):
  payload=replacements.get(i,binary[v.get('byteOffset',0):v.get('byteOffset',0)+v['byteLength']])
  result.extend(b'\0'*((-len(result))%4));v['byteOffset']=len(result);v['byteLength']=len(payload);result.extend(payload)
 result.extend(b'\0'*((-len(result))%4));d['buffers'][0]['byteLength']=len(result)
 j=json.dumps(d,separators=(',',':')).encode();j+=b' '*((-len(j))%4)
 path.write_bytes(struct.pack('<III',0x46546c67,2,28+len(j)+len(result))+struct.pack('<II',len(j),0x4e4f534a)+j+struct.pack('<II',len(result),0x004e4942)+result)
def clone(raw,reference,target,donor,role):
 original=Image.open(io.BytesIO(raw)).convert('RGB');sx,sy=original.width/reference[0],original.height/reference[1]
 box=tuple(round(v*(sx if i%2==0 else sy)) for i,v in enumerate(target));source=tuple(round(v*(sx if i%2==0 else sy)) for i,v in enumerate(donor))
 patch=original.crop(source);assert patch.size==(box[2]-box[0],box[3]-box[1])
 mask=Image.new('L',original.size);draw=ImageDraw.Draw(mask);edge=max(2,round(12*min(sx,sy)));draw.rectangle((box[0]+edge,box[1]+edge,box[2]-edge,box[3]-edge),fill=255);mask=mask.filter(ImageFilter.GaussianBlur(max(1,edge/2)))
 if role=='baseColorTexture':
  # Classic Poisson boundary blending, not a learned model: keep source grain
  # while matching the surrounding source colour instead of leaving a rectangle.
  local_mask=np.zeros((patch.height,patch.width),dtype=np.uint8);local_mask[8:-8,8:-8]=255
  painted=cv2.seamlessClone(np.array(patch),np.array(original),local_mask,((box[0]+box[2])//2,(box[1]+box[3])//2),cv2.NORMAL_CLONE)
  edited=Image.fromarray(painted)
 else:
  edited=original.copy();edited.paste(Image.composite(patch,original.crop(box),mask.crop(box)),box)
 difference=ImageChops.difference(original,edited).getbbox();assert difference and box[0]<=difference[0]<=difference[2]<=box[2] and box[1]<=difference[1]<=difference[3]<=box[3]
 out=io.BytesIO();edited.save(out,format='PNG');return out.getvalue(),dict(size=list(original.size),target=list(box),donor=list(source),changed_bounds=list(difference))

root=Path(sys.argv[1]);receipts=[]
for asset,wood in [('gear_double_bit_axe',1),('gear_weathered_double_bit_axe',2)]:
 folder=root/'ready'/asset
 for level in (0,1,2):
  path=folder/(asset+(f'_lod{level}' if level else '')+'.glb');before=hashlib.sha256(path.read_bytes()).hexdigest();d,b=read(path);patches={};edits=[]
  specs=[(wood,(1302,7584),(270,2980,485,3620),(270,2300,485,2940))]
  if wood==2:specs.append((0,(2365,1322),(320,500,600,1040),(650,500,930,1040)))
  for mi,reference,target,donor in specs:
   material=d['materials'][mi];slots={**material,**material.get('pbrMetallicRoughness',{})}
   for role in ('baseColorTexture','normalTexture','metallicRoughnessTexture','occlusionTexture'):
    if role not in slots:continue
    image_index=d['textures'][slots[role]['index']]['source'];image=d['images'][image_index];view_index=image['bufferView']
    if view_index in patches:continue
    v=d['bufferViews'][view_index];raw=b[v.get('byteOffset',0):v.get('byteOffset',0)+v['byteLength']];painted,edit=clone(raw,reference,target,donor,role);patches[view_index]=painted;image['mimeType']='image/png';edits.append(dict(material=mi,role=role,image=image_index,**edit))
  write(path,d,b,patches);receipts.append(dict(asset=asset,level=level,before_sha256=before,after_sha256=hashlib.sha256(path.read_bytes()).hexdigest(),method='Nearby original pixels; classic Poisson boundary blend on albedo and feathered local clone on data maps; no generative model',edits=edits))
 meta=folder/(asset+'_meta.json');record=json.loads(meta.read_text());record['branding_removal']='Manufacturer text/logos cloned out locally from the same source wood/steel maps; normal and roughness channels matched. No generative processing. See brand_removal_receipts.json.'
 for level in (0,1,2):record['lods'][level]['bytes']=(folder/(asset+(f'_lod{level}' if level else '')+'.glb')).stat().st_size
 meta.write_text(json.dumps(record,indent=2))
(root/'brand_removal_receipts.json').write_text(json.dumps(receipts,indent=2));print('Removed wood branding on W23/W24 and forged logo on W24, all native-map channels, all three levels. Other source pixels unchanged.')
