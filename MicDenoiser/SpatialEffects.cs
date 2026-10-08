namespace MicDenoiser;

/// <summary>Fixed-capacity delay with interpolated reads; no worker-thread allocations.</summary>
internal sealed class EffectDelay
{
    private readonly float[] _ring;
    private int _position;
    public EffectDelay(int capacity) => _ring = new float[capacity];
    public void Push(float sample)
    {
        _ring[_position] = Math.Abs(sample) < 1e-15f ? 0 : sample;
        if (++_position == _ring.Length) _position = 0;
    }
    public float Read(float samples)
    {
        int whole = (int)samples;
        float fraction = samples - whole;
        int index = _position - 1 - whole;
        if (index < 0) index += _ring.Length;
        int previous = index == 0 ? _ring.Length - 1 : index - 1;
        return _ring[index] + (_ring[previous] - _ring[index]) * fraction;
    }
}

/// <summary>Crossfades read heads instead of moving a delay tap abruptly or changing pitch.</summary>
internal sealed class DelayTap
{
    private float _current, _next, _requested;
    private int _remaining;
    private bool _initialized;
    private const int FadeSamples = 1440;
    public void SetTarget(float samples)
    {
        _requested = samples;
        if (!_initialized) { _current = _next = samples; _initialized = true; }
    }
    public float Read(EffectDelay delay)
    {
        if (_remaining == 0 && _requested != _current)
        { _next = _requested; _remaining = FadeSamples; }
        float value = delay.Read(_current);
        if (_remaining == 0) return value;
        float mix = 1 - _remaining / (float)FadeSamples;
        value += (delay.Read(_next) - value) * mix;
        if (--_remaining == 0) _current = _next;
        return value;
    }
}

/// <summary>Mono studio sends after dynamics and before the output ceiling. Dry onset is retained.</summary>
public sealed class SpatialEffects
{
    private readonly EffectDelay _echo = new(48002), _preDelay = new(4802), _chorus = new(2402);
    private readonly DelayTap _echoTap = new(), _preTap = new();
    private readonly SmoothedValue _echoSend = new(), _echoMix = new(), _feedback = new(50);
    private readonly SmoothedValue _reverbSend = new(), _reverbMix = new(), _damping = new(50);
    private readonly SmoothedValue _chorusSend = new(), _chorusMix = new(), _rate = new(50), _depth = new(50);
    private readonly ReverbComb[] _combs = { new(1499), new(1601), new(1747), new(1867) };
    private readonly ReverbAllPass _diffuser1 = new(225), _diffuser2 = new(556);
    private float _echoLow;
    private double _phase;

    public void Process(float[] samples, ProcessingSettings s)
    {
        bool echo = s.EchoEnabled && !s.Bypass, reverb = s.ReverbEnabled && !s.Bypass, chorus = s.ChorusEnabled && !s.Bypass;
        _echoSend.SetTarget(echo ? 1 : 0); _echoMix.SetTarget(echo ? s.EchoMix : 0);
        _reverbSend.SetTarget(reverb ? 1 : 0); _reverbMix.SetTarget(reverb ? s.ReverbMix : 0);
        _chorusSend.SetTarget(chorus ? 1 : 0); _chorusMix.SetTarget(chorus ? s.ChorusMix : 0);
        _echoTap.SetTarget(s.EchoDelayMs * 48 - 1); _preTap.SetTarget(s.ReverbPreDelayMs * 48);
        _feedback.SetTarget(s.EchoFeedback); _damping.SetTarget(s.ReverbDamping);
        _rate.SetTarget(s.ChorusRateHz); _depth.SetTarget(s.ChorusDepthMs * 48);
        foreach (var comb in _combs) comb.SetDecay(s.ReverbDecaySeconds);
        for (int i = 0; i < samples.Length; i++)
        {
            float dry = samples[i];
            // Read before writing: subtract one sample to retain the selected repeat interval.
            float repeat = _echoTap.Read(_echo);
            _echoLow += .35f * (repeat - _echoLow);
            if (Math.Abs(_echoLow) < 1e-15f) _echoLow = 0;
            _echo.Push(dry * _echoSend.Next() + _echoLow * _feedback.Next());
            _preDelay.Push(dry * _reverbSend.Next());
            float roomInput = _preTap.Read(_preDelay), room = 0, damping = _damping.Next();
            foreach (var comb in _combs) room += comb.Process(roomInput, damping);
            room = _diffuser2.Process(_diffuser1.Process(room * .25f));
            _chorus.Push(dry * _chorusSend.Next());
            float thick = _chorus.Read(15 * 48 + _depth.Next() * (float)Math.Sin(_phase));
            _phase += 2 * Math.PI * _rate.Next() / 48000;
            if (_phase >= 2 * Math.PI) _phase -= 2 * Math.PI;
            float blend = _chorusMix.Next();
            samples[i] = dry + (thick - dry) * blend + repeat * _echoMix.Next() + room * _reverbMix.Next();
        }
    }

    private sealed class ReverbComb
    {
        private readonly float[] _ring;
        private readonly SmoothedValue _gain = new(50);
        private int _position;
        private float _low;
        public ReverbComb(int size) => _ring = new float[size];
        public void SetDecay(float seconds) => _gain.SetTarget(MathF.Pow(10, -3 * _ring.Length / (seconds * 48000)));
        public float Process(float input, float damping)
        {
            float delayed = _ring[_position];
            _low = delayed + damping * (_low - delayed);
            if (Math.Abs(_low) < 1e-15f) _low = 0;
            float value = input + _low * _gain.Next();
            _ring[_position] = Math.Abs(value) < 1e-15f ? 0 : value;
            if (++_position == _ring.Length) _position = 0;
            return delayed;
        }
    }

    private sealed class ReverbAllPass
    {
        private readonly float[] _ring;
        private int _position;
        public ReverbAllPass(int size) => _ring = new float[size];
        public float Process(float input)
        {
            float delayed = _ring[_position];
            float value = input + delayed * .5f;
            _ring[_position] = Math.Abs(value) < 1e-15f ? 0 : value;
            if (++_position == _ring.Length) _position = 0;
            return delayed - value * .5f;
        }
    }
}
