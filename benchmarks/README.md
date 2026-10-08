# Real speech regression benchmark

The nine small recordings in `speech/` are unmodified Free Spoken Digit Dataset
recordings by Zohar Jackson, Nicolas Turpault and Theo. Source files, immutable
revision and SHA-256 checksums are in `fixtures.json`. FSDD is licensed under
[CC BY-SA 4.0](https://creativecommons.org/licenses/by-sa/4.0/), as stated in
the [upstream README](https://github.com/Jakobovski/free-spoken-digit-dataset/tree/26eb9aaf76e81b692f806f9140c2d2777410d7a1).
Attribution: Jakobovski and FSDD contributors. Generated mixtures/recordings
remain under CC BY-SA 4.0. These fixtures are development assets and are not
included in the Windows application or installer.

The 36 deterministic scenes cover three voices, fan/pink/brown/keyboard+hum
noise, and -5/0/10 dB input SNR. All speech is real, isolated English digits at
8 kHz, linearly upsampled to 48 kHz. This tests repeatable regressions; it does
not establish conversation, Egyptian Arabic, singing or Krisp equivalence.

Metrics use known clean speech and pause masks: speech SI-SDR, RMS suppression
in pauses after gate hold/release, and worst 10 ms speech-frame loss caused by
the gate relative to the same denoiser with the gate off. Tone/compression are
disabled in both passes. Output uses Float32 WAVs so PCM16 quantization near
silence does not inflate measured gate speech loss. A 6 dB gate can still attenuate missed weak speech by
6 dB; hysteresis does not fix every VAD false negative.

`run.py` verifies the fixture checksums and compares each scene to the reviewed
`baseline.json`. Limits: at most 1 dB SI-SDR regression, 2 dB pause-suppression
regression and 1 dB additional gate speech loss, capped at 6.25 dB. Values are
regression tolerances, not a product quality certification. `--write-baseline`
is a deliberate maintainer action; CI never rewrites the baseline.

Linux VAD uses the checksum-verified pyrnnoise 0.4.5 shared library installed
by `scripts/prepare-benchmark.py`. Its provenance is known; the bundled Windows
DLL's original build revision is unknown. Actual Windows DLL execution and
960-sample impulse delay are tested separately by xUnit on Windows.

Run the Linux workflow commands from `.github/workflows/windows.yml` locally
after building the native engine and the checks project. Reports and optional
listening WAVs are written only under `artifacts/benchmark/`.
