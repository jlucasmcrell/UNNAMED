"""Assemble labelled review sheets from ORIGINAL native viewport frames; never alter the subjects."""
import hashlib,json,sys
from pathlib import Path
from PIL import Image,ImageDraw,ImageOps
from build_npc_looks_sheet import build
root=Path(sys.argv[1])
if not (root/'CAPTURE_READBACK.json').is_file():raise SystemExit('Run check_role_review.py first')
readback=json.loads((root/'CAPTURE_READBACK.json').read_text())
if readback['errors']:raise SystemExit('Readback failed')
index=['<h1>Gate C native role outfits</h1><p>Actual Ashen Hollow and Cairnwold rosters, staged on the same clear Cairnwold ground at noon. Appearance fixture, not world performance proof. Original frames retained. Motion sheets sample native clips at phase .45; they are not continuous-video gates.</p>']
for town in ['ashen_hollow','cairnwold']:
 folder=root/town;build(folder);index+=['<h2>'+town+'</h2>',f'<img width="1000" src="{town}/NPC_LOOKS.png">']
 rows=json.loads((folder/'npc_looks.json').read_text())['npcs']
 for row in rows:
  identifier=row['id'];clips=['walk','run','crouch_idle','sword_attack_1']
  sheet=Image.new('RGB',(1600,560),(18,18,20));draw=ImageDraw.Draw(sheet)
  for i,clip in enumerate(clips):
   im=Image.open(folder/(identifier+'_motion_'+clip+'.png')).convert('RGB')
   left=(im.width-im.height)//2;im=im.crop((left,0,left+im.height,im.height))
   sheet.paste(ImageOps.pad(im,(400,520),method=Image.Resampling.LANCZOS),(i*400,25));draw.text((i*400+8,7),clip,fill='white')
  draw.text((8,545),identifier+' | '+row['outfit'],fill='white')
  name=identifier+'_MOTION.png';sheet.save(folder/name);index+=[f'<h3>{identifier}</h3><img width="1200" src="{town}/{name}">']
index+=['<p>Kal crops are prepared separately after viewing each original and selecting an honest ROI.</p>']
(root/'REVIEW.html').write_text('<!doctype html><meta charset="utf-8"><style>body{background:#151519;color:#eee;font:16px sans-serif}img{display:block;max-width:100%}</style>'+''.join(index),encoding='utf-8')
print('Two roster sheets and 20 motion sheets assembled; ORIGINAL pixel review still required.')
