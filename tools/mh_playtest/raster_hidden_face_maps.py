"""Rasterize read-only native-body triangle UV coverage into outfit Hidden Face Maps."""
from pathlib import Path
import json,hashlib
from PIL import Image,ImageDraw
root=Path('D:/Otherreach_Scratch/mh_playtest_20261019/runtime')
catalog=json.loads((root/'catalog.json').read_text())
records=[]
for id,c in catalog['characters'].items():
 path=root/c['hidden_mask'];source=Path(str(path)+'.triangles.json')
 if not source.exists():continue
 data=json.loads(source.read_text())
 image=Image.new('L',(2048,2048),0);draw=ImageDraw.Draw(image)
 for triangle in data['triangles']:
  draw.polygon([((triangle[i]%1)*2048,(triangle[i+1]%1)*2048) for i in (0,2,4)],fill=255)
 if image.getextrema()!=(0,255):raise RuntimeError('Empty/full hidden map '+id)
 image.save(path,optimize=True)
 source_hashes={p:hashlib.sha256((root/p).read_bytes()).hexdigest() for p in [c['body'],'wardrobe/chest.glb','wardrobe/pants.glb']+(['wardrobe/hood.glb'] if c.get('hood',True) else [])}
 row=dict(id=id,path=str(path),triangles=len(data['triangles']),resolution=2048,uv_origin='native Godot mesh UV; Image row V',source_sha256=source_hashes,config=c,native_buffers_modified=False)
 records.append(row);print(id,'hidden triangles',row['triangles'],'mask bytes',path.stat().st_size)
(root/'masks/bake_manifest.json').write_text(json.dumps(records,indent=2))
