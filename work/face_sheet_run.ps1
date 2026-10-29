# Gate D part 3 proof: the NPC look review plus the expression sweep over two faces per town.
$ErrorActionPreference = 'Continue'
$gatec = 'D:\Otherreach_Scratch\agy_gatec_wt'
$log = Join-Path $gatec 'work\gatec_logs\face_sheet.log'
function Say($t) { $line = "$(Get-Date -Format 'HH:mm:ss') $t"; Write-Host $line; Add-Content -Path $log -Value $line }
$plan = Join-Path $gatec 'tools\creator\plans\npc_looks.json'
$out = Join-Path $gatec 'work\creator\out\npc_looks\faces01'
$env:OTHERREACH_FACE_SHEET = '1'
for ($attempt = 1; $attempt -le 200; $attempt++) {
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $gatec 'tools\creator\run_npc_looks.ps1') -Label faces01 -Plan $plan *> (Join-Path $gatec 'work\gatec_logs\faces01.wrapper.log')
    $done = Test-Path (Join-Path $out 'complete.json')
    Say "attempt ${attempt}: complete=$done"
    if ($done) { break }
    Start-Sleep -Seconds 12
}
Remove-Item Env:OTHERREACH_FACE_SHEET -ErrorAction SilentlyContinue
Say "frames: $((Get-ChildItem $out -Filter '*.png' -Recurse -ErrorAction SilentlyContinue | Measure-Object).Count)"
Say 'face sheet done'
