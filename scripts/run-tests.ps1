$ErrorActionPreference = "Stop"
$env:DOTNET_ROOT = "$env:USERPROFILE\.dotnet"
$env:PATH = "$env:USERPROFILE\.dotnet;" + $env:PATH
Set-Location "$PSScriptRoot\.."

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "DWS 11 Enterprise v2.0 - Automated Tests" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

Write-Host "[1/3] Adding test project to solution..." -ForegroundColor Yellow
& dotnet sln DWS.Enterprise.sln add tests/DWS.Tests/DWS.Tests.csproj 2>$null

Write-Host "[2/3] Restoring test dependencies..." -ForegroundColor Yellow
& dotnet restore tests/DWS.Tests/DWS.Tests.csproj --nologo -v quiet

Write-Host "[3/3] Running xUnit Test Suite..." -ForegroundColor Yellow
& dotnet test tests/DWS.Tests/DWS.Tests.csproj --nologo -v normal

if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: Tests failed with exit code $LASTEXITCODE" -ForegroundColor Red
    exit $LASTEXITCODE
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host "All DWS 11 Enterprise Tests Passed! (100%)" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
