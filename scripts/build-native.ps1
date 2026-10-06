param([int]$Jobs = 4)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root 'MicDenoiser'
$model = Join-Path $project 'models/DeepFilterNet3_onnx.tar.gz'
$expected = 'c94d91f70911001c946e0fabb4aa9adc37045f45a03b56008cb0c8244cb63616'
if (!(Test-Path $model) -or (Get-FileHash $model -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expected) {
    throw 'Missing or corrupt pinned model. Restore models/DeepFilterNet3_onnx.tar.gz from the project archive.'
}
if (!(Get-Command cargo -ErrorAction SilentlyContinue)) {
    throw 'Install Rust using https://rustup.rs and Visual Studio Build Tools with Desktop development with C++.'
}
& rustup toolchain install 1.90.0 --profile minimal
if ($LASTEXITCODE -ne 0) { throw 'Rust toolchain installation failed.' }
& rustup target add --toolchain 1.90.0 x86_64-pc-windows-msvc
if ($LASTEXITCODE -ne 0) { throw 'Windows Rust target installation failed.' }
$native = Join-Path $project 'native-src'
& cargo +1.90.0 build --manifest-path (Join-Path $native 'Cargo.toml') --release --locked --target x86_64-pc-windows-msvc -j ([Math]::Max(1, [Math]::Min($Jobs, 4)))
if ($LASTEXITCODE -ne 0) { throw 'Native build failed. Check the MSVC x64 C++ toolchain is installed.' }
Copy-Item (Join-Path $native 'target/x86_64-pc-windows-msvc/release/micdenoiser_df.dll') (Join-Path $project 'native/micdenoiser_df.dll')
Write-Host 'DeepFilterNet DLL rebuilt. Rebuild the .NET app to copy it into the output.'
