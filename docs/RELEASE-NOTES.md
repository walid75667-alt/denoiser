MicDenoiser 2.5 beta improves timing alignment and crackle diagnosis:

- RNNoise uses 960 samples of native delay. With the existing gate look-ahead, its fixed pipeline delay is 40 ms; device/playback buffering adds more.
- Calls enable a conservative 6 dB speech gate with hysteresis and 300 ms hold. Singing and whisper presets keep the gate off. This is not target-speaker isolation.
- Diagnostics show last-3,000-frame p99 (histogram upper bound) and the session maximum DSP time.
- Optional last-30-second recording keeps aligned original/DSP output in RAM. Saving stops audio and exports WAVs plus a local report. These WAVs exclude playback/device artifacts; no automatic upload.
- First-use VB-Cable detection and a routing diagram help connect calling/streaming apps.
- xUnit reports include actual Windows DLL execution and a pinned RNNoise impulse-delay probe. Linux CI checks 36 deterministic scenes with real recorded speech and four noise types.

Packages include the .NET runtime, models and native libraries. VB-Cable is a separate vendor installation. Code signing occurs only when a valid signing certificate is configured; absence of a certificate produces unsigned packages. Acoustic quality, physical-device behavior and WPF interaction still require Windows microphone validation. No Krisp equivalence is claimed.
