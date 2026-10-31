"""Conceal feet inside full boots, using the unchanged native body's joint hierarchy and UVs.

python bake_boot_coverage.py BODY.glb PART_FOLDER [PART_FOLDER ...]
Only coverage PNG/JSON files change. Native geometry, controls, materials and rig stay untouched.
"""
from pathlib import Path
import hashlib,json,sys
import numpy as np
from PIL import Image,ImageDraw,ImageFilter

sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'animation'))
from audit_humgen_transport import Glb

def bake(body_file, folders):
    body_file=Path(body_file);body=Glb(body_file)
    bp=next(p for m in body.doc['meshes']if m['name'].endswith('_Body_LOD0')for p in m['primitives']
            if body.doc['materials'][p['material']]['name'].endswith('_Skin'))
    attrs=bp['attributes'];uv=body.accessor(attrs['TEXCOORD_0']);positions=body.accessor(attrs['POSITION'])
    joints=body.accessor(attrs['JOINTS_0']).astype(int);weights=body.accessor(attrs['WEIGHTS_0'])
    parents=body.parents
    def belongs(node,words):
        return any(w in body.nodes[node].get('name','').lower()for w in words)or(node in parents and belongs(parents[node],words))
    foot_joints=np.array([belongs(i,('foot','toe'))for i in body.joints])
    exposed_joints=np.array([belongs(i,('hand','head','neck'))for i in body.joints])
    foot=(foot_joints[joints]*weights).sum(1)>=.5
    exposed=(exposed_joints[joints]*weights).sum(1)>=.5
    faces=body.accessor(bp['indices']).astype(int).reshape(-1,3)
    triangles=faces[foot[faces].all(1)];protected=faces[exposed[faces].any(1)]
    assert len(triangles)>100 and not exposed[triangles].any(),'Foot selection must retain face, neck and every hand descendant'
    size=1024;mask=Image.new('L',(size,size),0);draw=ImageDraw.Draw(mask)
    # glTF UVs, unlike Blender's source UVs, already start at the image's top.
    def polygon(face):return [(float(uv[i,0])*size,float(uv[i,1])*size)for i in face]
    for face in triangles:draw.polygon(polygon(face),fill=255)
    mask=mask.filter(ImageFilter.MaxFilter(7))
    keep=Image.new('L',(size,size),0);keep_draw=ImageDraw.Draw(keep)
    for face in protected:keep_draw.polygon(polygon(face),fill=255)
    pixels=np.asarray(mask).copy();keep_pixels=np.asarray(keep)>0
    assert not pixels[keep_pixels].any(),'A boot mask must never erase exposed face/neck/hands'
    centres=uv[triangles].mean(1)
    samples=np.clip((centres*size).astype(int),0,size-1)
    assert (pixels[samples[:,1],samples[:,0]]>127).all(),'Native glTF foot UVs must sample covered pixels'
    digest=hashlib.sha256(body_file.read_bytes()).hexdigest();results=[]
    for folder in map(Path,folders):
        contract=json.loads((folder/'creator_part.json').read_text())
        assert contract['target_body_sha256']==digest,'Boot coverage targets another body'
        part_file=next(folder.glob('*_rigged.glb'));part_before=hashlib.sha256(part_file.read_bytes()).hexdigest()
        part=Glb(part_file)
        boots=[part.accessor(p['attributes']['POSITION'])for m in part.doc['meshes']if '_Boots_LOD0' in m['name']and not m['name'].endswith('_lining')for p in m['primitives']]
        assert boots,'Only a full-boot part may conceal the whole foot'
        collar=max(float(p[:,1].max())for p in boots)
        assert collar>float(positions[foot,1].max())+.025,'Boot collar does not cover the selected anatomy'
        png=folder/'body_coverage.png';old=np.asarray(Image.open(png).convert('L'))if png.exists()else np.zeros((size,size),np.uint8)
        assert old.shape==pixels.shape
        combined=np.maximum(old,pixels)
        assert np.array_equal(combined[keep_pixels],old[keep_pixels]),'Exposed anatomy changed'
        Image.fromarray(combined).save(png)
        path=folder/'body_coverage.json';data=json.loads(path.read_text())if path.exists()else dict(target_body_sha256=digest,body_vertex_count=len(uv),covered=0,uv_triangles=[])
        # Keep the native export's existing regions and append only the boot layer.
        if 'boot_coverage'not in data:
            for face in triangles:
                canonical=uv[face].copy();canonical[:,1]=1-canonical[:,1]
                data['uv_triangles'].append(canonical.reshape(-1).tolist())
        data['boot_coverage']=dict(method='native foot/toe hierarchy, >=50% skin weight; glTF top-left raster / Blender bottom-left coverage JSON; 3 px seam padding',
            vertices=int(foot.sum()),triangles=len(triangles),collar_m=collar,highest_covered_m=float(positions[foot,1].max()),
            native_part_sha256=part_before,exposed_face_neck_hands_unchanged=True)
        path.write_text(json.dumps(data,separators=(',',':'))+'\n')
        assert hashlib.sha256(part_file.read_bytes()).hexdigest()==part_before,'Native garment payload changed'
        results.append(dict(part=folder.name,**data['boot_coverage'],mask_sha256=hashlib.sha256(png.read_bytes()).hexdigest()))
    return results

if __name__=='__main__':
    print(json.dumps(bake(sys.argv[1],sys.argv[2:]),indent=2))
