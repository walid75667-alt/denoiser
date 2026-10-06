using System.Diagnostics;
using MicDenoiser;
using NAudio.Wave;

if (args.Length > 0)
{
    if (args.Length != 4 || args[0] != "--enhance" || !Enum.TryParse<DenoiserKind>(args[3], true, out var kind))
        throw new ArgumentException("Usage: --enhance input.wav output.wav DeepFilterNet3|RNNoise");
    WavEnhancer.Enhance(args[1], args[2], kind);
    return;
}

int passed = 0;
void Check(string name, Action test) { test(); Console.WriteLine("PASS " + name); passed++; }
void Require(bool value, string message) { if (!value) throw new Exception(message); }

Check("live resampling survives fragmented capture and empty gaps", () =>
{
    foreach (int rate in new[] { 16000, 44100, 96000 })
    {
        float[] Render(bool fragmented)
        {
            const int frames = 8891;
            var source = new BufferedWaveProvider(WaveFormat.CreateIeeeFloatWaveFormat(rate, 2)) { ReadFully = false };
            var mono = new DownmixSampleProvider(source.ToSampleProvider());
            var resampler = new StreamingResamplingSampleProvider(mono, () => source.BufferedBytes / 8, 48000);
            var output = new float[480]; var result = new List<float>();
            void Drain()
            {
                int n;
                while ((n = resampler.Read(output, 0, output.Length)) > 0) result.AddRange(output.Take(n));
                Require(resampler.Read(output, 0, output.Length) == 0, "Capture starvation synthesized extra audio.");
            }
            for (int offset = 0; offset < frames;)
            {
                int count = Math.Min(fragmented ? 13 + offset % 733 : frames, frames - offset);
                var samples = new float[count * 2];
                for (int i = 0; i < count; i++)
                {
                    samples[i * 2] = .2f * MathF.Sin((offset + i) * .11f);
                    samples[i * 2 + 1] = .1f * MathF.Cos((offset + i) * .07f);
                }
                var bytes = new byte[samples.Length * 4];
                Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
                source.AddSamples(bytes, 0, bytes.Length);
                offset += count; Drain();
            }
            int expected = (int)(frames * 48000.0 / rate);
            Require(result.Count >= expected - 32 && result.Count <= expected + 1, "Resampling changed the stream duration.");
            return result.ToArray();
        }
        var bulk = Render(false); var fragmented = Render(true);
        Require(bulk.Length == fragmented.Length, "Capture chunk size changed output duration.");
        for (int i = 0; i < bulk.Length; i++)
            Require(Math.Abs(bulk[i] - fragmented[i]) < .0001f, "Capture gaps changed the resampled waveform.");
    }
});

Check("sample delays across frame boundaries", () =>
{
    foreach (int delay in new[] { 0, 17, 480, 960, 1440, 2400 })
    {
        var ring = new SampleDelay(delay); var x = new float[480]; var y = new float[480];
        for (int frame = 0; frame < 10; frame++)
        {
            for (int i = 0; i < x.Length; i++) x[i] = frame * 480 + i + 1;
            ring.Process(x, y);
            for (int i = 0; i < y.Length; i++)
                Require(y[i] == Math.Max(0, frame * 480 + i + 1 - delay), "Incorrect sample delay.");
        }
    }
});

Check("wet/dry alignment for a delayed engine", () =>
{
    using var dry = new AudioPipeline(new DelayedIdentityEngine(1440));
    using var mixed = new AudioPipeline(new DelayedIdentityEngine(1440));
    using var wet = new AudioPipeline(new DelayedIdentityEngine(1440));
    var input = new float[480]; var a = new float[480]; var b = new float[480]; var c = new float[480];
    for (int frame = 0; frame < 20; frame++)
    {
        for (int i = 0; i < 480; i++) input[i] = 2000 * MathF.Sin((frame * 480 + i) * 0.1f);
        dry.Process(input, a, new ProcessingSettings { Strength = 0 });
        mixed.Process(input, b, new ProcessingSettings { Strength = 0.37f });
        wet.Process(input, c, new ProcessingSettings { Strength = 1 });
        for (int i = 0; i < 480; i++)
            Require(Math.Abs(a[i] - b[i]) < 0.001f && Math.Abs(a[i] - c[i]) < 0.001f, "Dry and wet paths are misaligned.");
    }
});

