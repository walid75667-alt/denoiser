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

Check("interface catalogs and XAML resource references are complete", () =>
{
    string assets = Path.Combine(AppContext.BaseDirectory, "UiValidation");
    var ar = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(assets, "ar.json")))!;
    var en = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(assets, "en.json")))!;
    Require(ar.Keys.Order().SequenceEqual(en.Keys.Order()), "A language catalog is missing keys.");
    foreach (string key in ar.Keys)
    {
        Require(!string.IsNullOrWhiteSpace(ar[key]) && !string.IsNullOrWhiteSpace(en[key]), "Empty translation: " + key);
        Require(System.Text.CompositeFormat.Parse(ar[key]).MinimumArgumentCount == System.Text.CompositeFormat.Parse(en[key]).MinimumArgumentCount,
            "Translation format arguments differ: " + key);
    }
    var xkey = System.Xml.Linq.XName.Get("Key", "http://schemas.microsoft.com/winfx/2006/xaml");
    var application = System.Xml.Linq.XDocument.Load(Path.Combine(assets, "App.xaml"));
    var staticKeys = application.Descendants().SelectMany(e => e.Attributes(xkey)).Select(a => a.Value).ToHashSet();
    foreach (string file in new[] { "MainWindow.xaml", "App.xaml" })
    {
        foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(
                     File.ReadAllText(Path.Combine(assets, file)), @"\{(StaticResource|DynamicResource) ([^}]+)\}"))
            Require(match.Groups[1].Value == "StaticResource" ? staticKeys.Contains(match.Groups[2].Value) : ar.ContainsKey(match.Groups[2].Value),
                "Unresolved XAML resource: " + match.Value);
    }
});

Check("output starvation and recovery avoid abrupt zero-fill clicks", () =>
{
    byte[] Tone(short value, int samples)
    {
        var bytes = new byte[samples * 2];
        for (int i = 0; i < samples; i++) System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(i * 2, 2), value);
        return bytes;
    }
    var old = new BufferedWaveProvider(new WaveFormat(48000, 16, 1));
    var block = Tone(12000, 480); old.AddSamples(block, 0, block.Length);
    var buffer = new byte[960]; old.Read(buffer, 0, buffer.Length); short before = BitConverter.ToInt16(buffer, 958);
    old.Read(buffer, 0, buffer.Length);
    Require(Math.Abs(before - BitConverter.ToInt16(buffer, 0)) == 12000, "Original discontinuity was not reproduced.");

    var source = new BufferedWaveProvider(new WaveFormat(48000, 16, 1));
    var output = new StablePlaybackProvider(source, 10);
    source.AddSamples(block, 0, block.Length); output.Read(buffer, 0, buffer.Length);
    before = BitConverter.ToInt16(buffer, 958);
    output.Read(buffer, 0, buffer.Length);
    for (int i = 0; i < buffer.Length; i += 2)
    {
        short sample = BitConverter.ToInt16(buffer, i);
        Require(Math.Abs(sample - before) <= 51, "Starvation caused a click-sized sample jump."); before = sample;
    }
    Require(before == 0 && output.Underruns == 1, "Gap did not decay to silence/count once.");
    output.Read(buffer, 0, buffer.Length);
    Require(output.Underruns == 1, "A single gap was counted repeatedly.");
    block = Tone(-12000, 480); source.AddSamples(block, 0, block.Length);
    output.Read(buffer, 0, buffer.Length);
    for (int i = 0; i < buffer.Length; i += 2)
    {
        short sample = BitConverter.ToInt16(buffer, i);
        Require(Math.Abs(sample - before) <= 51, "Recovery caused an abrupt sample jump."); before = sample;
    }
    Require(before == -12000, "Recovery did not return to full level.");
});

