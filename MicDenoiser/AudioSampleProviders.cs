using System.IO;
using NAudio.Dsp;
using NAudio.Wave;

namespace MicDenoiser;

public sealed class DownmixSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly float[] _scratch;
    private readonly int _channels;
    public WaveFormat WaveFormat { get; }
    public DownmixSampleProvider(ISampleProvider source)
    {
        _source = source; _channels = source.WaveFormat.Channels;
        _scratch = new float[4096 * _channels];
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, 1);
    }
    public int Read(float[] buffer, int offset, int count)
    {
        int read = _source.Read(_scratch, 0, Math.Min(count, 4096) * _channels);
        if (read % _channels != 0) throw new InvalidDataException("Incomplete microphone sample.");
        int frames = read / _channels;
        for (int i = 0; i < frames; i++)
        {
            float value = 0;
            for (int ch = 0; ch < _channels; ch++) value += _scratch[i * _channels + ch];
            buffer[offset + i] = value / _channels;
        }
        return frames;
    }
}

/// <summary>Feed WDL only available input; temporary capture starvation is not end-of-file.</summary>
public sealed class StreamingResamplingSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly Func<int> _availableInputFrames;
    private readonly WdlResampler _resampler = new();
    public WaveFormat WaveFormat { get; }
    public StreamingResamplingSampleProvider(ISampleProvider source, Func<int> availableInputFrames, int rate)
    {
        if (source.WaveFormat.Channels != 1) throw new ArgumentException("Resample mono audio after downmixing.");
        _source = source; _availableInputFrames = availableInputFrames;
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(rate, 1);
        _resampler.SetMode(true, 2, false);
        _resampler.SetFilterParms();
        _resampler.SetFeedMode(true);
        _resampler.SetRates(source.WaveFormat.SampleRate, rate);
    }
    public int Read(float[] buffer, int offset, int count)
    {
        int available = Math.Min(_availableInputFrames(), _source.WaveFormat.SampleRate / 10);
        int needed = _resampler.ResamplePrepare(available, 1, out float[] input, out int inputOffset);
        int read = 0;
        while (read < needed)
        {
            int n = _source.Read(input, inputOffset + read, needed - read);
            if (n == 0) throw new InvalidDataException("Capture buffer unexpectedly lost queued audio.");
            read += n;
        }
        // In feed mode read == needed even when both are zero, so WDL never flushes a live gap.
        return _resampler.ResampleOut(buffer, offset, read, count, 1);
    }
}
