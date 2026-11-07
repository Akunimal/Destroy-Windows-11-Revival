@echo off
setlocal
echo ========================================
echo DWS 11 Enterprise v2.0 - Automated Tests
echo ========================================
echo.

if exist "%USERPROFILE%\.dotnet\dotnet.exe" (
    set "PATH=%USERPROFILE%\.dotnet;%PATH%"
)

cd /d "%~dp0.."

echo [1/3] Adding test project to solution if needed...
dotnet sln DWS.Enterprise.sln add tests\DWS.Tests\DWS.Tests.csproj >nul 2>&1

echo [2/3] Restoring test dependencies...
dotnet restore tests\DWS.Tests\DWS.Tests.csproj --nologo -v quiet
if errorlevel 1 (
    echo ERROR: Failed to restore test dependencies
    exit /b 1
)

echo [3/3] Running xUnit Test Suite...
dotnet test tests\DWS.Tests\DWS.Tests.csproj --nologo -v normal
if errorlevel 1 (
    echo ERROR: One or more tests failed!
    exit /b 1
)

echo.
echo ========================================
echo All DWS 11 Enterprise Tests Passed! (100%%)
echo ========================================
