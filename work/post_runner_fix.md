## FARM RUNNER FIXED — THE OWNER NEEDS TO RESTART THE RUNNER ON THE 6900XT CONSOLE (2026-10-30 21:56)

**The queue deadlock is a code fault in the runner, not the machine, and it is fixed in the share.** The runner's wait used
`$p.WaitForExit($deadline)`, which Microsoft documents as stopping at the timeout but which with redirected stdout/stderr can block indefinitely -
that is what held the queen on `claude_proof_elk_close02` from 19:31 for over two hours against a 900 s timeout, and the timeout branch never ran.

### What changed

`runner\runner.ps1` (backed up first as `runner\runner.before_waitfix_20261030_2152.ps1`) now dot-sources `runner\jobprocess.ps1`:

- **start**: `Start-JobProcess` uses `Start-Process` with OS-level redirection to `stdout.log` / `stderr.log`, so nothing ever waits on a redirected
  pipe. It touches `$process.Handle` so `ExitCode` is readable afterwards.
- **wait**: `Wait-JobProcess` polls `HasExited` every 250 ms against the clock. There is **no `WaitForExit` with a deadline anywhere**.
- **stop**: at the deadline it runs `taskkill /PID <pid> /T /F` on **the job's own recorded PID tree only**, then `Stop-Process` on that same pid if
  needed. Nothing else on the box is touched.
- **marker**: it writes `results\<job>\TIMEOUT` (and copies it into the local output folder before robocopy), sets `exit.txt` to `timeout`, renames
  the job `.done`, and picks up the next job.

### Tested, twice

1. `work\test_runner_timeout.py` against dummy children: a child that exits 7 reports `TimedOut=False exit=7` with its output captured; a child that
   never finishes reports `TimedOut=True` after **5.5 s against a 5 s deadline**, with the tree stopped and the marker written.
2. `work\test_runner_end_to_end.py`: a **copy** of the patched runner pointed at a fake farm root, given two dummy jobs. Results:
   `a_fast: exit.txt = 3, done` and `b_slow: TIMEOUT written ("timed out after 5s; pid 47464 stopped with its tree"), exit.txt = timeout, done`.
   The queue moved on by itself. The real runner and the real job queue were not touched by the test.

Both scripts parse clean (`work\parse_check.py`).

### What is needed from the owner

**Restart the runner on the 6900XT console** - close its PowerShell window and start `\\BEAST\OtherreachFarm\runner\runner.ps1` again. The live
process is wedged inside the old `WaitForExit`, so it cannot pick the new file up by itself (its self-replace check only runs between jobs). After the
restart, `claude_fix_boulders02`, `claude_cap_crows3` and `claude_cap_trout03` are already queued and will run without anyone watching.
