$ErrorActionPreference='Stop'
$root='D:\Otherreach_Scratch\mh_playtest_20261019'
$report='C:\Otherreach_External_Assets\REPORT_FOR_CLAUDE_BODYSHELL_FOOTWEAR.md'
while(-not ((Get-Content -LiteralPath $report -Raw) -match '(?im)^#{1,3}[^\r\n]*trousers|_trousers\.glb')){
 if(Test-Path -LiteralPath 'C:\Otherreach_Recovery\OWNER_PLAYING.flag'){throw 'Owner playing; deferred'}
 Start-Sleep -Seconds 5
}
$catalog=Get-Content -LiteralPath "$root\runtime\catalog.json" -Raw|ConvertFrom-Json
foreach($id in $catalog.characters.PSObject.Properties.Name){
 if(Test-Path -LiteralPath "$root\evidence\trousers_$id.json"){continue}
 & 'D:\Otherreach_Scratch\mh_slice_20261019\scripts\run_guarded.ps1' -Executable 'C:\Program Files\Blender Foundation\Blender 5.2\blender.exe' -JobArguments @('--background','--factory-startup','--threads','4','--python-exit-code','2','--python',"$root\scripts\build_native_trousers.py",'--',$id) -Log "$root\evidence\trousers_$id.log" -Job "native-trousers-$id"
 $record=Get-Content -LiteralPath "$root\evidence\trousers_$id.json" -Raw|ConvertFrom-Json
 if($record.status -ne 'DONE'){throw "Native trousers incomplete $id"}
 Write-Output "NATIVE_TROUSERS_READY $id"
}
Write-Output 'NATIVE_TROUSER_ROSTER_READY_NOT_YET_STAGED'
