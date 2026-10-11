@echo off
REM ===========================================================================
REM  Build Destroy-Windows-11-Revival (Release) on Windows.
REM
REM  Requirements: any recent .NET SDK (it provides the Roslyn compiler and
REM  MSBuild). The .NET Framework 4.0 targeting pack is pulled automatically
REM  from NuGet, so no Visual Studio or extra SDK is needed.
REM ===========================================================================
setlocal

set DOTNET=dotnet
where %DOTNET% >nul 2>&1
if errorlevel 1 (
  echo ERROR: the .NET SDK ^(dotnet^) was not found on PATH.
  echo Install it from https://dotnet.microsoft.com/download and retry.
  exit /b 1
)

echo [1/2] Restoring NuGet packages...
"%DOTNET%" msbuild DWS\DWS_Lite.csproj /t:Restore
if errorlevel 1 exit /b 1

echo [2/2] Building Release...
"%DOTNET%" msbuild DWS\DWS_Lite.csproj /p:Configuration=Release
if errorlevel 1 exit /b 1

echo.
echo Done. Binary: DWS\bin\Release\DWS_Lite.exe
exit /b 0
