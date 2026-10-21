$ErrorActionPreference = 'Stop'

Write-Host ""
Write-Host "=====================================" -ForegroundColor Cyan
Write-Host "  Protoptera Player — сборка/обновление" -ForegroundColor Cyan
Write-Host "=====================================" -ForegroundColor Cyan
Write-Host ""

# 0. Проверяем dotnet
try { $ver = & dotnet --version } catch { $ver = $null }
if (-not $ver) {
    Write-Host "[X] Не найден dotnet." -ForegroundColor Red
    Write-Host "    Установи .NET 8 SDK, ЗАКРОЙ это окно и запусти setup.cmd заново:"
    Write-Host "    winget install Microsoft.DotNet.SDK.8"
    Write-Host "    или https://dotnet.microsoft.com/download/dotnet/8.0"
    return
}
Write-Host "[OK] dotnet $ver" -ForegroundColor Green

# 1. Ищем проект (в этой папке или вложенной)
$proj = Get-ChildItem -Path $PSScriptRoot -Recurse -Filter 'ProtopteraPlayer.csproj' -File -ErrorAction SilentlyContinue |
        Select-Object -First 1
if (-not $proj) {
    Write-Host "[X] Рядом нет ProtopteraPlayer.csproj." -ForegroundColor Red
    Write-Host "    Положи setup.cmd/setup.ps1 в папку проекта и запусти снова."
    return
}
$projDir    = $proj.Directory.FullName
$publishDir = Join-Path $projDir 'publish'
Write-Host "[OK] Проект найден: $projDir" -ForegroundColor Green

# 2. Гасим запущенные экземпляры (и новый, и старый MiniMp3) — иначе файлы заняты
Get-Process 'ProtopteraPlayer' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Get-Process 'MiniMp3'          -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

# 3. Сборка
Write-Host ""
Write-Host "Собираю... (первый раз качаются пакеты, до минуты)" -ForegroundColor Cyan
& dotnet publish $proj.FullName -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o $publishDir
if ($LASTEXITCODE -ne 0) {
    Write-Host "[X] dotnet publish завершился с ошибкой ($LASTEXITCODE) — смотри вывод выше." -ForegroundColor Red
    return
}
$builtExe = Join-Path $publishDir 'ProtopteraPlayer.exe'
if (-not (Test-Path $builtExe)) {
    Write-Host "[X] exe не собрался." -ForegroundColor Red
    return
}
Write-Host "[OK] Собрано" -ForegroundColor Green

# 4. Копируем в %LOCALAPPDATA%\ProtopteraPlayer
$installDir = Join-Path $env:LOCALAPPDATA 'ProtopteraPlayer'
New-Item -ItemType Directory -Force -Path $installDir | Out-Null
Copy-Item (Join-Path $publishDir '*') -Destination $installDir -Recurse -Force

# Кладём деинсталлятор рядом с exe (нужен для "Установленных приложений")
$uninstPs  = Get-ChildItem -Path $PSScriptRoot -Recurse -Filter 'uninstall.ps1' -File -ErrorAction SilentlyContinue | Select-Object -First 1
$uninstCmd = Get-ChildItem -Path $PSScriptRoot -Recurse -Filter 'uninstall.cmd' -File -ErrorAction SilentlyContinue | Select-Object -First 1
if ($uninstPs)  { Copy-Item $uninstPs.FullName  -Destination $installDir -Force }
if ($uninstCmd) { Copy-Item $uninstCmd.FullName -Destination $installDir -Force }

$exe = Join-Path $installDir 'ProtopteraPlayer.exe'
Write-Host "[OK] Установлено в: $exe" -ForegroundColor Green

