namespace MicDenoiser;

// A preallocated sample delay, also used to align dry and bypass with model output.
public sealed class SampleDelay
{
    private readonly float[] _ring;
    private int _position;
    public SampleDelay(int samples)
    {
        if (samples < 0) throw new ArgumentOutOfRangeException(nameof(samples));
        _ring = new float[samples];
    }
    public void Process(float[] input, float[] output)
    {
        if (input.Length != output.Length) throw new ArgumentException("Delay buffer lengths differ.");
        for (int i = 0; i < input.Length; i++)
        {
            float sample = input[i];
            if (_ring.Length == 0) { output[i] = sample; continue; }
            output[i] = _ring[_position];
            _ring[_position] = sample;
            _position = (_position + 1) % _ring.Length;
        }
    }
}

public readonly record struct FrameMeters(float InPeak, float OutPeak, float? Vad, float GateGain, float InRms = 0, float OutRms = 0, float CompressorReductionDb = 0, float DeEsserReductionDb = 0, float LimiterReductionDb = 0);

public sealed class AudioPipeline : IDisposable
{
    public const int SampleRate = 48000;
    private const int GateLookAheadSamples = 960;
    private readonly IDenoiseEngine _engine;
    private readonly SampleDelay _dryDelay, _bypassDelay, _lookAhead, _vadDelay;
    private readonly Biquad _hpf = new(), _mud = new(), _presence = new();
    private readonly VadGate _gate = new();
    private readonly Compressor _comp = new();
    private readonly ParametricEqualizer _eq = new();
    private readonly DeEsser _deEsser = new();
    private readonly SpatialEffects _spatial = new();
    private readonly SmoothedValue _ceiling = new(30);
    private RNNoise? _vadDetector;
    private readonly float[] _input, _dry, _wet, _mixed, _bypass, _vadInput, _compressed;
    private readonly SmoothedValue _inputGain = new(), _outputGain = new(), _strength = new();
    private readonly SmoothedValue _mudDb = new(30), _presenceDb = new(30), _compBlend = new();
    private readonly float[] _vadNow = new float[1], _vadAligned = new float[1];
    private float _lastMud = float.NaN, _lastPresence = float.NaN, _bypassMix, _highPassMix;
    public int FrameSize => _engine.FrameSize;
    public string EngineName => _engine.Name;
    public double AlgorithmicDelayMs => (_engine.DelaySamples + GateLookAheadSamples) * 1000.0 / SampleRate;

    public AudioPipeline(IDenoiseEngine engine)
    {
        _engine = engine;
        _input = new float[FrameSize]; _dry = new float[FrameSize]; _wet = new float[FrameSize];
        _mixed = new float[FrameSize]; _bypass = new float[FrameSize]; _vadInput = new float[FrameSize];
        _compressed = new float[FrameSize];
        _dryDelay = new SampleDelay(engine.DelaySamples);
        _bypassDelay = new SampleDelay(engine.DelaySamples + GateLookAheadSamples);
        _lookAhead = new SampleDelay(GateLookAheadSamples);
        // Optional RNNoise VAD is a parallel detector; its audio is discarded.
        _vadDelay = new SampleDelay(Math.Max(0, engine.DelaySamples / FrameSize - 1));
        _hpf.SetHighPass(SampleRate, 70);
    }

