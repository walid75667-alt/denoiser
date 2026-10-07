param([Parameter(Mandatory=$true)][string]$Path)
$ErrorActionPreference = 'Stop'
if (!$env:WINDOWS_SIGN_CERT_PFX_BASE64) {
    Write-Host 'No signing certificate configured: this package is unsigned.'
    return
}
if (!$env:WINDOWS_SIGN_CERT_PASSWORD) { throw 'Signing certificate password is missing.' }
$signTool = Get-ChildItem "${env:ProgramFiles(x86)}/Windows Kits/10/bin" -Recurse -Filter signtool.exe |
    Where-Object { $_.FullName -match '\\x64\\' } | Sort-Object FullName -Descending | Select-Object -First 1
if (!$signTool) { throw 'Windows SDK x64 signtool.exe is required for signing.' }
$temporary = Join-Path $env:RUNNER_TEMP ([Guid]::NewGuid().ToString() + '.pfx')
$certificate = $null
try {
    [IO.File]::WriteAllBytes($temporary, [Convert]::FromBase64String($env:WINDOWS_SIGN_CERT_PFX_BASE64))
    $password = ConvertTo-SecureString $env:WINDOWS_SIGN_CERT_PASSWORD -AsPlainText -Force
    $certificate = Import-PfxCertificate -FilePath $temporary -CertStoreLocation Cert:\CurrentUser\My -Password $password
    if (!$certificate.HasPrivateKey) { throw 'Certificate has no private key.' }
    & $signTool.FullName sign /sha1 $certificate.Thumbprint /fd SHA256 /tr https://timestamp.digicert.com /td SHA256 $Path
    if ($LASTEXITCODE -ne 0) { throw 'Authenticode signing failed.' }
    & $signTool.FullName verify /pa /all $Path
    if ($LASTEXITCODE -ne 0) { throw 'Authenticode signature verification failed.' }
}
finally {
    if (Test-Path $temporary) { Remove-Item $temporary -Force }
    if ($certificate) { Remove-Item ('Cert:\CurrentUser\My\' + $certificate.Thumbprint) }
}
