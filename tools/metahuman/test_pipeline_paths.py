"""Run directly with Python; never creates or edits a job directory."""
import os
from pathlib import Path
from pipeline_paths import pipeline_root

def test():
    original=os.environ.get('OTHERREACH_PIPELINE_ROOT')
    try:
        for value in ['', 'relative/job', 'D:/Otherreach_Scratch/mh_playtest_20261019', 'D:/Otherreach_Scratch/mh_playtest_20261019/runtime']:
            os.environ['OTHERREACH_PIPELINE_ROOT']=value
            try:pipeline_root()
            except ValueError:pass
            else:raise AssertionError('Unsafe root accepted: '+value)
        os.environ['OTHERREACH_PIPELINE_ROOT']='D:/Otherreach_Scratch/metahuman_future_job'
        assert pipeline_root()==Path('D:/Otherreach_Scratch/metahuman_future_job').resolve()
    finally:
        if original is None:os.environ.pop('OTHERREACH_PIPELINE_ROOT',None)
        else:os.environ['OTHERREACH_PIPELINE_ROOT']=original
    print('PIPELINE_ROOT_CHECK PASS')

if __name__=='__main__':test()
