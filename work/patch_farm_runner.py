"""Replace the runner's WaitForExit-based block with the polled wait from jobprocess.ps1, and write a TIMEOUT marker."""
from pathlib import Path

PATH = Path(r'C:\Otherreach_Farm\runner\runner.ps1')
text = PATH.read_text(encoding='utf-8')

DOT_SOURCE_ANCHOR = "$self = $MyInvocation.MyCommand.Path"
DOT_SOURCE = (". (Join-Path $PSScriptRoot 'jobprocess.ps1')    # start, wait and stop a job process without ever waiting on a redirected handle\n"
              "$self = $MyInvocation.MyCommand.Path")

START_OLD = """        $start = @{ FilePath = $exe; WorkingDirectory = (Split-Path $exe); PassThru = $true
                    RedirectStandardOutput = (Join-Path $jobOut 'stdout.log'); RedirectStandardError = (Join-Path $jobOut 'stderr.log') }
        if ($gameArgs.Count) { $start.ArgumentList = $gameArgs }    # Start-Process refuses an empty argument list"""
START_NEW = """        $outLog = Join-Path $jobOut 'stdout.log'
        $errLog = Join-Path $jobOut 'stderr.log'
        $timeoutMarker = Join-Path $jobOut 'TIMEOUT'"""

CALL_OLD = '            $p = Start-Process @start'
CALL_NEW = ('            $p = Start-JobProcess -Exe $exe -Arguments $gameArgs -WorkingDirectory (Split-Path $exe) '
            '-StdOutPath $outLog -StdErrPath $errLog')

WAIT_START = '        # WaitForExit(ms) can block past its timeout when stdout/stderr are redirected to files'
WAIT_END = "            else { 'timeout' | Set-Content (Join-Path $jobOut 'exit.txt') }\n        }"
WAIT_NEW = """        # Judged by the clock, never by a wait on a redirected handle: the farm's queue sat on one job for over two hours against a 900 s
        # timeout because WaitForExit(ms) never returned (2026-10-30). Wait-JobProcess polls HasExited, stops this job's own PID tree at the
        # deadline, and writes TIMEOUT, so the queue always moves on to the next job.
        $run = Wait-JobProcess -Process $p -TimeoutSeconds ([int]$spec.timeout_s) -TimeoutMarkerPath $timeoutMarker
        if ($run.TimedOut) {
            'timeout' | Set-Content (Join-Path $jobOut 'exit.txt')
            Copy-Item $timeoutMarker (Join-Path $out 'TIMEOUT') -Force
        } else {
            "$($run.ExitCode)" | Set-Content (Join-Path $jobOut 'exit.txt')
        }"""

if 'Start-JobProcess' in text:
    print('runner already uses the polled wait')
else:
    assert DOT_SOURCE_ANCHOR in text and START_OLD in text and CALL_OLD in text and WAIT_START in text, 'an anchor is missing'
    text = text.replace(DOT_SOURCE_ANCHOR, DOT_SOURCE, 1)
    text = text.replace(START_OLD, START_NEW, 1)
    text = text.replace(CALL_OLD, CALL_NEW, 1)
    begin = text.index(WAIT_START)
    end = text.index(WAIT_END, begin) + len(WAIT_END)
    text = text[:begin] + WAIT_NEW + text[end:]
    PATH.write_text(text, encoding='utf-8')
    print('runner patched: polled wait, PID-tree stop, TIMEOUT marker')
