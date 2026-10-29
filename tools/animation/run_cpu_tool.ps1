param([Parameter(Mandatory)][string]$Executable,[Parameter(Mandatory)][string[]]$JobArguments,
 [Parameter(Mandatory)][string]$Log,[string]$Job='humgen-cpu-tool',[int]$LimitSeconds=1200,[string]$FailIfStderrMatches='')
$ErrorActionPreference='Stop'
# GPU_LOCK_RULES.txt expressly permits these non-rendering jobs while the owner plays.
# Do not use the render guard here: it intentionally defers on OWNER_PLAYING.flag.
$name=[IO.Path]::GetFileName($Executable)
$allowed=($name -like 'Godot*' -and '--headless' -in $JobArguments) -or
 ($name -eq 'UnrealEditor-Cmd.exe' -and '-NullRHI' -in $JobArguments) -or
 ($name -eq 'blender.exe' -and '-b' -in $JobArguments -and '-f' -notin $JobArguments -and '-a' -notin $JobArguments)
if(-not $allowed){throw 'CPU guard requires headless Godot, NullRHI commandlet, or non-rendering background Blender'}
. 'D:\Otherreach_Scratch\ultra_foliage_20261022\scripts\start_no_console.ps1'
$owned=$null
try {
 $owned=Start-NoConsoleProcess -FilePath $Executable -ArgumentList $JobArguments -WindowStyle Hidden -PassThru -RedirectStandardOutput $Log -RedirectStandardError ($Log+'.stderr')
 $null=$owned.Handle
 if(-not $owned.HasExited){$owned.PriorityClass=[Diagnostics.ProcessPriorityClass]::BelowNormal}
 @{pid=$owned.Id;started=$owned.StartTime.ToString('o');job=$Job;cpu=$true;gpu=$false}|ConvertTo-Json|Set-Content -LiteralPath ($Log+'.process.json') -Encoding utf8
 $started=Get-Date
 while(-not $owned.HasExited){
  if(((Get-Date)-$started).TotalSeconds -gt $LimitSeconds){throw 'Owned CPU tool exceeded its limit'}
  if($FailIfStderrMatches -and (Test-Path -LiteralPath ($Log+'.stderr')) -and (Select-String -LiteralPath ($Log+'.stderr') -Pattern $FailIfStderrMatches -Quiet)){throw 'Owned CPU tool reported a fatal error; see stderr'}
  Start-Sleep -Seconds 2
  $owned.Refresh()
 }
 $owned.WaitForExit()
 if($FailIfStderrMatches -and (Test-Path -LiteralPath ($Log+'.stderr')) -and (Select-String -LiteralPath ($Log+'.stderr') -Pattern $FailIfStderrMatches -Quiet)){throw 'Owned CPU tool reported a fatal error; see stderr'}
 if($owned.ExitCode -ne 0){throw ('Owned CPU tool failed: '+$owned.ExitCode)}
 'Owned CPU tool completed: 0'
}finally{
 if($owned -and -not $owned.HasExited){
  $snapshot=Get-CimInstance Win32_Process
  $ids=[Collections.Generic.List[int]]::new();$ids.Add($owned.Id)
  for($i=0;$i -lt $ids.Count;$i++){
   foreach($child in $snapshot | Where-Object {$_.ParentProcessId -eq $ids[$i]}){$ids.Add([int]$child.ProcessId)}
  }
  for($i=$ids.Count-1;$i -ge 0;$i--){Stop-Process -Id $ids[$i] -Force -ErrorAction SilentlyContinue}
 }
}
