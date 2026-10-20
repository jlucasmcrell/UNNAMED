"""Read-only native mesh/garment cost and skin audit; never rewrites assets."""
from pathlib import Path
import json,struct,sys
sys.path.insert(0,'C:/Otherreach_Build/playtest_mh_characters_wt/tools/mh_playtest')
from audit_glb import audit
root=Path('D:/Otherreach_Scratch/mh_playtest_20261019');runtime=root/'runtime'
catalog=json.loads((runtime/'catalog.json').read_text());rows=[]
for id,c in catalog['characters'].items():
 parts={part:runtime/c[part] for part in ['body','head']}
 parts.update({p:runtime/'wardrobe/shell'/(id+'_'+p+'.glb') for p in ['boots','gloves']})
 record=dict(id=id,sex=c['sex'],preset=c.get('preset'),age_intent=c.get('age_intent'),parts={})
 for part,path in parts.items():
  result=audit(path)
  assert result['invalid_weight_sums']==0 and result['zero_count_accessors']==0,(id,part,result)
  assert result['skin_joint_counts']==[875 if part=='head' else 342],(id,part,result['skin_joint_counts'])
  raw=path.read_bytes();g=json.loads(raw[20:20+struct.unpack_from('<I',raw,12)[0]])
  result['normals_and_tangents']=all('NORMAL' in p['attributes'] and 'TANGENT' in p['attributes'] for m in g['meshes'] for p in m['primitives'])
  assert result['normals_and_tangents'],(id,part,'Required authored normals/tangents missing')
  record['parts'][part]=result
 rows.append(record)
assert not list(runtime.rglob('*derived*.glb')),'Derived body may never ship'
(root/'evidence/native_roster_mesh_audit.json').write_text(json.dumps(dict(status='PASS',characters=rows),indent=2))
print('NATIVE_ROSTER_MESH_AUDIT_PASS',len(rows))
for row in rows:print(row['id'],'body/head tris',[row['parts'][x]['triangles'] for x in ['body','head']])
