"""Rebuild masks from completed native role snapshots, without re-exporting art."""
import ast,json,sys
from pathlib import Path
import bpy
from HumGen3D.human.human import Human
source=Path(__file__).with_name('export_humgen_creator_parts.py')
definition=next(node for node in ast.parse(source.read_text()).body if isinstance(node,ast.FunctionDef) and node.name=='covered_body')
exec(compile(ast.Module(body=[definition],type_ignores=[]),str(source),'exec'))
for argument in sys.argv[sys.argv.index('--')+1:]:
    work=Path(argument)
    for folder in sorted((work/'game/primary/rigged').glob('*_outfit_outfit_period_*')):
        contract=folder/'creator_part.json'
        if not contract.is_file():continue
        scene=work/'parts_sources'/folder.name/'native.blend'
        bpy.ops.wm.open_mainfile(filepath=str(scene))
        candidates=[]
        for obj in list(bpy.context.scene.objects):
            if obj.type!='ARMATURE':continue
            human=Human.from_existing(obj,strict_check=False)
            if human and '_LOD0' in human.objects.body.name:candidates.append(human)
        assert len(candidates)==1,[(h.name,h.objects.body.name)for h in candidates]
        human=candidates[0]
        result=covered_body(human.objects.body,list(human.clothing.outfit.objects))
        assert result and result['covered']>0,folder.name
        data=json.loads(contract.read_text())
        (folder/'body_coverage.json').write_text(json.dumps(dict(target_body_sha256=data['target_body_sha256'],**result)))
        print('ROLE_COVERAGE_REBAKED',folder.name,result['covered'],flush=True)
