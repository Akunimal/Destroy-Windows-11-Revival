#Requires -RunAsAdministrator
$ErrorActionPreference = "Continue"

# --- CONFIGURACION DE RUTAS ---
$RunId          = Get-Date -Format "yyyyMMdd-HHmmss"
$ArtifactDir    = "C:\DWS_Artifacts\Run_$RunId"
$IsolatedDir    = "C:\DWS_Isolated"
$ProgramDataDWS = "$env:ProgramData\DWS"

New-Item -ItemType Directory -Force -Path $ArtifactDir, $IsolatedDir | Out-Null
Start-Transcript -Path "$ArtifactDir\Sandbox_Execution_Transcript.log" -Force

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " DWS 11 ENTERPRISE - SANDBOX VERIFICATION PIPELINE" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

# --- PRE-FLIGHT: NEUTRALIZAR SMART APP CONTROL (SAC) E IMPORTAR ROOT CA ---
Write-Host "`n[*] Configurando Code Integrity y Certificate Stores para Smart App Control..." -ForegroundColor Yellow

try {
    # 1. Desactivar Smart App Control en el registro de CI
    $sacRegPath = "HKLM:\SYSTEM\CurrentControlSet\Control\CI\Policy"
    if (-not (Test-Path $sacRegPath)) { New-Item -Path $sacRegPath -Force | Out-Null }
    Set-ItemProperty -Path $sacRegPath -Name "VerifiedAndReputablePolicyState" -Value 0 -Type DWord -Force
    Write-Host " -> VerifiedAndReputablePolicyState = 0 aplicado." -ForegroundColor Green
} catch {
    Write-Host " -> Advertencia al ajustar CI Policy: $_" -ForegroundColor DarkGray
}

try {
    # 2. Refrescar politicas de Code Integrity en caliente si CiTool esta presente
    $ciTool = "$env:SystemRoot\System32\CiTool.exe"
    if (Test-Path $ciTool) {
        & $ciTool --refresh -json 2>&1 | Out-Null
        Write-Host " -> CiTool --refresh ejecutado." -ForegroundColor Green
    }
} catch { }

try {
    # 3. Instalar certificado en LocalMachine\Root y LocalMachine\TrustedPublisher
    $sandboxCert = Get-ChildItem -Path "C:\DWS_Dist" -Filter "*.cer" | Select-Object -First 1
    if ($sandboxCert) {
        Import-Certificate -FilePath $sandboxCert.FullName -CertStoreLocation "Cert:\LocalMachine\Root" | Out-Null
        Import-Certificate -FilePath $sandboxCert.FullName -CertStoreLocation "Cert:\LocalMachine\TrustedPublisher" | Out-Null
        Write-Host " -> Certificado $($sandboxCert.Name) importado en Root y TrustedPublisher." -ForegroundColor Green
    } else {
        Write-Host " -> Certificado .cer no encontrado en C:\DWS_Dist." -ForegroundColor DarkGray
    }
} catch {
    Write-Host " -> Advertencia al importar certificado: $_" -ForegroundColor DarkGray
}

# --- FASE 0: PREPARACION Y AISLAMIENTO DEL BINARIO ---
Write-Host "`n[FASE 0] Copiando ejecutable al disco local del Sandbox..." -ForegroundColor Yellow
$ExeItem = Get-ChildItem -Path "C:\DWS_Dist" -Filter "DWS11-Enterprise-*.exe" | Sort-Object Name -Descending | Select-Object -First 1
if (-not $ExeItem) {
    $ExeItem = Get-ChildItem -Path "C:\DWS_Dist" -Filter "*.exe" | Select-Object -First 1
}
$ExeSource = $ExeItem.FullName
$ExeTarget = Join-Path $IsolatedDir $ExeItem.Name

Copy-Item -Path $ExeSource -Destination $ExeTarget -Force
Unblock-File -Path $ExeTarget -ErrorAction SilentlyContinue

$FileHash = (Get-FileHash -Path $ExeTarget -Algorithm SHA256).Hash
Write-Host " -> Binario aislado: $ExeTarget" -ForegroundColor Green
Write-Host " -> SHA256: $FileHash" -ForegroundColor Gray

