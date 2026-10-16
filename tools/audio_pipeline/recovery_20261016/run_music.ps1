$o = "audio-recovery-agent music-gen (SAO, ~10 min)"
powershell -ExecutionPolicy Bypass -File C:\Otherreach_Recovery\gpu_lock.ps1 acquire $o
try { & python -P C:\Otherreach_Recovery\asset_work\audio_recovery\tools\gen_music.py C:\Otherreach_Recovery\asset_work\audio_recovery }
finally { powershell -ExecutionPolicy Bypass -File C:\Otherreach_Recovery\gpu_lock.ps1 release $o }
