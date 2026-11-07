$ErrorActionPreference = "Stop"
$env:DOTNET_ROOT = "$env:USERPROFILE\.dotnet"
$env:PATH = "$env:USERPROFILE\.dotnet;" + $env:PATH
Set-Location "$PSScriptRoot\.."

Write-Host "Compiling DWS.Enterprise.sln (Release)..." -ForegroundColor Cyan
& dotnet build DWS.Enterprise.sln -c Release --nologo

if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed with exit code $LASTEXITCODE" -ForegroundColor Red
    exit $LASTEXITCODE
}

Write-Host "Build Succeeded (0 Errors)" -ForegroundColor Green
