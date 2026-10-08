namespace MicDenoiser;

public readonly record struct FrameTimingSnapshot(long SessionFrames, int WindowFrames,
    double P99UpperBoundMs, double SessionMaximumMs, long OverBudgetFrames);

/// <summary>Last 3,000 real DSP frames (30 s at 48 kHz/480), 0.025 ms histogram bins.
/// Worker owned, fixed storage, no sorting/allocation in the audio callback.
/// P99 is a conservative bin upper bound; maximum is measured exactly for the session.</summary>
public sealed class FrameTiming
{
    private const double BinMs = .025;
    private const int OverflowBin = 2000; // >= 50 ms; p99 becomes session maximum in this bin.
    private readonly int[] _ring = new int[3000], _counts = new int[OverflowBin + 1];
    private int _position, _filled;
    private long _total, _overBudget;
    private double _maximum;
    public void Observe(double milliseconds)
    {
        if (!double.IsFinite(milliseconds) || milliseconds < 0) throw new ArgumentOutOfRangeException(nameof(milliseconds));
        int bin = milliseconds >= OverflowBin * BinMs ? OverflowBin : (int)(milliseconds / BinMs);
        if (_filled == _ring.Length) _counts[_ring[_position]]--; else _filled++;
        _ring[_position] = bin; _position = (_position + 1) % _ring.Length; _counts[bin]++;
        _total++; if (milliseconds >= 10) _overBudget++;
        _maximum = Math.Max(_maximum, milliseconds);
    }
    public FrameTimingSnapshot Snapshot()
    {
        if (_filled == 0) return default;
        int rank = (int)Math.Ceiling(.99 * _filled), seen = 0, bin = 0;
        for (; bin < _counts.Length; bin++) { seen += _counts[bin]; if (seen >= rank) break; }
        double p99 = bin >= OverflowBin ? _maximum : (bin + 1) * BinMs;
        return new(_total, _filled, p99, _maximum, _overBudget);
    }
}
