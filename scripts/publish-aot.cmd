@echo off
setlocal
echo ========================================
echo DWS 11 Enterprise v2.0 - AOT Publish
echo ========================================
echo.

if exist "%USERPROFILE%\.dotnet\dotnet.exe" (
    set "DOTNET_ROOT=%USERPROFILE%\.dotnet"
    set "PATH=%USERPROFILE%\.dotnet;%PATH%"
)

cd /d "%~dp0.."

echo Deteniendo instancias previas de DWS...
taskkill /f /im DWS.GUI.exe >nul 2>&1
taskkill /f /im DWS.Worker.exe >nul 2>&1

echo [1/3] Publicando GUI con Native AOT (win-x64)...
dotnet publish src\DWS.GUI\DWS.GUI.csproj ^
    -c Release ^
    -r win-x64 ^
    --self-contained true ^
    -p:PublishAot=true ^
    -p:PublishSingleFile=false ^
    -o publish\gui

if errorlevel 1 (
    echo ERROR: GUI publish failed
    exit /b 1
)

echo.
echo [2/3] Publicando Worker con Native AOT (win-x64)...
dotnet publish src\DWS.Worker\DWS.Worker.csproj ^
    -c Release ^
    -r win-x64 ^
    --self-contained true ^
    -p:PublishAot=true ^
    -p:PublishSingleFile=false ^
    -o publish\worker

if errorlevel 1 (
    echo ERROR: Worker publish failed
    exit /b 1
)

echo.
echo [3/3] Empaquetando distribucion unificada con dependencias nativas...
if not exist "publish\bundle" mkdir "publish\bundle"
copy /y "publish\gui\DWS.GUI.exe" "publish\bundle\" >nul
copy /y "publish\gui\*.dll" "publish\bundle\" >nul
copy /y "publish\worker\DWS.Worker.exe" "publish\bundle\" >nul
copy /y "rules\rules.json" "publish\bundle\" >nul
copy /y "rules\rules.json.sig" "publish\bundle\" >nul

powershell -NoProfile -Command "Compress-Archive -Path 'publish\bundle\*' -DestinationPath 'publish\DWS-11-Enterprise-v2.0.1-win-x64.zip' -Force"

echo.
echo ========================================
echo Publicacion Native AOT completada con exito
echo ========================================
echo Binarios AOT y dependencias unificadas en:
echo   - publish\bundle\DWS.GUI.exe
echo   - publish\bundle\DWS.Worker.exe
echo   - publish\bundle\libSkiaSharp.dll
echo   - publish\bundle\libHarfBuzzSharp.dll
echo   - publish\bundle\av_libglesv2.dll
echo   - publish\bundle\rules.json
echo   - publish\bundle\rules.json.sig
echo   - publish\DWS-11-Enterprise-v2.0.1-win-x64.zip
echo.
