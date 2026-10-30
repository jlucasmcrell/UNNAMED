"""Fit a delivered garment to its unchanged native body and rebuild its skin stencil.

Run in background Blender (CPU geometry only): --python this_file -- BODY PART --fit
Only robe POSITION bases are corrected; all LODs, indices, skinning, morph deltas and
embedded texture bytes survive. Omit --fit to rebuild coverage alone.
"""
import hashlib,json,struct,sys
from pathlib import Path
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree

sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'animation'))
from audit_humgen_transport import Glb

def triangles(g,p):
    return g.accessor(p['indices']).astype(int).reshape((-1,3)).tolist()

def tree(points,faces):
    return BVHTree.FromPolygons([Vector(p) for p in points],faces,all_triangles=True)

def fit(points,body):
    offsets=np.zeros_like(points)
    for i,p in enumerate(points):
        q,n,_,distance=body.find_nearest(Vector(p))
        if q is None or distance>.06:continue
        signed=(Vector(p)-q).dot(n)
        if signed<.003:offsets[i]=np.array(n)*(.003-signed)
    # UV splits share a geometric point. Keep their clearance identical; moving
    # each incident facet separately opens the native cloth at its seams.
    _,weld=np.unique(np.round(points,6),axis=0,return_inverse=True)
    total=np.zeros((weld.max()+1,3));np.add.at(total,weld,offsets)
    return (total/np.bincount(weld)[:,None])[weld]

