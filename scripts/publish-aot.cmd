@echo off
setlocal
echo ========================================
echo DWS 11 Enterprise v2.0 - AOT Publish
echo ========================================
echo.

if exist "%USERPROFILE%\.dotnet\dotnet.exe" (
    set "PATH=%USERPROFILE%\.dotnet;%PATH%"
)

cd /d "%~dp0.."

echo [1/2] Publicando GUI con Native AOT (win-x64)...
dotnet publish src\DWS.GUI\DWS.GUI.csproj ^
    -c Release ^
    -r win-x64 ^
    --self-contained true ^
    -p:PublishAot=true ^
    -p:PublishSingleFile=true ^
    -o publish\gui

if errorlevel 1 (
    echo ERROR: GUI publish failed
    exit /b 1
)

echo.
echo [2/2] Publicando Worker con Native AOT (win-x64)...
dotnet publish src\DWS.Worker\DWS.Worker.csproj ^
    -c Release ^
    -r win-x64 ^
    --self-contained true ^
    -p:PublishAot=true ^
    -p:PublishSingleFile=true ^
    -o publish\worker

if errorlevel 1 (
    echo ERROR: Worker publish failed
    exit /b 1
)

echo.
echo ========================================
echo Publicacion Native AOT completada con exito
echo ========================================
echo Binarios AOT generados en:
echo   - publish\gui\DWS.GUI.exe
echo   - publish\worker\DWS.Worker.exe
echo.
