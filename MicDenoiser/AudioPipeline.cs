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

public readonly record struct FrameMeters(float InPeak, float OutPeak, float? Vad, float GateGain);

public sealed class AudioPipeline : IDisposable
{
    public const int SampleRate = 48000;
    private const int GateLookAheadSamples = 960;
    private readonly IDenoiseEngine _engine;
    private readonly SampleDelay _dryDelay, _bypassDelay, _lookAhead, _vadDelay;
    private readonly Biquad _hpf = new(), _mud = new(), _presence = new();
    private readonly VadGate _gate = new();
    private readonly Compressor _comp = new();
    private RNNoise? _vadDetector;
    private readonly float[] _input, _dry, _wet, _mixed, _bypass, _vadInput;
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
        float inGain = MathF.Pow(10f, s.InputGainDb / 20f), inPeak = 0, outPeak = 0;
        for (int i = 0; i < FrameSize; i++)
        {
            _input[i] = source[i] * inGain;
            inPeak = Math.Max(inPeak, Math.Abs(_input[i]) / 32768f);
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
        float? vad = _engine.Process(_input, _wet);
        if (vad is null && s.GateEnabled)
        {
            _vadDetector ??= new RNNoise();
            Array.Copy(_input, _vadInput, FrameSize);
            _vadNow[0] = _vadDetector.ProcessFrame(_vadInput);
            _vadDelay.Process(_vadNow, _vadAligned);
            vad = _vadAligned[0];
        }
        float wet = Math.Clamp(s.Strength, 0f, 1f);
        for (int i = 0; i < FrameSize; i++) _mixed[i] = wet * _wet[i] + (1f - wet) * _dry[i];
        _lookAhead.Process(_mixed, destination);
        // The newest aligned VAD opens the gate ahead of delayed audio onset.
        _gate.Process(destination, FrameSize, s.GateEnabled ? vad ?? 1f : 1f, s);
        if (s.MudCutDb != _lastMud) { _mud.SetPeaking(SampleRate, 280, 1, -s.MudCutDb); _lastMud = s.MudCutDb; }
        if (s.PresenceDb != _lastPresence) { _presence.SetPeaking(SampleRate, 3500, 0.9, s.PresenceDb); _lastPresence = s.PresenceDb; }
        for (int i = 0; i < FrameSize; i++)
        {
            if (s.MudCutDb > 0.05f) destination[i] = _mud.Process(destination[i]);
            if (s.PresenceDb > 0.05f) destination[i] = _presence.Process(destination[i]);
        }
        if (s.CompressorOn) _comp.Process(destination, FrameSize, s.CompThresholdDb, s.CompRatio, SampleRate);
        float outGain = MathF.Pow(10f, s.OutputGainDb / 20f), target = s.Bypass ? 1f : 0f;
        for (int i = 0; i < FrameSize; i++)
        {
            float blend = _bypassMix + (target - _bypassMix) * (i + 1) / FrameSize;
            destination[i] = Limiter.Process(destination[i] * outGain * (1f - blend) + _bypass[i] * blend);
            outPeak = Math.Max(outPeak, Math.Abs(destination[i]) / 32768f);
        }
        _bypassMix = target;
        return new FrameMeters(inPeak, outPeak, vad, _gate.Gain);
    }
    public void Dispose() { _vadDetector?.Dispose(); _engine.Dispose(); }
}
