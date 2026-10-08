namespace MicDenoiser;

/// <summary>Four fixed-allocation parametric bands with coefficient and bypass smoothing.</summary>
public sealed class ParametricEqualizer
{
    private readonly Biquad _low = new(), _body = new(), _presence = new(), _air = new();
    private readonly SmoothedValue _blend = new();
    public void Process(float[] samples, ProcessingSettings s)
    {
        _low.SetPeaking(48000, s.EqLowHz, s.EqLowQ, s.EqLowGainDb, smooth: true);
        _body.SetPeaking(48000, s.EqBodyHz, s.EqBodyQ, s.EqBodyGainDb, smooth: true);
        _presence.SetPeaking(48000, s.EqPresenceHz, s.EqPresenceQ, s.EqPresenceGainDb, smooth: true);
        _air.SetPeaking(48000, s.EqAirHz, s.EqAirQ, s.EqAirGainDb, smooth: true);
        _blend.SetTarget(s.EqEnabled ? 1 : 0);
        for (int i = 0; i < samples.Length; i++)
        {
            float original = samples[i];
            float equalized = _air.Process(_presence.Process(_body.Process(_low.Process(original))));
            float blend = _blend.Next();
            samples[i] = original + (equalized - original) * blend;
        }
    }
}

/// <summary>Complementary first-order split; only high-frequency audio is attenuated.</summary>
public sealed class DeEsser
{
    private readonly SmoothedValue _blend = new(), _coefficient = new(30), _threshold = new(30), _limit = new(30);
    private double _low;
    private float _highEnvelope, _fullEnvelope, _gainDb;
    public float GainReductionDb { get; private set; }
    public void Process(float[] samples, ProcessingSettings s)
    {
        _blend.SetTarget(s.DeEsserEnabled ? 1 : 0);
        _coefficient.SetTarget((float)Math.Exp(-2 * Math.PI * s.DeEsserHz / 48000));
        _threshold.SetTarget(s.DeEsserThresholdDb); _limit.SetTarget(s.DeEsserMaxReductionDb);
        const float detectAttack = .93291196f, detectRelease = .99739922f; // .3 / 8 ms
        const float gainAttack = .9896374f, gainRelease = .99965284f; // 2 / 60 ms
        GainReductionDb = 0;
        for (int i = 0; i < samples.Length; i++)
        {
            double input = samples[i], coefficient = _coefficient.Next();
            _low = input + coefficient * (_low - input);
            if (Math.Abs(_low) < 1e-20) _low = 0;
            float high = (float)(input - _low);
            float a = Math.Abs(high) / 32768, full = (float)Math.Abs(input) / 32768;
            _highEnvelope = a + (a > _highEnvelope ? detectAttack : detectRelease) * (_highEnvelope - a);
            _fullEnvelope = full + (full > _fullEnvelope ? detectAttack : detectRelease) * (_fullEnvelope - full);
            if (_highEnvelope < 1e-12f) _highEnvelope = 0;
            if (_fullEnvelope < 1e-12f) _fullEnvelope = 0;
            float threshold = _threshold.Next(), limit = _limit.Next();
            float over = 20 * MathF.Log10(Math.Max(1e-6f, _highEnvelope)) - threshold;
            // Gradually qualify HF-dominated material so crossing the detector ratio cannot click.
            float qualification = Math.Clamp((_highEnvelope / Math.Max(1e-6f, _fullEnvelope) - .35f) / .2f, 0, 1);
            float target = -Math.Min(limit, Math.Max(0, over) * .8f) * qualification;
            float decay = target < _gainDb ? gainAttack : gainRelease;
            _gainDb = target + decay * (_gainDb - target);
            if (Math.Abs(_gainDb) < 1e-8f) _gainDb = 0;
            float reduction = (1 - MathF.Pow(10, _gainDb / 20)) * _blend.Next();
            samples[i] = (float)input - high * reduction;
            GainReductionDb = Math.Max(GainReductionDb, -20 * MathF.Log10(Math.Max(1e-6f, 1 - reduction)));
        }
    }
}

/// <summary>Static target EQ/HP response for UI display; excludes nonlinear dynamics and denoising.</summary>
public static class EqualizerResponse
{
    public static double[] Curve(ProcessingSettings settings, int points = 128)
    {
        if (points < 2 || points > 4096) throw new ArgumentOutOfRangeException(nameof(points));
        var s = settings.SanitizedClone();
        var filters = new List<Biquad>();
        void Peak(double hz, double q, double db) { var f = new Biquad(); f.SetPeaking(48000, hz, q, db); filters.Add(f); }
        if (s.HighPassEnabled) { var highpass = new Biquad(); highpass.SetHighPass(48000, s.HighPassHz); filters.Add(highpass); }
        Peak(280, 1, -s.MudCutDb); Peak(3500, .9, s.PresenceDb);
        if (s.EqEnabled)
        {
            Peak(s.EqLowHz, s.EqLowQ, s.EqLowGainDb); Peak(s.EqBodyHz, s.EqBodyQ, s.EqBodyGainDb);
            Peak(s.EqPresenceHz, s.EqPresenceQ, s.EqPresenceGainDb); Peak(s.EqAirHz, s.EqAirQ, s.EqAirGainDb);
        }
        var curve = new double[points];
        for (int i = 0; i < points; i++)
        {
            double hz = 40 * Math.Pow(400, i / (double)(points - 1));
            foreach (var filter in filters) curve[i] += filter.ResponseDb(48000, hz);
        }
        return curve;
    }
}
