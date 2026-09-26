# Renders every Animated Armour v2 verification sheet from the exported GLB into docs\phase_b\demo\armour.
# Usage: powershell -File render_all_sheets.ps1
$B = "C:\Program Files\Blender Foundation\Blender 5.2\blender.exe"
$T = $PSScriptRoot
$G = "G:\UNNAMED_PHASEB\assets\rigged\creature_animated_armour_v2\creature_animated_armour_v2_rigged.glb"
$C = "G:\UNNAMED_PHASEB\assets\animation\ready\creatures"
$ST = "G:\UNNAMED_PHASEB\assets\_staging\armour\sheets"
$D = "G:\UNNAMED_PHASEB\docs\phase_b\demo\armour"
New-Item -ItemType Directory -Force $ST, $D | Out-Null
function Run($script, $argList) { & $B -b --factory-startup -P "$T\$script" -- @argList 2>&1 | Select-String -Pattern "(Error|Traceback)" | ForEach-Object { $_.Line } }

$env:HDRI = "studio.exr"
# (a) clay + wireframe at rest, plus the idle stance
Run "render_blend.py" @($G, $ST, "wire", "wire", "0:5,90:5,180:5,35:10", "640x900")
Run "render_poses.py" @($G, "$C\anim.creature.animated_armour.idle.glb", "0", "35:10", $ST, "idlewire", "640x900", "wire")
python "$T\make_sheet.py" "$D\armour_v2_clay_wire_rest.jpg" 5 "Animated Armour v2 - clay + wireframe, bind pose (41,430 tris). Last column: idle clip f0 = in-game stance" `
    "$ST\wire_0_5.png|front (rest)" "$ST\wire_90_5.png|left side (rest)" "$ST\wire_180_5.png|back (rest)" "$ST\wire_35_10.png|3/4 front-left (rest)" "$ST\idlewire_0_35_10.png|idle f0 stance, 3/4"
# (b) textured
Run "render_blend.py" @($G, $ST, "tex", "tex", "0:5,90:5,180:5,35:10", "640x900")
$env:FRAME = "0,-0.12,1.45,0.8"; Run "render_blend.py" @($G, $ST, "ctorso", "tex", "30:8", "640x900")
$env:FRAME = "0,-0.12,0.45,0.9"; Run "render_blend.py" @($G, $ST, "clegs", "tex", "30:8", "640x900")
$env:FRAME = $null
python "$T\make_sheet.py" "$D\armour_v2_textured.jpg" 6 "Animated Armour v2 - textured (2k baked PBR: basecolor / normal / ORM), bind pose, neutral studio HDRI" `
    "$ST\tex_0_5.png|front" "$ST\tex_90_5.png|left side" "$ST\tex_180_5.png|back" "$ST\tex_35_10.png|3/4 front-left" "$ST\ctorso_30_8.png|close-up torso" "$ST\clegs_30_8.png|close-up legs"
# (c) each clip at three moments, front-3/4 and back-3/4
$plan = [ordered]@{ idle = "0,30,60"; walk = "0,3.6,10.8"; run = "0,2.4,4.8"; attack = "5.4,10.8,16.2"; hit = "2,6.6,11"; death = "11.4,22.8,45.6" }
foreach ($k in $plan.Keys) {
    Run "render_poses.py" @($G, "$C\anim.creature.animated_armour.$k.glb", $plan[$k], "35:8,215:12", $ST, $k, "460x600")
    $items = @()
    foreach ($f in $plan[$k].Split(",")) { $fs = $f.Replace(".", "_"); $items += "$ST\$($k)_$($fs)_35_8.png|$k frame $f (24 fps) front-3/4"; $items += "$ST\$($k)_$($fs)_215_12.png|$k frame $f back-3/4" }
    python "$T\make_sheet.py" "$D\armour_v2_clip_$k.jpg" 6 "Animated Armour v2 on anim.creature.animated_armour.$k.glb (clip unchanged) - 3 moments" @items | Out-Null
}
# (d) joints at the clips' most extreme frames
$jobs = @(@("run", "4.8", "hips,0.9", "90:5,35:8"), @("attack", "5.4", "upper_arm.R,0.7", "-60:10,150:15"), @("attack", "10.8", "forearm.R,0.7", "-10:10,90:10"),
          @("death", "22.8", "shin.L,0.8", "60:25,200:25"), @("hit", "6.6", "neck,0.6", "60:5,200:10"), @("walk", "3.6", "hips,0.9", "90:5,215:10"))
$items = @()
foreach ($j in $jobs) {
    $env:FOCUS = $j[2]
    Run "render_poses.py" @($G, "$C\anim.creature.animated_armour.$($j[0]).glb", $j[1], $j[3], $ST, "close_$($j[0])", "420x420")
    foreach ($v in $j[3].Split(",")) { $items += "$ST\close_$($j[0])_$($j[1].Replace('.','_'))_$($v.Replace(':','_')).png|$($j[0]) f$($j[1]) at $($j[2].Split(',')[0]), cam $v" }
}
$env:FOCUS = $null
python "$T\make_sheet.py" "$D\armour_v2_joint_closeups.jpg" 6 "Animated Armour v2 - joints at the clips' most extreme frames (gaps show dark padding / void interior)" @items
# (e) concept beside the result at a matching angle
Run "render_blend.py" @($G, $ST, "cmp", "tex", "44:12", "900x1200")
Run "render_poses.py" @($G, "$C\anim.creature.animated_armour.idle.glb", "0", "44:12", $ST, "cmpidle", "900x1200")
python -c "from PIL import Image; im=Image.open(r'G:\UNNAMED_PHASEB\assets\concepts\creature_animated_armour.png').convert('RGB'); w,h=im.size; c=im.crop((int(w*0.12),0,int(w*0.88),h)).resize((900,int(900*h/(w*0.76)))); bg=Image.new('RGB',(900,1200),(160,160,162)); bg.paste(c,(0,(1200-c.height)//2)); bg.save(r'$ST\concept_fit.png')"
python "$T\make_sheet.py" "$D\armour_v2_concept_compare.jpg" 3 "Concept vs Animated Armour v2 at a matching front-left 3/4 angle (studio HDRI)" `
    "$ST\concept_fit.png|concept (creature_animated_armour.png)" "$ST\cmp_44_12.png|v2 bind pose" "$ST\cmpidle_0_44_12.png|v2 idle clip f0 (in-game stance)"
$env:HDRI = $null
Get-ChildItem $D | Select-Object Name, Length
