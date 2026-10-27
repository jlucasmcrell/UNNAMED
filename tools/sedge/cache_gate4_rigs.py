"""Cache installed Gate-4 rigs with the existing CPU BC7 writer; preserve the full manifest.
Only run against an OFFLINE candidate. Args: PRIMARY GODOT CPU_PROJECT CACHE_SCRIPT [relative GLBs...].
"""
from pathlib import Path
import json,struct,hashlib,subprocess,sys,tempfile,io
from PIL import Image
root,godot,project,script=map(Path,sys.argv[1:5]);manifest=root/'texture_cache/manifest.json';before=json.loads(manifest.read_text());entries=[]
with tempfile.TemporaryDirectory(prefix='sedge_cache_') as temp:
 temp=Path(temp)
 files=[root/rel for rel in sys.argv[5:]] if len(sys.argv)>5 else [root/'rigged'/id/(id+'_rigged.glb') for id in ['sedge_drowned','sedge_eel_serpent']]
 for f in files:
  data=f.read_bytes();n=struct.unpack_from('<I',data,12)[0];doc=json.loads(data[20:20+n]);binary=data[28+n:];sig=hashlib.sha256(data[:65536]+(data[-min(65536,len(data)-65536):] if len(data)>65536 else b'')).hexdigest()[:24].upper();roles={}
  for m in doc.get('materials',[]):
   for key in ['baseColorTexture','metallicRoughnessTexture','normalTexture','occlusionTexture','emissiveTexture']:
    t=m.get('pbrMetallicRoughness',{}).get(key) or m.get(key)
    if t:roles[doc['textures'][t['index']]['source']]='normal' if key=='normalTexture' else 'data' if key in ['metallicRoughnessTexture','occlusionTexture'] else 'color'
  for i,image in enumerate(doc.get('images',[])):
   v=doc['bufferViews'][image['bufferView']];raw=binary[v.get('byteOffset',0):v.get('byteOffset',0)+v['byteLength']]
   source_image=Image.open(io.BytesIO(raw))
   if min(source_image.size)<4:
    padded=source_image.resize(tuple(max(4,n) for n in source_image.size),Image.Resampling.NEAREST);stream=io.BytesIO();padded.save(stream,format='PNG');raw=stream.getvalue();image=dict(image,mimeType='image/png')
   p=temp/(hashlib.sha256(raw).hexdigest()[:24]+('.png' if image['mimeType']=='image/png' else '.jpg'));p.write_bytes(raw);entries.append(dict(key=f.relative_to(root).as_posix()+f'#{i}',source=str(p),sig=sig,size=len(data),role=roles.get(i,'color')))
 source=temp/'entries.json';source.write_text(json.dumps(entries))
 try:
  subprocess.run([str(godot),'--headless','--path',str(project),'--script',str(script),'--',str(source),str(root)],check=True)
  new=json.loads(manifest.read_text());before['entries'].update(new['entries'])

 finally:manifest.write_text(json.dumps(before,indent=2))
 print('Gate4 cache merged:',len(before['entries']),'entries; native resolution retained')
