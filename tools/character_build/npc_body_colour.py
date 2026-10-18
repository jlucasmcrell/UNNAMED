"""Optional colour-only identity/Siann skin layer; anatomy and base-layer pixels stay intact."""
import argparse, hashlib, json
from pathlib import Path
import numpy as np
from PIL import Image

ap=argparse.ArgumentParser(description=__doc__)
ap.add_argument('--id',required=True)
ap.add_argument('--workspace',type=Path,required=True)
ap.add_argument('--player-root',type=Path,required=True)
ap.add_argument('--native-body',type=Path,help='Original native identity Body_Basecolor.png, palette reference only')
a=ap.parse_args()
r=json.loads((Path(__file__).resolve().parent/'npc_bodies'/(a.id+'.json')).read_text())
body='mh_veth_'+r['sex']
source=a.player_root/'rigged'/body/'textures'/(body+'_body_albedo.png')
rgba=np.array(Image.open(source).convert('RGBA'));rgb=rgba[:,:,:3].astype(np.float32)/255
mask=(rgb[:,:,0]>rgb[:,:,1]*1.10)&(rgb[:,:,0]>rgb[:,:,2]*1.15)
if r['people']=='people.siann':
    lum=rgb@np.array([.2126,.7152,.0722],np.float32)
    level=float(np.median(lum[mask]))
    colour=np.array([.80,.79,.86],np.float32)*(.66+.34*lum[:,:,None]/max(level,.05))+(rgb-lum[:,:,None])*.12
    operation='Siann opal skin palette'
else:
    if a.native_body is None:ap.error('--native-body is required for identity palette matching')
    native=np.array(Image.open(a.native_body).convert('RGB'),dtype=np.float32)/255
    keep=(native[:,:,0]>native[:,:,1]*1.10)&(native[:,:,0]>native[:,:,2]*1.15)
    gain=np.median(native[keep],axis=0)/np.maximum(np.median(rgb[mask],axis=0),.02)
    colour=rgb*gain
    operation='Native identity palette gain '+str(gain.tolist())
result=rgba.copy();result[mask,:3]=np.clip(colour[mask]*255,0,255).astype(np.uint8)
assert np.array_equal(result[~mask],rgba[~mask]) and np.array_equal(result[:,:,3],rgba[:,:,3])
for branch in ('approved_models','approved_wing_models'):
    folder=a.workspace/branch/('mh_'+a.id)
    if not folder.exists():continue
    Image.fromarray(result).save(folder/'body_skin_colour.png')
    (folder/'body_skin_colour.json').write_text(json.dumps({'source':str(source),'source_sha256':hashlib.sha256(source.read_bytes()).hexdigest(),'native_reference':str(a.native_body) if a.native_body else None,'operation':operation,'geometry_skin_weights_bind_matrices_normal_maps_unchanged':True,'alpha_and_non_skin_pixels_unchanged':True,'status':'surface candidate; visual review required'},indent=2)+'\n')
print('BODY_COLOUR_ONLY',a.id)