    public FrameMeters Process(float[] source, float[] destination, ProcessingSettings s)
    {
        if (source.Length != FrameSize || destination.Length != FrameSize)
            throw new ArgumentException("Invalid audio frame size.");
        _inputGain.SetTarget(MathF.Pow(10f, s.InputGainDb / 20f));
        _outputGain.SetTarget(MathF.Pow(10f, s.OutputGainDb / 20f));
        _strength.SetTarget(Math.Clamp(s.Strength, 0f, 1f));
        _compBlend.SetTarget(s.CompressorOn ? 1f : 0f);
        _ceiling.SetTarget(MathF.Pow(10, s.OutputCeilingDb / 20) * 32768);
        _hpf.SetHighPass(SampleRate, s.HighPassHz, smooth: true);
        _mudDb.SetTarget(s.MudCutDb); _presenceDb.SetTarget(s.PresenceDb);
        float inPeak = 0, outPeak = 0, limiterReduction = 0, compBlend = 0;
        double inputEnergy = 0, outputEnergy = 0;
        for (int i = 0; i < FrameSize; i++)
        {
            if (!float.IsFinite(source[i])) throw new ArgumentException("Invalid microphone sample.");
            _input[i] = source[i] * _inputGain.Next();
            inPeak = Math.Max(inPeak, Math.Abs(_input[i]) / 32768f);
            inputEnergy += (double)_input[i] * _input[i] / (32768.0 * 32768);
        }
        _bypassDelay.Process(_input, _bypass);
        float highPassTarget = s.HighPassEnabled ? 1f : 0f;
        for (int i = 0; i < FrameSize; i++)
        {
            float filtered = _hpf.Process(_input[i]);
            float blend = _highPassMix + (highPassTarget - _highPassMix) * (i + 1) / FrameSize;
            _input[i] = _input[i] * (1f - blend) + filtered * blend;
        }
        _highPassMix = highPassTarget;
        _dryDelay.Process(_input, _dry);
        _engine.Configure(s);
        float? vad = _engine.Process(_input, _wet);
        if (vad is null && s.GateEnabled)
        {
            _vadDetector ??= new RNNoise();
            Array.Copy(_input, _vadInput, FrameSize);
            _vadNow[0] = _vadDetector.ProcessFrame(_vadInput);
            _vadDelay.Process(_vadNow, _vadAligned);
            vad = _vadAligned[0];
        }
        for (int i = 0; i < FrameSize; i++)
        {
            float wet = _strength.Next();
            _mixed[i] = wet * _wet[i] + (1f - wet) * _dry[i];
        }
        _lookAhead.Process(_mixed, destination);
        // The newest aligned VAD opens the gate ahead of delayed audio onset.
        _gate.Process(destination, FrameSize, s.GateEnabled ? vad ?? 1f : 1f, s);
        float mud = _mudDb.Next(FrameSize), presence = _presenceDb.Next(FrameSize);
        if (mud != _lastMud) { _mud.SetPeaking(SampleRate, 280, 1, -mud, smooth: true); _lastMud = mud; }
        if (presence != _lastPresence) { _presence.SetPeaking(SampleRate, 3500, 0.9, presence, smooth: true); _lastPresence = presence; }
        for (int i = 0; i < FrameSize; i++)
        {
            destination[i] = _presence.Process(_mud.Process(destination[i]));
        }
        _eq.Process(destination, s);
        _deEsser.Process(destination, s);
        Array.Copy(destination, _compressed, FrameSize);
        _comp.Process(_compressed, FrameSize, s.CompThresholdDb, s.CompRatio, SampleRate, s.CompAttackMs, s.CompReleaseMs, s.CompKneeDb, s.CompMakeupDb);
        for (int i = 0; i < FrameSize; i++)
        {
            float comp = compBlend = _compBlend.Next();
            destination[i] += (_compressed[i] - destination[i]) * comp;
        }
        _spatial.Process(destination, s);
        float target = s.Bypass ? 1f : 0f;
        for (int i = 0; i < FrameSize; i++)
        {
            float blend = _bypassMix + (target - _bypassMix) * (i + 1) / FrameSize;
            float unlimited = destination[i] * _outputGain.Next() * (1f - blend) + _bypass[i] * blend;
            destination[i] = Limiter.Process(unlimited, _ceiling.Next());
            if (Math.Abs(unlimited) > 1 && Math.Abs(destination[i]) < Math.Abs(unlimited))
                limiterReduction = Math.Max(limiterReduction, 20 * MathF.Log10(Math.Abs(unlimited) / Math.Max(1e-6f, Math.Abs(destination[i]))));
            outputEnergy += (double)destination[i] * destination[i] / (32768.0 * 32768);
            outPeak = Math.Max(outPeak, Math.Abs(destination[i]) / 32768f);
        }
        _bypassMix = target;
        return new FrameMeters(inPeak, outPeak, vad, _gate.Gain, (float)Math.Sqrt(inputEnergy / FrameSize), (float)Math.Sqrt(outputEnergy / FrameSize),
            s.Bypass ? 0 : _comp.GainReductionDb * compBlend, s.Bypass ? 0 : _deEsser.GainReductionDb, limiterReduction);
    }
    public void Dispose() { _vadDetector?.Dispose(); _engine.Dispose(); }
}
