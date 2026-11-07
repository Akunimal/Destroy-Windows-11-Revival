$repoDir = Resolve-Path "$PSScriptRoot\.."
$workerDir = Join-Path $repoDir "publish\worker"
$guiDir = Join-Path $repoDir "publish\gui"

# Check if Worker is running
$workerProc = Get-Process -Name "DWS.Worker" -ErrorAction SilentlyContinue
if (-not $workerProc) {
    Start-Process -FilePath (Join-Path $workerDir "DWS.Worker.exe") -WorkingDirectory $workerDir -WindowStyle Hidden
    Start-Sleep -Seconds 2
}

# Check if GUI is running
$guiProc = Get-Process -Name "DWS.GUI" -ErrorAction SilentlyContinue
if (-not $guiProc) {
    Start-Process -FilePath (Join-Path $guiDir "DWS.GUI.exe") -WorkingDirectory $guiDir
    Start-Sleep -Seconds 1
}

# Test health
try {
    $health = Invoke-RestMethod -Uri "http://localhost:5123/health" -Method Get -TimeoutSec 5
    Write-Host "SUCCESS_HEALTH: status=$($health.status), os=$($health.os)"
} catch {
    Write-Host "FAIL_HEALTH: $_"
}

# Test dry-run
try {
    $body = @{ dryRun = $true; profile = "default" } | ConvertTo-Json
    $result = Invoke-RestMethod -Uri "http://localhost:5123/api/rules/apply" -Method Post -Body $body -ContentType "application/json" -TimeoutSec 30
    Write-Host "SUCCESS_DRYRUN: applied=$($result.appliedRules)/$($result.totalRules), tx=$($result.transactionId)"
} catch {
    Write-Host "FAIL_DRYRUN: $_"
}

$running = Get-Process -Name "DWS.Worker", "DWS.GUI" -ErrorAction SilentlyContinue | Select-Object -Property Id, ProcessName
Write-Host "RUNNING_PROCESSES: $($running | ConvertTo-Json -Compress)"
