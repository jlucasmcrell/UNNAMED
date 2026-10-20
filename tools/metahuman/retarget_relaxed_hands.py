"""Compatibility entry point: all retargets use the corrected shared implementation."""
import runpy
from pathlib import Path
runpy.run_path(str(Path(__file__).with_name("retarget_native_animations.py")),run_name="__main__")
