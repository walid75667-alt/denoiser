MicDenoiser 2.6 beta applies the attached compact/Studio interface design:

- Compact home with large power control, three isolation levels, live input/output meters, folded devices, mute, held original comparison and a background-reduction estimate.
- Three-step VB-Cable setup with a live microphone meter, ten-second local before/after test and illustrated app routing. Installer purpose selection initializes calls or narration presets without replacing existing settings.
- Global Ctrl+Alt+M mute and Ctrl+Alt+D start/stop, plus quick tray controls. Engine/buffer changes restart audio automatically; the speech gate can toggle live with its detector initialized before processing.
- Simplified formal Arabic, matched English, embedded IBM Plex Sans Arabic, green/neutral light/dark palette and five Studio pages. Detailed mixer controls expand and each strip has an independent reset.
- Version is shown in About; designer attribution and WhatsApp/Facebook links are included.
- Windows CI renders 21 actual WPF screenshots without recording, alongside native DLL checks. Linux has 53 passed portable/native checks and 2 Windows-only skips; the 36 real-speech scenes pass the existing benchmark thresholds.

This beta retains the 960-sample RNNoise delay, conservative 6 dB default call gate, individual-frame p99/session maximum and optional last-30-second paired local diagnostics from 2.5. Background reduction is an estimate during low VAD, not a speech-quality score. Strong mode may attenuate weak speech. Packages include .NET, models and libraries; VB-Cable remains a separate vendor install. Signing requires an owner-provided trusted certificate. Physical-device crackling and singing quality still require listening tests; no Krisp equivalence is claimed.

Previous audio changes:

MicDenoiser 2.5 beta improves timing alignment and crackle diagnosis:

- RNNoise uses 960 samples of native delay. With the existing gate look-ahead, its fixed pipeline delay is 40 ms; device/playback buffering adds more.
- Calls enable a conservative 6 dB speech gate with hysteresis and 300 ms hold. Singing and whisper presets keep the gate off. This is not target-speaker isolation.
- Diagnostics show last-3,000-frame p99 (histogram upper bound) and the session maximum DSP time.
- Optional last-30-second recording keeps aligned original/DSP output in RAM. Saving stops audio and exports WAVs plus a local report. These WAVs exclude playback/device artifacts; no automatic upload.
- First-use VB-Cable detection and a routing diagram help connect calling/streaming apps.
- xUnit reports include actual Windows DLL execution and a pinned RNNoise impulse-delay probe. Linux CI checks 36 deterministic scenes with real recorded speech and four noise types.

Packages include the .NET runtime, models and native libraries. VB-Cable is a separate vendor installation. Code signing occurs only when a valid signing certificate is configured; absence of a certificate produces unsigned packages. Acoustic quality, physical-device behavior and WPF interaction still require Windows microphone validation. No Krisp equivalence is claimed.
