param([switch]$RebuildNative, [switch]$Publish)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if ($RebuildNative -or !(Test-Path (Join-Path $root 'MicDenoiser/native/micdenoiser_df.dll'))) {
    & (Join-Path $PSScriptRoot 'build-native.ps1')
}
$project = Join-Path $root 'MicDenoiser/MicDenoiser.csproj'
& dotnet restore $project --locked-mode
if ($LASTEXITCODE -ne 0) { throw 'Dependency restoration failed.' }
& dotnet build $project -c Release --no-restore
if ($LASTEXITCODE -ne 0) { throw 'Application build failed.' }
$checks = Join-Path $root 'MicDenoiser.Checks/MicDenoiser.Checks.csproj'
& dotnet restore $checks --locked-mode
if ($LASTEXITCODE -ne 0) { throw 'Check dependency restoration failed.' }
& dotnet run --project $checks -c Release --no-restore
if ($LASTEXITCODE -ne 0) { throw 'Processing checks failed.' }
if ($Publish) {
    & dotnet publish $project -c Release -r win-x64 --self-contained true -o (Join-Path $root 'artifacts/windows') -p:PublishSingleFile=false
    if ($LASTEXITCODE -ne 0) { throw 'Publishing failed.' }
    Copy-Item (Join-Path $root 'installer/START-HERE.txt') (Join-Path $root 'artifacts/windows/START-HERE.txt')
    Write-Host 'Windows application: artifacts/windows/MicDenoiser.exe'
}
