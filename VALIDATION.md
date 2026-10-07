# Validation of MicDenoiser 2.4

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
- All 45 checks passed: Arabic/English catalog parity and XAML resource
  references; output starvation/recovery fades; startup rebuffering and byte
  offsets; gain/compressor continuity; malformed persisted settings;
  fragmented live resampling at 16/44.1/96 kHz; sample delay; aligned wet/dry
  and bypass paths; native loading/metadata/silence; stationary noise; model
  integrity; and the attenuation control. The Windows RNNoise/optional VAD
  execution check was skipped on Linux.
- The new comparison checks cover exactly 480,000 captured samples, delays of
  0/517/1440/2400 samples, cancellation, a 1 kHz BS.1770 loudness reference,
  matched attenuating playback, immutable source data, silence, buffer offsets
  and clean playback completion. Stability checks distinguish isolated spikes,
  sustained processing overload, fresh gap counts and unchanged advice. All
  four use-case presets survive configuration serialization with gate disabled.
- Version 2.2 checks measured a 6 dB parametric band against its target,
  neutral EQ transparency, coefficient/bypass transitions, graph response,
  selective high-band de-essing with low fundamentals retained, soft-knee
  continuity and attack control, output sample ceilings and metering,
  bounded/versioned profile exchange, sustained dry singing, WAV read-back
  at captured levels and zero allocations on a warmed effects worker.
  A completed float-feedback fade was reproduced retaining a subnormal tail;
  double state with an inaudible settling threshold now reaches exact zero.
  Filter/envelope states also clear only below negligible levels. This is not
  a diagnosis of the user's microphone crackling.
  These are DSP/regression checks, not a listening-quality score.
- Version 2.3 adds impulse checks for exact echo repeat timing/level and
  feedback decay to silence, reverb pre-delay and longer-tail energy, chorus
  modulation with retained dry onset, effect switches and rapidly changed
  delays on constant input (adjacent steps below 15 PCM units), output ceilings
  with all three effects active, aligned global bypass, old-profile neutrality,
  bounded parameters and complete copy/profile round trips. The warmed
  allocation check now enables reverb, echo and chorus too. These checks do
  not establish perceptual quality or predict clicks for every microphone.
- Version 2.4 checks prove Fast singing constructs a DirectVoiceEngine,
  retains undelayed dry audio with zero model/gate delay, preserves settings
  and selects smaller buffer targets. Raw and post-gain peaks are measured
  separately. Diagnostics distinguish source/gain/load/gaps/output pressure,
  expire stale evidence, retain counters, reject nonfinite input and allocate
  no managed memory on the warmed worker. Direct-path overload advice does
  not recommend replacing a model that is not running. A/B checks use the
  identical immutable input, compensate differing pipeline delays, preserve
  a non-frame-multiple length, apply gain and match preview loudness without
  boosting. Cancellation releases the active engine before starting B.
  Snapshot/profile application retains the local path and live gate ownership.
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

- Simple/Studio selection and saved state, linked quick gain sliders, fast-path
  start/stop and total physical latency, diagnosis/report controls, persisted
  snapshots, A/B dialogs/preview/cancellation/close and device removal during
  rendering. Offline checks do not validate WPF interaction or live routing.
- Reverb/echo/chorus controls, combined effect listening, rapid changes on real
  singing, stored profiles/presets and monitoring delay on a physical BM800
  or other microphone. No hardware sound-card emulation is claimed.
- Mixer layout/faders at different DPI/window sizes, EQ curve, numeric field
  entry/validation with Arabic and English, per-module switches, profile
  dialogs, Float32 WAV export and recording-software import.
- Actual singing/voice-over listening, sibilant consonants, extreme live EQ
  moves, compressor timing, gain staging and gain-reduction/RMS meters.
  Live routing remains mono PCM16. Singing defaults to a dry denoiser mix;
  no vocal/music quality benchmark or low-latency audio-interface claim.
- WPF rendering, text fitting at different DPI settings, immediate language
  switching, and device refresh.
- Ten-second recording/playback through physical devices, switching language
  during capture, canceling, closing and USB removal during recording/listening.
  Offline checks establish alignment and matching logic, not perceived matching
  for every recording or the cause of the user's crackling.
- Automatic advice under sustained real Windows load and applying/restarting.
- Dark theme, combobox keyboard navigation, RTL/LTR layouts and saved preferences.
- Optional current-user startup registration, startup with missing devices,
  minimized launch, and startup entry removal on uninstall.
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
