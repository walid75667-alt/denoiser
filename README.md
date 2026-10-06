# MicDenoiser

Windows x64 microphone noise suppression with DeepFilterNet3 (default) and a
lightweight RNNoise option. The WPF/.NET 8 application was imported from the
attached MicDenoiser project and upgraded with a streaming native engine,
WASAPI audio, a dedicated processing worker, and aligned dry/bypass paths.

Download the [Windows x64 installer](downloads/MicDenoiser-Setup-x64.exe)
or the [portable ZIP](downloads/MicDenoiser-Windows-x64.zip). Both include
the .NET runtime, model, and native DLLs. On GitHub, open the file and select
Download raw file. Run the installer, or extract the entire ZIP folder and run
`MicDenoiser.exe`. VB-Cable is installed separately from its vendor. See [validation and remaining Windows checks](VALIDATION.md).

On Windows, install VB-Cable and the .NET 8 SDK, then run:

```powershell
powershell -File scripts/build.ps1
dotnet run --project MicDenoiser -c Release --no-build
```

Select the physical microphone, `CABLE Input`, and the engine. Select
`CABLE Output` in your calling application.

[Arabic setup, architecture, and comparison instructions](MicDenoiser/README.md)
explain the native rebuild and file-based processing checks. Native source,
model, and dependency versions are pinned. A Windows CI workflow rebuilds the
native library, runs processing checks, and packages the installer. Its CI
results have not been verified in this environment.

Acoustic quality, microphone routing, and total latency require validation on
Windows. This project does not claim measured equivalence to Krisp.

Version 2.0 adds Arabic/English switching, a tabbed interface, input/output
levels, stability diagnostics, device refresh, and gentler speech suppression.
DeepFilterNet now starts at a 35 dB noise reduction limit; Softer sound uses
25 dB. Balanced/Stable/Low latency modes target 60/100/30 ms playback buffers.
Short fades soften output starvation and recovery, while gain, wet/dry mix,
and compressor transitions are smoothed. These changes address reproducible
click risks; the user's intermittent speech crackling still needs a Windows
microphone test.

To rebuild the installer on Windows, install NSIS 3 and run after publishing:

```powershell
powershell -File scripts/build-installer.ps1 -Makensis 'C:/Program Files (x86)/NSIS/makensis.exe'
```

Setup installs for the current user under `%LocalAppData%/Programs/MicDenoiser`,
adds Start menu shortcuts and an optional desktop shortcut, and registers an
uninstaller. Existing settings are preserved. Installer and application can
both use Arabic or English. See [installer details](installer/README.md).
