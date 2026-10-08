using System.Buffers.Binary;
using NAudio.Wave;

namespace MicDenoiser;

/// <summary>Bounded rebuffering with a short decay on starvation and fade-in on recovery.</summary>
public sealed class StablePlaybackProvider : IWaveProvider
{
    private readonly BufferedWaveProvider _source;
    private readonly int _resumeBytes, _fadeSamples;
    private bool _primed;
    private int _fadeInPosition, _decayPosition, _underruns;
    private float _lastSample, _decayStart;
    public WaveFormat WaveFormat => _source.WaveFormat;
    public int Underruns => Volatile.Read(ref _underruns);

    public StablePlaybackProvider(BufferedWaveProvider source, int resumeMilliseconds)
    {
        if (source.WaveFormat.Channels != 1 || source.WaveFormat.BitsPerSample != 16 || source.WaveFormat.Encoding != WaveFormatEncoding.Pcm)
            throw new ArgumentException("Playback protection expects mono PCM16.");
        _source = source;
        _source.ReadFully = false;
        _resumeBytes = source.WaveFormat.SampleRate * Math.Clamp(resumeMilliseconds, 10, 120) / 1000 * 2;
        _fadeSamples = Math.Max(1, source.WaveFormat.SampleRate / 200); // 5 ms
        _decayPosition = _fadeSamples;
    }

    public int Read(byte[] buffer, int offset, int count)
    {
        if (count < 0 || offset < 0 || offset > buffer.Length - count || count % 2 != 0)
            throw new ArgumentOutOfRangeException(nameof(count));
        if (count == 0) return 0;
        if (!_primed && _source.BufferedBytes >= _resumeBytes)
        {
            _primed = true;
            _fadeInPosition = 0;
        }
        int read = _primed ? _source.Read(buffer, offset, count) : 0;
        for (int position = 0; position < read; position += 2)
        {
            float value = BinaryPrimitives.ReadInt16LittleEndian(buffer.AsSpan(offset + position, 2));
            if (_fadeInPosition < _fadeSamples)
            {
                // Continue any unfinished decay when input returns inside the 5 ms fade.
                float mix = ++_fadeInPosition / (float)_fadeSamples;
                value = value * mix + DecaySample() * (1f - mix);
            }
            Write(buffer, offset + position, value);
            _lastSample = value;
        }
        if (read < count && _primed)
        {
            _primed = false;
            _decayStart = _lastSample;
            _decayPosition = 0;
            Interlocked.Increment(ref _underruns);
        }
        for (int position = read; position < count; position += 2)
        {
            _lastSample = DecaySample();
            Write(buffer, offset + position, _lastSample);
        }
        return count; // Keep the endpoint clock running; do not flush/restart WASAPI on a gap.
    }

    private float DecaySample()
    {
        if (_decayPosition >= _fadeSamples) return 0;
        return _decayStart * (1f - ++_decayPosition / (float)_fadeSamples);
    }
    private static void Write(byte[] buffer, int offset, float value) =>
        BinaryPrimitives.WriteInt16LittleEndian(buffer.AsSpan(offset, 2), (short)Math.Clamp(value, short.MinValue, short.MaxValue));
}
