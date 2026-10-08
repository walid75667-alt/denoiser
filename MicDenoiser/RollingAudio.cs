namespace MicDenoiser;

public sealed record AudioHistory(float[] Original, float[] Processed)
{
    public double Seconds => Original.Length / 48000.0;
}

/// <summary>Worker-owned, bounded PCM16-float recording. Snapshots must be made only
/// after the worker has stopped; no locks, file IO or allocations while collecting.</summary>
public sealed class RollingAudio
{
    private readonly float[] _original, _processed, _delayed;
    private readonly SampleDelay _delay;
    private int _position, _count, _skip;
    public RollingAudio(int frameSize, int delaySamples, int seconds = 30)
    {
        if (frameSize <= 0 || delaySamples < 0 || seconds is < 1 or > 30) throw new ArgumentOutOfRangeException(nameof(seconds));
        _original = new float[seconds * 48000]; _processed = new float[_original.Length];
        _delayed = new float[frameSize]; _delay = new SampleDelay(delaySamples); _skip = delaySamples;
    }
    public void Add(float[] original, float[] processed)
    {
        if (original.Length != _delayed.Length || processed.Length != original.Length) throw new ArgumentException("Frame size changed.");
        _delay.Process(original, _delayed);
        for (int i = 0; i < original.Length; i++)
        {
            if (_skip > 0) { _skip--; continue; }
            _original[_position] = _delayed[i]; _processed[_position] = processed[i];
            _position = (_position + 1) % _original.Length; _count = Math.Min(_count + 1, _original.Length);
        }
    }
    public AudioHistory SnapshotAfterStop()
    {
        var original = new float[_count]; var processed = new float[_count];
        int start = (_position - _count + _original.Length) % _original.Length;
        for (int i = 0; i < _count; i++)
        {
            int index = (start + i) % _original.Length;
            original[i] = _original[index]; processed[i] = _processed[index];
        }
        return new(original, processed);
    }
}
