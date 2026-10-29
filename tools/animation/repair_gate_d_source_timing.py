"""Match isolated native sampling metadata to the original source-key rate.

UE 5.8's IK batch counts target sampling keys but evaluates source frame indexes.
Migrated assets with differing rates therefore request frames past the source end.
Only target sampling metadata changes; raw keys, source rate and duration stay intact.
"""
import json
import os
from pathlib import Path
import traceback
import unreal

job = Path(os.environ['OTHERREACH_HUMGEN_ANIMATION_JOB'])
report_path = job / 'source_timing_repair.json'
prior = json.loads(report_path.read_text()).get('repaired', []) if report_path.exists() else []
report = dict(status='INSPECTING', checked=0, repaired=prior)
try:
    sources = json.loads((job / 'extra_sources.json').read_text())['sources']
    paths = {item['asset'] for source in sources for item in source['assets']
             if item['kind'] == 'animation' and not item['asset'].startswith('/Game/GateDSources/')}
    for path in sorted(paths):
        animation = unreal.load_asset(path)
        model = animation.get_editor_property('data_model_interface')
        source_rate = model.get_frame_rate()
        target = animation.get_editor_property('platform_target_frame_rate')
        target_rate = target.get_editor_property('default')
        report['checked'] += 1
        if source_rate.numerator * target_rate.denominator == target_rate.numerator * source_rate.denominator:
            continue
        keys, seconds = model.get_number_of_keys(), animation.sequence_length
        before = dict(numerator=target_rate.numerator, denominator=target_rate.denominator)
        # The reflected struct is a reference to the asset property. Editing
        # its Default member emits the native owner's change notification.
        target.set_editor_property('default', source_rate)
        repaired_rate = animation.get_editor_property('platform_target_frame_rate').get_editor_property('default')
        assert repaired_rate.numerator * source_rate.denominator == source_rate.numerator * repaired_rate.denominator, (
            repaired_rate.numerator, repaired_rate.denominator, source_rate.numerator, source_rate.denominator)
        assert model.get_number_of_keys() == keys and abs(animation.sequence_length - seconds) < 1e-6
        assert unreal.EditorAssetLibrary.save_loaded_asset(animation, False)
        report['repaired'].append(dict(asset=path, old_target=before,
            source_rate=dict(numerator=source_rate.numerator, denominator=source_rate.denominator), keys=keys, seconds=seconds))
        report_path.write_text(json.dumps(report, indent=2))
    affected = {row['asset'] for row in report['repaired']}
    report['invalidated'] = {}
    for actor in ('humgen_creator_female', 'humgen_creator_male'):
        receipt = job / ('retarget_' + actor + '.json')
        if not receipt.exists():
            continue
        data = json.loads(receipt.read_text())
        old = data['clips']
        data['clips'] = [clip for clip in old if clip.get('source_asset') not in affected]
        if len(data['clips']) != len(old):
            receipt.with_suffix('.before_timing_repair.json').write_text(json.dumps(data | {'clips': old}, indent=2))
            data.update(status='RESUME_REQUIRED', source_sampling_metadata_repair=report['repaired'])
            receipt.write_text(json.dumps(data, indent=2))
            report['invalidated'][actor] = len(old) - len(data['clips'])
    report['status'] = 'DONE'
except Exception:
    report.update(status='FAILED', error=traceback.format_exc())
report_path.write_text(json.dumps(report, indent=2))
unreal.SystemLibrary.quit_editor()
