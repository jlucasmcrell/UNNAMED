import importlib.util
from pathlib import Path
import sys
import tempfile
import threading
import unittest

spec = importlib.util.spec_from_file_location('owner_safe', Path(__file__).parents[2] / 'tools/run_owner_safe.py')
launcher = importlib.util.module_from_spec(spec)
spec.loader.exec_module(launcher)


class OwnerPlayingTests(unittest.TestCase):
    def test_present_flag_does_not_launch_the_command(self):
        with tempfile.TemporaryDirectory() as folder:
            flag = Path(folder) / 'owner.flag'
            flag.touch()
            marker = Path(folder) / 'launched'
            code = 'from pathlib import Path; Path(' + repr(str(marker)) + ').touch()'
            self.assertEqual(75, launcher.run([sys.executable, '-c', code], owner_flag=flag))
            self.assertFalse(marker.exists())

    def test_flag_appearing_stops_only_the_sleeping_test_child(self):
        with tempfile.TemporaryDirectory() as folder:
            flag = Path(folder) / 'owner.flag'
            timer = threading.Timer(.15, flag.touch)
            timer.start()
            try:
                self.assertEqual(75, launcher.run([sys.executable, '-c', 'import time; time.sleep(30)'], owner_flag=flag))
            finally:
                timer.join()

    def test_unflagged_exit_code_is_preserved(self):
        with tempfile.TemporaryDirectory() as folder:
            self.assertEqual(7, launcher.run([sys.executable, '-c', 'raise SystemExit(7)'], owner_flag=Path(folder) / 'absent'))


if __name__ == '__main__':
    unittest.main()
