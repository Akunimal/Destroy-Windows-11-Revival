@echo off
cd /d "%~dp0dist"
for %%F in (DWS11-Enterprise-*.exe) do (
    start "" "%%F"
    goto :done
)
echo No se encontro ningun ejecutable en dist\
pause
:done
