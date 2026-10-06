namespace MicDenoiser;

public enum StabilityAdvice { Observing, Healthy, MoreBuffer, Lightweight, CheckDevice }

/// <summary>Evaluates five-second windows of 100 ms telemetry; never changes a live device.</summary>
public sealed class StabilityAdvisor
{
    private readonly DenoiserKind _engine;
    private readonly AudioBufferMode _mode;
    private int _observations, _nearBudget, _overBudget, _baselineUnderruns;
    public StabilityAdvice Current { get; private set; } = StabilityAdvice.Observing;
    public StabilityAdvisor(DenoiserKind engine, AudioBufferMode mode) { _engine=engine; _mode=mode; }
    public StabilityAdvice? Observe(double frameMs, int underruns)
    {
        if (!double.IsFinite(frameMs) || frameMs<0 || underruns<0) throw new ArgumentOutOfRangeException(nameof(frameMs));
        if (frameMs>=8) _nearBudget++;
        if (frameMs>=10) _overBudget++;
        if (++_observations<50) return null;
        int gaps=Math.Max(0,underruns-_baselineUnderruns);
        var next=_overBudget>=10 ? (_engine==DenoiserKind.DeepFilterNet3 ? StabilityAdvice.Lightweight : StabilityAdvice.CheckDevice)
            : gaps>=2 || _nearBudget>=10 ? (_mode==AudioBufferMode.Stable ? StabilityAdvice.CheckDevice : StabilityAdvice.MoreBuffer)
            : StabilityAdvice.Healthy;
        _observations=_nearBudget=_overBudget=0; _baselineUnderruns=underruns;
        if (next==Current) return null;
        Current=next; return next;
    }
}
