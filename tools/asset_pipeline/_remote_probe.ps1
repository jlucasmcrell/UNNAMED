# Probe the ASTRAL host: identity, resources, and where ComfyUI is listening.
Write-Output ("host=" + $env:COMPUTERNAME)
Write-Output ("user=" + (whoami))
$cs = Get-CimInstance Win32_ComputerSystem
Write-Output ("ram_gb=" + [math]::Round($cs.TotalPhysicalMemory / 1GB, 1))
$cpu = Get-CimInstance Win32_Processor | Select-Object -First 1
Write-Output ("cpu=" + $cpu.Name.Trim())

Write-Output "--- listening ports 8180-8200 ---"
Get-NetTCPConnection -State Listen -ErrorAction SilentlyContinue |
    Where-Object { $_.LocalPort -ge 8180 -and $_.LocalPort -le 8200 } |
    ForEach-Object { Write-Output ("port=" + $_.LocalPort + " pid=" + $_.OwningProcess) }

Write-Output "--- comfy processes ---"
Get-CimInstance Win32_Process -Filter "Name='python.exe'" |
    Where-Object { $_.CommandLine -match 'ComfyUI|comfy' } |
    ForEach-Object {
        $line = $_.CommandLine
        if ($line.Length -gt 200) { $line = $line.Substring(0, 200) }
        Write-Output ($_.ProcessId.ToString() + " :: " + $line)
    }

Write-Output "--- comfy installs on this box ---"
foreach ($p in @('G:\ComfyUI_LTX25', 'G:\ComfyUI_CLEANTEST', 'G:\ComfyUI_H3')) {
    if (Test-Path $p) { Write-Output ("present: " + $p) }
}
