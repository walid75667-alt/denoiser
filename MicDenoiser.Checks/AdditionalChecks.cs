using MicDenoiser;
namespace MicDenoiser.Checks;

internal static class AdditionalChecks
{
    public static void Register(Action<string, Action> check, Action<bool, string> require)
    {
        check("p99 counts individual frames, rare stalls, and a bounded rolling window", () =>
        {
            var timing = new FrameTiming();
            for (int i = 0; i < 3000; i++) timing.Observe(i < 31 ? 14.3 : 1.26);
            var snapshot = timing.Snapshot();
            require(snapshot.WindowFrames == 3000 && snapshot.OverBudgetFrames == 31 && snapshot.P99UpperBoundMs >= 14.3
                && snapshot.P99UpperBoundMs <= 14.326 && snapshot.SessionMaximumMs == 14.3, "Tail latency was hidden by averaging.");
            for (int i = 0; i < 31; i++) timing.Observe(1.26);
            snapshot = timing.Snapshot();
            require(snapshot.P99UpperBoundMs >= 1.26 && snapshot.P99UpperBoundMs <= 1.286 && snapshot.SessionMaximumMs == 14.3
                && snapshot.SessionFrames == 3031 && snapshot.WindowFrames == 3000, "Window expiry or session maximum is incorrect.");
            try { timing.Observe(double.NaN); throw new Exception("Non-finite timing accepted."); }
            catch (ArgumentOutOfRangeException) { }
        });
        check("rolling diagnostic audio keeps the latest samples aligned across ring wrap", () =>
        {
            foreach (int delay in new[] { 0, 517, 1920, 2400 })
            {
                var history = new RollingAudio(480, delay, 1);
                var alignment = new SampleDelay(delay);
                var input = new float[480]; var output = new float[480];
                for (int frame = 0; frame < 220; frame++)
                {
                    for (int i = 0; i < 480; i++) input[i] = frame * 480 + i;
                    alignment.Process(input, output); history.Add(input, output);
                }
                var snapshot = history.SnapshotAfterStop();
                require(snapshot.Original.Length == 48000 && snapshot.Seconds == 1 && snapshot.Original.SequenceEqual(snapshot.Processed), "Audio pair is misaligned or exceeds its bound.");
                require(snapshot.Original[0] == 220 * 480 - delay - 48000 && snapshot.Original[^1] == 220 * 480 - delay - 1, "History did not keep the latest second.");
                snapshot.Original[0] = -1;
                require(history.SnapshotAfterStop().Original[0] >= 0, "Snapshot shares the live ring.");
            }
            var shortHistory = new RollingAudio(480, 517);
            var first = new float[480]; var wet = new float[480];
            shortHistory.Add(first, wet);
            require(shortHistory.SnapshotAfterStop().Original.Length == 0, "Model startup delay appeared as recorded speech.");
            shortHistory.Add(first, wet);
            require(shortHistory.SnapshotAfterStop().Original.Length == 443, "Partial history length is incorrect.");
        });
        check("gate hysteresis protects weak syllables after onset and releases in silence", () =>
        {
            var settings = ProcessingSettings.FromPreset("calls");
            var gate = new VadGate(); var frame = new float[480];
            void Run(float vad) { Array.Fill(frame, 1000); gate.Process(frame, frame.Length, vad, settings); }
            for (int i = 0; i < 200; i++) Run(0);
            require(Math.Abs(20 * Math.Log10(gate.Gain) + 6) < .01, "Call gate floor exceeds its conservative limit.");
            for (int i = 0; i < 3; i++) Run(.6f);
            for (int i = 0; i < 100; i++) Run(i % 2 == 0 ? .24f : .32f);
            require(gate.Gain > .99f, "Weak syllables inside hysteresis were attenuated.");
            for (int i = 0; i < 20; i++) Run(0);
            require(gate.Gain > .99f, "A short pause closed the gate before hold elapsed.");
            for (int i = 0; i < 200; i++) Run(0);
            require(gate.Gain < .51f, "Silence never closes the gate.");
            require(!ProcessingSettings.FromPreset("singing").GateEnabled && !ProcessingSettings.FromPreset("whisper").GateEnabled, "Speech gate enabled for singing/whisper.");
        });
    }
}
