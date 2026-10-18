"""Arrange actual game screenshots for a human turnaround/run review."""
from pathlib import Path
from PIL import Image, ImageDraw
import sys

folder=Path(sys.argv[1])
fit=sys.argv[2]
files=[folder/f'{fit}_idle_00_{view}.png' for view in ('front','side','back')]
files += [folder/f'{fit}_run_{phase:02}_{view}.png' for phase in (0,3,6,9) for view in ('front','side','back')]
sheet=Image.new('RGB',(1250,1650),'#333333')
draw=ImageDraw.Draw(sheet)
for i,path in enumerate(files):
    pic=Image.open(path).convert('RGB')
    pic=pic.crop((730,60,1190,1080))
    pic.thumbnail((250,520))
    x=(i%5)*250; y=(i//5)*550
    sheet.paste(pic,(x+(250-pic.width)//2,y+24))
    draw.text((x+6,y+5),path.stem.removeprefix(fit+'_'),fill='white')
sheet.save(folder/'review_contact.jpg',quality=95)
print(folder/'review_contact.jpg')
