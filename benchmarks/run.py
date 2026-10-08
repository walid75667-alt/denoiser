"""Deterministic real-speech/noise regression benchmark; not a perceptual quality score."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import subprocess
import struct
import sys
import wave

import numpy as np

ROOT = Path(__file__).resolve().parent
RATE = 48000


def read_wave(path):
    raw = Path(path).read_bytes()
    if raw[:4] != b"RIFF" or raw[8:12] != b"WAVE": raise ValueError("Invalid WAV")
    offset = 12; fmt = None; payload = None
    while offset + 8 <= len(raw):
        name = raw[offset:offset + 4]; size = struct.unpack_from("<I", raw, offset + 4)[0]
        chunk = raw[offset + 8:offset + 8 + size]
        if len(chunk) != size: raise ValueError("Truncated WAV")
        if name == b"fmt ": fmt = struct.unpack_from("<HHIIHH", chunk)
        if name == b"data": payload = chunk
        offset += 8 + size + size % 2
    if fmt is None or payload is None or fmt[1] != 1: raise ValueError("Expected mono WAV")
    format_id, channels, rate, _, _, bits = fmt
    if (format_id, bits) == (1, 16): samples = np.frombuffer(payload, dtype="<i2").astype(np.float64) / 32768
    elif (format_id, bits) == (3, 32): samples = np.frombuffer(payload, dtype="<f4").astype(np.float64)
    else: raise ValueError("Expected PCM16 or Float32 WAV")
    if not np.all(np.isfinite(samples)): raise ValueError("Non-finite rendered audio")
    if rate != RATE:
        samples = np.interp(np.arange(round(len(samples) * RATE / rate)) * rate / RATE,
                            np.arange(len(samples)), samples)
    return samples


def write_wave(path, samples):
    if np.max(np.abs(samples)) >= 1:
        raise ValueError("Scene would clip before denoising")
    with wave.open(str(path), "wb") as wav:
        wav.setparams((1, 2, RATE, len(samples), "NONE", "not compressed"))
        wav.writeframes(np.round(samples * 32767).astype("<i2").tobytes())


def rms(samples):
    return float(np.sqrt(np.mean(samples * samples)))


def db_ratio(a, b):
    return float(20 * np.log10(max(1e-10, rms(a)) / max(1e-10, rms(b))))


def si_sdr(reference, estimate):
    reference = reference - np.mean(reference)
    estimate = estimate - np.mean(estimate)
    projection = reference * np.sum(reference * estimate) / max(1e-12, np.sum(reference * reference))
    return float(10 * np.log10(max(1e-12, np.sum(projection * projection)) /
                              max(1e-12, np.sum((estimate - projection) ** 2))))


def noise_scene(kind, length, random):
    t = np.arange(length) / RATE
    white = random.normal(size=length)
    if kind == "fan":
        return .7 * np.sin(2 * np.pi * 93 * t) + .3 * np.sin(2 * np.pi * 186 * t) + .15 * white
    if kind == "pink":
        spectrum = np.fft.rfft(white)
        spectrum /= np.sqrt(np.maximum(1, np.arange(len(spectrum))))
        return np.fft.irfft(spectrum, n=length)
    if kind == "brown":
        # Deterministic leaky integration in the frequency domain; no unbounded DC drift.
        spectrum = np.fft.rfft(white)
        spectrum /= np.maximum(1, np.arange(len(spectrum)))
        return np.fft.irfft(spectrum, n=length)
    result = .2 * white + .4 * np.sin(2 * np.pi * 50 * t)
    for index in range(RATE // 2, length - 1200, RATE // 3):
        result[index:index + 1200] += random.normal(size=1200) * np.exp(-np.arange(1200) / 150) * 8
    return result


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--checks", required=True, help="Built MicDenoiser.Checks.dll")
    parser.add_argument("--dotnet", default="dotnet")
    parser.add_argument("--output", default="artifacts/benchmark")
    parser.add_argument("--write-baseline", action="store_true", help="Review changes before committing a new baseline")
    args = parser.parse_args()
    output = Path(args.output); output.mkdir(parents=True, exist_ok=True)
    fixtures = json.loads((ROOT / "fixtures.json").read_text())
    for fixture in fixtures["sources"]:
        if hashlib.sha256((ROOT / fixture["file"]).read_bytes()).hexdigest() != fixture["sha256"]:
            raise ValueError("Speech fixture checksum mismatch: " + fixture["file"])
    rows = []
    for speaker in ("jackson", "nicolas", "theo"):
        utterances = [read_wave(ROOT / "speech" / f"{digit}_{speaker}_0.wav") for digit in (0, 1, 2)]
        clean_parts = [np.zeros(RATE)]; speech_parts = [np.zeros(RATE, dtype=bool)]
        pause_parts = [np.zeros(RATE, dtype=bool)]
        for samples in utterances:
            samples = samples * .06 / max(1e-8, rms(samples))
            clean_parts.extend((samples, np.zeros(2 * RATE)))
            speech_parts.extend((np.abs(samples) > .0003, np.zeros(2 * RATE, dtype=bool)))
            # Measure pause noise after hold/release and avoid the onset of the next word.
            pause_parts.extend((np.zeros(len(samples), dtype=bool), np.r_[np.zeros(RATE, dtype=bool),
                               np.ones(RATE // 2, dtype=bool), np.zeros(RATE // 2, dtype=bool)]))
        clean = np.concatenate(clean_parts); speech = np.concatenate(speech_parts); pauses = np.concatenate(pause_parts)
        for kind_index, kind in enumerate(("fan", "pink", "brown", "keyboard_hum")):
            for snr in (-5, 0, 10):
                random = np.random.Generator(np.random.PCG64(75667 + kind_index))
                noise = noise_scene(kind, len(clean), random)
                noise *= rms(clean[speech]) / rms(noise[speech]) / (10 ** (snr / 20))
                reference = clean.copy()
                noisy = reference + noise
                # Keep the measured SNR while lowering the whole scene to avoid clipping.
                gain = min(1, .8 / max(1e-8, float(np.max(np.abs(noisy)))))
                reference *= gain; noise *= gain; noisy *= gain
                name = f"{speaker}_{kind}_{snr}"
                source = output / (name + "_input.wav"); write_wave(source, noisy)
                rendered = []
                for gated in (False, True):
                    path = output / (name + ("_gate.wav" if gated else "_native.wav"))
                    # CLI refuses to overwrite input/output; remove only this generated output.
                    path.unlink(missing_ok=True)
                    command = [args.dotnet, args.checks, "--enhance", str(source), str(path), "DeepFilterNet3", "--float"]
                    if gated: command.append("--calls-gate")
                    subprocess.run(command, check=True, stdout=subprocess.DEVNULL)
                    rendered.append(read_wave(path))
                native, gated = rendered
                losses = []
                for start in range(0, len(clean) - 480 + 1, 480):
                    frame = slice(start, start + 480)
                    if np.count_nonzero(speech[frame]) >= 240 and rms(native[frame]) > 1e-5:
                        losses.append(db_ratio(native[frame], gated[frame]))
                row = dict(scene=name, input_si_sdr_db=si_sdr(reference[speech], noisy[speech]),
                           native_si_sdr_db=si_sdr(reference[speech], native[speech]),
                           gate_si_sdr_db=si_sdr(reference[speech], gated[speech]),
                           native_pause_reduction_db=db_ratio(noisy[pauses], native[pauses]),
                           gate_pause_reduction_db=db_ratio(noisy[pauses], gated[pauses]),
                           worst_gate_speech_loss_db=max(losses, default=0))
                rows.append(row); print(json.dumps(row), flush=True)
    report = dict(seed=75667, numpy_version=np.__version__, output_format="Float32 to avoid PCM16 quantization bias in quiet speech-loss metrics", fixture_revision=fixtures["revision"],
                  linux_rnnoise="pyrnnoise 0.4.5; does not establish Windows DLL provenance",
                  speech="Three speakers, isolated digits, 8 kHz upsampled to 48 kHz; not full-band conversation",
                  scenes=rows)
    (output / "report.json").write_text(json.dumps(report, indent=2) + "\n")
    baseline_path = ROOT / "baseline.json"
    if args.write_baseline:
        baseline_path.write_text(json.dumps(report, indent=2) + "\n")
    else:
        baseline = json.loads(baseline_path.read_text())
        if baseline["fixture_revision"] != report["fixture_revision"]:
            raise ValueError("Fixture changed without baseline review")
        previous = {row["scene"]: row for row in baseline["scenes"]}
        if set(previous) != {row["scene"] for row in rows}: raise ValueError("Scene set changed")
        failures = []
        for row in rows:
            old = previous[row["scene"]]
            for metric, tolerance in (("native_si_sdr_db", 1), ("gate_si_sdr_db", 1),
                                      ("native_pause_reduction_db", 2), ("gate_pause_reduction_db", 2)):
                if row[metric] < old[metric] - tolerance: failures.append(row["scene"] + ": " + metric)
            if row["worst_gate_speech_loss_db"] > min(6.25, old["worst_gate_speech_loss_db"] + 1):
                failures.append(row["scene"] + ": gate speech loss")
        if failures: raise AssertionError("Benchmark regressions: " + ", ".join(failures))
    print(f"Benchmark complete: {len(rows)} real-speech scenes", flush=True)


if __name__ == "__main__":
    main()
