if(-not [IO.Path]::IsPathRooted($env:OTHERREACH_PIPELINE_ROOT)){throw 'Set an absolute OTHERREACH_PIPELINE_ROOT'}
$jobRoot=[IO.Path]::GetFullPath($env:OTHERREACH_PIPELINE_ROOT).TrimEnd('\','/')
$frozenRoot='D:\Otherreach_Scratch\mh_playtest_20261019'
if($jobRoot -ieq $frozenRoot -or $jobRoot.StartsWith($frozenRoot+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Frozen f87 data cannot be a job output root'}
$ErrorActionPreference='Stop'
$r=$env:OTHERREACH_PIPELINE_ROOT
$catalog=Get-Content -LiteralPath "$r\runtime\catalog.json" -Raw | ConvertFrom-Json
$rows=@()
foreach($entry in $catalog.characters.PSObject.Properties){
 $id=$entry.Name; $body=Join-Path "$r\runtime" $entry.Value.body
 $name="${id}_trousers.glb" # build_native_trousers.py exports this canonical per-body input.
 $source="$r\runtime\wardrobe\shell\$name"; $dest="$r\runtime\wardrobe\shell\${id}_direction_hose.glb"
 $hash=(Get-FileHash -LiteralPath $body).Hash
 $p=Start-Process -FilePath 'C:\Program Files\Blender Foundation\Blender 5.2\blender.exe' -ArgumentList @('--background','--factory-startup','--threads','4','--python-exit-code','2','--python',"$PSScriptRoot\rebind_piece_direction.py",'--',$source,$body,$dest) -WindowStyle Hidden -Wait -PassThru -RedirectStandardOutput "$r\evidence\hose_rebind_$id.log" -RedirectStandardError "$r\evidence\hose_rebind_$id.stderr"
 if($p.ExitCode -ne 0 -or (Get-FileHash -LiteralPath $body).Hash -ne $hash){throw "Hose rebind failed/native body changed: $id"}
 $rows+=@{id=$id;source=$source;target_sha256=$hash;output=$dest;output_sha256=(Get-FileHash -LiteralPath $dest).Hash}
 $rows|ConvertTo-Json -Depth 5|Set-Content -LiteralPath "$r\evidence\hose_direction_rebind_manifest.json"
 Write-Output "Rebound hose $id"
}
