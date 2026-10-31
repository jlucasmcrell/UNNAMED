<#
.SYNOPSIS
  Counts this machine's TCP and UDP endpoints per owning process and writes them to one CSV per run.

.DESCRIPTION
  Read-only diagnostic for the recurring "all ports in use" events on BEAST
  (ROADMAP\BEAST_NETWORK_DIAGNOSIS.md, item 3: Tcpip 4231/4266, 21 times in 30 days). It samples every
  -IntervalMinutes and stops by itself at -MaxHours (24 is the cap). It changes no setting, closes no
  connection and kills no process: it only reads Get-NetTCPConnection / Get-NetUDPEndpoint and appends
  rows. Memory stays flat because each sample is written and dropped - nothing accumulates in a list.

  One run writes:
    <OutDir>\port_watch_<yyyyMMdd_HHmmss>.csv   one row per owning process per sample, plus a TOTAL row
    <OutDir>\port_watch.log                     start, stop, each sample's index and this process's own MB
    <OutDir>\port_watch.lock                    the running instance's pid, so a second copy refuses

.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\port_watch.ps1
  powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\port_watch.ps1 -Once
#>
param(
    [ValidateRange(1, 24)][int]$MaxHours = 24,
    [ValidateRange(1, 60)][int]$IntervalMinutes = 2,
    [string]$OutDir = 'D:\Otherreach_Scratch\port_watch',
    [switch]$Once
)

$ErrorActionPreference = 'Stop'
$script:Started = Get-Date
$script:RunId = $script:Started.ToString('yyyyMMdd_HHmmss')

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$csv = Join-Path $OutDir ("port_watch_{0}.csv" -f $script:RunId)
$log = Join-Path $OutDir 'port_watch.log'
$lock = Join-Path $OutDir 'port_watch.lock'

function Write-Log([string]$message) {
    $line = '{0} {1}' -f (Get-Date).ToString('s'), $message
    Add-Content -Path $log -Value $line -Encoding utf8
}

# One instance only: a second copy would interleave samples in its own CSV and double the sampling cost.
if (Test-Path $lock) {
    $held = (Get-Content $lock -ErrorAction SilentlyContinue | Select-Object -First 1)
    $heldPid = 0
    if ($held -and [int]::TryParse(($held -split '\s+')[0], [ref]$heldPid)) {
        if (Get-Process -Id $heldPid -ErrorAction SilentlyContinue) {
            Write-Host "port_watch is already running as pid $heldPid (lock $lock); this copy exits."
            exit 0
        }
    }
    Write-Log "stale lock from pid $heldPid (gone); taking it"
}
Set-Content -Path $lock -Value ("{0} started {1}" -f $PID, $script:Started.ToString('s')) -Encoding utf8

$header = 'sampled_at,sample,elapsed_s,watcher_mb,pid,process,tcp_total,tcp_listen,tcp_established,tcp_timewait,tcp_closewait,tcp_other,udp_total,endpoints_total'
# Endpoints held by the kernel itself are attributed to pid 4 / pid 0 by Windows and show up here as well.
$names = @{}

function Get-ProcessName([int]$processId) {
    if ($names.ContainsKey($processId)) { return $names[$processId] }
    $name = '?'
    if ($processId -eq 0) { $name = 'Idle' }
    elseif ($processId -eq 4) { $name = 'System' }
    else {
        $p = Get-Process -Id $processId -ErrorAction SilentlyContinue
        if ($p) { $name = $p.ProcessName }
    }
    # Bounded on purpose: a machine that churns pids for a day must not grow this table without limit.
    if ($names.Count -gt 4096) { $names.Clear() }
    $names[$processId] = $name
    return $name
}

