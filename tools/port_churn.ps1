<#
.SYNOPSIS
  Names the processes whose short-lived connections end up in TIME_WAIT, which is what runs BEAST's
  ephemeral port band out (ROADMAP\BEAST_NETWORK_DIAGNOSIS.md item 3).

.DESCRIPTION
  A socket in TIME_WAIT has no owning process in Windows' tables (it reports pid 0), so the creator has
  to be caught while the connection is still alive. This samples the live socket table every
  -IntervalSeconds, remembers each four-tuple's owning process while it is ESTABLISHED, and when the
  same four-tuple turns up as TimeWait it credits that close to the process that held it. Read-only:
  Get-NetTCPConnection and nothing else. Writes one CSV per run and stops itself at -Minutes.

  Memory stays flat: the map is pruned of tuples that have not been seen for -ForgetSeconds and every
  sample is written and dropped. One instance at a time by lock file.

.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\port_churn.ps1 -Minutes 10
#>
param(
    [ValidateRange(1, 120)][int]$Minutes = 10,
    [ValidateRange(5, 60)][int]$IntervalSeconds = 10,
    [ValidateRange(30, 3600)][int]$ForgetSeconds = 300,
    [string]$OutDir = 'D:\Otherreach_Scratch\port_watch',
    [switch]$Once
)

$ErrorActionPreference = 'Stop'
$started = Get-Date
$runId = $started.ToString('yyyyMMdd_HHmmss')
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$csv = Join-Path $OutDir ("port_churn_{0}.csv" -f $runId)
$log = Join-Path $OutDir 'port_churn.log'
$lock = Join-Path $OutDir 'port_churn.lock'

function Write-Log([string]$message) {
    Add-Content -Path $log -Value ('{0} {1}' -f (Get-Date).ToString('s'), $message) -Encoding utf8
}

if (Test-Path $lock) {
    $held = (Get-Content $lock -ErrorAction SilentlyContinue | Select-Object -First 1)
    $heldPid = 0
    if ($held -and [int]::TryParse(($held -split '\s+')[0], [ref]$heldPid) -and (Get-Process -Id $heldPid -ErrorAction SilentlyContinue)) {
        Write-Host "port_churn is already running as pid $heldPid; this copy exits."
        exit 0
    }
}
Set-Content -Path $lock -Value ("{0} started {1}" -f $PID, $started.ToString('s')) -Encoding utf8

$owner = @{}      # four-tuple -> @{ pid = n; seen = datetime }
$names = @{}      # pid -> process name, bounded
$spawned = @{}    # pid -> how many of its closes landed in TIME_WAIT this sample
$header = 'sampled_at,sample,process,pid,timewait_closed,still_established,map_entries,watcher_mb'

function Get-Name([int]$processId) {
    if ($names.ContainsKey($processId)) { return $names[$processId] }
    $name = if ($processId -eq 0) { 'Idle' } elseif ($processId -eq 4) { 'System' } else {
        $p = Get-Process -Id $processId -ErrorAction SilentlyContinue
        if ($p) { $p.ProcessName } else { '?' }
    }
    if ($names.Count -gt 4096) { $names.Clear() }
    $names[$processId] = $name
    return $name
}

function Invoke-Sample([int]$index) {
    $now = Get-Date
    $rows = @(Get-NetTCPConnection -ErrorAction SilentlyContinue)
    $alive = @{}
    $spawned.Clear()
    foreach ($connection in $rows) {
        $tuple = '{0}:{1}->{2}:{3}' -f $connection.LocalAddress, $connection.LocalPort, $connection.RemoteAddress, $connection.RemotePort
        switch ([string]$connection.State) {
            'Established' {
                $pidOfSocket = [int]$connection.OwningProcess
                $owner[$tuple] = @{ pid = $pidOfSocket; seen = $now }
                $alive[$pidOfSocket] = 1 + [int]$alive[$pidOfSocket]
            }
            'TimeWait' {
                if ($owner.ContainsKey($tuple)) {
                    $key = [int]$owner[$tuple].pid
                    $spawned[$key] = 1 + [int]$spawned[$key]
                    [void]$owner.Remove($tuple)
                }
            }
        }
    }
    # Forget tuples nobody has refreshed: a socket closed by someone else must not be credited hours later.
    $horizon = $now.AddSeconds(-$ForgetSeconds)
    foreach ($key in @($owner.Keys)) { if ($owner[$key].seen -lt $horizon) { [void]$owner.Remove($key) } }

    $self = [math]::Round((Get-Process -Id $PID).WorkingSet64 / 1MB, 1)
    $text = New-Object System.Text.StringBuilder
    if (-not (Test-Path $csv)) { [void]$text.AppendLine($header) }
    $at = $now.ToString('s')
    foreach ($pidOfSocket in ($spawned.Keys | Sort-Object { - $spawned[$_] })) {
        $row = '{0},{1},"{2}",{3},{4},{5},{6},{7}' -f $at, $index, (Get-Name $pidOfSocket), $pidOfSocket,
            $spawned[$pidOfSocket], [int]$alive[$pidOfSocket], $owner.Count, $self
        [void]$text.AppendLine($row)
    }
    if ($spawned.Count -eq 0) {
        [void]$text.AppendLine(('{0},{1},"(none)",0,0,{2},{3},{4}' -f $at, $index, $alive.Count, $owner.Count, $self))
    }
    Add-Content -Path $csv -Value $text.ToString().TrimEnd() -Encoding utf8
    $summary = ($spawned.Keys | Sort-Object { - $spawned[$_] } | Select-Object -First 4 |
        ForEach-Object { '{0}={1}' -f (Get-Name $_), $spawned[$_] }) -join ' '
    Write-Log ("sample {0}: {1} tuples tracked, watcher {2} MB, closes {3}" -f $index, $owner.Count, $self, $summary)
}

$deadline = $started.AddMinutes($Minutes)
Write-Log ("start pid {0}: every {1}s for {2} min until {3}, csv {4}" -f $PID, $IntervalSeconds, $Minutes, $deadline.ToString('s'), $csv)
$index = 0
try {
    do {
        $index++
        Invoke-Sample $index
        if ($Once) { break }
        $left = [int]($deadline - (Get-Date)).TotalSeconds
        if ($left -le 0) { break }
        Start-Sleep -Seconds ([Math]::Max(1, [Math]::Min($IntervalSeconds, $left)))
    } while ((Get-Date) -lt $deadline)
}
finally {
    $held = Get-Content $lock -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($held -and ($held -split '\s+')[0] -eq "$PID") { Remove-Item $lock -Force -ErrorAction SilentlyContinue }
    Write-Log ("stop pid {0} after {1} samples" -f $PID, $index)
}
