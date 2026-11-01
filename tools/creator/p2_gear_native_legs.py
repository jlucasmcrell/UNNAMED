"""Fit real source leg armour inside native boots; preserve explicit source masks.

CPU Blender: -- <gear work> <creator work> <female|male> A04|A08|A14|A18.
"""
import bpy,bmesh,json,sys
from pathlib import Path
from mathutils.bvhtree import BVHTree
from HumGen3D.human.human import Human
sys.path.insert(0,str(Path(__file__).resolve().parent))
from p2_gear_leg_fit import fit_leg_geometry
from p2_gear_native_export import export_candidate
from p2_gear_native_layers import default_office
args=sys.argv[sys.argv.index('--')+1:];work,creator=map(Path,args[:2]);sex,review=args[2:4]
assert sex in ('female','male') and review in ('A04','A08','A14','A18')
row=next(r for r in json.loads((work/'selected_manifest_armour_native_v1.json').read_text()) if r['review_id']==review)
row={**row,'native_candidate_root':'armour_native_legs_v3'}
bpy.ops.wm.open_mainfile(filepath=str(creator/sex/'native_unchanged.blend'))
human=Human.from_existing(bpy.data.objects['humgen_creator_'+sex])
human.clothing.outfit.remove();human.clothing.footwear.remove()
obj,audit,adjusted=fit_leg_geometry(human,row,work,sex)
solver=human.clothing.outfit.add_obj(obj,'pants',context=bpy.context)
export_candidate(human,row,work,creator,sex,Path(row['source']),solver,adjusted,audit)
