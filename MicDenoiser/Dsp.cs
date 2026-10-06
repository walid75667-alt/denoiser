namespace MicDenoiser;

/// <summary>RBJ-cookbook biquad filter (samples are floats in 16-bit range).</summary>
public sealed class Biquad
{
    private double _b0 = 1, _b1, _b2, _a1, _a2, _z1, _z2;

    public void SetHighPass(double fs, double f0, double q = 0.7071)
    {
        double w0 = 2 * Math.PI * f0 / fs;
        double cos = Math.Cos(w0);
        double alpha = Math.Sin(w0) / (2 * q);
        double a0 = 1 + alpha;
        _b0 = (1 + cos) / 2 / a0;
        _b1 = -(1 + cos) / a0;
        _b2 = (1 + cos) / 2 / a0;
        _a1 = -2 * cos / a0;
        _a2 = (1 - alpha) / a0;
    }

    public void SetPeaking(double fs, double f0, double q, double gainDb)
    {
        double a = Math.Pow(10, gainDb / 40);
        double w0 = 2 * Math.PI * f0 / fs;
        double cos = Math.Cos(w0);
        double alpha = Math.Sin(w0) / (2 * q);
        double a0 = 1 + alpha / a;
        _b0 = (1 + alpha * a) / a0;
        _b1 = -2 * cos / a0;
        _b2 = (1 - alpha * a) / a0;
        _a1 = -2 * cos / a0;
        _a2 = (1 - alpha / a) / a0;
    }

    public float Process(float x)
    {
        double y = _b0 * x + _z1;
        _z1 = _b1 * x - _a1 * y + _z2;
        _z2 = _b2 * x - _a2 * y;
        return (float)y;
    }
}

/// <summary>
/// Voice-activity-driven downward expander. The neural denoiser's VAD output decides
/// whether the gate is open; a hold time bridges short pauses and the gain is smoothed
/// to avoid clicks.
/// </summary>
public sealed class VadGate
{
    private const float FrameMs = 10f;
    private const float AttackMs = 5f;

    private float _gain = 1f;
    private int _hold;

    public float Gain => _gain;

    public void Process(float[] x, int n, float vad, ProcessingSettings s)
    {
        float floor = MathF.Pow(10f, -s.GateDepthDb / 20f);
        int holdFrames = Math.Max(0, (int)(s.GateHoldMs / FrameMs));

        float target;
        if (vad >= s.GateThreshold) { _hold = holdFrames; target = 1f; }
        else if (_hold > 0) { _hold--; target = 1f; }
        else target = floor;

        float tau = target > _gain ? AttackMs : Math.Max(10f, s.GateReleaseMs);
        float coef = 1f - MathF.Exp(-FrameMs / tau);
        float newGain = _gain + (target - _gain) * coef;

        // linear ramp across the frame → no zipper noise
        for (int i = 0; i < n; i++)
        {
            float g = _gain + (newGain - _gain) * (i + 1) / n;
            x[i] *= g;
        }
        _gain = newGain;
    }
}

/// <summary>Feed-forward peak compressor with smoothed gain.</summary>
public sealed class Compressor
{
    private float _env;
    private float _gainDb;

    public void Process(float[] x, int n, float thresholdDb, float ratio, float fs)
    {
        float atk = MathF.Exp(-1f / (0.008f * fs));   // 8 ms
        float rel = MathF.Exp(-1f / (0.120f * fs));   // 120 ms
        float slope = 1f - 1f / Math.Max(1f, ratio);

        for (int i = 0; i < n; i++)
        {
            float a = MathF.Abs(x[i]) / 32768f;
            float c = a > _env ? atk : rel;
            _env = a + c * (_env - a);

            float envDb = 20f * MathF.Log10(Math.Max(_env, 1e-5f));
            float over = envDb - thresholdDb;
            float targetDb = over > 0f ? -over * slope : 0f;

            float gc = targetDb < _gainDb ? atk : rel;
            _gainDb = targetDb + gc * (_gainDb - targetDb);

            x[i] *= MathF.Pow(10f, _gainDb / 20f);
        }
    }
}

public static class Limiter
{
    private const float Knee = 0.80f * 32768f;
    private const float Ceiling = 0.944f * 32768f;   // ≈ -0.5 dBFS

    /// <summary>Soft-knee limiter — never exceeds the ceiling.</summary>
    public static float Process(float x)
    {
        float ax = MathF.Abs(x);
        if (ax <= Knee) return x;
        float y = Knee + (Ceiling - Knee) * MathF.Tanh((ax - Knee) / (Ceiling - Knee));
        return x < 0 ? -y : y;
    }
}
