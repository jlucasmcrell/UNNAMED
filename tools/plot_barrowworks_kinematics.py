"""Plot CSVs exported by the actual C# Kinematics reachability test; no collision logic is duplicated here."""
import argparse
import csv
from pathlib import Path
from PIL import Image, ImageDraw

parser=argparse.ArgumentParser()
parser.add_argument('directory',type=Path)
args=parser.parse_args()
scale=5
image=Image.new('RGB',(104*scale*2,144*scale),(24,27,30))
draw=ImageDraw.Draw(image)
for index,filename in enumerate(('closed.csv','open.csv')):
    origin=index*104*scale
    for row in csv.reader((args.directory/filename).open()):
        x,z=map(int,row)
        draw.rectangle((origin+x*scale,z*scale,origin+(x+1)*scale-1,(z+1)*scale-1),fill=(66,126,148))
    for room,x,z in csv.reader((args.directory/'rooms.csv').open()):
        x,z=float(x),float(z)
        draw.text((origin+x*scale-8,z*scale-6),room,fill='white')
    draw.text((origin+8,8),'Gates closed' if index==0 else 'Gates open',fill='white')
image.save(args.directory/'kinematics_fill.png')
