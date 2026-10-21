# Protoptera Player - uninstaller.
# Removes ProgID, .mp3/.flac/.wav open-with entries, the Add/Remove-Programs record and the install folder.
$ErrorActionPreference = 'SilentlyContinue'

$progId = 'ProtopteraPlayer.AudioFile'

# 1. Close the app if running.
Get-Process 'ProtopteraPlayer' | Stop-Process -Force

# 2. Remove file-type registration.
Remove-Item "HKCU:\Software\Classes\$progId" -Recurse -Force
foreach ($ext in '.mp3', '.flac', '.wav') {
    Remove-ItemProperty "HKCU:\Software\Classes\$ext\OpenWithProgids" -Name $progId
}
Remove-Item "HKCU:\Software\Classes\Applications\ProtopteraPlayer.exe" -Recurse -Force
Remove-Item "HKCU:\Software\ProtopteraPlayer" -Recurse -Force
Remove-ItemProperty "HKCU:\Software\RegisteredApplications" -Name 'Protoptera Player'

# 3. Remove the Add/Remove Programs (Installed apps) record.
Remove-Item "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\ProtopteraPlayer" -Recurse -Force

# 4. Delete the install folder AFTER this script exits (it lives inside that folder).
$dir = $PSScriptRoot
if ($dir -and (Test-Path $dir)) {
    Start-Process -FilePath 'cmd.exe' `
                  -WorkingDirectory $env:TEMP `
                  -WindowStyle Hidden `
                  -ArgumentList '/c', ('timeout /t 2 >nul & rmdir /s /q "{0}"' -f $dir)
}
