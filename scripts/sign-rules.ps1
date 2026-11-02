# ========================================
# DWS 11 Enterprise v2.0 - Sign Rules
# ========================================

param(
    [string]$RulesFile = "rules\rules.json",
    [string]$PrivateKeyFile = "keys\private.key",
    [string]$OutputFile = "rules\rules.json"
)

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "DWS 11 Enterprise v2.0 - Sign Rules" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Verificar archivos
if (-not (Test-Path $RulesFile)) {
    Write-Host "ERROR: Rules file not found: $RulesFile" -ForegroundColor Red
    exit 1
}

if (-not (Test-Path $PrivateKeyFile)) {
    Write-Host "NOTE: Private key file not found: $PrivateKeyFile" -ForegroundColor Yellow
    Write-Host "To sign with custom authority, generate key pair with:" -ForegroundColor Yellow
    Write-Host "  openssl genrsa -out keys\private.key 4096" -ForegroundColor Yellow
    Write-Host "  openssl rsa -in keys\private.key -pubout -out keys\public.key" -ForegroundColor Yellow
    Write-Host "Using default internal pinned authority signature." -ForegroundColor Green
    exit 0
}

# Leer reglas
Write-Host "[1/3] Leyendo reglas..." -ForegroundColor Yellow
$rulesJson = Get-Content $RulesFile -Raw | ConvertFrom-Json

# Crear payload sin firma
Write-Host "[2/3] Creando payload para firma..." -ForegroundColor Yellow
$payloadForSigning = @{
    schemaVersion = $rulesJson.schemaVersion
    rules = $rulesJson.rules
    signature = $null
} | ConvertTo-Json -Depth 100

# Firmar
Write-Host "[3/3] Firmando con RSA-4096..." -ForegroundColor Yellow
$payloadBytes = [System.Text.Encoding]::UTF8.GetBytes($payloadForSigning)

$rsa = [System.Security.Cryptography.RSA]::Create()
$privateKey = Get-Content $PrivateKeyFile -Raw
$rsa.ImportFromPem($privateKey)

$signatureBytes = $rsa.SignData(
    $payloadBytes,
    [System.Security.Cryptography.HashAlgorithmName]::SHA256,
    [System.Security.Cryptography.RSASignaturePadding]::Pkcs1
)

$signature = [Convert]::ToBase64String($signatureBytes)

# Agregar firma al JSON original
$rulesJson | Add-Member -MemberType NoteProperty -Name "signature" -Value $signature -Force

# Guardar
$rulesJson | ConvertTo-Json -Depth 100 | Set-Content $OutputFile -Encoding UTF8

Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host "Reglas firmadas exitosamente" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host "Archivo de salida: $OutputFile" -ForegroundColor Cyan
Write-Host ""
