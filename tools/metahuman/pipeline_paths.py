"""Require a deliberate output directory; the frozen f87 library is always read-only."""
from pathlib import Path
import os

def pipeline_root():
    value=os.environ.get('OTHERREACH_PIPELINE_ROOT','').strip()
    if not value or not Path(value).is_absolute():
        raise ValueError('Set OTHERREACH_PIPELINE_ROOT to an absolute job directory')
    path=Path(value).resolve()
    frozen=Path('D:/Otherreach_Scratch/mh_playtest_20261019').resolve()
    if path==frozen or frozen in path.parents:
        raise ValueError('Frozen f87 data cannot be a job output root')
    return path
