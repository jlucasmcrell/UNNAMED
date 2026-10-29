"""Check the captured Gate C receipts; pixels still require personal visual review."""
import hashlib,json,math,re,sys
from pathlib import Path
from PIL import Image

root=Path(sys.argv[1]); errors=[]; rows=[]
if not (root/'complete.json').is_file():errors.append('Both-town completion marker missing')
for town in ['ashen_hollow','cairnwold']:
    folder=root/town
    manifest=json.loads((folder/'npc_looks.json').read_text())
    poses=json.loads((folder/'pose_samples.json').read_text())
    if manifest['missing'] or len(manifest['npcs'])!=10:errors.append(town+': incomplete roster')
    if not manifest.get('full_body') or manifest['hour']!=12:errors.append(town+': framing/hour mismatch')
    if manifest.get('fixture_ground_y') is None:errors.append(town+': authoritative staging height was not recorded')
    for row in manifest['npcs']:
        identifier=row['id']; samples=[p for p in poses if p['id']==identifier]
        if not row['outfit'].startswith('outfit.period.'):errors.append(identifier+': non-period outfit')
        expected='humgen_creator_female' if row['sex']=='f' else 'humgen_creator_male'
        if row['model']!=expected:errors.append(identifier+': wrong exact-target body: '+row['model'])
        if row['original_position']==row['review_position']:errors.append(identifier+': stage was overwritten')
        if manifest.get('fixture_ground_y') is not None:
            position=[float(x) for x in re.findall(r'-?\d+(?:\.\d+)?',row['review_position'])]
            if len(position)!=3 or not math.isclose(position[1],manifest['fixture_ground_y'],abs_tol=.002):errors.append(identifier+': incorrect fixture ground height')
        if {p['requested'] for p in samples}!={'walk','run','crouch_idle','sword_attack_1'}:errors.append(identifier+': motion coverage missing')
        if any(p['requested']!=p['actual'] for p in samples):errors.append(identifier+': wrong held motion')
        if len({json.dumps(p['bone_origins']) for p in samples})!=4:errors.append(identifier+': repeated bone pose')
        for i,a in enumerate(samples):
            for b in samples[i+1:]:
                movement=max(sum((x-y)**2 for x,y in zip(u,v))**.5 for u,v in zip(a['bone_origins'],b['bone_origins']))
                if movement<.02:errors.append(identifier+': body poses not meaningfully distinct: '+a['requested']+'/'+b['requested'])
        if any(not math.isfinite(v) for p in samples for bone in p['bone_origins'] for v in bone):errors.append(identifier+': nonfinite pose')
        materials=row.get('garment_materials',[])
        if not materials:errors.append(identifier+': material readback missing')
        for material in materials:
            name=material['mesh'].lower()
            amount=(.0115 if '_elvs_mens_apron1_' in name or 'cloak' in name else .0035)*(-1 if name.endswith('_lining') else 1)
            if not material['grow'] or not math.isclose(material['grow_amount'] or 0,amount,abs_tol=1e-6):errors.append(identifier+': material stand-off/layer was overwritten: '+name)
        files=[folder/(identifier+'.png')]+[folder/(identifier+'_motion_'+p['requested']+'.png') for p in samples]
        if row['people']=='people.kal':files += [folder/(identifier+'_'+side+'.png') for side in ['chest','back']]
        for file in files:
            if not file.is_file():errors.append(str(file)+': missing');continue
            if Image.open(file).size!=(1920,1080):errors.append(str(file)+': wrong resolution')
        rows.append(dict(town=town,id=identifier,people=row['people'],sex=row['sex'],outfit=row['outfit'],motion_samples=len(samples)))
receipt=dict(status='CAPTURE_READBACK_PASS_VISUAL_REVIEW_REQUIRED' if not errors else 'FAIL',rows=rows,errors=errors,
             files={str(file.relative_to(root)):hashlib.sha256(file.read_bytes()).hexdigest() for file in root.rglob('*.png')})
(root/'CAPTURE_READBACK.json').write_text(json.dumps(receipt,indent=2))
print(receipt['status'],len(rows),'actors;',len(receipt['files']),'original frames;',len(errors),'errors')
if errors:raise SystemExit(1)
