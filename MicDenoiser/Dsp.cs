namespace MicDenoiser;

public sealed class SmoothedValue
{
    private readonly double _decay;
    private double _value, _target;
    private bool _initialized;
    public SmoothedValue(double milliseconds = 20) => _decay = Math.Exp(-1.0 / (milliseconds * 48));
    public void SetTarget(float value)
    {
        _target = value;
        if (!_initialized) { _value = value; _initialized = true; }
    }
    public float Next(int samples = 1)
    {
        _value = _target + (_value - _target) * (samples == 1 ? _decay : Math.Pow(_decay, samples));
        // Settle far below audibility; float feedback can otherwise retain subnormal values forever.
        if (Math.Abs(_value - _target) < 1e-12 * Math.Max(1, Math.Abs(_target))) _value = _target;
        return (float)_value;
    }
}

/// <summary>RBJ-cookbook biquad filter (samples are floats in 16-bit range).</summary>
public sealed class Biquad
{
    private double _b0 = 1, _b1, _b2, _a1, _a2, _z1, _z2;

    private double _t0 = 1, _t1, _t2, _ta1, _ta2;
    private bool _smooth;
    private const double CoefficientDecay = .9993057966262922; // 30 ms at 48 kHz
    private void SetCoefficients(double b0, double b1, double b2, double a1, double a2, bool smooth)
    {
        _t0 = b0; _t1 = b1; _t2 = b2; _ta1 = a1; _ta2 = a2; _smooth = smooth;
        if (!smooth) { _b0 = b0; _b1 = b1; _b2 = b2; _a1 = a1; _a2 = a2; }
    }
    public double ResponseDb(double fs, double frequency)
    {
        var z = System.Numerics.Complex.FromPolarCoordinates(1, -2 * Math.PI * frequency / fs);
        var response = (_t0 + _t1 * z + _t2 * z * z) / (1 + _ta1 * z + _ta2 * z * z);
        return 20 * Math.Log10(Math.Max(1e-12, response.Magnitude));
    }

    public void SetHighPass(double fs, double f0, double q = 0.7071, bool smooth = false)
    {
        double w0 = 2 * Math.PI * f0 / fs;
        double cos = Math.Cos(w0);
        double alpha = Math.Sin(w0) / (2 * q);
        double a0 = 1 + alpha;
        SetCoefficients((1 + cos) / 2 / a0, -(1 + cos) / a0, (1 + cos) / 2 / a0, -2 * cos / a0, (1 - alpha) / a0, smooth);
    }

    public void SetPeaking(double fs, double f0, double q, double gainDb, bool smooth = false)
    {
        double a = Math.Pow(10, gainDb / 40);
        double w0 = 2 * Math.PI * f0 / fs;
        double cos = Math.Cos(w0);
        double alpha = Math.Sin(w0) / (2 * q);
        double a0 = 1 + alpha / a;
        SetCoefficients((1 + alpha * a) / a0, -2 * cos / a0, (1 - alpha * a) / a0, -2 * cos / a0, (1 - alpha / a) / a0, smooth);
    }

    public float Process(float x)
    {
        if (_smooth)
        {
            _b0 = _t0 + (_b0 - _t0) * CoefficientDecay; _b1 = _t1 + (_b1 - _t1) * CoefficientDecay;
            _b2 = _t2 + (_b2 - _t2) * CoefficientDecay; _a1 = _ta1 + (_a1 - _ta1) * CoefficientDecay; _a2 = _ta2 + (_a2 - _ta2) * CoefficientDecay;
        }
        double y = _b0 * x + _z1;
        _z1 = _b1 * x - _a1 * y + _z2;
        _z2 = _b2 * x - _a2 * y;
        if (Math.Abs(_z1) < 1e-20) _z1 = 0;
        if (Math.Abs(_z2) < 1e-20) _z2 = 0;
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
    private float _smoothedVad;
    private bool _open;
    private int _hold;

    public float Gain => _gain;

    public void Process(float[] x, int n, float vad, ProcessingSettings s)
    {
        float floor = MathF.Pow(10f, -s.GateDepthDb / 20f);
        int holdFrames = Math.Max(0, (int)(s.GateHoldMs / FrameMs));

        // Open immediately on speech evidence. A slower falling probability and separate
        // closing threshold protect weak syllables after a detected onset from VAD jitter.
        // This cannot identify a target speaker or recover speech missed by the detector.
        vad = float.IsFinite(vad) ? Math.Clamp(vad, 0, 1) : 1;
        _smoothedVad = vad >= _smoothedVad ? vad
            : vad + (_smoothedVad - vad) * MathF.Exp(-FrameMs / 30);
        float closeThreshold = s.GateThreshold * .65f;
        if (vad >= s.GateThreshold) { _open = true; _hold = holdFrames; }
        else if (_open && _smoothedVad >= closeThreshold) _hold = holdFrames;
        else if (_hold > 0) _hold--;
        else _open = false;
        float target = _open || _hold > 0 ? 1 : floor;

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
    private float _env, _gainDb;
    private readonly SmoothedValue _threshold = new(30), _ratio = new(30), _knee = new(30), _makeup = new(30), _attack = new(30), _release = new(30);
    public float GainReductionDb { get; private set; }
    public static float GainCurveDb(float levelDb, float thresholdDb, float ratio, float kneeDb)
    {
        float over = levelDb - thresholdDb, slope = 1 - 1 / Math.Max(1, ratio);
        if (kneeDb <= 0) return -Math.Max(0, over) * slope;
        if (over <= -kneeDb / 2) return 0;
        if (over >= kneeDb / 2) return -over * slope;
        return -slope * (over + kneeDb / 2) * (over + kneeDb / 2) / (2 * kneeDb);
    }
    public void Process(float[] x, int n, float thresholdDb, float ratio, float fs,
        float attackMs = 8, float releaseMs = 120, float kneeDb = 0, float makeupDb = 0)
    {
        _threshold.SetTarget(thresholdDb); _ratio.SetTarget(ratio); _knee.SetTarget(kneeDb);
        _makeup.SetTarget(MathF.Pow(10, makeupDb / 20)); _attack.SetTarget(attackMs); _release.SetTarget(releaseMs);
        float atk = MathF.Exp(-1 / (_attack.Next(n) * .001f * fs)), rel = MathF.Exp(-1 / (_release.Next(n) * .001f * fs));
        GainReductionDb = 0;
        for (int i = 0; i < n; i++)
        {
            float amplitude = MathF.Abs(x[i]) / 32768, c = amplitude > _env ? atk : rel;
            _env = amplitude + c * (_env - amplitude);
            if (_env < 1e-12f) _env = 0;
            float envDb = 20 * MathF.Log10(Math.Max(_env, 1e-6f));
            float target = GainCurveDb(envDb, _threshold.Next(), _ratio.Next(), _knee.Next());
            float gc = target < _gainDb ? atk : rel;
            _gainDb = target + gc * (_gainDb - target);
            if (Math.Abs(_gainDb) < 1e-8f) _gainDb = 0;
            GainReductionDb = Math.Max(GainReductionDb, -_gainDb);
            x[i] *= MathF.Pow(10, _gainDb / 20) * _makeup.Next();
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
        return Process(x, Ceiling);
    }
    public static float Process(float x, float ceiling)
    {
        float knee = ceiling * (Knee / Ceiling);
        float ax = MathF.Abs(x);
        if (ax <= knee) return x;
        float y = knee + (ceiling - knee) * MathF.Tanh((ax - knee) / (ceiling - knee));
        return x < 0 ? -y : y;
    }
}
