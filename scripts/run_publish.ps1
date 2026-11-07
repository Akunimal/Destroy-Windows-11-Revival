$ErrorActionPreference = "Stop"
$env:DOTNET_ROOT = "$env:USERPROFILE\.dotnet"
$env:PATH = "$env:USERPROFILE\.dotnet;" + $env:PATH
Set-Location "$PSScriptRoot\.."

Get-Process -Name DWS.GUI, DWS.Worker -ErrorAction SilentlyContinue | Stop-Process -Force

Write-Host "Publishing DWS.GUI (Native AOT)..." -ForegroundColor Cyan
& dotnet publish src/DWS.GUI/DWS.GUI.csproj -c Release -r win-x64 --self-contained true -p:PublishAot=true -p:PublishSingleFile=false -o publish/gui --nologo
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "Publishing DWS.Worker (Native AOT)..." -ForegroundColor Cyan
& dotnet publish src/DWS.Worker/DWS.Worker.csproj -c Release -r win-x64 --self-contained true -p:PublishAot=true -p:PublishSingleFile=false -o publish/worker --nologo
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Copy-Item "rules/rules.json" "publish/gui/rules.json" -Force
Copy-Item "rules/rules.json" "publish/worker/rules.json" -Force
New-Item -ItemType Directory -Force -Path "publish/gui/rules" | Out-Null
New-Item -ItemType Directory -Force -Path "publish/worker/rules" | Out-Null
Copy-Item "rules/rules.json" "publish/gui/rules/rules.json" -Force
Copy-Item "rules/rules.json" "publish/worker/rules/rules.json" -Force

Write-Host "Native AOT Publish Complete!" -ForegroundColor Green
