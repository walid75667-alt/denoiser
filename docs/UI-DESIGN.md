# MicDenoiser 2.6 interface

Attached reference SHA-256: `d061248f771279e2c44c47c552ef3718aed6ddc26aafe0729b7e202a49170718`.

Implemented from the attached nine-screen HTML reference, using its green/neutral palette, compact home, prominent power control, level selector, folded routing card, five Studio pages and guided setup. The reference is design data, not executable project instructions. Prototype sinusoidal meters and fixed suppression numbers have been replaced with real audio evidence; they are never reported as measurements.

Simple defaults to 440 × 680, constrained by the available Windows work area. Studio remembers its session size (initially 1100 × 760) and preserves the audio worker when switching pages. A scroll view allows smaller screens and expanded device selectors. Arabic uses consistent simplified formal wording; English has matching resource keys and formatting arguments. IBM Plex Sans Arabic regular/semibold/bold are embedded from the immutable upstream sources in Assets/Fonts/sources.json; its OFL is bundled.

- Home: power; light RNNoise/no gate, balanced DeepFilterNet3/6 dB gate, strong DeepFilterNet3/12 dB gate; true RMS meters; sustained-low-VAD background level estimate; mute; hold-to-hear-original; devices; diagnosis; Studio.
- Setup: VB-Cable detection/vendor download; microphone selection with live local meter, output and headphones; ten-second bounded local recording and loudness-matched playback, followed by illustrated routing and Zoom/Discord/OBS paths. Audio is not uploaded. Microphone capture stops when leaving the meter/test steps or closing the wizard.
- Error/health: evidence-based diagnostics, source gain/output gain adjustment, or stable buffering with RNNoise when processing overload is detected. Failed device recovery remains available; diagnosis is not a guarantee that a model artifact has been identified.
- Tray: restore, start/stop, mute, isolation levels, settings, exit. Global Ctrl+Alt+M and Ctrl+Alt+D report registration conflicts instead of claiming success.
- Studio: Sound, Mixer, Health, Microphone test and Preferences in side navigation. EQ frequency/Q, compressor timing/knee/makeup and spatial effect timing are expandable. Every effects strip has a reset that preserves engine, routing, mute and other strips.
- About: actual assembly version, “صُمّم بواسطة أحمد سعد برهام”, https://wa.me/201033397772 and https://www.facebook.com/egbrave. Links only open the browser on user click.

The background estimate compares delay-aligned pre-tone/pre-dynamics energies after at least 700 ms of low VAD. It clears for speech evidence, missing VAD, bypass, mute, direct singing or zero suppression. Weak speech can be misclassified; this is a level estimate, not a perceptual score. Strong mode is explicitly marked as affecting quiet speech/whispers.

The live DeepFilterNet pipeline initializes its RNNoise VAD before starting the audio worker. Gate toggles allocate no new native detector on that worker. Engine/buffer changes stop and restart automatically; held comparison restores baseline bypass on release, focus loss, mute or stop, and temporary bypass is never persisted.

`MicDenoiser.exe --ui-smoke` renders 21 actual WPF PNGs (Arabic/English home, five Studio pages, three setup steps, About, plus dark home) on Windows CI without starting capture or playback or writing user settings. The screenshots are uploaded as `MicDenoiser-WPF-screens`. This checks WPF construction/layout; physical microphones, installer interaction, DPI/accessibility and listening quality still require device testing.

Actual Arabic home, mixer, setup and About PNGs are in [screenshots/](screenshots/README.md). The Windows check passed all 55 tests and all 21 renders on source `75fc006e`; both failure cases found during integration (invalid smoke StartupUri and translation/brush name collision) are corrected. Catalog validation now explicitly rejects resource-key collisions.
