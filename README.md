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
