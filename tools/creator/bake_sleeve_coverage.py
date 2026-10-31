"""Complete a native long-sleeved tunic's body mask; keep its rig, geometry and maps.

python bake_sleeve_coverage.py BODY.glb PART_FOLDER
"""
from pathlib import Path
import hashlib,json,sys
import numpy as np
from PIL import Image,ImageDraw,ImageFilter
sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'animation'))
from audit_humgen_transport import Glb

def bake(body_path,folder):
    body_path=Path(body_path);folder=Path(folder);g=Glb(body_path)
    p=next(p for m in g.doc['meshes']if m['name'].endswith('_Body_LOD0')for p in m['primitives']
        if g.doc['materials'][p['material']]['name'].endswith('_Skin'))
    a=p['attributes'];uv=g.accessor(a['TEXCOORD_0']);vertices=g.accessor(a['POSITION'])
    joints=g.accessor(a['JOINTS_0']).astype(int);weights=g.accessor(a['WEIGHTS_0'])
    def belongs(i,words):
        return any(w in g.nodes[i].get('name','').lower()for w in words)or(i in g.parents and belongs(g.parents[i],words))
    arms=np.array([belongs(i,('upper_arm','forearm','clavicle','shoulder'))for i in g.joints])
    exposed=np.array([belongs(i,('hand','head','neck','foot','toe'))for i in g.joints])
    protected=(exposed[joints]*weights).sum(1)>.01
    selected=((arms[joints]*weights).sum(1)>=.5)&~protected
    faces=g.accessor(p['indices']).astype(int).reshape(-1,3)
    triangles=faces[selected[faces].all(1)];keep=faces[protected[faces].any(1)]
    assert len(triangles)>500
    contract=json.loads((folder/'creator_part.json').read_text())
    assert contract['target_body_sha256']==hashlib.sha256(body_path.read_bytes()).hexdigest()
    assert folder.name.endswith('_outfit_period_tunic'),'Only the native long-sleeved tunic is approved here'
    model=next(folder.glob('*_rigged.glb'));digest=hashlib.sha256(model.read_bytes()).hexdigest();cloth=Glb(model)
    pieces=[cloth.accessor(p['attributes']['POSITION'])for m in cloth.doc['meshes']if m['name'].endswith('_tunicviking_LOD0')for p in m['primitives']]
    assert pieces;points=np.concatenate(pieces)
    assert np.all(vertices[selected].min(0)>=points.min(0)-.05)and np.all(vertices[selected].max(0)<=points.max(0)+.05),'Sleeves do not reach the selected arm anatomy'
    size=1024;paint=Image.new('L',(size,size),0);draw=ImageDraw.Draw(paint)
    polygon=lambda f:[(float(uv[i,0])*size,float(uv[i,1])*size)for i in f]
    for f in triangles:draw.polygon(polygon(f),fill=255)
    paint=paint.filter(ImageFilter.MaxFilter(7));pixels=np.asarray(paint).copy()
    guard=Image.new('L',(size,size),0);draw=ImageDraw.Draw(guard)
    for f in keep:draw.polygon(polygon(f),fill=255)
    protected_pixels=np.asarray(guard)>0;pixels[protected_pixels]=0
    png=folder/'body_coverage.png';before=np.asarray(Image.open(png).convert('L'));assert before.shape==pixels.shape
    result=np.maximum(before,pixels);assert np.array_equal(result[protected_pixels],before[protected_pixels])
    centres=uv[triangles].mean(1);samples=np.clip((centres*size).astype(int),0,size-1)
    assert (result[samples[:,1],samples[:,0]]>127).mean()>.99
    Image.fromarray(result).save(png)
    path=folder/'body_coverage.json';data=json.loads(path.read_text())
    assert 'sleeve_coverage'not in data,'Preserve an existing bake; start from its original'
    for f in triangles:
        canonical=uv[f].copy();canonical[:,1]=1-canonical[:,1]
        data['uv_triangles'].append(canonical.reshape(-1).tolist())
    receipt=dict(part=folder.name,method='native arm hierarchy; hand/head/neck/foot descendants protected above1% weight; glTF top-left raster; Blender bottom-left JSON',
        vertices=int(selected.sum()),triangles=len(triangles),native_part_sha256=digest,exposed_face_neck_hands_unchanged=True,
        mask_sha256=hashlib.sha256(png.read_bytes()).hexdigest())
    data['sleeve_coverage']=receipt;path.write_text(json.dumps(data,separators=(',',':'))+'\n')
    assert hashlib.sha256(model.read_bytes()).hexdigest()==digest
    print(json.dumps(receipt))
    return receipt

if __name__=='__main__':bake(*sys.argv[1:])
