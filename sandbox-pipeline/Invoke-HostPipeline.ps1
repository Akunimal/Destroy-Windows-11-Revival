$ErrorActionPreference = "Continue"

$BaseDir      = "D:\WORK\DWS 11\Destroy-Windows-10-Spying\sandbox-pipeline"
$WsbPath      = "$BaseDir\DWS-Verification.wsb"
$ArtifactsDir = "$BaseDir\artifacts"

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " LANZANDO PIPELINE EN WINDOWS SANDBOX PARA DWS 11 v2.1.1" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "Configuracion WSB: $WsbPath" -ForegroundColor Gray
Write-Host "Carpeta de Artefactos: $ArtifactsDir" -ForegroundColor Gray
Write-Host ""

Write-Host "[1/3] Iniciando Windows Sandbox..." -ForegroundColor Yellow
Start-Process -FilePath "C:\Windows\System32\WindowsSandbox.exe" -ArgumentList "`"$WsbPath`""

Write-Host "[2/3] Sandbox iniciado. Esperando interaccion del usuario dentro del Sandbox..." -ForegroundColor Cyan
Write-Host "  (El Sandbox ejecutara primero /SILENT-AUDIT y luego abrira la GUI)" -ForegroundColor DarkGray
Write-Host ""

$CompletedFlag = $null
while ($null -eq $CompletedFlag) {
    Start-Sleep -Seconds 3
    $CompletedFlag = Get-ChildItem -Path $ArtifactsDir -Filter "_PIPELINE_COMPLETED.flag" -Recurse -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1

    if (-not (Get-Process -Name "WindowsSandboxClient" -ErrorAction SilentlyContinue)) {
        Write-Host "[AVISO] La ventana de Windows Sandbox se cerro." -ForegroundColor Yellow
        break
    }
}

Write-Host ""
Write-Host "[3/3] Comprobando resultados exportados..." -ForegroundColor Green

$LatestRun = Get-ChildItem -Path $ArtifactsDir -Directory | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if ($LatestRun) {
    Write-Host "Reportes disponibles en: $($LatestRun.FullName)" -ForegroundColor Cyan
    $SummaryFile = Join-Path $LatestRun.FullName "00_Pipeline_Summary.json"
    if (Test-Path $SummaryFile) {
        Write-Host "`n--- RESUMEN DE EJECUCION ---" -ForegroundColor Green
        Get-Content $SummaryFile
    }
}
