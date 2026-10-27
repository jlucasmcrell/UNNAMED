"""Keep gameplay work landings and encounter tells clear of the accepted reed scatter.
Preserve every other instance/LOD/tint; apply to a private candidate, never a running package.
"""
from pathlib import Path
import json,sys,math
root=Path(sys.argv[1]);p=root/'sedge_reeds.json';d=json.loads(p.read_text());before=len(d['instances'])
rects=[(4984.5,1075,4997.3,1085),(5002.7,1084,5015.5,1094)]
clearings=[(4777,1268,18),(4865,730,20),(5135,1265,18),(4798,882,2.5)]
def keep(e):
 x,_,z=e['at']
 if any(a<x<c and b<z<q for a,b,c,q in rects):return False
 return not any(math.hypot(x-cx,z-cz)<r*(1+.12*math.sin(x*.19+z*.11)+.08*math.sin(x*.31-z*.27)) for cx,cz,r in clearings)
d['instances']=[e for e in d['instances'] if keep(e)];p.write_text(json.dumps(d,separators=(',',':')))
(root/'sedge_gate4_clearings.json').write_text(json.dumps(dict(before=before,after=len(d['instances']),removed=before-len(d['instances']),work_landings=rects,encounter_banks=clearings,note='Authored gameplay clearings, not a capture-only visibility override.'),indent=2));print(before,len(d['instances']),before-len(d['instances']))
