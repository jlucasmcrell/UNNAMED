import sys
import numpy as np
from kal_stone_bake import read_glb
g, acc, img = read_glb(sys.argv[1])
for m in g['meshes']:
    if m['name'].endswith(('HairCards_LOD0', 'Body_LOD0')):
        p = acc(m['primitives'][0]['attributes']['POSITION'])
        print(m['name'], 'y', round(p[:, 1].min(), 3), round(p[:, 1].max(), 3), 'x', round(p[:, 0].min(), 3), round(p[:, 0].max(), 3), 'z', round(p[:, 2].min(), 3), round(p[:, 2].max(), 3))
for i, im in enumerate(g.get('images', [])):
    if 'Hair' in im.get('name', ''):
        a = np.asarray(img(i).convert('RGBA')).astype(float) / 255
        on = a[..., 3] > .5
        print(im['name'], a.shape, a[..., :3][on].mean(0).round(3) if on.any() else a[..., :3].mean((0, 1)).round(3))
