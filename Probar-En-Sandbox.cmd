@echo off
cd /d "%~dp0"
echo Iniciando Pipeline de Verificacion en Windows Sandbox...
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "sandbox-pipeline\Invoke-HostPipeline.ps1"
pause
