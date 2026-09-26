"""Every ready model whose occlusion channel (glTF occlusionTexture, R) is near black where its mesh samples it: Godot applies baked
occlusion, Blender does not, so such a model shows black patches only in the game.

    python ao_check.py [model.glb ...]    (default: every assets/ready model)
"""
import json,struct,io,glob,os,sys
from PIL import Image
import numpy as np
Image.MAX_IMAGE_PIXELS=None
def acc(g,bin_,i):
    a=g['accessors'][i]; bv=g['bufferViews'][a['bufferView']]; o=bv.get('byteOffset',0)+a.get('byteOffset',0)
    k={'SCALAR':1,'VEC2':2,'VEC3':3,'VEC4':4}[a['type']]
    dt={5126:'<f4',5123:'<u2',5121:'u1',5125:'<u4'}[a['componentType']]
    stride=bv.get('byteStride')
    arr=np.frombuffer(bin_[o:o+ (stride or np.dtype(dt).itemsize*k)*a['count']],dtype=dt)
    if stride and stride!=np.dtype(dt).itemsize*k: arr=np.lib.stride_tricks.as_strided(arr,(a['count'],k),(stride,np.dtype(dt).itemsize))
    else: arr=arr.reshape(a['count'],k)
    if a.get('normalized') and dt!='<f4': arr=arr/np.iinfo(dt).max
    return arr
def check(p):
    b=open(p,'rb').read(); n=struct.unpack('<I',b[12:16])[0]; g=json.loads(b[20:20+n]); bin_=b[20+n+8:]
    out=[]
    imgs={}
    for mesh in g.get('meshes',[]):
        for prim in mesh['primitives']:
            m=g['materials'][prim['material']] if 'material' in prim else {}
            o=m.get('occlusionTexture')
            if not o or 'TEXCOORD_%d'%o.get('texCoord',0) not in prim['attributes']: continue
            t=g['textures'][o['index']]; src=t.get('source', next(iter(t.get('extensions',{}).values()),{}).get('source'))
            if src not in imgs:
                img=g['images'][src]; bv=g['bufferViews'][img['bufferView']]
                imgs[src]=np.asarray(Image.open(io.BytesIO(bin_[bv.get('byteOffset',0):bv.get('byteOffset',0)+bv['byteLength']])).convert('RGB'))[...,0]/255
            ao=imgs[src]; h,w=ao.shape
            uv=acc(g,bin_,prim['attributes']['TEXCOORD_%d'%o.get('texCoord',0)])
            x=np.clip((uv[:,0]%1)*w,0,w-1).astype(int); y=np.clip((uv[:,1]%1)*h,0,h-1).astype(int)
            v=ao[y,x]; out.append(((v<0.15).mean(), float(np.median(v)), m.get('name')))
    return out
ASSETS=os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)),'..','..','assets'))
paths=sys.argv[1:] or [p for p in sorted(glob.glob(os.path.join(ASSETS,'ready','*','*.glb'))) if '_lod' not in os.path.basename(p)]
bad=[]
for p in paths:
    try: r=check(p)
    except Exception as e: continue
    for dark,med,name in r:
        if dark>0.10: bad.append((round(100*dark,1),round(med,2),os.path.relpath(p,ASSETS),name))
for x in sorted(bad,reverse=True)[:60]: print(x)
print('flagged',len(bad),'of',len(paths))
