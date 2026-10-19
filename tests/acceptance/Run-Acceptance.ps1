# The playtest acceptance run: every checklist step (steps.csv) gets one line, "[§n.m] PASS|FAIL|HUMAN|NOT AUTOMATED|SHOT reason".
#   powershell -ExecutionPolicy Bypass -File tests\acceptance\Run-Acceptance.ps1 [-Report <file>] [-NoBuild]   (the report defaults to unnamed_acceptance_report.txt in the temp folder)
# - steps the walkthrough checks itself (source "walk") come from tests/Application.Tests/Acceptance (AcceptanceWalkthroughTests, three tests);
# - steps proved by existing tests (source "tests") PASS when every named test class or method ran and passed;
# - steps that need eyes (source "human", "stale", "shot") are listed with why; "shot" steps are looked at in the --ui-shots pass the run's notes name.
# Exit code 1 when a step FAILs for a reason that is not a recorded bug (BUG-n in its reason).
param([string]$Report = (Join-Path ([IO.Path]::GetTempPath()) "unnamed_acceptance_report.txt"), [switch]$NoBuild)

$sec = [string][char]0xA7
$repo = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
Set-Location $repo
$work = Join-Path ([IO.Path]::GetTempPath()) ("unnamed-acceptance-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force $work | Out-Null
if (-not $NoBuild) { dotnet build src/UNNAMED.sln | Out-Null; if ($LASTEXITCODE -ne 0) { throw "build failed" } }

# 1. the walkthrough
$walkReport = Join-Path $work "walk.txt"
$env:UNNAMED_ACCEPTANCE_REPORT = $walkReport
dotnet test tests/Application.Tests --no-build --filter "FullyQualifiedName~Acceptance" --logger "trx;LogFileName=$work\walk.trx" | Out-Null
$walk = @{}
if (Test-Path $walkReport) {
    foreach ($line in Get-Content $walkReport) { if ($line -match '^\[§(\d+\.\d+[a-z]?)\] (.*)$') { $walk[$Matches[1]] = $Matches[2] } }
}

# 2. the tests that cover the other steps
$steps = Import-Csv (Join-Path $PSScriptRoot "steps.csv")
$covering = @{}
foreach ($step in $steps | Where-Object source -eq "tests") {
    foreach ($ref in $step.note -split '\|') { $project, $name = $ref -split ':', 2; $covering["$project|$name"] = $project }
}
$outcomes = @{}
foreach ($project in ($covering.Values | Sort-Object -Unique)) {
    $names = $covering.Keys | Where-Object { $_ -like "$project|*" } | ForEach-Object { "FullyQualifiedName~" + ($_ -split '\|', 2)[1] }
    $trx = Join-Path $work "$project.trx"
    dotnet test "tests/$project" --no-build --filter ($names -join '|') --logger "trx;LogFileName=$trx" | Out-Null
    if (Test-Path $trx) {
        $xml = [xml](Get-Content $trx)
        foreach ($r in $xml.TestRun.Results.UnitTestResult) { $outcomes[$project + "|" + $r.testName] = $r.outcome }
    }
}

# 3. the report
$lines = foreach ($step in $steps) {
    $id = $step.id
    switch ($step.source) {
        "walk" {
            if ($walk.ContainsKey($id)) { "[$sec$id] " + $walk[$id] } else { "[$sec$id] FAIL the walkthrough did not reach this step" }
            if ($walk.ContainsKey($id + "b")) { "[${sec}${id}b] " + $walk[$id + "b"] }
        }
        "tests" {
            $parts = @()
            $bad = $false
            foreach ($ref in $step.note -split '\|') {
                $project, $name = $ref -split ':', 2
                $hit = $outcomes.Keys | Where-Object { $_ -like "$project|*$name*" }
                if (-not $hit) { $bad = $true; $parts += "$name (no test ran)"; continue }
                $failed = $hit | Where-Object { $outcomes[$_] -ne "Passed" }
                if ($failed) { $bad = $true }
                $parts += "$name ($(@($hit).Count) tests)"
            }
            if ($bad) { "[$sec$id] FAIL covering tests did not all pass: " + ($parts -join ", ") } else { "[$sec$id] PASS covered by " + ($parts -join ", ") }
        }
        "human" { "[$sec$id] HUMAN " + $step.note }
        "stale" { "[$sec$id] NOT AUTOMATED " + $step.note }
        "shot" { "[$sec$id] SHOT " + $step.note }
    }
}
$lines | Set-Content -Encoding utf8 $Report
$lines
$unexplained = $lines | Where-Object { $_ -match '\] FAIL' -and $_ -notmatch 'BUG-\d' }
$known = $lines | Where-Object { $_ -match '\] FAIL' -and $_ -match 'BUG-\d' }
Write-Host ("`n{0} steps: {1} PASS, {2} FAIL ({3} recorded bugs), {4} HUMAN, {5} SHOT, {6} NOT AUTOMATED. Report: {7}" -f $lines.Count, @($lines -match '\] PASS').Count,
    @($lines -match '\] FAIL').Count, @($known).Count, @($lines -match '\] HUMAN').Count, @($lines -match '\] SHOT').Count, @($lines -match '\] NOT AUTOMATED').Count, $Report)
if ($unexplained) { exit 1 }
