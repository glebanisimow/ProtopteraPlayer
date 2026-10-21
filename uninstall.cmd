@echo off
chcp 65001 >nul
echo Udalenie Protoptera Player...
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0uninstall.ps1"
