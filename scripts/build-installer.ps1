param([string]$Makensis = 'makensis.exe')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$app = [System.IO.Path]::GetFullPath((Join-Path $root 'artifacts/windows'))
$out = Join-Path $root 'artifacts/MicDenoiser-Setup-x64.exe'
$manifest = Join-Path $root 'artifacts/uninstall-files.nsh'
foreach ($required in @('MicDenoiser.exe', 'micdenoiser_df.dll', 'rnnoise.dll', 'vcruntime140.dll', 'models/DeepFilterNet3_onnx.tar.gz')) {
    if (!(Test-Path (Join-Path $app $required))) { throw "Publish first: missing $required" }
}
# Delete only published files. Never recursively remove a user-selected folder.
$lines = [System.Collections.Generic.List[string]]::new()
Get-ChildItem $app -Recurse -File | Sort-Object FullName | ForEach-Object {
    $relative = $_.FullName.Substring($app.Length).TrimStart([System.IO.Path]::DirectorySeparatorChar).Replace('/', '\')
    if ($relative.Contains('$') -or $relative.Contains('"')) { throw 'Unsupported filename in installer payload.' }
    $lines.Add('Delete "$INSTDIR\' + $relative + '"')
}
Get-ChildItem $app -Recurse -Directory | Sort-Object { $_.FullName.Length } -Descending | ForEach-Object {
    $relative = $_.FullName.Substring($app.Length).TrimStart([System.IO.Path]::DirectorySeparatorChar).Replace('/', '\')
    $lines.Add('RMDir "$INSTDIR\' + $relative + '"')
}
[System.IO.File]::WriteAllLines($manifest, $lines, [System.Text.UTF8Encoding]::new($false))
& $Makensis /INPUTCHARSET UTF8 "/DAPP_DIR=$app" "/DOUT_FILE=$out" "/DUNINSTALL_MANIFEST=$manifest" (Join-Path $root 'installer/MicDenoiser.nsi')
if ($LASTEXITCODE -ne 0) { throw 'Installer build failed.' }
Write-Host "Windows installer: $out"
