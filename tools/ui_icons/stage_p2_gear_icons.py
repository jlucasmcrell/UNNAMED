"""Stage native gear renders in the existing 128/256px icon format.

Usage: python stage_p2_gear_icons.py <manifest> <renders> <private-root>
The root may contain an existing icon manifest; entries are merged by ID.
"""
from pathlib import Path
from PIL import Image,ImageOps
import json,sys,hashlib

manifest,renders,root=map(Path,sys.argv[1:4]);rows=json.loads(manifest.read_text())
icons=root/'ui/icons';icons.mkdir(parents=True,exist_ok=True)
path=root/'manifests/ui_icons.json';path.parent.mkdir(parents=True,exist_ok=True)
doc=json.loads(path.read_text(encoding='utf-8-sig')) if path.exists() else dict(version=2,icons={})
receipts=[]
for row in rows:
    source=renders/(row['art_id']+'.png')
    if not source.exists():continue
    with Image.open(source) as image:
        image=image.convert('RGBA');box=image.getchannel('A').getbbox()
        assert box,row['item_id']+': empty render'
        crop=image.crop(box);crop.thumbnail((224,224),Image.Resampling.LANCZOS)
        master=Image.new('RGB',(256,256),(25,29,33));master.paste(crop,((256-crop.width)//2,(256-crop.height)//2),crop)
        basename=row['icon'].replace('.','_');master.save(icons/(basename+'_256.png'))
        master.resize((128,128),Image.Resampling.LANCZOS).save(icons/(basename+'_128.png'))
    doc['icons'][row['icon']]=dict(slot=row['icon'],source='native_model_render',sprite='ui/icons/'+basename+'_128.png',sprite_px=128,master='ui/icons/'+basename+'_256.png',master_px=256,alpha='none',note=row['name']+'; source '+row['review_id']+'; no generative processing')
    receipts.append(dict(item_id=row['item_id'],icon=row['icon'],render_sha256=hashlib.sha256(source.read_bytes()).hexdigest()))
path.write_text(json.dumps(doc,indent=2))
(root/'gear_icon_receipts.json').write_text(json.dumps(receipts,indent=2))
print('Staged',len(receipts),'native gear icons; merged manifest has',len(doc['icons']),'entries')
