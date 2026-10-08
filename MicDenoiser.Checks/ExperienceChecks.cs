using MicDenoiser;
namespace MicDenoiser.Checks;
internal static class ExperienceChecks
{
    public static void Register(Action<string, Action> check, Action<bool, string> require)
    {
        check("mute silences dry and processed paths smoothly without reporting limiter reduction", () =>
        {
            foreach (bool bypass in new[] { false, true })
            {
                using var pipeline = new AudioPipeline(new DelayedIdentityEngine(960));
                var input = Enumerable.Repeat(8000f, 480).ToArray(); var output = new float[480];
                var settings = new ProcessingSettings { Bypass = bypass };
                for (int i = 0; i < 30; i++) pipeline.Process(input, output, settings);
                float previous = output[^1]; settings.Muted = true; float jump = 0; FrameMeters meters = default;
                for (int i = 0; i < 30; i++)
                {
                    meters = pipeline.Process(input, output, settings);
                    foreach (float sample in output) { jump = Math.Max(jump, Math.Abs(sample - previous)); previous = sample; }
                }
                require(jump < 40 && Math.Abs(previous) < .001f && meters.LimiterReductionDb == 0, "Mute clicks, leaks audio, or looks like limiting.");
                settings.Muted = false; jump = 0;
                for (int i = 0; i < 30; i++)
                {
                    pipeline.Process(input, output, settings);
                    foreach (float sample in output) { jump = Math.Max(jump, Math.Abs(sample - previous)); previous = sample; }
                }
                require(jump < 40 && Math.Abs(previous - 8000) < 1, "Unmute clicks or fails to recover.");
            }
        });
        check("background reduction waits for silence and clears on speech bypass mute or missing VAD", () =>
        {
            var estimate = new BackgroundReduction(); var settings = new ProcessingSettings();
            var quiet = new FrameMeters(0, 0, .01f, 1, DenoiseInputRms: .1f, DenoiseOutputRms: .01f);
            for (int i = 0; i < 69; i++) estimate.Observe(quiet, settings);
            require(estimate.Decibels == null, "Estimate appeared before sustained quiet.");
            estimate.Observe(quiet, settings);
            require(Math.Abs(estimate.Decibels!.Value - 20) < .001, "Estimate has wrong power ratio.");
            foreach (var changed in new[] { quiet with { Vad = .9f }, quiet with { Vad = null } })
            {
                estimate.Observe(changed, settings); require(estimate.Decibels == null, "Speech or missing evidence retained a score.");
            }
            foreach (var flags in new[] { new ProcessingSettings { Muted = true }, new ProcessingSettings { Bypass = true }, new ProcessingSettings { Strength = 0 }, new ProcessingSettings { FastSinging = true } })
            {
                for (int i = 0; i < 70; i++) estimate.Observe(quiet, flags);
                require(estimate.Decibels == null, "Ineligible signal generated an estimate.");
            }
        });
        check("noise meter compares aligned samples before output gain and effects", () =>
        {
            using var a = new AudioPipeline(new DelayedIdentityEngine(960));
            using var b = new AudioPipeline(new DelayedIdentityEngine(960));
            var plain = new ProcessingSettings(); var colored = new ProcessingSettings { OutputGainDb = 12, EqEnabled = true, EqPresenceGainDb = 6, CompressorOn = true };
            var input = new float[480]; var output = new float[480];
            for (int frame = 0; frame < 40; frame++)
            {
                for (int i = 0; i < input.Length; i++) input[i] = (frame % 7 + 1) * 1000 * MathF.Sin(i * .1f);
                var first = a.Process(input, output, plain); var second = b.Process(input, output, colored);
                require(Math.Abs(first.DenoiseInputRms - first.DenoiseOutputRms) < 1e-7, "Dry and wet meter paths are misaligned.");
                require(first.DenoiseInputRms == second.DenoiseInputRms && first.DenoiseOutputRms == second.DenoiseOutputRms, "Tone or output gain biases suppression estimate.");
            }
        });
        check("simple levels preserve routing gains and mute while balanced protects weak speech", () =>
        {
            var current = new ProcessingSettings { BufferMode = AudioBufferMode.Stable, InputGainDb = 3, OutputGainDb = -2, Bypass = true, Muted = true, ReverbEnabled = true, FastSinging = true };
            foreach (string level in new[] { "light", "balanced", "strong" })
            {
                var settings = SuppressionLevel.Create(level, current);
                require(settings.BufferMode == current.BufferMode && settings.InputGainDb == 3 && settings.OutputGainDb == -2 && settings.Bypass && settings.Muted && !settings.FastSinging && !settings.ReverbEnabled, "Level changed routing/gains/mute or retained studio effects.");
                require(level == "light" ? settings.Engine == DenoiserKind.RNNoise && !settings.GateEnabled : settings.Engine == DenoiserKind.DeepFilterNet3 && settings.GateEnabled, "Incorrect engine/gate choice.");
                require(SuppressionLevel.Matches(level, settings), "Applied level is not identified correctly.");
                var custom = settings.SanitizedClone(); custom.ReverbEnabled = true;
                require(!SuppressionLevel.Matches(level, custom), "Custom Studio effects are labeled as an isolation level.");
                if (level == "balanced") require(settings.GateDepthDb == 6 && settings.GateThreshold == .35f, "Balanced mode uses an aggressive gate.");
            }
        });
        check("per-module reset preserves routing mute bypass and unrelated effects", () =>
        {
            var current = ProcessingSettings.FromPreset("broadcast", DenoiserKind.RNNoise);
            current.BufferMode = AudioBufferMode.LowLatency; current.Bypass = current.Muted = current.ReverbEnabled = true; current.ReverbDecaySeconds = 3;
            current.CompAttackMs = 50; current.EqAirQ = 2;
            var reset = EffectsModule.Reset("compressor", current);
            require(!reset.CompressorOn && reset.CompAttackMs == 8 && reset.CompThresholdDb == -18, "Compressor not fully reset.");
            require(reset.Engine == current.Engine && reset.BufferMode == current.BufferMode && reset.Bypass && reset.Muted && reset.ReverbDecaySeconds == 3 && reset.EqAirQ == 2, "Reset touched another strip or routing.");
            foreach (string module in new[] { "eq", "gate", "input", "output", "deesser", "reverb", "echo", "chorus" })
                require(EffectsModule.Reset(module, current).Muted, "Reset lost mute state.");
        });
    }
}
