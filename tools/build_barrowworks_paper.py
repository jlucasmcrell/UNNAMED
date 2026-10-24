"""Rasterize existing reading copy into worn handwritten paper; no added story text."""
from pathlib import Path
import random,textwrap,re
from PIL import Image,ImageDraw,ImageFont,ImageFilter
ROOT=Path(__file__).resolve().parents[1]
OUT=Path(r'D:\Otherreach_Depot\barrowworks_codex\materials\paper');OUT.mkdir(parents=True,exist_ok=True)
texts=re.findall(r'done_text: "([^"]+)"',(ROOT/'content/regions/barrowworks.yaml').read_text())
S=4  # Ultra first: the 512 x 768 layout drawn at four times the resolution
W,H=512*S,768*S
font=ImageFont.truetype(r'C:\Windows\Fonts\segoepr.ttf',19*S)
for index,text in enumerate(texts[:5]):
 rng=random.Random(901+index);image=Image.new('RGB',(W,H));pix=image.load()
 for y in range(H):
  for x in range(W):
   edge=min(x,W-1-x,y,H-1-y)/S;wear=max(0,20-edge)*1.2;v=rng.gauss(0,2)
   pix[x,y]=(int(188-wear+v),int(166-wear+v),int(125-wear+v))
 draw=ImageDraw.Draw(image)
 for row,line in enumerate(textwrap.wrap(text,36)[:25]):
  y=(42+row*25)*S;draw.text(((26+rng.randrange(-3,4))*S,y),line,font=font,fill=(43,37,26))
 for _ in range(40):
  x,y=rng.randrange(W),rng.randrange(H);draw.line((x,y,x+rng.randrange(4,35)*S,y+S),fill=(160,139,103),width=S)
 image.filter(ImageFilter.GaussianBlur(.25*S)).save(OUT/f'reading_{index}.png')
print('worn paper textures',len(texts[:5]))
