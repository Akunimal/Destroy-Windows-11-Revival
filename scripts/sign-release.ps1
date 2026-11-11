param(
    [string]$Version = "2.1.5"
)

$baseDir = Split-Path -Parent $PSScriptRoot
$exePath = Join-Path $baseDir "dist\DWS11-Enterprise-v$Version-win-x64.exe"
$shaPath = Join-Path $baseDir "dist\DWS11-Enterprise-v$Version-win-x64.exe.sha256"

if (-not (Test-Path $exePath)) {
    Write-Error "Binary not found: $exePath"
    exit 1
}

$cert = Get-Item Cert:\CurrentUser\My\16CBD4AB25DF304F2D4299575E663C91D7510D39 -ErrorAction SilentlyContinue
if ($cert) {
    Set-AuthenticodeSignature -FilePath $exePath -Certificate $cert | Out-Null
    Write-Host "Authenticode signed: $exePath"
} else {
    Write-Warning "Certificate 16CBD4AB25DF304F2D4299575E663C91D7510D39 not found in Cert:\CurrentUser\My"
}

$hash = (Get-FileHash -Path $exePath -Algorithm SHA256).Hash
$fileName = [System.IO.Path]::GetFileName($exePath)
"$hash *$fileName" | Set-Content -Path $shaPath -Encoding ascii

Write-Host "Hash: $hash"
Get-AuthenticodeSignature $exePath
Get-Content $shaPath
