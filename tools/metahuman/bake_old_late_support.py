# Blender's documented glTF importer + BVHTree ray_cast; reads only source geometry.
import bpy,json,sys,math,hashlib
from mathutils import Vector
from mathutils.bvhtree import BVHTree
from pathlib import Path
j=json.loads(Path(sys.argv[sys.argv.index('--')+1]).read_text())
assert hashlib.sha256(Path(j['asset']).read_bytes()).hexdigest()==j['source_sha256'], 'Source geometry changed'
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=j['asset'])
verts=[];tris=[]
for obj in bpy.context.scene.objects:
 if obj.type!='MESH':continue
 obj.data.calc_loop_triangles();offset=len(verts)
 verts.extend(obj.matrix_world@v.co for v in obj.data.vertices)
 tris.extend(tuple(offset+i for i in t.vertices) for t in obj.data.loop_triangles)
lo=Vector(tuple(min(v[k] for v in verts) for k in range(3)))
hi=Vector(tuple(max(v[k] for v in verts) for k in range(3)))
assert 45<hi.z-lo.z<70,('Unexpected landmark scale',lo,hi)
centre=(lo+hi)*.5
rootverts=[v for v in verts if v.z<lo.z+12]
step=j['spacing'];cx,cz=j['anchor'];t=j['terrain'];ox,oz=t['origin_m'];ts=t['spacing_m']
def floor(x,z):
 fx=max(0,min(t['columns']-1,(x-ox)/ts));fz=max(0,min(t['rows']-1,(z-oz)/ts))
 i=min(int(fx),t['columns']-2);k=min(int(fz),t['rows']-2);u=fx-i;v=fz-k;h=t['heights_m']
 a,b,c,d=h[k][i],h[k][i+1],h[k+1][i],h[k+1][i+1]
 return a+((b-a)*u+(d-b)*v if u>=v else (d-c)*u+(c-a)*v)
x0=cx+math.floor((min(v.x for v in rootverts)-centre.x)/step)*step-step
z0=cz+math.floor((centre.y-max(v.y for v in rootverts))/step)*step-step
x1=cx+math.ceil((max(v.x for v in rootverts)-centre.x)/step)*step+step
z1=cz+math.ceil((centre.y-min(v.y for v in rootverts))/step)*step+step
cols=round((x1-x0)/step)+1;rows=round((z1-z0)/step)+1
assert cols*rows<200000
# Exclude the crown; cast from above every retained root face, never from inside a buttress.
rootfaces=[tri for tri in tris if max(verts[i].z for i in tri)<=lo.z+12]
bvh=BVHTree.FromPolygons(verts,rootfaces,all_triangles=True)
base=floor(cx,cz)-j['sink'];heights=[];raised=0;maxrise=0
for row in range(rows):
 z=z0+row*step;line=[]
 for col in range(cols):
  x=x0+col*step;ground=floor(x,z)
  origin=Vector((x-cx+centre.x,centre.y-(z-cz),lo.z+13))
  hit,normal,_,_=bvh.ray_cast(origin,Vector((0,0,-1)),13.1)
  top=base+hit.z-lo.z if hit is not None and normal.z>.05 else ground
  y=max(ground,top)
  raised+=y>ground+.05;maxrise=max(maxrise,y-ground)
  line.append(round(y,3))
 heights.append(line)
assert raised>100 and maxrise>1
out=dict(id='old_late_roots',origin_m=[x0,z0],spacing_m=step,columns=cols,rows=rows,heights_m=heights)
Path(j['out']).write_text(json.dumps(out))
Path(j['out']+'.provenance.json').write_text(json.dumps(dict(source=j['asset'],source_sha256=j['source_sha256'],sink_m=j['sink'],base_y=base,bounds=[list(lo),list(hi)],raised_samples=raised,max_rise_m=maxrise,vertices=len(verts),triangles=len(tris),method='Blender glTF import and downward BVHTree rays; no source mesh edits'),indent=2))
print('OLD_LATE_SUPPORT',cols,rows,'raised',raised,'max_rise',maxrise)
