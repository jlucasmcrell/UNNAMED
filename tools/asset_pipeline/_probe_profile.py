import sys, os, json
sys.argv = ['blender','--','--input','W:\\UNNAMED\\assets\\ready\\weaponcomp_mace_head_flanged_a\\weaponcomp_mace_head_flanged_a.glb','--sockets','W:\\UNNAMED\\assets\\sockets\\weaponcomp_mace_head_flanged_a.json','--socket','SOCK_head','--out','W:\\UNNAMED\\assets\\review\\stubcut\\probe.glb']
sys.path.insert(0, r'W:\UNNAMED\tools\asset_pipeline')
import importlib.util
spec = importlib.util.spec_from_file_location('cut', r'W:\UNNAMED\tools\asset_pipeline\_blender_cut_stub.py')
cut = importlib.util.module_from_spec(spec); spec.loader.exec_module(cut)
args = cut.parse_args()
with open(args.sockets, encoding='utf-8') as h: d = json.load(h)
cut.reset_scene()
obj = cut.join_meshes(cut.import_glb(args.input))
spec_s = d['sockets'][args.socket]
axis = cut.export_frame_to_blender(spec_s['primary']).normalized()
origin = cut.export_frame_to_blender(spec_s['position'])
profile, low, high = cut.measure_profile(obj, axis, origin)
print('PROFILE low=%.4f high=%.4f slices=%d' % (low, high, len(profile)))
print('envelope radius = %.4f' % (float(spec_s['envelope'])/2))
for i,(pos,r) in enumerate(profile):
    if i % 6 == 0:
        print('  %6.4f  r=%.4f  %s' % (pos, r, '#'*int(r*400)))
