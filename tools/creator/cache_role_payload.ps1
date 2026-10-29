param([string]$Requests='D:\gc_delivery\TEXTURE_REQUESTS01.json',[string]$Label='role_cache01')
$ErrorActionPreference='Stop'
$wt='D:\Otherreach_Scratch\agy_gatec_wt'
& 'D:\Otherreach_Scratch\humgen_rcd8_20261023\transition\run_candidate_guard.ps1' -Executable 'C:\Otherreach_Worker\tools\godot_4_7_2_net8_0_31\godot\Godot_v4.7.2-stable_mono_win64.exe' -JobArguments @('--headless','--path',"$wt\src\Presentation",'res://Diagnostics/PackageTextureCacheGate.tscn','--',$Requests) -Log "D:\gc_delivery\$Label" -Job "codex Gate C $Label" -LimitSeconds 540 -FailIfStderrMatches 'SCRIPT ERROR|InvalidDataException|Compression/resolution gate failed'
