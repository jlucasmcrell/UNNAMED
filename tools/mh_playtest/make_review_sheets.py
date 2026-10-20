"""Make lossless-indexed review sheets from captured renderer evidence; never replace originals."""
from pathlib import Path
import sys,json
from PIL import Image,ImageOps,ImageDraw
folder=Path(sys.argv[1]);out=folder/'review_sheets';out.mkdir(exist_ok=True)
if (folder/'frames.json').exists():
 rows=json.loads((folder/'frames.json').read_text())['frames']
 groups={}
 for row in rows:groups.setdefault(row['native_clip'],[]).append(row['frame']+'.png')
else:
 groups={}
 for p in sorted(folder.glob('*.png')):
  key=p.stem.split('_')[1] if p.stem[:2].isdigit() else 'other'
  groups.setdefault(key,[]).append(p.name)
index=[]
for group,names in groups.items():
 for start in range(0,len(names),9):
  batch=names[start:start+9];cols=3;w=640;h=384
  sheet=Image.new('RGB',(cols*w,((len(batch)+cols-1)//cols)*h),'#182028');draw=ImageDraw.Draw(sheet)
  for i,name in enumerate(batch):
   im=Image.open(folder/name).convert('RGB');im.thumbnail((w,360),Image.Resampling.LANCZOS)
   x=(i%cols)*w;y=(i//cols)*h
   sheet.paste(im,(x,y+24));draw.text((x+5,y+5),name,fill='white')
  name=f'{group}_{start//9:02d}.png';sheet.save(out/name)
  index.append(dict(sheet=name,frames=batch))
(out/'index.json').write_text(json.dumps(index,indent=2));print(out,len(index),'sheets')
