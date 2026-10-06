# Validation of MicDenoiser 2.0

The Windows application was imported from the user's uploaded project. The
repository initially contained only a README. This development environment is
Linux; no live Windows microphone session or installer execution is claimed.

Verified:

- WPF/.NET 8 Release build and self-contained `win-x64` publish completed with
  zero warnings and errors.
- Linux and Windows GNU x64 DeepFilterNet libraries were rebuilt from the
  pinned upstream revision and locked dependencies. ABI version 2 and the
  live attenuation setter are exported by the Windows DLL. Its imports use
  only Windows system DLLs.
- All 13 checks passed: Arabic/English catalog parity and XAML resource
  references; output starvation/recovery fades; startup rebuffering and byte
  offsets; gain/compressor continuity; malformed persisted settings;
  fragmented live resampling at 16/44.1/96 kHz; sample delay; aligned wet/dry
  and bypass paths; native loading/metadata/silence; stationary noise; model
  integrity; and the attenuation control. The Windows RNNoise/optional VAD
  execution check was skipped on Linux.
- The starvation regression reproduces a 12,000 PCM-unit jump in the old
  zero-fill output. The guarded output fades the same transition over 5 ms,
  with adjacent sample changes at most 51 units, and counts a continuous
  outage once. Recovery also fades in. This softens discontinuities; it
  cannot recover speech lost while capture or processing is stalled.
- DeepFilterNet now uses the upstream C API inference thresholds
  `(-15, 35, 35)` and a default 35 dB attenuation limit. The interface's
  Softer sound profile uses 25 dB. The live limit changes at most 1 dB per
  10 ms frame. Gain/mix/compressor controls also transition gradually.
- The paired upstream `assets/noisy_snr0.wav` and
  `assets/clean_freesound_33711.wav` example was processed through the final
  default pipeline. All 508,591 samples were retained, 50 ms algorithmic
  delay was compensated, output was finite and nonzero, and the peak was
  0.641 full scale. SI-SDR improved from 6.05 to 19.97 dB on this single
  example. The prior implementation achieved 6.91 dB; these figures do not
  measure the user's microphone or establish a Krisp comparison.
- Processing that 10.60-second clip took 1.84 seconds on this Linux host,
  excluding model construction. This is an offline observation, not a
  guarantee of real-time operation on another machine.
- The generated application icon has 10 decoded ICO sizes from 16 to 256
  pixels. Its executable/installer resource groups were inspected after
  compilation. The PNG source and ICO are bundled as WPF resources.
- Native/model hashes in `native-checksums.json` match both the source and
  self-contained publish copies. The app-local runtime copy used by the
  uploaded RNNoise DLL exports all five of its required VCRUNTIME140 symbols.
  This export check is not a Windows RNNoise execution test.
- The Arabic/English NSIS 3.11 installer compiled without warnings. Its
  extracted payload and the portable ZIP were checked against the publish
  directory. Setup includes the application runtime, model, instructions
  and libraries; VB-Cable is installed separately.

Still to validate on Windows:

- WPF rendering, text fitting at different DPI settings, immediate language
  switching, and device refresh.
- Notification icon rendering, tray menus, minimize/restore (normal and
  maximized), audio continuity while hidden, switching languages, exiting
  during model startup, Explorer restart and icon cleanup at shutdown.
- Installation, installer language selection, shortcuts, upgrade and
  uninstall. Settings are preserved; uninstall deletes only listed payload
  files and empty subdirectories.
- Windows native binding, RNNoise and optional sidechain VAD execution.
  GitHub Actions is configured to rebuild/check/package them; no CI result
  is claimed here.
- Physical microphone capture, format negotiation, VB-Cable routing,
  stopping/restarting, device removal, CPU load and prolonged real-time use.
- The user's intermittent DeepFilterNet3 crackling during speech. Buffer
  gaps and model artifacts have separate possible causes; diagnostics and
  the softer profile help distinguish them. No recording of that symptom
  was supplied.
- Total audio latency, Egyptian Arabic, whispers, background speakers, and
  a matched-input listening comparison with Krisp.

These checks establish functioning file processing and buildable Windows
artifacts. They do not establish Krisp equivalence, target speaker isolation,
echo cancellation or production readiness.
