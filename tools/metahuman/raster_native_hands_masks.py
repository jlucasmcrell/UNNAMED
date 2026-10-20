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
 variants=[('leather',c['hidden_mask'])]

 hands=json.loads((root/(c['hidden_mask']+'.hands.json')).read_text())
 hand_image=rasterize(hands['triangles'])
 for variant,relative in variants:
  path=root/relative; data=json.loads(Path(str(path)+'.triangles.json').read_text())
  image=rasterize(data['triangles'])
  from PIL import ImageChops,ImageStat
  legs_path=Path(str(path)+'.legs.json')
  assert legs_path.exists(),'Full trousers/boots require explicit covered-leg UVs'
  legs=json.loads(legs_path.read_text())
  leg_image=rasterize(legs['triangles'])
  image=ImageChops.lighter(image,leg_image)
  before=ImageStat.Stat(ImageChops.multiply(image,hand_image)).sum[0]/255
  image.paste(0,(0,0),hand_image)
  assert ImageChops.multiply(image,hand_image).getextrema()==(0,0),'Native hand UV still masked'
  assert image.getextrema()==(0,255),'Empty/full body mask'
  image.save(path,optimize=True)
  paths=[c['body'],'wardrobe/'+c.get('garment_directory','')+'chest.glb','wardrobe/'+c.get('trousers_path',c.get('garment_directory','')+'pants.glb'),'wardrobe/'+c['boots_path']]
  if c.get('hood',True):paths.append('wardrobe/'+c.get('garment_directory','')+'hood.glb')
  records.append(dict(id=id,variant=variant,mask=relative,hidden_triangles=len(data['triangles']),covered_leg_triangles=len(legs['triangles']),waist_cutoff_m=legs['waist_cutoff_m'],native_hand_triangles=len(hands['triangles']),previously_hidden_hand_pixels=before,hidden_hand_pixels_after=0,native_body_modified=False,source_sha256={p:hashlib.sha256((root/p).read_bytes()).hexdigest() for p in paths}))
  print(id,variant,'native hands restored; hidden-hand pixels = 0')
(_pipeline_root()/'evidence/native_hands_boots_manifest.json').write_text(json.dumps(records,indent=2))