Check("bypass preserves latency and the raw waveform", () =>
{
    using var pipeline = new AudioPipeline(new DelayedIdentityEngine(1440));
    var input = new float[480]; var output = new float[480];
    int delay = (int)(pipeline.AlgorithmicDelayMs * 48);
    for (int frame = 0; frame < 20; frame++)
    {
        for (int i = 0; i < 480; i++) input[i] = Signal(frame * 480 + i);
        pipeline.Process(input, output, new ProcessingSettings { Bypass = true });
        for (int i = 0; i < 480; i++)
        {
            int source = frame * 480 + i - delay;
            Require(Math.Abs(output[i] - (source < 0 ? 0 : Signal(source))) < 0.001f, "Bypass path is not aligned.");
        }
    }
});

Check("DeepFilterNet native loading, metadata, and silence", () =>
{
    using var engine = new DeepFilterNetEngine();
    Require(engine.FrameSize == 480 && engine.DelaySamples == 1440, "Unexpected pinned model metadata.");
    var input = new float[480]; var output = new float[480];
    for (int frame = 0; frame < 30; frame++)
    {
        Require(engine.Process(input, output) is null, "SNR was exposed as VAD.");
        Require(output.All(v => float.IsFinite(v) && Math.Abs(v) < 0.01f), "Silence produced invalid/non-silent output.");
    }
});

Check("DeepFilterNet processes and suppresses stationary noise", () =>
{
    using var pipeline = new AudioPipeline(new DeepFilterNetEngine());
    var input = new float[480]; var output = new float[480]; var random = new Random(75667);
    double inputEnergy = 0, outputEnergy = 0; var times = new List<double>();
    for (int frame = 0; frame < 400; frame++)
    {
        for (int i = 0; i < 480; i++) input[i] = (float)(random.NextDouble() * 2 - 1) * 2000;
        long begin = Stopwatch.GetTimestamp();
        var meters = pipeline.Process(input, output, new ProcessingSettings());
        double elapsed = Stopwatch.GetElapsedTime(begin).TotalMilliseconds;
        Require(meters.Vad is null, "Disabled gate unexpectedly ran a voice detector.");
        Require(output.All(float.IsFinite), "Invalid processed audio.");
        if (frame < 100) continue;
        times.Add(elapsed);
        for (int i = 0; i < 480; i++) { inputEnergy += input[i] * input[i]; outputEnergy += output[i] * output[i]; }
    }
    double ratio = Math.Sqrt(outputEnergy / inputEnergy);
    Require(ratio < 0.75, $"Stationary noise not reduced: RMS ratio {ratio:0.000}.");
    times.Sort();
    string reduction = ratio == 0 ? "output is silent" : $"RMS reduction {20 * Math.Log10(ratio):0.0} dB";
    Console.WriteLine($"  Noise-only check: {reduction}; frame mean {times.Average():0.00} ms, p95 {times[(int)(times.Count * .95)]:0.00} ms (this host only).");
});

Check("model integrity errors are actionable", () =>
{
    string path = Path.GetTempFileName();
    try
    {
        File.WriteAllText(path, "invalid model");
        try { using var engine = new DeepFilterNetEngine(path); throw new Exception("Corrupt model accepted."); }
        catch (InvalidDataException) { }
    }
    finally { File.Delete(path); }
});
if (OperatingSystem.IsWindows())
{
    Check("RNNoise fallback and DeepFilterNet optional sidechain VAD", () =>
    {
        var input = new float[480]; var output = new float[480];
        using (var engine = new RNNoiseEngine())
        {
            var vad = engine.Process(input, output);
            Require(vad is >= 0f and <= 1f && output.All(float.IsFinite), "RNNoise returned invalid audio or VAD.");
        }
        using var pipeline = new AudioPipeline(new DeepFilterNetEngine());
        var meters = pipeline.Process(input, output, new ProcessingSettings { GateEnabled = true });
        Require(meters.Vad is >= 0f and <= 1f && output.All(float.IsFinite), "Parallel VAD did not run.");
    });
}
else Console.WriteLine("SKIP Windows RNNoise binary and optional sidechain VAD (Windows DLL).");
Console.WriteLine($"{passed} checks passed. Live Windows audio and speech quality need separate validation.");

