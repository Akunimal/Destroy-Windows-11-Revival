@echo off
REM ===========================================================================
REM  Build Destroy-Windows-11-Revival (Release) on Windows.
REM
REM  Requirements: any recent .NET SDK or Visual Studio / Build Tools MSBuild.
REM  The .NET Framework 4.0 targeting pack is pulled automatically from NuGet.
REM ===========================================================================
setlocal

set MSBUILD_CMD=
set HAS_SDK=
for /f "delims=" %%S in ('dotnet --list-sdks 2^>nul') do set HAS_SDK=1
if defined HAS_SDK (
  set MSBUILD_CMD=dotnet msbuild
)

if not defined MSBUILD_CMD (
  for %%P in (
    "%ProgramFiles(x86)%\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe"
    "%ProgramFiles%\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe"
    "%ProgramFiles(x86)%\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe"
    "%ProgramFiles%\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe"
    "%ProgramFiles(x86)%\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe"
    "%ProgramFiles%\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe"
    "%ProgramFiles(x86)%\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe"
    "%ProgramFiles%\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe"
  ) do (
    if exist "%%~P" (
      set MSBUILD_CMD="%%~P"
      goto :found_msbuild
    )
  )
)

:found_msbuild
if not defined MSBUILD_CMD (
  echo ERROR: Neither the .NET SDK nor Visual Studio MSBuild was found.
  echo Install a .NET SDK or VS Build Tools and retry.
  exit /b 1
)

echo [1/2] Restoring NuGet packages with %MSBUILD_CMD%...
%MSBUILD_CMD% "%~dp0DWS\DWS_Lite.csproj" /t:Restore
if errorlevel 1 exit /b 1

echo [2/2] Building Release...
%MSBUILD_CMD% "%~dp0DWS\DWS_Lite.csproj" /p:Configuration=Release
if errorlevel 1 exit /b 1

echo.
echo Done. Binary: DWS\bin\Release\DWS_Lite.exe
exit /b 0