# --- FUNCION DE CAPTURA DE ESTADO DEL SISTEMA ---
function Get-SystemTelemetryState {
    $State = [ordered]@{
        Timestamp = (Get-Date).ToString("o")
        Registry  = [ordered]@{}
        Services  = [ordered]@{}
    }

    $RegPaths = @(
        @{ Name = "AllowTelemetry_Policy"; Path = "HKLM:\SOFTWARE\Policies\Microsoft\Windows\DataCollection"; Value = "AllowTelemetry" },
        @{ Name = "DisableWindowsConsumerFeatures"; Path = "HKLM:\SOFTWARE\Policies\Microsoft\Windows\CloudContent"; Value = "DisableWindowsConsumerFeatures" },
        @{ Name = "DisableCortana"; Path = "HKLM:\SOFTWARE\Policies\Microsoft\Windows\Windows Search"; Value = "AllowCortana" },
        @{ Name = "AdvertisingInfo"; Path = "HKLM:\SOFTWARE\Policies\Microsoft\Windows\AdvertisingInfo"; Value = "DisabledByGroupPolicy" },
        @{ Name = "DisableWPBT"; Path = "HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager"; Value = "DisableWPBTExecution" }
    )

    foreach ($Item in $RegPaths) {
        if (Test-Path $Item.Path) {
            $Val = (Get-ItemProperty -Path $Item.Path -Name $Item.Value -ErrorAction SilentlyContinue).$($Item.Value)
            $State.Registry[$Item.Name] = if ($null -ne $Val) { $Val } else { "NOT_SET" }
        } else {
            $State.Registry[$Item.Name] = "KEY_MISSING"
        }
    }

    foreach ($SvcName in @("DiagTrack", "dmwappushservice", "WerSvc")) {
        $Svc = Get-Service -Name $SvcName -ErrorAction SilentlyContinue
        if ($Svc) {
            $State.Services[$SvcName] = @{
                Status    = $Svc.Status.ToString()
                StartType = $Svc.StartType.ToString()
            }
        } else {
            $State.Services[$SvcName] = "SERVICE_NOT_FOUND"
        }
    }
    return $State
}

# Capturar Baseline
$BaselineState = Get-SystemTelemetryState
$BaselineState | ConvertTo-Json -Depth 4 | Set-Content "$ArtifactDir\01_Baseline_State.json" -Encoding UTF8
Write-Host " -> Baseline del sistema capturado en 01_Baseline_State.json" -ForegroundColor Green

# --- FASE 1: PRUEBA HEADLESS (/SILENT-AUDIT) ---
Write-Host "`n[FASE 1] Ejecutando modo Headless (/SILENT-AUDIT)..." -ForegroundColor Yellow
$AuditStopwatch = [System.Diagnostics.Stopwatch]::StartNew()

$AuditProc = Start-Process -FilePath $ExeTarget `
    -ArgumentList "/SILENT-AUDIT" `
    -WorkingDirectory $IsolatedDir `
    -PassThru -Wait

$AuditStopwatch.Stop()
$AuditExitCode = $AuditProc.ExitCode

Write-Host " -> /SILENT-AUDIT finalizado en $($AuditStopwatch.ElapsedMilliseconds) ms con ExitCode: $AuditExitCode" -ForegroundColor $(if ($AuditExitCode -in 0, 2) { "Green" } else { "Red" })

# Verificar que el modo auditoria no muto el registro
$PostAuditState = Get-SystemTelemetryState
$PostAuditState | ConvertTo-Json -Depth 4 | Set-Content "$ArtifactDir\02_PostAudit_State.json" -Encoding UTF8

if (Test-Path $ProgramDataDWS) {
    New-Item -ItemType Directory -Force -Path "$ArtifactDir\SilentAudit_ProgramData" | Out-Null
    Copy-Item -Path "$ProgramDataDWS\*" -Destination "$ArtifactDir\SilentAudit_ProgramData" -Recurse -Force
    Write-Host " -> Logs y reportes de %ProgramData%\DWS respaldados al Host." -ForegroundColor Green
}

# --- FASE 2 & 3: PRUEBA INTERACTIVA GUI ---
Write-Host "`n[FASE 2 & 3] Lanzando GUI Interactiva (Avalonia 11.2 / Skia)..." -ForegroundColor Yellow
Write-Host "=========================================================================" -ForegroundColor Magenta
Write-Host " INSTRUCCIONES:" -ForegroundColor White
Write-Host "  1. Verifica que la ventana Avalonia renderice correctamente." -ForegroundColor White
Write-Host "  2. Aplica optimizaciones desde la GUI." -ForegroundColor White
Write-Host "  3. Vuelve a esta consola y presiona ENTER para capturar snapshot de reglas aplicadas." -ForegroundColor White
Write-Host "  4. Ejecuta el ROLLBACK desde la pestana de historial en la GUI." -ForegroundColor White
Write-Host "  5. Cierra la GUI para finalizar la prueba automatizada." -ForegroundColor White
Write-Host "=========================================================================" -ForegroundColor Magenta

