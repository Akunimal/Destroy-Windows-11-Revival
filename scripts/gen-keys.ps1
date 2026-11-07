$ErrorActionPreference = "Stop"
$rsa = [System.Security.Cryptography.RSA]::Create(4096)
$pubBytes = $rsa.ExportSubjectPublicKeyInfo()
$privBytes = $rsa.ExportPkcs8PrivateKey()

$keysDir = Join-Path $PSScriptRoot "..\tools\keys"
if (-not (Test-Path $keysDir)) {
    New-Item -ItemType Directory -Path $keysDir -Force | Out-Null
}

[System.IO.File]::WriteAllBytes((Join-Path $keysDir "dws_enterprise_public.spki"), $pubBytes)
[System.IO.File]::WriteAllBytes((Join-Path $keysDir "dws_enterprise_private.pkcs8"), $privBytes)

$pubB64 = [Convert]::ToBase64String($pubBytes)
[System.IO.File]::WriteAllText((Join-Path $keysDir "dws_enterprise_public.b64"), $pubB64)

Write-Host "Public Key Base64 Length: $($pubB64.Length)"
Write-Host "Public Key Bytes Length: $($pubBytes.Length)"
