param([ValidateSet('f','m')][string]$Sex='f',[string]$JobRoot='D:\gc_roles02')
$ErrorActionPreference='Stop'
$wt='D:\Otherreach_Scratch\agy_gatec_wt'
$roles=Get-Content "$wt\tools\creator\role_garments.json" -Raw | ConvertFrom-Json
$results=@()
foreach($item in $roles.PSObject.Properties){
 if($item.Value.sexes -and $Sex -notin $item.Value.sexes){continue}
 try {
  & "$wt\tools\creator\run_role_export.ps1" -Role $item.Name -Sex $Sex -JobRoot $JobRoot
  $results+=@{role=$item.Name;sex=$Sex;status='EXPORTED_REQUIRES_RENDER_REVIEW'}
 } catch {
  $results+=@{role=$item.Name;sex=$Sex;status='FAILED';error=$_.Exception.Message}
 }
 $results | ConvertTo-Json -Depth 5 | Set-Content "$JobRoot\BATCH_STATE.json" -Encoding utf8
}
if($results.status -contains 'FAILED'){throw 'Role export failures recorded in BATCH_STATE.json'}
'0' | Set-Content "$JobRoot\batch.exit" -Encoding ascii
