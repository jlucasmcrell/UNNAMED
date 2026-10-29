param([string]$Role='outfit.period.robe',[string]$Sex='f',[string]$JobRoot='D:\gc_roles02')
$ErrorActionPreference='Stop'
$wt='D:\Otherreach_Scratch\agy_gatec_wt'
New-Item -ItemType Directory -Force -Path "$JobRoot\game\primary\rigged" | Out-Null
foreach($id in 'humgen_creator_male','humgen_creator_female'){
 if(-not(Test-Path "$JobRoot\game\primary\rigged\$id")){
  New-Item -ItemType Junction -Path "$JobRoot\game\primary\rigged\$id" -Target "D:\Otherreach_Scratch\codex_f1_siann_wt\work\creator\game\primary\rigged\$id" | Out-Null
 }
}
$env:OTHERREACH_ROLE_GARMENTS="$wt\tools\creator\role_garments.json"
$env:OTHERREACH_ROLE_SEX=$Sex
$part='humgen_creator_'+$Sex+'_outfit_'+$Role.Replace('.','_')
if(Test-Path "$JobRoot\game\primary\rigged\$part\creator_part.json") {Write-Host "Native role already exported: $part";return}
& 'D:\Otherreach_Scratch\codex_f1_siann_wt\work\creator\spawn_cpu_tool.ps1' -Executable 'C:\Program Files\Blender Foundation\Blender 5.2\blender.exe' -JobArguments @('-b','--threads','4','--python-exit-code','1','--python',"$wt\tools\creator\export_humgen_creator_parts.py",'--',$JobRoot,"$wt\src\Presentation\Art\humgen_creator.json",$Role) -Log "$JobRoot\$($Role.Replace('.','_'))_$Sex.log" -Job 'Codex Gate C native role fit' -LimitSeconds 2700
