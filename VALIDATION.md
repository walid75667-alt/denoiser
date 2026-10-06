# Validation of this implementation

The uploaded Windows project was imported into `MicDenoiser/`; the initial
repository contained only a README. No application is claimed to have run on
Windows in this Linux development environment.

Verified:

- WPF/.NET 8 Release build and self-contained `win-x64` publish: passed with
  zero warnings and errors.
- Linux DeepFilterNet native library: built from the pinned source and locked
  dependencies. The Windows GNU x64 DLL was also cross-compiled. Its exports
  and imports were inspected; it needs only Windows system DLLs.
- Seven processing checks: passed. These exercise fragmented live resampling
  at 16/44.1/96 kHz, sample delay across frames, dry/wet and bypass alignment,
  native loading and model metadata, silence, stationary noise, and model
  integrity errors. The Windows-only RNNoise/sidechain check was skipped.
- Corrupt model passed directly to the native C ABI: returned an error without
  aborting the process.
- The paired `assets/noisy_snr0.wav` / `assets/clean_freesound_33711.wav`
  example from the pinned upstream DeepFilterNet repository was processed
  through the final C# pipeline. All 508,591 samples were retained, the 50 ms
  algorithmic delay was compensated, and the speech output was nonzero and
  unclipped. SI-SDR improved from 6.05 to 6.91 dB on this one example.
- The 10.60-second speech clip was processed in 1.10 seconds on this Linux
  host, excluding model construction. This is an offline observation, not a
  guarantee about a Windows microphone session or another CPU.
- Windows publish contains the executable, runtime, model, both native DLLs,
  and license files. Published DLL hashes match their source-package copies.

The initial pipeline's fixed high-pass filter reduced the paired speech
example's SI-SDR. High-pass is now optional and disabled in the default preset.
Both the live application and the file-processing tool use the same final
pipeline.

Still to validate on Windows:

- Opening the WPF UI and loading the Windows DLLs.
- Physical microphone capture, format negotiation, VB-Cable routing,
  stopping/restarting, device removal, and sustained real-time operation.
- The optional RNNoise voice detector and lightweight engine using the
  uploaded Windows binary. A Windows CI job is included to rebuild the native
  library and run those processing checks, but it has not run here.
- Total audio latency, quality on Egyptian Arabic/whispers/background
  speakers, and a matched-input listening comparison against Krisp.

These results establish functioning file-based DeepFilterNet processing and
buildable Windows code. They do not establish equivalence to Krisp, target
speaker isolation, echo cancellation, or production readiness.
