import importlib.util
import json
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).parents[2] / 'tools'))
spec = importlib.util.spec_from_file_location('art3', Path(__file__).parents[2] / 'tools/install_barrowworks_production.py')
art3 = importlib.util.module_from_spec(spec)
spec.loader.exec_module(art3)


class ProductionPlanTests(unittest.TestCase):
    def tiny_plan(self, root):
        lods = []
        for level in range(3):
            source = root / f'source_{level}.glb'
            source.write_bytes(b'tiny fixture ' + bytes([level]))
            lods.append({'level': level, 'file': str(source), 'sha256': art3.digest(source)})
        return {'assets': [{'asset_id': 'bw_codex_art3_floor', 'source_key': 'production_floor', 'lods': lods}]}

    def test_copy_preserves_every_lod_and_can_resume_only_its_own_plan(self):
        with tempfile.TemporaryDirectory() as folder, patch.object(art3, 'owner_check'):
            root = Path(folder)
            document = self.tiny_plan(root)
            destination = root / 'new-owned-depot'
            art3.install(document, destination)
            art3.install(document, destination)
            for lod in document['assets'][0]['lods']:
                suffix = '' if lod['level'] == 0 else f"_lod{lod['level']}"
                copied = destination / 'ready/bw_codex_art3_floor' / ('bw_codex_art3_floor' + suffix + '.glb')
                self.assertEqual(Path(lod['file']).read_bytes(), copied.read_bytes())
            with self.assertRaisesRegex(ValueError, 'not owned by this exact'):
                art3.install({'assets': []}, destination)

    def test_changed_source_is_refused_before_any_destination_is_created(self):
        with tempfile.TemporaryDirectory() as folder, patch.object(art3, 'owner_check'):
            root = Path(folder)
            document = self.tiny_plan(root)
            Path(document['assets'][0]['lods'][1]['file']).write_bytes(b'changed after approval')
            destination = root / 'not-created'
            with self.assertRaisesRegex(ValueError, 'Source changed since the plan'):
                art3.install(document, destination)
            self.assertFalse(destination.exists())

    def test_unapproved_room_composites_are_refused(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            (root / 'manifest.json').write_text('[]')
            (root / 'accepted.json').write_text('{"assets": []}')
            config = {'art_root': str(root), 'dressing_acceptance': 'accepted.json',
                      'core': {'production_dressing_r01': 'bw_bad'}, 'architecture': []}
            with self.assertRaisesRegex(ValueError, 'Unaccepted room composites'):
                art3.plan(config)

    def test_all_supplied_lods_are_required(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            (root / 'manifest.json').write_text(json.dumps([{'key': 'production_floor', 'lods': [{'level': 0}]}]))
            (root / 'accepted.json').write_text('{"assets": []}')
            config = {'art_root': str(root), 'dressing_acceptance': 'accepted.json',
                      'core': {'production_floor': 'bw_floor'}, 'architecture': []}
            with self.assertRaisesRegex(ValueError, 'all three supplied LODs'):
                art3.plan(config)

    def test_present_owner_flag_prevents_even_destination_creation(self):
        with tempfile.TemporaryDirectory() as folder:
            flag = Path(folder) / 'owner.flag'
            flag.touch()
            destination = Path(folder) / 'not-created'
            with patch.object(art3, 'OWNER_FLAG', flag), self.assertRaisesRegex(RuntimeError, 'OWNER_PLAYING'):
                art3.install({'assets': []}, destination)
            self.assertFalse(destination.exists())

    def test_frozen_rc_is_refused_before_source_reads(self):
        with patch.object(art3, 'owner_check'), self.assertRaisesRegex(ValueError, 'RC d is frozen'):
            art3.install({'assets': []}, art3.FROZEN_RC / 'never-write')


if __name__ == '__main__':
    unittest.main()
