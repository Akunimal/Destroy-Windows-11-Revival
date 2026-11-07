$repoDir = Resolve-Path "$PSScriptRoot\.."
$workerDir = Join-Path $repoDir "publish\worker"
$guiDir = Join-Path $repoDir "publish\gui"

# Stop existing
Get-Process -Name "DWS.Worker", "DWS.GUI" -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 1

# Launch Worker
Write-Host "Starting DWS.Worker..."
Start-Process -FilePath (Join-Path $workerDir "DWS.Worker.exe") -WorkingDirectory $workerDir -WindowStyle Hidden
Start-Sleep -Seconds 2

# Verify Worker
try {
    $health = Invoke-RestMethod -Uri "http://localhost:5123/health" -TimeoutSec 5
    Write-Host "Worker OK: status=$($health.status), os=$($health.os)"
} catch {
    Write-Host "Worker check failed: $_"
}

# Launch GUI via Explorer on Default Desktop
Write-Host "Starting DWS.GUI on interactive desktop..."
Start-Process "explorer.exe" -ArgumentList "`"$guiDir\DWS.GUI.exe`""
Start-Sleep -Seconds 2

$gui = Get-Process -Name "DWS.GUI" -ErrorAction SilentlyContinue
if ($gui) {
    Write-Host "GUI started: PID=$($gui.Id), Responding=$($gui.Responding)"
}
