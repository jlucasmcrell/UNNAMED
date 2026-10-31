"""Reuse unchanged LOD0 native material images across an exported weapon's geometry LODs.

No pixels or geometry are resampled. This avoids a material change at a mesh LOD switch.
Usage: python p2_gear_share_lod_maps.py <private-art-root> <manifest.json>
"""
from pathlib import Path
import hashlib,json,struct,sys

def read(path):
    raw=path.read_bytes();assert raw[:4]==b'glTF'
    size,kind=struct.unpack_from('<II',raw,12);assert kind==0x4e4f534a
    doc=json.loads(raw[20:20+size]);n,kind=struct.unpack_from('<II',raw,20+size);assert kind==0x004e4942
    return doc,raw[28+size:28+size+n]

def image_bytes(doc,binary):
    return [binary[v.get('byteOffset',0):v.get('byteOffset',0)+v['byteLength']]
            for image in doc.get('images',[]) for v in [doc['bufferViews'][image['bufferView']]]]

def share(folder,asset):
    source=folder/(asset+'.glb');doc,binary=read(source);images=image_bytes(doc,binary)
    receipt={'source_sha256':hashlib.sha256(source.read_bytes()).hexdigest(),'native_images':[hashlib.sha256(b).hexdigest() for b in images],'lods':[]}
    for level in (1,2):
        file=folder/(asset+f'_lod{level}.glb');lod,data=read(file)
        assert len(lod.get('images',[]))==len(images),str(file)+': image count mismatch'
        def shape(material):return {k:v for k,v in material.items() if k!='name'}
        assert [shape(m) for m in lod.get('materials',[])]==[shape(m) for m in doc.get('materials',[])],str(file)+': material layout mismatch'
        replacement={im['bufferView']:images[i] for i,im in enumerate(lod.get('images',[]))}
        result=bytearray();geometry=[]
        for index,view in enumerate(lod['bufferViews']):
            payload=replacement.get(index,data[view.get('byteOffset',0):view.get('byteOffset',0)+view['byteLength']])
            if index not in replacement:geometry.append(hashlib.sha256(payload).hexdigest())
            result.extend(b'\0'*((-len(result))%4));view['byteOffset']=len(result);view['byteLength']=len(payload);result.extend(payload)
        result.extend(b'\0'*((-len(result))%4));lod['buffers'][0]['byteLength']=len(result)
        lod['images']=doc.get('images',[]).copy()
        # Image bufferView indices belong to this LOD, not the source geometry.
        for i,im in enumerate(lod['images']):lod['images'][i]=dict(im,bufferView=list(replacement)[i])
        for key in ('materials','textures','samplers'):
            if key in doc:lod[key]=doc[key]
        encoded=json.dumps(lod,separators=(',',':')).encode();encoded+=b' '*((-len(encoded))%4)
        file.write_bytes(struct.pack('<III',0x46546c67,2,28+len(encoded)+len(result))+struct.pack('<II',len(encoded),0x4e4f534a)+encoded+struct.pack('<II',len(result),0x004e4942)+result)
        checked,buff=read(file);assert image_bytes(checked,buff)==images
        assert [hashlib.sha256(buff[v['byteOffset']:v['byteOffset']+v['byteLength']]).hexdigest() for j,v in enumerate(checked['bufferViews']) if j not in replacement]==geometry
        receipt['lods'].append({'level':level,'geometry_buffers_unchanged':True,'sha256':hashlib.sha256(file.read_bytes()).hexdigest()})
    meta=folder/(asset+'_meta.json');record=json.loads(meta.read_text());record['lod_material_policy']='Unchanged full-resolution LOD0 maps on every geometry LOD; complete GPU mip chains retained.'
    for level in (1,2):record['lods'][level]['bytes']=(folder/(asset+f'_lod{level}.glb')).stat().st_size;record['lods'][level]['texture_px']=record['lods'][0]['texture_px']
    record['native_map_reuse']=receipt;meta.write_text(json.dumps(record,indent=2))
    return receipt

if __name__=='__main__':
    root,manifest=map(Path,sys.argv[1:3]);rows=json.loads(manifest.read_text());receipts={}
    for row in rows:receipts[row['art_id']]=share(root/'ready'/row['art_id'],row['art_id'])
    (root/'native_lod_material_receipts.json').write_text(json.dumps(receipts,indent=2));print('Shared unchanged native maps across',len(receipts),'weapon LOD ladders; all geometry buffers unchanged.')
