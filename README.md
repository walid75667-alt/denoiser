# MicDenoiser

Version 2.6 applies the attached compact/Studio design: power, three isolation
levels, live meters, held original comparison, mute/global shortcuts, guided
setup and module resets. See [interface details](docs/UI-DESIGN.md). It retains
the corrected RNNoise delay, conservative call gate, p99 diagnostics and
optional local 30-second recording from 2.5.

New packages belong in [GitHub Releases](https://github.com/walid75667-alt/denoiser/releases)
after Linux and Windows CI pass. Both 2.5 beta assets were downloaded and their
SHA-256 checksums verified; the links below point to that verified beta while
the 2.6 workflow runs. Binaries are no longer tracked in the current checkout.
See [release workflow](docs/RELEASING.md), [release notes](docs/RELEASE-NOTES.md)
and [real-speech benchmark](benchmarks/README.md).

Windows x64 microphone noise suppression with DeepFilterNet3 (default) and a
lightweight RNNoise option. The WPF/.NET 8 application was imported from the
attached MicDenoiser project and upgraded with a streaming native engine,
WASAPI audio, a dedicated processing worker, and aligned dry/bypass paths.

Download the [Windows x64 installer](https://github.com/walid75667-alt/denoiser/releases/download/v2.5.0-beta.1/MicDenoiser-Setup-x64.exe)
or the [portable ZIP](https://github.com/walid75667-alt/denoiser/releases/download/v2.5.0-beta.1/MicDenoiser-Windows-x64.zip). Both include
the .NET runtime, model, and native DLLs. Run the installer, or extract the entire ZIP folder and run
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
native library, runs processing checks, and packages the installer. The 2.5 branch CI passed on both Linux and Windows. Each new version requires
its own CI/native and WPF rendering checks; see VALIDATION.md.

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

The application now includes a custom microphone icon in the executable,
window header, taskbar and installer. Minimize to tray keeps audio processing
running while the window is hidden. Double-click the notification icon to
restore; its localized menu can open the app, start/stop denoising or exit.
Uncheck the tray option for normal taskbar minimization. The window close
button exits the application. Icon and tray behavior need a Windows UI test.

Version 2.1 adds a 10-second microphone comparison, automatic stability
recommendations, calls/streaming/weak-microphone/quiet-speech presets, opt-in
Windows startup, device-disconnection recovery and a saved dark theme.
The mic test records original and processed audio from the same stream,
aligns processing delay, and attenuates to matching integrated loudness.
Recordings stay in memory and are discarded at exit. Use headphones; preview
playback stops live denoising until you start it again.

Automatic buffering begins with the balanced 60 ms target and evaluates
five-second telemetry windows. It recommends Stable buffering or lighter
RNNoise; clicking Apply explicitly restarts processing. Severe backlog stops
processing rather than accumulating stale audio, and offers recovery advice.
Device removal also stops processing; the app never silently switches to a
replacement microphone. Reconnect or select devices and start again.

Enable Start with Windows in Preferences to register the current executable
in the current user's Run key. Startup minimizes to the tray and starts only
if both saved device IDs are available. This option is off by default;
uninstall removes the app's startup entry and preserves user settings.
Actual Windows rendering, registration, notifications and microphone behavior
still need Windows validation; see VALIDATION.md.

Version 2.2 adds a voice-studio mixer aimed at singing and voice-over:
four parametric EQ bands with frequency/gain/Q, a logarithmic target-response
curve, variable high-pass cutoff, compressor attack/release/soft knee/make-up,
a split-band de-esser, output ceiling and RMS/gain-reduction meters. Each
module has live controls with smoothed transitions. Effects settings can be
saved/loaded as versioned JSON without changing devices, engine or buffering.

Natural singing starts with wet mix at 0% and the speech gate off, plus gentle
1.5:1 compression. DeepFilterNet is trained for speech and may alter sustained
notes; add wet mix only after comparing. Voice-over and Broadcast voice offer
subtle starting points, not calibrated mastering presets. Output protection
limits sample peaks with a soft curve; it is not a true-peak limiter.

The ten-second mic test can export original/processed 48 kHz mono Float32 WAV
at captured levels, separately from matched preview playback. Float export
preserves captured samples; it does not increase microphone resolution. Live
WASAPI output remains 48 kHz mono PCM16. Use the app's output in your recording
software via your configured audio routing. Full-session/multitrack recording
is outside the current mic-test workflow. Windows UI, device latency and
listening with actual singing/voice-over still require a Windows check.

Version 2.3 adds mono vocal effects to the mixer: damped room/hall reverb
(0.2–3 s decay, 0–100 ms pre-delay), echo (40–1000 ms repeats, feedback up
to 70%), and chorus (0.1–3 Hz, 1–10 ms modulation). Each has its own switch
and level; Disable all three effects restores a dry voice while preserving
EQ/dynamics. Old settings keep the new modules off. Settings and exchanged
profiles include all effect controls. Vocal room, Vocal hall and Vocal
slapback presets start from Natural singing, keeping the speech denoiser mix
and VAD gate off. Choose Voice-over for dry narration.

The effect chain is EQ → de-esser → compressor → chorus/reverb/echo → output
gain → sample ceiling. Reverb and echo are parallel sends; chorus blends a
modulated delayed copy. Parameters fade gradually and echo/pre-delay changes
crossfade read heads. Delay storage is allocated once; the warmed processing
worker allocates no managed memory. Dry signal onset and the reported model
delay are retained; wet effects intentionally have their own delay/tail.
This is a software effects rack, not hardware emulation, pitch correction,
echo cancellation, stereo processing or a low-latency ASIO interface. BM800
is a microphone; it can be used as the selected physical input. Use headphones
for monitoring and validate actual Windows latency before live singing.

Version 2.4 adds Simple/Studio views, a model-free Fast singing path,
rolling crackle evidence and matched A/B preview. New installations start in
Simple view; existing configurations retain Studio view. Switching views
preserves settings. Simple view includes devices, input/output gain, common
presets and the mic test; Studio exposes engine/buffer selection and the mixer.

Enable Fast singing while stopped. No denoising or VAD model is loaded; gate
look-ahead and model delay are removed, while EQ/dynamics/effects remain.
Playback targets are 30 ms Balanced/Automatic, 60 ms Stable and 20 ms Low
latency (regular targets remain 60/100/30 ms). Disabling Fast singing restores
the full denoiser mix; gate stays off until chosen while stopped. This does
not establish total round-trip latency or ASIO support.

Health separates near-full-scale source levels, gain-induced overflow,
sustained processing windows over 10 ms, recent output gaps and frequent
heavy output protection. A 2.5-second rolling window lets old evidence expire;
session counters remain. Reports contain observed settings/counters, without
recordings or device names/IDs. Source levels cannot prove ADC clipping, and
absence of telemetry evidence cannot rule out model/audio artifacts.

In Mic test, save two settings slots A/B, record ten seconds, then Prepare
comparison. Fresh offline engines process exactly the same recorded input;
model/look-ahead delays are compensated and preview loudness is attenuated
to match, with peak headroom and no boosts. The current engine, Fast singing
path and buffering are retained for both slots; live bypass is ignored for
rendering. Rendering/listening pause live audio and rendering can be canceled.
Snapshots survive restart, but recordings/rendered previews stay in memory.
The preview retains the ten-second duration; wet tails beyond it are omitted.
Apply A/B preserves the current path and cannot enable a stopped VAD on the
live worker. Applying a slot uses its actual gain settings; only preview is
loudness-matched.
