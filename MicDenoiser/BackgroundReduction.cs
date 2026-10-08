namespace MicDenoiser;

/// <summary>Level estimate during sustained low VAD, not an acoustic quality score.
/// Inputs are delay aligned and measured before tone/dynamics/output gain.</summary>
public sealed class BackgroundReduction
{
    private int _quietFrames;
    private double _inputEnergy, _outputEnergy;
    public double? Decibels { get; private set; }
    public void Observe(FrameMeters meters, ProcessingSettings settings)
    {
        if (settings.Bypass || settings.Muted || settings.FastSinging || settings.Strength <= 0 || meters.Vad is not < .1f)
        { _quietFrames = 0; _inputEnergy = _outputEnergy = 0; Decibels = null; return; }
        if (++_quietFrames <= 50) { Decibels = null; return; }
        _inputEnergy = .95 * _inputEnergy + .05 * meters.DenoiseInputRms * meters.DenoiseInputRms;
        _outputEnergy = .95 * _outputEnergy + .05 * meters.DenoiseOutputRms * meters.DenoiseOutputRms;
        Decibels = _quietFrames < 70 || _inputEnergy < 1e-8 ? null
            : Math.Clamp(10 * Math.Log10(_inputEnergy / Math.Max(1e-12, _outputEnergy)), -20, 80);
    }
}
