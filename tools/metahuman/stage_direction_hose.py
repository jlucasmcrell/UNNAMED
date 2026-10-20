from pathlib import Path as _PipelinePath
import sys as _pipeline_sys
_pipeline_sys.path.insert(0, str(_PipelinePath(__file__).parent))
from pipeline_paths import pipeline_root as _pipeline_root
from pathlib import Path
import json,struct,io,re,hashlib
from PIL import Image
r=Path((_pipeline_root().as_posix() + ''));runtime=r/'runtime'
catalog=json.loads((runtime/'catalog.json').read_text());rows=json.loads((r/'evidence/hose_direction_rebind_manifest.json').read_text())
assert len(rows)==len(catalog['characters'])
def wool_texture(path):
 raw=path.read_bytes();length=struct.unpack_from('<I',raw,12)[0];doc=json.loads(raw[20:20+length]);binary=raw[28+length:]
 texture=doc['materials'][0]['pbrMetallicRoughness']['baseColorTexture']['index']
 image=doc['images'][doc['textures'][texture]['source']];view=doc['bufferViews'][image['bufferView']];start=view.get('byteOffset',0)
 return Image.open(io.BytesIO(binary[start:start+view['byteLength']])).convert('RGB')
def linear(v):return v/12.92 if v<=.04045 else ((v+.055)/1.055)**2.4
def srgb(v):return 12.92*v if v<=.0031308 else 1.055*v**(1/2.4)-.055
records=[]
palette_path=Path(__file__).with_name('WOOL_PALETTE.json')
palette=json.loads(palette_path.read_text())['colours']
for id,c in catalog['characters'].items():
 relative=f'shell/{id}_direction_hose.glb';path=runtime/'wardrobe'/relative;assert path.exists()
 c['trousers_path']=relative
 variant=c.get('wardrobe_variant','farmer_a') # Owner requested dark brown wool for both players.
 colour=palette[variant]
 image=wool_texture(path);channels=[]
 for band,multiplier in zip(image.split(),colour):
  channels.append(band.point([round(max(0,min(1,srgb(linear(i/255)*linear(multiplier))))*255) for i in range(256)]))
 image=Image.merge('RGB',channels);relative_albedo=f'wardrobe/recolour/{id}_{variant}_wool_hose.png'
 image.save(runtime/relative_albedo);c['trousers_albedo']=relative_albedo
 records.append(dict(id=id,variant=variant,source_palette=str(palette_path),source_colour_srgb=colour,linear_multiplier=[linear(x) for x in colour],texture=relative_albedo,sha256=hashlib.sha256((runtime/relative_albedo).read_bytes()).hexdigest()))
catalog['animation_status']='312 native Epic IK Retargeter clips, relaxed fingers and corrected foot/ball retarget-pose offsets; rest audit passed'
(runtime/'catalog.json').write_text(json.dumps(catalog,indent=2))
(r/'evidence/direction_hose_selection.json').write_text(json.dumps(dict(status='STAGED_REQUIRES_PACKAGED_VISUAL_REVIEW',characters=len(rows),rebinds=rows,materials=records,reason='Covered-leg mask removes underlying body; direction-rebound wool hose must pass packaged visual gates',native_body_modified=False),indent=2))
print('Staged',len(rows),'direction-rebound hose with shared wool palette; visual review pending')
