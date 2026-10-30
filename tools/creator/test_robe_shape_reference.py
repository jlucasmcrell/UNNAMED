import json, struct, tempfile, unittest
from pathlib import Path
import numpy as np
from repair_robe_shape_reference import Glb, main, values

class ReferenceTests(unittest.TestCase):
    def test_rest_fit_is_removed_once_from_controls_but_not_correctives(self):
        common=np.array([[.03,0,0],[0,.02,0]],dtype='<f4')
        desired=np.array([[0,.01,0],[0,0,.02]],dtype='<f4')
        names=['s_{eyes}_'+str(i) for i in range(4)]+['b_shape','cor_ElbowBend_Lt']
        arrays=[common]*4+[common+desired,desired]
        binary=b''.join(a.tobytes() for a in arrays)
        doc=dict(nodes=[dict(name='Hips')],skins=[dict(joints=[0])],buffers=[dict(byteLength=len(binary))],
                 bufferViews=[dict(buffer=0,byteOffset=24*i,byteLength=24) for i in range(6)],
                 accessors=[dict(bufferView=i,componentType=5126,count=2,type='VEC3') for i in range(6)],
                 meshes=[dict(name='Robe_LOD0',extras=dict(targetNames=names),primitives=[dict(targets=[dict(POSITION=i) for i in range(6)])])])
        encoded=json.dumps(doc).encode();encoded+=b' '*(-len(encoded)%4)
        data=struct.pack('<III',0x46546c67,2,28+len(encoded)+len(binary))+struct.pack('<II',len(encoded),0x4e4f534a)+encoded+struct.pack('<II',len(binary),0x004e4942)+binary
        with tempfile.TemporaryDirectory() as folder:
            path=Path(folder)/'robe.glb';path.write_bytes(data)
            for _ in range(2):
                main(path);g=Glb(path);targets=g.doc['meshes'][0]['primitives'][0]['targets']
                for t in targets[:4]:np.testing.assert_array_equal(values(g,t['POSITION']),np.zeros((2,3)))
                np.testing.assert_allclose(values(g,targets[4]['POSITION']),desired,atol=1e-8)
                np.testing.assert_array_equal(values(g,targets[5]['POSITION']),desired)
                self.assertEqual(g.binary[:len(binary)],binary)

if __name__=='__main__':unittest.main()
