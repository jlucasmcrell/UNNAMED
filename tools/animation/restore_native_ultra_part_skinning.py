"""Restore native eight-weight equipment skinning; retain every original geometry/image/morph byte.

Background CPU Blender: -- <original folder> <native authoring blend> <settings> <new creator base> <fresh output folder>
"""
import bpy
import json
from pathlib import Path
import shutil
import struct
import sys
from HumGen3D.human.human import Human
from HumGen3D.human.process.settings import ExportSettings
sys.path.insert(0,str(Path(__file__).resolve().parent))
from restore_native_ultra_skinning import Glb, sha, transport_native_weights

original, source, configuration, target, output = [Path(value) for value in sys.argv[sys.argv.index('--')+1:]]
assert original.resolve()!=output.resolve() and not output.exists(), 'Fresh private output only'
source_hash=sha(source)
contract=json.loads((original/'creator_part.json').read_text())
assert contract['kind']=='equipment', 'Equipment native-authoring route only'
target_proof=json.loads((target.parent/'rig_equivalence.json').read_text())
assert contract['target_body_sha256']==target_proof['old_sha256']
assert target_proof['maximum_rest_error']<1e-5 and target_proof['same_parents']
assert sha(target)==target_proof['new_sha256']
bpy.ops.wm.open_mainfile(filepath=str(source))
bpy.context.scene.render.threads_mode='FIXED'
bpy.context.scene.render.threads=2
bpy.context.scene.cycles.device='CPU'
humans={}
for obj in bpy.data.objects:
    human=Human.from_existing(obj,strict_check=False)
    if human: humans[human.objects.rig.name]=human
assert len(humans)==1
settings=ExportSettings.from_dict(json.loads(configuration.read_text()))
settings.output.format='in_file'
settings.textures.enabled=False
settings.lods=settings.lods[:1]
assert settings.lods[0].clothing=='original'
settings.lods[0].bones_per_vertex=8
settings.scripts.enabled=False
human=next(iter(humans.values())).process.run(settings,context=bpy.context).humans[0]
native={obj.name:obj for obj in human.objects if obj.type=='MESH'}
body=original/(original.name+'_rigged.glb')
g=Glb(body)
binary,prefix,reports=transport_native_weights(g,native,all_lods=True)
assert reports and all(row['maximum_position_error']<1e-5 for row in reports)
g.doc['buffers'][0]['byteLength']=len(binary)
encoded=json.dumps(g.doc,separators=(',',':')).encode()
encoded+=b' '*(-len(encoded)%4)
output.mkdir(parents=True)
for file in original.iterdir():
    if file.is_file(): shutil.copy2(file,output/file.name)
restored=output/body.name
restored.write_bytes(struct.pack('<III',0x46546c67,2,28+len(encoded)+len(binary))
 +struct.pack('<II',len(encoded),0x4e4f534a)+encoded+struct.pack('<II',len(binary),0x004e4942)+binary)
assert sha(source)==source_hash
contract.update(target_body_sha256=sha(target),native_quality=settings.lods[0].to_dict())
(output/'creator_part.json').write_text(json.dumps(contract,indent=2))
for file in output.glob('*.json'):
    if file.name=='creator_part.json': continue
    data=json.loads(file.read_text())
    if isinstance(data,dict) and data.get('target_body_sha256')==target_proof['old_sha256']:
        data['target_body_sha256']=sha(target)
        file.write_text(json.dumps(data,indent=2))
proof={'source':str(source),'source_sha256':source_hash,'input_body_sha256':sha(body),'output_body_sha256':sha(restored),
 'original_binary_prefix_sha256':prefix,'original_binary_unchanged':True,'geometry_morphs_images_unchanged':True,
 'source_bones_per_vertex':8,'meshes':reports,'base_rig_equivalence':target_proof}
(output/'native_skinning.json').write_text(json.dumps(proof,indent=2))
print('NATIVE_EQUIPMENT_ULTRA_SKINNING_STAGED',original.name,len(reports),'meshes',flush=True)