static float Signal(int sample) => 1000 * MathF.Sin(sample * .13f);

sealed class DelayedIdentityEngine(int delay) : IDenoiseEngine
{
    private readonly SampleDelay _delay = new(delay);
    public string Name => "test";
    public int FrameSize => 480;
    public int DelaySamples => delay;
    public float? Process(float[] input, float[] output) { _delay.Process(input, output); return null; }
    public void Dispose() { }
}

static class WavEnhancer
{
    public static void Enhance(string inputPath, string outputPath, DenoiserKind kind)
    {
        if (Path.GetFullPath(inputPath) == Path.GetFullPath(outputPath)) throw new ArgumentException("Use a different output file.");
        if (File.Exists(outputPath)) throw new IOException("Output exists; choose a new filename.");
        using var reader = new BinaryReader(File.OpenRead(inputPath));
        string FourCc() => new(reader.ReadChars(4));
        if (FourCc() != "RIFF") throw new InvalidDataException("Expected RIFF WAV.");
        reader.ReadUInt32();
        if (FourCc() != "WAVE") throw new InvalidDataException("Expected WAVE.");
        byte[]? data = null; bool validFormat = false;
        while (reader.BaseStream.Position + 8 <= reader.BaseStream.Length)
        {
            string id = FourCc(); uint size = reader.ReadUInt32(); long end = reader.BaseStream.Position + size;
            if (end > reader.BaseStream.Length) throw new InvalidDataException("Truncated WAV.");
            if (id == "fmt ")
            {
                if (size < 16) throw new InvalidDataException("Invalid WAV format.");
                ushort format = reader.ReadUInt16(), channels = reader.ReadUInt16(); uint rate = reader.ReadUInt32();
                reader.ReadUInt32(); reader.ReadUInt16(); ushort bits = reader.ReadUInt16();
                validFormat = format == 1 && channels == 1 && rate == 48000 && bits == 16;
            }
            if (id == "data") data = reader.ReadBytes(checked((int)size));
            reader.BaseStream.Position = end + (size % 2);
        }
        if (!validFormat || data is null || data.Length % 2 != 0)
            throw new InvalidDataException("Use 48kHz mono PCM16 WAV.");
        using var pipeline = new AudioPipeline(kind == DenoiserKind.DeepFilterNet3 ? new DeepFilterNetEngine() : new RNNoiseEngine());
        int samples = data.Length / 2, delay = (int)(pipeline.AlgorithmicDelayMs * 48);
        var input = new float[480]; var output = new float[480]; var result = new short[samples];
        long begin = Stopwatch.GetTimestamp();
        for (int offset = 0; offset < samples + delay; offset += 480)
        {
            for (int i = 0; i < 480; i++) input[i] = offset + i < samples ? BitConverter.ToInt16(data, (offset + i) * 2) : 0;
            pipeline.Process(input, output, new ProcessingSettings { Engine = kind });
            for (int i = 0; i < 480; i++)
            {
                int index = offset + i - delay;
                if (index >= 0 && index < samples) result[index] = (short)Math.Clamp(output[i], short.MinValue, short.MaxValue);
            }
        }
        using var writer = new BinaryWriter(new FileStream(outputPath, FileMode.CreateNew));
        void Cc(string value) => writer.Write(System.Text.Encoding.ASCII.GetBytes(value));
        Cc("RIFF"); writer.Write(36 + samples * 2); Cc("WAVE"); Cc("fmt "); writer.Write(16);
        writer.Write((ushort)1); writer.Write((ushort)1); writer.Write(48000); writer.Write(96000);
        writer.Write((ushort)2); writer.Write((ushort)16); Cc("data"); writer.Write(samples * 2);
        foreach (short value in result) writer.Write(value);
        Console.WriteLine($"Enhanced {samples / 48000.0:0.00}s using {pipeline.EngineName} in {Stopwatch.GetElapsedTime(begin).TotalSeconds:0.00}s; compensated {delay / 48.0:0}ms delay.");
    }
}
