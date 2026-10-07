using NAudio.Wave;

namespace MicDenoiser;

/// <summary>Preallocated, bounded capture. Only the audio worker writes sample buffers.</summary>
public sealed class ComparisonCapture
{
    private readonly SampleDelay _delay;
    private readonly float[] _aligned;
    private int _skip, _count, _state; // 0 recording, 1 complete, 2 canceled
    public float[] Original { get; }
    public float[] Processed { get; }
    public int SampleCount => Volatile.Read(ref _count);
    public bool IsComplete => Volatile.Read(ref _state) == 1;
    public bool IsCanceled => Volatile.Read(ref _state) == 2;
    public ComparisonCapture(int frameSize, int delaySamples, int sampleCount = 480000)
    {
        if (frameSize < 1 || delaySamples < 0 || sampleCount < 1 || sampleCount > 480000) throw new ArgumentOutOfRangeException(nameof(sampleCount));
        _delay = new SampleDelay(delaySamples); _skip = delaySamples; _aligned = new float[frameSize];
        Original = new float[sampleCount]; Processed = new float[sampleCount];
    }
    public void Cancel() => Interlocked.CompareExchange(ref _state, 2, 0);
    public bool Add(float[] original, float[] processed)
    {
        if (Volatile.Read(ref _state) != 0) return false;
        if (original.Length != _aligned.Length || processed.Length != _aligned.Length) throw new ArgumentException("Invalid comparison frame.");
        _delay.Process(original, _aligned);
        int start = Math.Min(_skip, original.Length); _skip -= start;
        int n = Math.Min(original.Length - start, Original.Length - _count);
        Array.Copy(_aligned, start, Original, _count, n); Array.Copy(processed, start, Processed, _count, n);
        Volatile.Write(ref _count, _count + n);
        return _count == Original.Length && Interlocked.CompareExchange(ref _state, 1, 0) == 0;
    }
}

public sealed record ComparisonResult(float[] Original, float[] Processed, double OriginalGain, double ProcessedGain,
    double OriginalLufs, double ProcessedLufs)
{
    public static ComparisonResult Create(ComparisonCapture capture)
    {
        if (!capture.IsComplete) throw new InvalidOperationException("Comparison is incomplete.");
        return Create(capture.Original, capture.Processed);
    }
    public static ComparisonResult Create(float[] original, float[] processed)
    {
        if (original.Length == 0 || original.Length != processed.Length || original.Length > 480000) throw new ArgumentException("Invalid paired audio.");
        double raw = IntegratedLoudness.Measure(original), wet = IntegratedLoudness.Measure(processed);
        // Attenuate to the quieter clip (and at most -20 LUFS); never boost quiet noise or clip.
        double target = Math.Min(-20, Math.Min(double.IsFinite(raw) ? raw : -20, double.IsFinite(wet) ? wet : -20));
        double a = double.IsFinite(raw) ? Math.Min(1, Math.Pow(10, (target - raw) / 20)) : 1;
        double b = double.IsFinite(wet) ? Math.Min(1, Math.Pow(10, (target - wet) / 20)) : 1;
        double peak = 0;
        for (int i = 0; i < original.Length; i++)
            peak = Math.Max(peak, Math.Max(Math.Abs(original[i] / 32768.0 * a), Math.Abs(processed[i] / 32768.0 * b)));
        double headroom = peak > .89 ? .89 / peak : 1;
        return new(original, processed, a * headroom, b * headroom, raw, wet);
    }
    public void SaveWaveFile(string path, bool original)
    {
        var samples = original ? Original : Processed;
        var normalized = new float[samples.Length];
        for (int i = 0; i < samples.Length; i++) normalized[i] = samples[i] / 32768;
        using var writer = new WaveFileWriter(path, WaveFormat.CreateIeeeFloatWaveFormat(48000, 1));
        writer.WriteSamples(normalized, 0, normalized.Length);
    }
    public ISampleProvider CreatePlayback(bool original) => new ComparisonSampleProvider(original ? Original : Processed,
        original ? OriginalGain : ProcessedGain);
}

/// <summary>Mono 48 kHz BS.1770 K weighting, 400 ms blocks, absolute/relative gating.</summary>
public static class IntegratedLoudness
{
    public static double Measure(float[] samples)
    {
        var prefix = new double[samples.Length + 1];
        double x1 = 0, x2 = 0, y1 = 0, y2 = 0, h1 = 0, h2 = 0, z1 = 0, z2 = 0;
        for (int i = 0; i < samples.Length; i++)
        {
            if (!float.IsFinite(samples[i])) throw new ArgumentException("Invalid comparison audio.");
            double x = samples[i] / 32768.0;
            double y = 1.53512485958697 * x - 2.69169618940638 * x1 + 1.19839281085285 * x2 + 1.69065929318241 * y1 - .73248077421585 * y2;
            x2 = x1; x1 = x; y2 = y1; y1 = y;
            double z = y - 2 * h1 + h2 + 1.99004745483398 * z1 - .99007225036621 * z2;
            h2 = h1; h1 = y; z2 = z1; z1 = z;
            prefix[i + 1] = prefix[i] + z * z;
        }
        var blocks = new List<double>();
        int length = Math.Min(19200, samples.Length);
        if (length == 0) return double.NegativeInfinity;
        for (int start = 0; start + length <= samples.Length; start += 4800)
        {
            double energy = (prefix[start + length] - prefix[start]) / length;
            if (energy > Math.Pow(10, (-70 + .691) / 10)) blocks.Add(energy);
        }
        if (blocks.Count == 0) return double.NegativeInfinity;
        double relative = blocks.Average() * .1;
        return -.691 + 10 * Math.Log10(blocks.Where(e => e >= relative).Average());
    }
}

public sealed class ComparisonSampleProvider : ISampleProvider
{
    private readonly float[] _samples; private readonly double _gain; private int _position;
    public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(48000, 1);
    public ComparisonSampleProvider(float[] samples, double gain) { _samples = samples; _gain = gain; }
    public int Read(float[] buffer, int offset, int count)
    {
        if (offset < 0 || count < 0 || offset > buffer.Length - count) throw new ArgumentOutOfRangeException(nameof(count));
        int n = Math.Min(count, _samples.Length - _position);
        for (int i = 0; i < n; i++)
        {
            int p = _position + i;
            double fade = Math.Min(1, Math.Min((p + 1) / 240.0, (_samples.Length - p) / 240.0));
            buffer[offset + i] = (float)(_samples[p] / 32768.0 * _gain * fade);
        }
        _position += n; return n;
    }
}