$GuiProc = Start-Process -FilePath $ExeTarget -WorkingDirectory $IsolatedDir -PassThru

Start-Sleep -Seconds 4
if ($GuiProc.HasExited) {
    Write-Host " [!] ERROR: La GUI se cerro prematuramente con codigo $($GuiProc.ExitCode)." -ForegroundColor Red
} else {
    Write-Host " -> GUI activa (PID: $($GuiProc.Id)). Verificando librerias nativas cargadas en memoria..." -ForegroundColor Green
    $LoadedModules = Get-Process -Id $GuiProc.Id | Select-Object -ExpandProperty Modules -ErrorAction SilentlyContinue |
        Where-Object { $_.ModuleName -match "Skia|HarfBuzz|Avalonia|d3d|lib|GLES" } |
        Select-Object ModuleName, FileName, FileVersion
    $LoadedModules | Format-Table -AutoSize
    $LoadedModules | Export-Csv "$ArtifactDir\03_Loaded_GUI_Modules.csv" -NoTypeInformation

    Write-Host "`n>> Aplica las reglas en la GUI y presiona ENTER aqui cuando termines..." -ForegroundColor Cyan
    $null = Read-Host

    $AppliedState = Get-SystemTelemetryState
    $AppliedState | ConvertTo-Json -Depth 4 | Set-Content "$ArtifactDir\04_RulesApplied_State.json" -Encoding UTF8
    Write-Host " -> Snapshot de reglas aplicadas guardado en 04_RulesApplied_State.json." -ForegroundColor Green

    Write-Host "`n>> Ahora ejecuta el ROLLBACK en la GUI y luego CIERRA la ventana de DWS 11..." -ForegroundColor Yellow
    $GuiProc.WaitForExit()
}

Write-Host " -> GUI finalizada con ExitCode: $($GuiProc.ExitCode)" -ForegroundColor Green

# --- FASE 4: VERIFICACION DE ROLLBACK Y EXTRACCION FINAL ---
Write-Host "`n[FASE 4] Verificando integridad del Rollback..." -ForegroundColor Yellow

$PostRollbackState = Get-SystemTelemetryState
$PostRollbackState | ConvertTo-Json -Depth 4 | Set-Content "$ArtifactDir\05_PostRollback_State.json" -Encoding UTF8

$Diffs = @()
foreach ($Key in $BaselineState.Registry.Keys) {
    $Before = $BaselineState.Registry[$Key]
    $After  = $PostRollbackState.Registry[$Key]
    if ($Before -ne $After) {
        $Diffs += [PSCustomObject]@{ Component = "Registry:$Key"; Baseline = $Before; PostRollback = $After; Status = "MISMATCH" }
    } else {
        $Diffs += [PSCustomObject]@{ Component = "Registry:$Key"; Baseline = $Before; PostRollback = $After; Status = "RESTORED_OK" }
    }
}

$Diffs | Format-Table -AutoSize
$Diffs | Export-Csv "$ArtifactDir\06_Rollback_Verification_Matrix.csv" -NoTypeInformation

if (Test-Path $ProgramDataDWS) {
    New-Item -ItemType Directory -Force -Path "$ArtifactDir\Final_ProgramData_DWS" | Out-Null
    Copy-Item -Path "$ProgramDataDWS\*" -Destination "$ArtifactDir\Final_ProgramData_DWS" -Recurse -Force
}

$Summary = [ordered]@{
    RunId               = $RunId
    ExecutableSHA256    = $FileHash
    SilentAuditExitCode = $AuditExitCode
    SilentAuditDuration = "$($AuditStopwatch.ElapsedMilliseconds) ms"
    GuiExitCode         = $GuiProc.ExitCode
    RollbackMismatches  = ($Diffs | Where-Object { $_.Status -eq "MISMATCH" }).Count
    CompletedAt         = (Get-Date).ToString("o")
}
$Summary | ConvertTo-Json | Set-Content "$ArtifactDir\00_Pipeline_Summary.json" -Encoding UTF8

Stop-Transcript

New-Item -ItemType File -Path "$ArtifactDir\_PIPELINE_COMPLETED.flag" -Force | Out-Null

Write-Host "`n[COMPLETADO] Todos los artefactos fueron exportados al Host en:" -ForegroundColor Green
Write-Host " $ArtifactDir" -ForegroundColor White
Write-Host "Puedes cerrar la ventana de Windows Sandbox." -ForegroundColor Cyan
