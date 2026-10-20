param([Parameter(Mandatory)][string]$Action,[Parameter(Mandatory)][string]$Owner)
# A fresh Windows PowerShell process plus terminating errors prevents a queue I/O failure looping forever.
$ErrorActionPreference='Stop'
& 'C:\Otherreach_Recovery\gpu_lock.ps1' $Action $Owner
