"""Small CPU-only Blender controls; synthetic triangles are not asset evidence."""
import sys
from pathlib import Path
import numpy as np
sys.path.insert(0,str(Path(__file__).resolve().parent))
from fit_native_creator_part import lining_offsets

points=np.array([[0,0,0],[1,0,0],[0,1,0],[0,0,0]],dtype=np.float32)
delta=np.array([[.01,.02,.03],[.04,.02,.03],[.01,.08,.03],[.01,.02,.03]],dtype=np.float32)
faces=[[0,3,3],[0,1,2]]
before=points.copy()
actual=lining_offsets(points,faces,delta,np.array([[.25,.25,.003],[0,0,.003]],dtype=np.float32))
assert np.allclose(actual,[[.0175,.035,.03],[.01,.02,.03]],atol=1e-7),actual
assert np.array_equal(points,before)
for case in ('collapsed','distant','nonfinite'):
    bad_delta=delta.copy()
    if case=='nonfinite':bad_delta[0]=np.nan
    try:
        lining_offsets(points,[[0,3,3]] if case=='collapsed' else faces,bad_delta,
            np.array([[.25,.25,.01 if case=='distant' else .003]],dtype=np.float32))
    except AssertionError:pass
    else:raise AssertionError('Unsafe native lining input accepted: '+case)
print('NATIVE_LINING_TRANSFER_CONTROLS_PASS: degenerate lookup excluded without dropping faces; exact interpolation; collapsed/distant/nonfinite rejected')
