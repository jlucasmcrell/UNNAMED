"""Stage the B-005 piling from the accepted mossy beam, retaining every LOD and embedded image byte.
Usage: python stage_cairnwold_piling.py <private-art-root> <receipt.json>
"""
import json, struct, math, hashlib, sys
from pathlib import Path
art = Path(sys.argv[1]).resolve(strict=True)
oldid, newid = 'prop_bp_beam_mossy', 'prop_cw_jetty_piling'
target = art / 'ready' / newid
target.mkdir(parents=True, exist_ok=True)
def signature(blob):
 head=blob[:65536];tail=blob[-min(65536,len(blob)-len(head)):] if len(blob)>65536 else b''
 return hashlib.sha256(head+tail).hexdigest().upper()[:24]
changed={}
assert (art/'ready'/oldid/(oldid+'.glb')).is_file(), 'Accepted mossy beam is required'
for src in sorted((art/'ready'/oldid).glob('*.glb')):
 raw=src.read_bytes(); magic,version,length=struct.unpack_from('<III',raw);assert magic==0x46546c67 and version==2 and length==len(raw)
 size,kind=struct.unpack_from('<II',raw,12);assert kind==0x4e4f534a
 doc=json.loads(raw[20:20+size]);binary=raw[20+size:]
 scene=doc['scenes'][doc.get('scene',0)]; children=list(scene.get('nodes',[]));node={'name':newid,'children':children,'rotation':[0,0,math.sqrt(.5),math.sqrt(.5)],'scale':[3.8/6.4026,.32/.5072,.32/.4844],'translation':[.16,1.9,0]}
 index=len(doc['nodes']);doc['nodes'].append(node);scene['nodes']=[index]
 encoded=json.dumps(doc,separators=(',',':')).encode();encoded+=b' '*((-len(encoded))%4)
 cooked=struct.pack('<III',magic,version,20+len(encoded)+len(binary))+struct.pack('<II',len(encoded),kind)+encoded+binary
 dst=target/src.name.replace(oldid,newid);dst.write_bytes(cooked)
 assert cooked[20+len(encoded):]==raw[20+size:], 'Texture and mesh BIN payload must be unchanged'
 changed[src.name]={'path':str(dst),'size':len(cooked),'sig':signature(cooked),'sha256':hashlib.sha256(cooked).hexdigest(),'source_sha256':hashlib.sha256(raw).hexdigest(),'binary_payload_identical':True}
meta=json.loads((art/'ready'/oldid/(oldid+'_meta.json')).read_text());meta.update(asset_id=newid,name='Cairnwold jetty timber piling',derivation='Existing accepted mossy timber beam stood vertically, authored 0.32 x 3.8 x 0.32 m; texture/mesh BIN payload and LOD detail retained.',source_asset_id=oldid)
meta['transform']={'dimensions_m':{'x':.32,'y':3.8,'z':.32},'min':[-.16,0,-.16],'max':[.16,3.8,.16]};(target/(newid+'_meta.json')).write_text(json.dumps(meta,indent=2))
cachefile=art/'texture_cache/manifest.json';cache=json.loads(cachefile.read_text());added={}
for key,value in list(cache['entries'].items()):
 if not key.startswith('ready/'+oldid+'/'):continue
 basename=key.split('#')[0].split('/')[-1];info=changed[basename]
 added[key.replace(oldid,newid)]={**value,'size':info['size'],'sig':info['sig']}
cache['entries'].update(added);cachefile.write_text(json.dumps(cache,indent=2))
receipt = {'asset': newid, 'source_asset': oldid, 'files': changed,
           'cache_entries_added': len(added), 'cache_dds_bytes_reused_unchanged': True,
           'dimensions_m': [0.32, 3.8, 0.32], 'all_source_lods_retained': True}
Path(sys.argv[2]).write_text(json.dumps(receipt, indent=2), encoding='utf-8')
print('CAIRNWOLD_PILING_STAGED', len(changed), 'LODs,', len(added), 'signed image entries')
