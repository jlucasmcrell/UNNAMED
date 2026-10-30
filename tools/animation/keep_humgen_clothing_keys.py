"""Keep creator body controls on fitted garments with HumGen's native fit field."""
import numpy as np
from HumGen3D.human.clothing import garment_fit as native
STAGE='before_meshes'

def coords(data):
    result=np.empty(len(data)*3,dtype=np.float64)
    data.foreach_get('co',result)
    return result.reshape((-1,3))

def main(context,human):
    body=human.objects.body
    source=[key for key in body.data.shape_keys.key_blocks if key.name.startswith(('b_','s_'))]
    if not source:return
    matrix=native._rig_matrix(body,human.objects.rig)
    base=native._shape(native._shaped_co(human),native._body(human))
    deltas=[(key,(coords(key.data)-coords(body.data.shape_keys.reference_key.data))@matrix[:3,:3].T) for key in source]
    for obj in list(human.clothing.outfit.objects) + list(human.clothing.footwear.objects):
        transform=native._rig_matrix(obj,human.objects.rig)
        original=coords(obj.data.shape_keys.reference_key.data)
        at=native._transformed(original,transform)
        garment=native._garment(at,native._triangles(obj),base)
        changes=[]
        for key,delta in deltas:
            covariance=native.vertex_covariance(base.co,base.co+delta,native._body(human).edges)
            fitted=native.fit_shape(at,garment.binding,base.co,base.co+delta,covariance)
            change=(fitted-at)@np.linalg.inv(transform[:3,:3]).T
            if np.max(np.abs(change),initial=0)>.00001:changes.append((key,change))
        correction=sum((change*key.value for key,change in changes),np.zeros_like(original))
        for existing in obj.data.shape_keys.key_blocks:
            existing.data.foreach_set('co',(coords(existing.data)-correction).ravel())
        basis=original-correction
        for source,change in changes:
            key=obj.data.shape_keys.key_blocks.get(source.name) or obj.shape_key_add(name=source.name,from_mix=False)
            key.data.foreach_set('co',(basis+change).ravel())
            key.value=source.value;key.slider_min=source.slider_min;key.slider_max=source.slider_max
        print('ROLE_NATIVE_BODY_KEYS',obj.name,len(changes),flush=True)
