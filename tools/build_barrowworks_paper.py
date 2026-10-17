"""Rasterize existing reading copy into worn handwritten paper; no added story text."""
from pathlib import Path
import random,textwrap,re
from PIL import Image,ImageDraw,ImageFont,ImageFilter
ROOT=Path(__file__).resolve().parents[1]
OUT=Path(r'D:\Otherreach_Depot\barrowworks_codex\materials\paper');OUT.mkdir(parents=True,exist_ok=True)
texts=re.findall(r'done_text: "([^"]+)"',(ROOT/'content/regions/barrowworks.yaml').read_text())
font=ImageFont.truetype(r'C:\Windows\Fonts\segoepr.ttf',19)
for index,text in enumerate(texts[:5]):
 rng=random.Random(901+index);image=Image.new('RGB',(512,768));pix=image.load()
 for y in range(768):
  for x in range(512):
   edge=min(x,511-x,y,767-y);wear=max(0,20-edge)*1.2;v=rng.gauss(0,2)
   pix[x,y]=(int(188-wear+v),int(166-wear+v),int(125-wear+v))
 draw=ImageDraw.Draw(image)
 for row,line in enumerate(textwrap.wrap(text,36)[:25]):
  y=42+row*25;draw.text((26+rng.randrange(-3,4),y),line,font=font,fill=(43,37,26))
 for _ in range(40):
  x,y=rng.randrange(512),rng.randrange(768);draw.line((x,y,x+rng.randrange(4,35),y+1),fill=(160,139,103),width=1)
 image.filter(ImageFilter.GaussianBlur(.25)).save(OUT/f'reading_{index}.png')
print('worn paper textures',len(texts[:5]))