def main(body_file,part_file,refit):
    body,part=Glb(body_file),Glb(part_file)
    bm=next(m for m in body.doc['meshes'] if m['name'].endswith('_Body_LOD0'))
    bp=next(p for p in bm['primitives'] if body.doc['materials'][p['material']]['name'].endswith('_Skin'))
    bpos=body.accessor(bp['attributes']['POSITION']);bt=tree(bpos,triangles(body,bp))
    joints=body.accessor(bp['attributes']['JOINTS_0']).astype(int);weights=body.accessor(bp['attributes']['WEIGHTS_0'])
    joint_names=[body.doc['nodes'][i]['name'].lower() for i in body.doc['skins'][0]['joints']]
    binary=bytearray(part.binary);changes=[];written=set()
    by_name={n['name']:part.doc['meshes'][n['mesh']] for n in part.doc['nodes'] if 'mesh' in n}
    for name,mesh in by_name.items():
        if name.endswith('_lining'):continue
        lining=by_name.get(name+'_lining')
        for surface,p in enumerate(mesh['primitives']):
            index=p['attributes']['POSITION'];pos=part.accessor(index)
            delta=fit(pos,bt) if refit else np.zeros_like(pos)
            pairs=[(index,pos+delta)]
            if lining:
                li=lining['primitives'][surface]['attributes']['POSITION'];lp=part.accessor(li)
                assert len(lp)==len(pos) and np.max(np.linalg.norm(lp-pos,axis=1))<.0031,'Lining order/clearance differs'
                pairs.append((li,lp+delta))
            for accessor,values in pairs:
                if accessor in written:continue
                written.add(accessor);a=part.doc['accessors'][accessor];v=part.doc['bufferViews'][a['bufferView']]
                assert a['type']=='VEC3' and a['componentType']==5126 and 'sparse' not in a
                dest=np.ndarray((a['count'],3),dtype='<f4',buffer=binary,offset=v.get('byteOffset',0)+a.get('byteOffset',0),strides=(v.get('byteStride',12),4))
                dest[:]=values;a['min']=values.min(0).tolist();a['max']=values.max(0).tolist()
            changes.append(dict(mesh=name,vertices=len(pos),moved=int((np.linalg.norm(delta,axis=1)>1e-7).sum()),max_offset_m=float(np.linalg.norm(delta,axis=1).max())))
    # Verify every byte outside POSITION remained unchanged, including morphs,
    # weights, joint indices, normals, UVs, texture payloads and all inverse binds.
    allowed=np.zeros(len(binary),dtype=bool)
    for index in written:
        a=part.doc['accessors'][index];v=part.doc['bufferViews'][a['bufferView']];start=v.get('byteOffset',0)+a.get('byteOffset',0)
        for i in range(a['count']):allowed[start+i*v.get('byteStride',12):start+i*v.get('byteStride',12)+12]=True
    old=np.frombuffer(part.binary,dtype=np.uint8);new=np.frombuffer(binary,dtype=np.uint8)
    assert np.array_equal(old[~allowed],new[~allowed]),'Non-position payload changed'
    before=hashlib.sha256(Path(part_file).read_bytes()).hexdigest()
    if refit:
        encoded=json.dumps(part.doc,separators=(',',':')).encode();encoded+=b' '*(-len(encoded)%4)
        cooked=struct.pack('<III',0x46546c67,2,28+len(encoded)+len(binary))+struct.pack('<II',len(encoded),0x4e4f534a)+encoded+struct.pack('<II',len(binary),0x004e4942)+binary
        Path(part_file).write_bytes(cooked)
    # Use the delivered LOD0 outer shell and the delivered body's own UV0.
    points=[];faces=[]
    updated=Glb(part_file)
    for name,mesh in by_name.items():
        if not name.endswith('_LOD0'):continue
        for p in mesh['primitives']:
            base=len(points);points.extend(updated.accessor(p['attributes']['POSITION']).tolist())
            faces.extend([[base+i for i in tri] for tri in triangles(updated,p)])
    cloth=tree(points,faces);uv=body.accessor(bp['attributes']['TEXCOORD_0'])
    covered=set();protected=set()
    full_robe='outfit_period_robe' in Path(part_file).name
    normals=body.accessor(bp['attributes']['NORMAL'])
    for i,p in enumerate(bpos):
        bone=joint_names[joints[i,int(weights[i].argmax())]]
        exposed_bones=('head','neck','hand','finger','thumb') + (('foot','toe') if full_robe else ())
        if any(word in bone for word in exposed_bones):
            protected.add(i);continue
        # This authored full-length, long-sleeved robe conceals torso, arms and
        # legs. A nearest-surface stencil leaves patches where its coarse cloth
        # spans a rounded calf or an animated forearm; those are still clothed.
        if full_robe:
            covered.add(i);continue
        probe=Vector(p);hit=cloth.find_nearest(probe)
        if hit[0] is not None and hit[3]<=.02 and ((probe-hit[0]).dot(hit[1])<.001 or hit[3]<=.008):covered.add(i)
        outward=Vector(normals[i]).normalized();ray=cloth.ray_cast(probe,outward,.6)
        # A thick garment's authored lining faces inward. It still covers skin;
        # an outward ray need not reach an outward-facing triangle first.
        if ray[0] is not None:covered.add(i)
    painted=[];exposed=[]
    for face in triangles(body,bp):
        uv_face=[value for i in face for value in (float(uv[i,0]),float(1-uv[i,1]))]
        if any(i in protected for i in face):exposed.append(uv_face)
        if not all(i in covered for i in face):continue
        # bake_body_coverage consumes Blender UVs; glTF's V is already flipped.
        painted.append(uv_face)
    folder=Path(part_file).parent
    (folder/'body_coverage.json').write_text(json.dumps(dict(target_body_sha256=hashlib.sha256(Path(body_file).read_bytes()).hexdigest(),body_vertex_count=len(bpos),covered=len(covered),uv_triangles=painted,protected_uv_triangles=exposed,source='Native full robe coverage with exposed head/neck/hands/feet protected' if full_robe else 'Exported native body UV0 / LOD0 outer garment, protected head/neck/hands; all triangle corners covered')))
    receipt=dict(body=str(body_file),part=str(part_file),before_sha256=before,after_sha256=hashlib.sha256(Path(part_file).read_bytes()).hexdigest(),refitted=refit,position_changes=changes,all_non_position_payload_bytes_preserved=True,coverage_vertices=len(covered),coverage_triangles=len(painted))
    (folder/('native_fit_receipt.json' if refit else 'native_coverage_receipt.json')).write_text(json.dumps(receipt,indent=2));print(json.dumps(receipt),flush=True)

if __name__=='__main__':
    args=sys.argv[sys.argv.index('--')+1:];main(Path(args[0]),Path(args[1]),'--fit' in args[2:])
