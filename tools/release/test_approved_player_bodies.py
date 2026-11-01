import hashlib,tempfile,unittest
from pathlib import Path
from approved_player_bodies import check_native
class NativeDeliveryTests(unittest.TestCase):
 def test_accepted_bytes_then_same_size_corruption_is_refused(self):
  with tempfile.TemporaryDirectory() as folder:
   base=Path(folder);p=base/'native.res';p.write_bytes(b'approved')
   records={'native.res':{'bytes':8,'sha256':hashlib.sha256(b'approved').hexdigest()}}
   check_native(base,records);p.write_bytes(b'changed!')
   with self.assertRaisesRegex(AssertionError,'Unapproved character bytes'):check_native(base,records)
 def test_missing_motion_is_refused(self):
  with tempfile.TemporaryDirectory() as folder:
   with self.assertRaisesRegex(AssertionError,'Missing accepted character file'):check_native(Path(folder),{'motion.res':{'bytes':1,'sha256':'0'*64}})
 def test_empty_or_external_manifest_is_refused(self):
  with tempfile.TemporaryDirectory() as folder:
   with self.assertRaises(AssertionError):check_native(Path(folder),{})
   with self.assertRaisesRegex(AssertionError,'External character file'):check_native(Path(folder),{'../foreign.res':{'bytes':1,'sha256':'0'*64}})
if __name__=='__main__':unittest.main()
