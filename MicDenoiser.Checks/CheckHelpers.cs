using System.Diagnostics;
using MicDenoiser;
namespace MicDenoiser.Checks;

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
    public static void Enhance(string inputPath, string outputPath, DenoiserKind kind, bool callsGate = false, bool floatOutput = false)
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
        var input = new float[480]; var output = new float[480]; var result = new float[samples];
        long begin = Stopwatch.GetTimestamp();
        var settings = callsGate ? ProcessingSettings.FromPreset("calls", kind) : new ProcessingSettings { Engine = kind };
        // Benchmark denoiser + gate without tone/dynamics confounding speech-loss comparisons.
        settings.CompressorOn = false; settings.PresenceDb = 0;
        for (int offset = 0; offset < samples + delay; offset += 480)
        {
            for (int i = 0; i < 480; i++) input[i] = offset + i < samples ? BitConverter.ToInt16(data, (offset + i) * 2) : 0;
            pipeline.Process(input, output, settings);
            for (int i = 0; i < 480; i++)
            {
                int index = offset + i - delay;
                if (index >= 0 && index < samples) result[index] = Math.Clamp(output[i], short.MinValue, short.MaxValue);
            }
        }
        using var writer = new BinaryWriter(new FileStream(outputPath, FileMode.CreateNew));
        void Cc(string value) => writer.Write(System.Text.Encoding.ASCII.GetBytes(value));
        int width = floatOutput ? 4 : 2;
        Cc("RIFF"); writer.Write(36 + samples * width + (floatOutput ? 12 : 0)); Cc("WAVE"); Cc("fmt "); writer.Write(16);
        writer.Write((ushort)(floatOutput ? 3 : 1)); writer.Write((ushort)1); writer.Write(48000); writer.Write(48000 * width);
        writer.Write((ushort)width); writer.Write((ushort)(width * 8));
        if (floatOutput) { Cc("fact"); writer.Write(4); writer.Write(samples); }
        Cc("data"); writer.Write(samples * width);
        foreach (float value in result)
            if (floatOutput) writer.Write(value / 32768); else writer.Write((short)value);
        Console.WriteLine($"Enhanced {samples / 48000.0:0.00}s using {pipeline.EngineName} in {Stopwatch.GetElapsedTime(begin).TotalSeconds:0.00}s; compensated {delay / 48.0:0}ms delay.");
    }
}

sealed class CancelingEngine(CancellationTokenSource cancellation) : IDenoiseEngine
{
    private int _calls;
    public bool Disposed { get; private set; }
    public string Name => "Cancellation test";
    public int FrameSize => 480;
    public int DelaySamples => 0;
    public float? Process(float[] input,float[] output)
    {
        Array.Copy(input,output,480);
        if(++_calls==12)cancellation.Cancel();
        return null;
    }
    public void Dispose() => Disposed=true;
}
