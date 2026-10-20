"""Rasterize read-only native-body triangle UV coverage into outfit Hidden Face Maps."""
from pathlib import Path as _PipelinePath
import sys as _pipeline_sys
_pipeline_sys.path.insert(0, str(_PipelinePath(__file__).parent))
from pipeline_paths import pipeline_root as _pipeline_root
from pathlib import Path
import json,hashlib,math
from PIL import Image,ImageDraw
root=Path((_pipeline_root().as_posix() + '/runtime'))
catalog=json.loads((root/'catalog.json').read_text())
def rasterize(triangles,size=2048):
 image=Image.new('L',(size,size),0);draw=ImageDraw.Draw(image)
 for triangle in triangles:
  # Keep each triangle continuous at exact UDIM edges; wrap the whole polygon, never individual corners.
  base_u=math.floor(min(triangle[0::2]));base_v=math.floor(min(triangle[1::2]))
  polygon=[((triangle[i]-base_u)*size,(triangle[i+1]-base_v)*size) for i in (0,2,4)]
  for dx in [-size,0,size]:
   for dy in [-size,0,size]:
    shifted=[(x+dx,y+dy) for x,y in polygon]
    if max(x for x,y in shifted)<0 or min(x for x,y in shifted)>=size or max(y for x,y in shifted)<0 or min(y for x,y in shifted)>=size:continue
    draw.polygon(shifted,fill=255)
 return image
edge=rasterize([[1.99,.1,2.0,.1,2.0,.2]])
assert edge.getpixel((1024,300))==0 and edge.getpixel((2040,300))==255,'UDIM edge triangle flooded the texture'
records=[]
for id,c in catalog['characters'].items():
 path=root/c['hidden_mask'];source=Path(str(path)+'.triangles.json')
 if not source.exists():continue
 data=json.loads(source.read_text())
 image=rasterize(data['triangles'])
 if image.getextrema()!=(0,255):raise RuntimeError('Empty/full hidden map '+id)
 image.save(path,optimize=True)
 paths=[c['body'],'wardrobe/'+c.get('garment_directory','')+'chest.glb','wardrobe/'+c.get('trousers_path',c.get('garment_directory','')+'pants.glb')]
 if c.get('hood',True):paths.append('wardrobe/'+c.get('garment_directory','')+'hood.glb')
 if c.get('boots_path'):paths.append('wardrobe/'+c['boots_path'])
 source_hashes={p:hashlib.sha256((root/p).read_bytes()).hexdigest() for p in paths}
 row=dict(id=id,path=str(path),triangles=len(data['triangles']),resolution=2048,uv_origin='native Godot mesh UV; Image row V',source_sha256=source_hashes,config=c,native_buffers_modified=False)
 records.append(row);print(id,'hidden triangles',row['triangles'],'mask bytes',path.stat().st_size)
(_pipeline_root()/'evidence/bake_manifest.json').write_text(json.dumps(records,indent=2))
