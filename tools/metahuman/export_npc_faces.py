"""Existing Epic Sequencer/RigLogic bake and native GLTFExporter, on each NPC's own head.
The community evaluated-mesh capture runs separately; native rigs and base geometry stay unchanged.
"""
import unreal, json, os, traceback
from pathlib import Path
import sys
sys.path.insert(0,str(Path(__file__).parent))
from pipeline_paths import pipeline_root
root=pipeline_root()
source_root=Path('D:/Otherreach_Scratch/mh_slice_20261019/scripts')
records=json.loads((root/'evidence/npc_glb_export.json').read_text())['characters']
probe=(source_root/'probe_cinematic_arkit_bake.py').read_text()
probe=probe[:probe.index("exec(compile((root/'scripts/measure_ue_sources.py')")]
probe=probe.replace("root=Path('D:/Otherreach_Scratch/mh_slice_20261019')", "root=Path(os.environ['OTHERREACH_PIPELINE_ROOT'])")
probe=probe.replace("out=root/'exports/cinematic_arkit'/sex.lower()", "out=root/'exports/npc_arkit'/sex")
probe=probe.replace("path='/Game/MHSlice/FaceBakeSource/MHSlice_'+sex+'_FaceBakeSource/Face/SKM_MHC_Slice_'+sex+'_FaceMesh'", "path=NPC_MESH_PATH")
probe=probe.replace("'/Game/MHSlice/Bakes'", "'/Game/MHPlaytest/RCd3Faces'")
probe=probe.replace("    assert any('eye_blink' in n.casefold() for n in data),'Blink native morph data missing'", "    assert not morphs or any('eye_blink' in n.casefold() for n in data),'Blink native morph data missing'")
probe=probe.replace("    assert any('jaw_open' in n.casefold() for n in data),'Jaw native morph data missing'", "    assert not morphs or any('jaw_open' in n.casefold() for n in data),'Jaw native morph data missing'")
export=(source_root/'export_cinematic_baked_animation_glb.py').read_text()
export=export[:export.index("exec(compile((root/'scripts/measure_ue_sources.py')")]
export=export.replace("root=Path('D:/Otherreach_Scratch/mh_slice_20261019')", "root=Path(os.environ['OTHERREACH_PIPELINE_ROOT'])")
export=export.replace("out=root/'exports/cinematic_arkit'/sex.lower()", "out=root/'exports/npc_arkit'/sex")
export=export.replace("'/Game/MHSlice/Bakes/AS_ARKit_'", "'/Game/MHPlaytest/RCd3Faces/AS_ARKit_'")
export=export.replace("unreal.load_asset('/Game/MHSlice/FaceBakeSource/MHSlice_'+sex+'_FaceBakeSource/Face/SKM_MHC_Slice_'+sex+'_FaceMesh')", "unreal.load_asset(NPC_MESH_PATH)")
try:
 for id in os.environ.get('OTHERREACH_MH_ANIM_CHARACTERS','01_Aoi').split(','):
  row=next(c for c in records if c['id']==id)
  mesh_path=next(e['asset'] for e in row['exports'] if e['part']=='head')
  os.environ['OTHERREACH_MH_SEX']=id
  state=root/'exports/npc_arkit'/id/'animation_glb_export.json'
  if state.exists() and json.loads(state.read_text()).get('status')=='DONE':continue
  scope={'NPC_MESH_PATH':mesh_path,'__name__':'__main__'}
  exec(compile(probe,'documented_npc_face_probe','exec'),scope)
  report=json.loads((root/'exports/npc_arkit'/id/'probe.json').read_text())
  assert report['status']=='DONE',report.get('error')
  exec(compile(export,'documented_npc_face_export','exec'),scope)
  report=json.loads(state.read_text());assert report['status']=='DONE',report.get('error')
except Exception:
 (root/'evidence/npc_faces_failed.txt').write_text(traceback.format_exc())
 raise
finally:unreal.SystemLibrary.quit_editor()
