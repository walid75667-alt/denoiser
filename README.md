# MicDenoiser

Windows x64 microphone noise suppression with DeepFilterNet3 (default) and a
lightweight RNNoise option. The WPF/.NET 8 application was imported from the
attached MicDenoiser project and upgraded with a streaming native engine,
WASAPI audio, a dedicated processing worker, and aligned dry/bypass paths.

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
native library and runs the processing checks; it still needs to run on GitHub.

Acoustic quality, microphone routing, and total latency require validation on
Windows. This project does not claim measured equivalence to Krisp.
