"""Read-only diagnostic for the LOD-sensitive vertex-band measurement; no asset writes."""
from pathlib import Path as _PipelinePath
import sys as _pipeline_sys
_pipeline_sys.path.insert(0, str(_PipelinePath(__file__).parent))
from pipeline_paths import pipeline_root as _pipeline_root
import unreal,json,math,traceback,runpy
from pathlib import Path
root=Path((_pipeline_root().as_posix() + ''));out=root/'evidence/etta_lod_diagnostic.json'
if not out.exists():
 result={'status':'STARTING','assets':[]}
 try:
  def read(asset,lod_index):
   mesh=unreal.load_asset(asset);assert mesh
   lod=unreal.GeometryScriptMeshReadLOD();lod.lod_type=unreal.GeometryScriptLODType.SOURCE_MODEL;lod.lod_index=lod_index
   mesh,outcome=unreal.GeometryScript_AssetUtils.copy_mesh_from_skeletal_mesh(mesh,unreal.DynamicMesh(),unreal.GeometryScriptCopyMeshFromAssetOptions(),lod)
   assert outcome==unreal.GeometryScriptOutcomePins.SUCCESS
   _,v,gaps=unreal.GeometryScript_MeshQueries.get_all_vertex_positions(mesh,False);assert not gaps
   points=[(p.x*.01,p.z*.01,p.y*.01) for p in unreal.GeometryScript_List.convert_vector_list_to_array(v)]
   _,t,gaps=unreal.GeometryScript_MeshQueries.get_all_triangle_indices(mesh,False);assert not gaps
   triangles=[(t.x,t.y,t.z) for t in unreal.GeometryScript_List.convert_triangle_list_to_array(t)]
   return points,triangles
  def waist(points,triangles,y):
   graph={};coords={}
   for tri in triangles:
    line=[]
    for j in range(3):
     a=points[tri[j]];b=points[tri[(j+1)%3]]
     if a[1]<=y<b[1] or b[1]<=y<a[1]:
      t=(y-a[1])/(b[1]-a[1]);p=(a[0]+t*(b[0]-a[0]),a[2]+t*(b[2]-a[2]));line.append(p)
    if len(line)!=2:continue
    keys=[tuple(round(v*100000) for v in p) for p in line]
    for j,k in enumerate(keys):coords[k]=line[j];graph.setdefault(k,set()).add(keys[1-j])
   seen=set();loops=[]
   for first in graph:
    if first in seen:continue
    stack=[first];component=[]
    while stack:
     k=stack.pop()
     if k in seen:continue
     seen.add(k);component.append(coords[k]);stack.extend(graph[k]-seen)
    loops.append(component)
   body=min(loops,key=lambda c:abs(sum(p[0] for p in c)/len(c)))
   # Convex section perimeter, matching the gate's circumference convention.
   def cross(o,a,b):return (a[0]-o[0])*(b[1]-o[1])-(a[1]-o[1])*(b[0]-o[0])
   p=sorted(set(body));a=[];b=[]
   for q in p:
    while len(a)>=2 and cross(a[-2],a[-1],q)<=0:a.pop()
    a.append(q)
   for q in reversed(p):
    while len(b)>=2 and cross(b[-2],b[-1],q)<=0:b.pop()
    b.append(q)
   h=a[:-1]+b[:-1]
   return dict(circumference_m=sum(math.dist(h[i],h[(i+1)%len(h)]) for i in range(len(h))),components=[len(c) for c in loops],torso_samples=len(body))
  base='/Game/MHPlaytest/NPC/05_Etta/Source/MHC_NPC_05_Etta_'
  full,tri=read(base+'FullBody',0);low=min(p[1] for p in full);height=max(p[1] for p in full)-low;y=low+height*.62
  result['assets'].append(dict(asset=base+'FullBody',lod=0,waist=waist(full,tri,y)))
  for lod in range(4):
   p,t=read(base+'Body',lod)
   result['assets'].append(dict(asset=base+'Body',lod=lod,waist=waist(p,t,y),bounds=[[min(v[k] for v in p) for k in range(3)],[max(v[k] for v in p) for k in range(3)]]))
  result['status']='DONE'
 except Exception:result.update(status='FAILED',error=traceback.format_exc())
 out.write_text(json.dumps(result,indent=2))
