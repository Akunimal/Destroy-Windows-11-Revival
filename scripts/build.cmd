@echo off
setlocal
echo ========================================
echo DWS 11 Enterprise v2.0 - Build Script
echo ========================================
echo.

if exist "%USERPROFILE%\.dotnet\dotnet.exe" (
    set "PATH=%USERPROFILE%\.dotnet;%PATH%"
)

cd /d "%~dp0.."

echo [1/2] Restaurando paquetes con .NET 8 SDK...
dotnet restore DWS.Enterprise.sln
if errorlevel 1 (
    echo ERROR: Failed to restore packages
    exit /b 1
)

echo.
echo [2/2] Compilando DWS.Enterprise.sln en Release...
dotnet build DWS.Enterprise.sln -c Release --no-restore
if errorlevel 1 (
    echo ERROR: Build failed
    exit /b 1
)

echo.
echo ========================================
echo Build completado exitosamente (0 errores)
echo ========================================
echo Binarios generados en:
echo   - src\DWS.GUI\bin\Release\net8.0-windows\
echo   - src\DWS.Worker\bin\Release\net8.0-windows\
echo.