Check("playback rebuffering preserves queued input and respects offsets", () =>
{
    var source = new BufferedWaveProvider(new WaveFormat(48000, 16, 1));
    var output = new StablePlaybackProvider(source, 60);
    var data = new byte[480 * 2]; source.AddSamples(data, 0, data.Length);
    var destination = Enumerable.Repeat((byte)0xa5, 1000).ToArray();
    output.Read(destination, 7, 960);
    Require(source.BufferedBytes == 960 && output.Underruns == 0, "Startup consumed undersized queued input or counted an underrun.");
    Require(destination.Take(7).All(v => v == 0xa5) && destination.Skip(967).All(v => v == 0xa5), "Playback overwrote bytes outside requested range.");
    for (int i = 0; i < 5; i++) source.AddSamples(data, 0, data.Length);
    output.Read(destination, 7, 960);
    Require(source.BufferedBytes == 4800, "Buffered input did not resume at target occupancy.");
});

Check("gain and compressor changes are continuous across frames", () =>
{
    using var pipeline = new AudioPipeline(new DelayedIdentityEngine(0));
    var input = Enumerable.Repeat(1000f, 480).ToArray(); var output = new float[480];
    var settings = new ProcessingSettings();
    for (int frame = 0; frame < 20; frame++) pipeline.Process(input, output, settings);
    float previous = output[^1], largestJump = 0;
    settings.InputGainDb = settings.OutputGainDb = 12;
    for (int frame = 0; frame < 20; frame++)
    {
        pipeline.Process(input, output, settings);
        foreach (float sample in output) { largestJump = Math.Max(largestJump, Math.Abs(sample - previous)); previous = sample; }
    }
    Require(largestJump < 100 && output[^1] > 14000, $"Gain transition jumped {largestJump:0.0} samples or did not reach target.");
    settings.CompressorOn = true; settings.CompThresholdDb = -40;
    for (int frame = 0; frame < 30; frame++) pipeline.Process(input, output, settings);
    previous = output[^1]; settings.CompressorOn = false;
    largestJump = 0;
    for (int frame = 0; frame < 20; frame++)
    {
        pipeline.Process(input, output, settings);
        foreach (float sample in output) { largestJump = Math.Max(largestJump, Math.Abs(sample - previous)); previous = sample; }
    }
    Require(largestJump < 100, "Disabling the compressor jumped the output level.");
});

Check("persisted settings cannot inject nonfinite/extreme audio parameters", () =>
{
    var s = new ProcessingSettings { Engine = (DenoiserKind)99, BufferMode = (AudioBufferMode)99,
        Strength = float.NaN, NoiseReductionDb = float.PositiveInfinity, InputGainDb = 1000, OutputGainDb = float.NaN }.SanitizedClone();
    Require(s.Engine == DenoiserKind.DeepFilterNet3 && s.BufferMode == AudioBufferMode.Balanced &&
        s.Strength == 1 && s.NoiseReductionDb == 35 && s.InputGainDb == 18 && s.OutputGainDb == 0, "Invalid settings were not bounded.");
});

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

Check("DeepFilterNet attenuation control retains more detail at lower limits", () =>
{
    using var mild = new DeepFilterNetEngine(); using var strong = new DeepFilterNetEngine();
    var a = new ProcessingSettings { NoiseReductionDb = 10 }; var b = new ProcessingSettings { NoiseReductionDb = 60 };
    var input = new float[480]; var x = new float[480]; var y = new float[480]; var random = new Random(75667);
    double mildEnergy = 0, strongEnergy = 0;
    for (int frame = 0; frame < 200; frame++)
    {
        for (int i = 0; i < input.Length; i++) input[i] = (float)(random.NextDouble() * 2 - 1) * 2000;
        mild.Configure(a); strong.Configure(b); mild.Process(input, x); strong.Process(input, y);
        if (frame < 100) continue;
        for (int i = 0; i < x.Length; i++) { mildEnergy += x[i] * x[i]; strongEnergy += y[i] * y[i]; }
    }
    Require(mildEnergy > strongEnergy * 20 && mildEnergy > 0, "Native attenuation control did not change suppression strength.");
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
