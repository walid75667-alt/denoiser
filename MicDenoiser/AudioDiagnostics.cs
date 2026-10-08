namespace MicDenoiser;

public enum AudioConcern { Observing, NoEvidence, SourceLevel, InputGain, ProcessingLoad, OutputGaps, OutputPressure }
public readonly record struct DiagnosticSnapshot(AudioConcern Concern, int Windows, int SourceHotWindows, int GainHotWindows,
    int SlowWindows, int OutputPressureWindows, int Underruns, double WorstFrameMs);

/// <summary>Rolling 2.5-second evidence with session counters; does not infer model listening quality.</summary>
public sealed class AudioDiagnostics
{
    private readonly int[] _recent = new int[25];
    private int _position, _windows, _source, _gain, _slow, _pressure, _lastGaps, _gapAge = 25;
    private double _worst;
    public DiagnosticSnapshot Observe(float rawPeak, float gainedPeak, float limiterDb, double frameMs, int underruns)
    {
        if (!float.IsFinite(rawPeak) || !float.IsFinite(gainedPeak) || !float.IsFinite(limiterDb)
            || !double.IsFinite(frameMs) || rawPeak < 0 || gainedPeak < 0 || limiterDb < 0 || frameMs < 0 || underruns < 0)
            throw new ArgumentOutOfRangeException(nameof(rawPeak));
        int flags = 0;
        if (rawPeak >= .999f) { flags |= 1; _source++; }
        if (gainedPeak >= 1 && rawPeak < .999f) { flags |= 2; _gain++; }
        if (frameMs >= 10) { flags |= 4; _slow++; }
        if (limiterDb >= 6) { flags |= 8; _pressure++; }
        _recent[_position] = flags; _position = (_position + 1) % _recent.Length; _windows++;
        _worst = Math.Max(_worst, frameMs);
        _gapAge = underruns > _lastGaps ? 0 : Math.Min(25, _gapAge + 1); _lastGaps = underruns;
        int source = 0, gain = 0, slow = 0, pressure = 0;
        foreach (int value in _recent) { if ((value & 1) != 0) source++; if ((value & 2) != 0) gain++; if ((value & 4) != 0) slow++; if ((value & 8) != 0) pressure++; }
        var concern = source >= 2 ? AudioConcern.SourceLevel : gain >= 2 ? AudioConcern.InputGain
            : slow >= 5 ? AudioConcern.ProcessingLoad : _gapAge < 25 ? AudioConcern.OutputGaps
            : pressure >= 5 ? AudioConcern.OutputPressure : _windows < 10 ? AudioConcern.Observing : AudioConcern.NoEvidence;
        return new(concern, _windows, _source, _gain, _slow, _pressure, underruns, _worst);
    }
}
