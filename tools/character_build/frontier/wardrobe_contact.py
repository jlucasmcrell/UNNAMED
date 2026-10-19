"""Arrange actual game screenshots for a human turnaround/run review."""
from pathlib import Path
from PIL import Image, ImageDraw
import sys

folder=Path(sys.argv[1])
fit=sys.argv[2]
files=[folder/f'{fit}_idle_00_{view}.png' for view in ('front','side','back')]
walk=[folder/f'{fit}_walk_{phase:02}_{view}.png' for phase in (3,9) for view in ('front','side','back')]
if all(path.exists() for path in walk):files+=walk
files += [folder/f'{fit}_run_{phase:02}_{view}.png' for phase in (0,3,6,9) for view in ('front','side','back')]
sheet=Image.new('RGB',(1750,550*((len(files)+4)//5)),'#333333')
draw=ImageDraw.Draw(sheet)
for i,path in enumerate(files):
    pic=Image.open(path).convert('RGB')
    pic=pic.crop((600,60,1320,1080))
    pic.thumbnail((350,520))
    x=(i%5)*350; y=(i//5)*550
    sheet.paste(pic,(x+(350-pic.width)//2,y+24))
    draw.text((x+6,y+5),path.stem.removeprefix(fit+'_'),fill='white')
sheet.save(folder/'review_contact.jpg',quality=95)
print(folder/'review_contact.jpg')