# 5. Регистрируем ассоциации с .mp3 / .flac / .wav (для текущего пользователя, без админа)
$progId  = 'ProtopteraPlayer.AudioFile'
$classes = 'HKCU:\Software\Classes'
New-Item -Force -Path "$classes\$progId\shell\open\command" | Out-Null
Set-ItemProperty -Path "$classes\$progId"                    -Name '(default)' -Value 'Audio (Protoptera Player)'
Set-ItemProperty -Path "$classes\$progId\shell\open\command" -Name '(default)' -Value ('"{0}" "%1"' -f $exe)
New-Item -Force -Path "$classes\$progId\DefaultIcon" | Out-Null
Set-ItemProperty -Path "$classes\$progId\DefaultIcon"        -Name '(default)' -Value ('{0},0' -f $exe)
foreach ($ext in '.mp3', '.flac', '.wav') {
    New-Item -Force -Path "$classes\$ext\OpenWithProgids" | Out-Null
    New-ItemProperty -Force -Path "$classes\$ext\OpenWithProgids" -Name $progId -Value ([byte[]]@()) -PropertyType None | Out-Null
}
# Регистрируем как приложение — чтобы Windows предлагала его в «Открыть с помощью» для всех трёх форматов
$app = "$classes\Applications\ProtopteraPlayer.exe"
New-Item -Force -Path "$app\shell\open\command" | Out-Null
Set-ItemProperty -Path "$app" -Name 'FriendlyAppName' -Value 'Protoptera Player'
Set-ItemProperty -Path "$app\shell\open\command" -Name '(default)' -Value ('"{0}" "%1"' -f $exe)
New-Item -Force -Path "$app\SupportedTypes" | Out-Null
foreach ($ext in '.mp3', '.flac', '.wav') {
    New-ItemProperty -Force -Path "$app\SupportedTypes" -Name $ext -Value '' -PropertyType String | Out-Null
}
# Capabilities + RegisteredApplications — тогда в Параметрах -> Приложения по умолчанию
# у плеера есть своя страница с кнопкой «Сделать по умолчанию» для всех форматов разом
$caps = 'HKCU:\Software\ProtopteraPlayer\Capabilities'
New-Item -Force -Path "$caps\FileAssociations" | Out-Null
Set-ItemProperty -Path $caps -Name 'ApplicationName'        -Value 'Protoptera Player'
Set-ItemProperty -Path $caps -Name 'ApplicationDescription' -Value 'Плеер mp3 / flac / wav'
Set-ItemProperty -Path $caps -Name 'ApplicationIcon'        -Value ('{0},0' -f $exe)
foreach ($ext in '.mp3', '.flac', '.wav') {
    Set-ItemProperty -Path "$caps\FileAssociations" -Name $ext -Value $progId
}
New-Item -Force -Path 'HKCU:\Software\RegisteredApplications' | Out-Null
Set-ItemProperty -Path 'HKCU:\Software\RegisteredApplications' -Name 'Protoptera Player' -Value 'Software\ProtopteraPlayer\Capabilities'
# Сообщаем проводнику, что ассоциации поменялись (обновит иконки файлов)
Add-Type -Namespace Win32 -Name Shell -MemberDefinition '[DllImport("shell32.dll")] public static extern void SHChangeNotify(int e, int f, IntPtr a, IntPtr b);'
[Win32.Shell]::SHChangeNotify(0x08000000, 0, [IntPtr]::Zero, [IntPtr]::Zero)
Write-Host "[OK] Ассоциации с mp3 / flac / wav зарегистрированы" -ForegroundColor Green

# 6. Регистрируем в "Установленные приложения" (чтобы можно было удалять штатно)
$arp = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\ProtopteraPlayer'
$uninstallCmd = ('powershell.exe -NoProfile -ExecutionPolicy Bypass -File "{0}"' -f (Join-Path $installDir 'uninstall.ps1'))
New-Item -Force -Path $arp | Out-Null
Set-ItemProperty -Path $arp -Name 'DisplayName'      -Value 'Protoptera Player'
Set-ItemProperty -Path $arp -Name 'DisplayVersion'   -Value '1.2.0'
Set-ItemProperty -Path $arp -Name 'Publisher'        -Value 'Protoptera'
Set-ItemProperty -Path $arp -Name 'InstallLocation'  -Value $installDir
Set-ItemProperty -Path $arp -Name 'DisplayIcon'      -Value ('{0},0' -f $exe)
Set-ItemProperty -Path $arp -Name 'UninstallString'      -Value $uninstallCmd
Set-ItemProperty -Path $arp -Name 'QuietUninstallString' -Value $uninstallCmd
New-ItemProperty -Force -Path $arp -Name 'NoModify' -Value 1 -PropertyType DWord | Out-Null
New-ItemProperty -Force -Path $arp -Name 'NoRepair' -Value 1 -PropertyType DWord | Out-Null
Write-Host "[OK] Добавлено в 'Установленные приложения'" -ForegroundColor Green

# 7. Подчищаем старую версию MiniMp3 (если была)
try {
    Remove-Item "HKCU:\Software\Classes\MiniMp3.AudioFile" -Recurse -Force -ErrorAction SilentlyContinue
    Remove-ItemProperty "HKCU:\Software\Classes\.mp3\OpenWithProgids" -Name 'MiniMp3.AudioFile' -ErrorAction SilentlyContinue
    Remove-Item "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\MiniMp3" -Recurse -Force -ErrorAction SilentlyContinue
    $oldDir = Join-Path $env:LOCALAPPDATA 'MiniMp3'
    if (Test-Path $oldDir) { Remove-Item $oldDir -Recurse -Force -ErrorAction SilentlyContinue }
    Write-Host "[OK] Старая версия MiniMp3 удалена" -ForegroundColor Green
} catch { }

# 8. Запускаем плеер для проверки
Start-Process $exe

Write-Host ""
Write-Host "================ ГОТОВО ================" -ForegroundColor Green
Write-Host ""
Write-Host "Плеер обновлён и уже открылся — перетащи в него mp3, flac или wav." -ForegroundColor White
Write-Host ""
Write-Host "Открываю Параметры -> Приложения по умолчанию -> Protoptera Player." -ForegroundColor Yellow
Write-Host "Нажми там кнопку 'Сделать по умолчанию' (Set default) — сразу для mp3, flac и wav." -ForegroundColor Yellow
Write-Host "(Windows не даёт программам назначать себя по умолчанию сами — нужен один клик.)" -ForegroundColor DarkGray
Start-Process 'ms-settings:defaultapps?registeredAppUser=Protoptera%20Player'
Write-Host ""
Write-Host "Удалить потом можно штатно: Параметры -> Приложения -> Protoptera Player." -ForegroundColor DarkGray