function Write-Sample([int]$index) {
    $tcp = @(Get-NetTCPConnection -ErrorAction SilentlyContinue)
    $udp = @(Get-NetUDPEndpoint -ErrorAction SilentlyContinue)

    $counts = @{}
    foreach ($connection in $tcp) {
        $key = [int]$connection.OwningProcess
        if (-not $counts.ContainsKey($key)) {
            $counts[$key] = @{ tcp = 0; listen = 0; established = 0; timewait = 0; closewait = 0; other = 0; udp = 0 }
        }
        $slot = $counts[$key]
        $slot.tcp++
        switch ([string]$connection.State) {
            'Listen' { $slot.listen++ }
            'Established' { $slot.established++ }
            'TimeWait' { $slot.timewait++ }
            'CloseWait' { $slot.closewait++ }
            default { $slot.other++ }
        }
    }
    foreach ($endpoint in $udp) {
        $key = [int]$endpoint.OwningProcess
        if (-not $counts.ContainsKey($key)) {
            $counts[$key] = @{ tcp = 0; listen = 0; established = 0; timewait = 0; closewait = 0; other = 0; udp = 0 }
        }
        $counts[$key].udp++
    }

    $at = (Get-Date).ToString('s')
    $elapsed = [int]((Get-Date) - $script:Started).TotalSeconds
    $self = [math]::Round((Get-Process -Id $PID).WorkingSet64 / 1MB, 1)
    $text = New-Object System.Text.StringBuilder
    if (-not (Test-Path $csv)) { [void]$text.AppendLine($header) }
    $totals = @{ tcp = 0; udp = 0; listen = 0; established = 0; timewait = 0; closewait = 0; other = 0 }
    foreach ($key in ($counts.Keys | Sort-Object { - ($counts[$_].tcp + $counts[$_].udp) })) {
        $slot = $counts[$key]
        $total = $slot.tcp + $slot.udp
        $totals.tcp += $slot.tcp; $totals.udp += $slot.udp; $totals.listen += $slot.listen
        $totals.established += $slot.established; $totals.timewait += $slot.timewait
        $totals.closewait += $slot.closewait; $totals.other += $slot.other
        $row = '{0},{1},{2},{3},{4},"{5}",{6},{7},{8},{9},{10},{11},{12},{13}' -f $at, $index, $elapsed, $self, $key,
            (Get-ProcessName $key), $slot.tcp, $slot.listen, $slot.established, $slot.timewait, $slot.closewait,
            $slot.other, $slot.udp, $total
        [void]$text.AppendLine($row)
    }
    $totalRow = '{0},{1},{2},{3},{4},"{5}",{6},{7},{8},{9},{10},{11},{12},{13}' -f $at, $index, $elapsed, $self, -1,
        'TOTAL', $totals.tcp, $totals.listen, $totals.established, $totals.timewait, $totals.closewait, $totals.other,
        $totals.udp, ($totals.tcp + $totals.udp)
    [void]$text.AppendLine($totalRow)
    Add-Content -Path $csv -Value $text.ToString().TrimEnd() -Encoding utf8
    Write-Log ("sample {0}: {1} processes, {2} tcp, {3} udp ({4} time-wait), watcher {5} MB" -f $index,
        $counts.Count, $totals.tcp, $totals.udp, $totals.timewait, $self)
    if ($self -gt 250) {
        Write-Log "watcher working set is $self MB, above the 250 MB guard; stopping rather than growing"
        return $false
    }
    return $true
}

$deadline = $script:Started.AddHours($MaxHours)
Write-Log ("start pid {0}: every {1} min until {2}, csv {3}" -f $PID, $IntervalMinutes, $deadline.ToString('s'), $csv)
$index = 0
$stop = $false
try {
    do {
        $index++
        if (-not (Write-Sample $index)) { $stop = $true; break }
        if ($Once) { break }
        $left = [int]($deadline - (Get-Date)).TotalSeconds
        if ($left -le 0) { break }
        Start-Sleep -Seconds ([Math]::Max(1, [Math]::Min($IntervalMinutes * 60, $left)))
    } while (((Get-Date) -lt $deadline) -and -not $stop)
}
finally {
    $held = Get-Content $lock -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($held -and ($held -split '\s+')[0] -eq "$PID") { Remove-Item $lock -Force -ErrorAction SilentlyContinue }
    Write-Log ("stop pid {0} after {1} samples" -f $PID, $index)
}
