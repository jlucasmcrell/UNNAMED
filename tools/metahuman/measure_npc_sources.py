"""Read-only Epic Geometry Script measurement, identical to the accepted pair's G2 baseline."""
from pathlib import Path as _PipelinePath
import sys as _pipeline_sys
_pipeline_sys.path.insert(0, str(_PipelinePath(__file__).parent))
from pipeline_paths import pipeline_root as _pipeline_root
import unreal,json,traceback,math
from pathlib import Path
root=Path((_pipeline_root().as_posix() + ''))
def hull(points):
    p=sorted(set(points))
    def cross(o,a,b):return (a[0]-o[0])*(b[1]-o[1])-(a[1]-o[1])*(b[0]-o[0])
    a=[];b=[]
    for v in p:
        while len(a)>=2 and cross(a[-2],a[-1],v)<=0:a.pop()
        a.append(v)
    for v in reversed(p):
        while len(b)>=2 and cross(b[-2],b[-1],v)<=0:b.pop()
        b.append(v)
    return a[:-1]+b[:-1]
def measure(points):
    low=min(p[1] for p in points);high=max(p[1] for p in points);height=high-low
    result={'height':height}
    for name,f in [('shoulders',.82),('chest',.72),('waist',.62),('hips',.54)]:
        central_width=.12 if name=='chest' else .18
        section=[(x,z) for x,y,z in points if abs(y-(low+height*f))<=height*.005 and abs(x)<=height*central_width]
        assert len(section)>10,(name,len(section))
        if name=='shoulders':result[name]=max(x for x,z in section)-min(x for x,z in section)
        else:
            polygon=hull(section)
            result[name]=sum(math.dist(polygon[i],polygon[(i+1)%len(polygon)]) for i in range(len(polygon)))
    return result

path=root/'evidence/ue_npc_measurements.json'
state=json.loads(path.read_text()) if path.exists() else {'characters':[]}
try:
 sources=json.loads((root/'evidence/npc_cloud_export.json').read_text())['characters']
 for row in sources:
  if any(x['id']==row['id'] for x in state['characters']):continue
  asset=next(p for p in row['geometry'] if '_FullBody.' in p)
  mesh=unreal.load_asset(asset);assert mesh
  dynamic=unreal.DynamicMesh()
  lod=unreal.GeometryScriptMeshReadLOD();lod.lod_type=unreal.GeometryScriptLODType.SOURCE_MODEL;lod.lod_index=0
  dynamic,outcome=unreal.GeometryScript_AssetUtils.copy_mesh_from_skeletal_mesh(mesh,dynamic,unreal.GeometryScriptCopyMeshFromAssetOptions(),lod)
  assert outcome==unreal.GeometryScriptOutcomePins.SUCCESS,outcome
  _,positions,gaps=unreal.GeometryScript_MeshQueries.get_all_vertex_positions(dynamic,True)
  points=unreal.GeometryScript_List.convert_vector_list_to_array(positions)
  points=[(p.x*.01,p.z*.01,p.y*.01) for p in points]
  full_measurements=measure(points)
  exported=next(x for x in json.loads((root/'evidence/npc_glb_export.json').read_text())['characters'] if x['id']==row['id'])
  selected_points=[];selected=[]
  for part in exported['exports']:
   if part['part'] not in ['body','head']:continue
   lod.lod_index=part['native_lod'];part_mesh=unreal.load_asset(part['asset'])
   dynamic,outcome=unreal.GeometryScript_AssetUtils.copy_mesh_from_skeletal_mesh(part_mesh,unreal.DynamicMesh(),unreal.GeometryScriptCopyMeshFromAssetOptions(),lod)
   assert outcome==unreal.GeometryScriptOutcomePins.SUCCESS,outcome
   _,positions,gaps=unreal.GeometryScript_MeshQueries.get_all_vertex_positions(dynamic,True)
   selected_points.extend((p.x*.01,p.z*.01,p.y*.01) for p in unreal.GeometryScript_List.convert_vector_list_to_array(positions))
   selected.append(dict(part=part['part'],asset=part['asset'],native_lod=part['native_lod']))
  state['characters'].append(dict(id=row['id'],asset=asset,vertex_count=len(points),fullbody_measurements_m=full_measurements,selected_lod_measurements_m=measure(selected_points),selected_native_assets=selected,native_source_model_lod=0))
  path.write_text(json.dumps(state,indent=2))
 state['status']='DONE'
except Exception:state.update(status='FAILED',error=traceback.format_exc())
path.write_text(json.dumps(state,indent=2))
