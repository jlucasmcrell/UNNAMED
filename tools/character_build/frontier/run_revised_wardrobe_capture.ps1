param([string]$Tag='v3',[string]$Candidate='region_candidate2',[string]$Fit='mh_veth_f',[string[]]$States=@('idle','walk','run'))
$ErrorActionPreference='Stop'
$root='D:\Otherreach_Scratch\frontier_native_20261018'
$candidate="$root\$Candidate"
$owner="codex approved-wardrobe-$Tag"
Get-FileHash -LiteralPath 'C:\Otherreach_Build\integ_bodies_20261018\src\Presentation\.godot\mono\temp\bin\Debug\UNNAMED.Presentation.dll' | Export-Csv "$root\$($Tag)_$($Fit)_runtime.csv" -NoTypeInformation
Get-ChildItem -LiteralPath "$candidate\primary\rigged" -Directory | Where-Object { $_.Name -eq $Fit -or $_.Name.EndsWith("__$Fit") } | ForEach-Object { Get-ChildItem -LiteralPath $_.FullName -Recurse -File -Filter '*_rigged.glb' } | Get-FileHash | Export-Csv "$root\$($Tag)_$($Fit)_inputs.csv" -NoTypeInformation
$player=if($Fit -match 'veth_f') {'metahuman_f'} else {'metahuman_m'}
$expected=36*$States.Count
$statesArgument=$States -join ','
$acquired=$false
for($attempt=1; -not $acquired; $attempt++) {
    try { & C:\Otherreach_Recovery\gpu_lock.ps1 acquire $owner; $acquired=$true }
    catch { if($attempt -ge 5) { throw }; Start-Sleep -Seconds 5 }
}
try {
    foreach($variant in @('frontier','armour')) {
        & 'C:\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe' --fixed-fps 20 --path 'C:\Otherreach_Build\integ_bodies_20261018\src\Presentation' -- --anim-sheet "$candidate\shots_$($Tag)_$($Fit)_$variant" --anim-profiles --anim-states $statesArgument --asset-root "$candidate\primary" --bindings-overlay "$candidate\bindings_$($Fit)_$variant.json" --visual "tier=high,barrowworks=0,player=$player" *> "$root\$($Tag)_$($Fit)_${variant}_gpu.log"
        if($LASTEXITCODE -ne 0) { throw "Capture failed:$variant" }
        if(Select-String -LiteralPath "$root\$($Tag)_$($Fit)_${variant}_gpu.log" -Pattern '^ERROR:|SCRIPT ERROR:') { throw "Capture errors:$variant" }
        $pictures=Get-ChildItem -LiteralPath "$candidate\shots_$($Tag)_$($Fit)_$variant" -Filter '*.png'
        if($pictures.Count -ne $expected) { throw "Expected$expected $variant captures, found$($pictures.Count)" }
    }
    Write-Output "Game$($expected*2) wardrobe motion pictures recorded ($Tag); review pending"
} finally { & C:\Otherreach_Recovery\gpu_lock.ps1 release $owner }
