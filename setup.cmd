@echo off
chcp 65001 >nul
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0setup.ps1"
echo.
echo Nazhmi lyubuyu klavishu, chtoby zakryt eto okno...
pause >nul
