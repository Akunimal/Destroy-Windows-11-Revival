@echo off
REM ===========================================================================
REM  Build DWS 11 Enterprise v2.0 (Release) on Windows.
REM  Modern .NET 8.0 SDK (Native AOT, Avalonia UI, Kestrel Minimal APIs)
REM ===========================================================================
setlocal

if exist "%USERPROFILE%\.dotnet\dotnet.exe" (
    set "PATH=%USERPROFILE%\.dotnet;%PATH%"
)

cd /d "%~dp0"

echo ===================================================
echo [1/2] Restaurando dependencias NuGet para .NET 8...
echo ===================================================
dotnet restore DWS.Enterprise.sln
if errorlevel 1 (
    echo ERROR: Restore failed.
    exit /b 1
)

echo.
echo ===================================================
echo [2/2] Compilando DWS.Enterprise.sln en modo Release...
echo ===================================================
dotnet build DWS.Enterprise.sln -c Release --no-restore
if errorlevel 1 (
    echo ERROR: Build failed.
    exit /b 1
)

echo.
echo ===================================================
echo Compilacion exitosa.
echo Binarios generados:
echo   - src\DWS.GUI\bin\Release\net8.0-windows\DWS.GUI.exe
echo   - src\DWS.Worker\bin\Release\net8.0-windows\DWS.Worker.exe
echo ===================================================
