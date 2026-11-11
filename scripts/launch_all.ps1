$repoDir = Resolve-Path "$PSScriptRoot\.."
$distDir = Join-Path $repoDir "dist"

# Find single-file executable in dist
$exe = Get-ChildItem -Path $distDir -Filter "DWS11-Enterprise-*.exe" | Select-Object -First 1

if (-not $exe) {
    Write-Host "No se encontro ningun ejecutable en dist\. Ejecute scripts\publish.cmd primero." -ForegroundColor Red
    exit 1
}

Write-Host "Iniciando $($exe.Name)..." -ForegroundColor Cyan
Start-Process -FilePath $exe.FullName -WorkingDirectory $distDir
