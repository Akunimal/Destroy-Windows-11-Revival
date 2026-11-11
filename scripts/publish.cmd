@echo off
setlocal enabledelayedexpansion

echo ========================================================
echo DWS 11 Enterprise - Publish Single-File GUI
echo ========================================================
echo.

if exist "%USERPROFILE%\.dotnet\dotnet.exe" (
    set "DOTNET_ROOT=%USERPROFILE%\.dotnet"
    set "PATH=%USERPROFILE%\.dotnet;%PATH%"
)

cd /d "%~dp0.."

:: Read Version from Directory.Build.props
set "VERSION="
for /f "tokens=3 delims=<>" %%a in ('findstr /i "<Version>" Directory.Build.props') do (
    set "VERSION=%%a"
)
if "%VERSION%"=="" set "VERSION=2.1.0"

echo Detectada version objetivo: v%VERSION%
echo.

echo Deteniendo instancias previas de DWS...
taskkill /f /im DWS.GUI.exe >nul 2>&1
taskkill /f /im DWS11-Enterprise-*.exe >nul 2>&1

echo [1/3] Limpiando carpetas de salida...
if exist "publish\singlefile" rmdir /s /q "publish\singlefile"
if not exist "dist" mkdir "dist"

echo [2/3] Publicando DWS.GUI en Self-Contained Single-File (win-x64)...
dotnet publish src\DWS.GUI\DWS.GUI.csproj ^
    -c Release ^
    -r win-x64 ^
    --self-contained true ^
    -p:PublishSingleFile=true ^
    -p:IncludeNativeLibrariesForSelfExtract=true ^
    -p:EnableCompressionInSingleFile=true ^
    -p:PublishTrimmed=false ^
    -p:PublishAot=false ^
    -o publish\singlefile

if errorlevel 1 (
    echo ERROR: Fallo al publicar DWS.GUI.
    exit /b 1
)

echo.
echo [3/3] Generando binario final en dist\ y calculando SHA-256...
set "OUT_EXE=dist\DWS11-Enterprise-v%VERSION%-win-x64.exe"
set "OUT_SHA=dist\DWS11-Enterprise-v%VERSION%-win-x64.exe.sha256"

copy /y "publish\singlefile\DWS.GUI.exe" "%OUT_EXE%" >nul
if errorlevel 1 (
    echo ERROR: Fallo al copiar el ejecutable a %OUT_EXE%
    exit /b 1
)

powershell -NoProfile -Command "(Get-FileHash -Algorithm SHA256 '%OUT_EXE%').Hash.ToLower() + '  ' + [System.IO.Path]::GetFileName('%OUT_EXE%')" > "%OUT_SHA%"

echo.
echo ========================================================
echo Publicacion completada con exito
echo ========================================================
echo Artefactos generados:
echo   - %OUT_EXE%
echo   - %OUT_SHA%
echo.
