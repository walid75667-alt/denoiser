using System.Diagnostics;
using System.IO;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace MicDenoiser;

public readonly record struct MeterData(float InPeak, float OutPeak, float? Vad, float GateGain,
    bool Bypass, double FrameMs, double QueuedMs, int Underruns);

/// <summary>WASAPI capture only queues audio; a dedicated worker resamples and denoises it.</summary>
public sealed class NoiseSuppressor : IDisposable
{
    private readonly MMDeviceEnumerator _devices = new();
    private readonly MMDevice _inputDevice, _outputDevice;
    private readonly WasapiCapture _capture;
    private readonly WasapiOut _playback;
    private readonly BufferedWaveProvider _captureBuffer, _outputBuffer;
    private readonly ISampleProvider _samples;
    private readonly AudioPipeline _pipeline;
    private readonly StablePlaybackProvider _monitor;
    private readonly AutoResetEvent _available = new(false);
    private readonly Thread _worker;
    private ProcessingSettings _settings;
    private ComparisonCapture? _comparison;
    private readonly StabilityAdvisor _advisor;
    private long _lastInputAt;
    private int _running, _faulted, _playbackStarted, _disposed;
    public event Action<MeterData>? Meters;
    public event Action<Exception>? Failed;
    public event Action<ComparisonCapture>? ComparisonCompleted;
    public event Action<StabilityAdvice>? AdviceChanged;
    public string EngineName => _pipeline.EngineName;
    public double AlgorithmicDelayMs => _pipeline.AlgorithmicDelayMs;
    public int PlaybackBufferTargetMs { get; }
    public string InputFormatDescription => $"{_capture.WaveFormat.SampleRate / 1000.0:0.#} kHz / {_capture.WaveFormat.Channels} channels";

    public NoiseSuppressor(string inputId, string outputId, ProcessingSettings settings)
    {
        _settings = settings.SanitizedClone();
        _advisor = new StabilityAdvisor(_settings.Engine, _settings.BufferMode);
        PlaybackBufferTargetMs = _settings.BufferMode switch { AudioBufferMode.Stable => 100, AudioBufferMode.LowLatency => 30, _ => 60 };
        IDenoiseEngine engine = _settings.Engine == DenoiserKind.DeepFilterNet3
            ? new DeepFilterNetEngine() : new RNNoiseEngine();
        _pipeline = new AudioPipeline(engine);
        try
        {
            // Initialize model/DSP buffers and optional VAD before starting capture.
            var silent = new float[_pipeline.FrameSize]; var discard = new float[_pipeline.FrameSize];
            for (int i = 0; i < 10; i++) _pipeline.Process(silent, discard, _settings);
            _inputDevice = _devices.GetDevice(inputId);
            _outputDevice = _devices.GetDevice(outputId);
            _capture = new WasapiCapture(_inputDevice, true, 10);
            _captureBuffer = new BufferedWaveProvider(_capture.WaveFormat)
            {
                BufferDuration = TimeSpan.FromMilliseconds(200),
                ReadFully = false,
                DiscardOnBufferOverflow = false
            };
            ISampleProvider mono = new DownmixSampleProvider(_captureBuffer.ToSampleProvider());
            _samples = mono.WaveFormat.SampleRate == AudioPipeline.SampleRate
                ? mono : new StreamingResamplingSampleProvider(mono,
                    () => _captureBuffer.BufferedBytes / _captureBuffer.WaveFormat.BlockAlign, AudioPipeline.SampleRate);
            _outputBuffer = new BufferedWaveProvider(new WaveFormat(AudioPipeline.SampleRate, 16, 1))
            {
                BufferDuration = TimeSpan.FromMilliseconds(400),
                ReadFully = false,
                DiscardOnBufferOverflow = false
            };
            _monitor = new StablePlaybackProvider(_outputBuffer, PlaybackBufferTargetMs);
            _playback = new WasapiOut(_outputDevice, AudioClientShareMode.Shared, true,
                _settings.BufferMode switch { AudioBufferMode.Stable => 60, AudioBufferMode.LowLatency => 20, _ => 40 });
            _playback.Init(_monitor);
            _capture.DataAvailable += OnData;
            _capture.RecordingStopped += (_, e) => { if (e.Exception != null) ReportFailure(e.Exception); };
            _playback.PlaybackStopped += (_, e) => { if (e.Exception != null) ReportFailure(e.Exception); };
            _worker = new Thread(ProcessAudio) { IsBackground = true, Name = "MicDenoiser audio", Priority = ThreadPriority.AboveNormal };
        }
        catch
        {
            foreach (var resource in new IDisposable?[] { _capture, _playback, _inputDevice, _outputDevice, _pipeline, _devices, _available })
                try { resource?.Dispose(); } catch { } // Preserve the constructor's original failure.
            throw;
        }
    }

    public void UpdateSettings(ProcessingSettings settings) => Volatile.Write(ref _settings, settings.SanitizedClone());
    public ComparisonCapture BeginComparison()
    {
        if (Volatile.Read(ref _running) == 0) throw new InvalidOperationException("Audio processing is stopped.");
        var capture = new ComparisonCapture(_pipeline.FrameSize, (int)Math.Round(AlgorithmicDelayMs * 48));
        if (Interlocked.CompareExchange(ref _comparison, capture, null) != null) throw new InvalidOperationException("A comparison is already recording.");
        return capture;
    }
    public void CancelComparison() => Interlocked.Exchange(ref _comparison, null)?.Cancel();
    public void Start()
    {
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0) return;
        Interlocked.Exchange(ref _lastInputAt, Stopwatch.GetTimestamp());
        _worker.Start();
        try { _capture.StartRecording(); }
        catch { Stop(); throw; }
    }
    public void Stop()
    {
        CancelComparison();
        Volatile.Write(ref _running, 0);
        _available.Set();
        try { _capture.StopRecording(); }
        finally
        {
            if (_worker.IsAlive && Thread.CurrentThread != _worker) _worker.Join();
            _playback.Stop();
        }
    }
    private void OnData(object? sender, WaveInEventArgs e)
    {
        if (Volatile.Read(ref _running) == 0) return;
        try
        {
            if (e.BytesRecorded > 0) Interlocked.Exchange(ref _lastInputAt, Stopwatch.GetTimestamp());
            _captureBuffer.AddSamples(e.Buffer, 0, e.BytesRecorded);
            _available.Set();
        }
        catch (Exception ex) { ReportFailure(ex); }
    }
    private void ReportFailure(Exception error)
    {
        Volatile.Write(ref _running, 0);
        _available.Set();
        if (Interlocked.Exchange(ref _faulted, 1) == 0) Failed?.Invoke(error);
    }
    private void ProcessAudio()
    {
        int size = _pipeline.FrameSize, pending = 0;
        var input = new float[size]; var output = new float[size]; var bytes = new byte[size * 2];
        var uiClock = Stopwatch.StartNew();
        float inputPeak = 0, outputPeak = 0;
        double maximumFrameMs = 0;
        try
        {
            using var priority = new AudioThreadPriority();
            while (Volatile.Read(ref _running) != 0)
            {
                _available.WaitOne(100);
                if (Volatile.Read(ref _running) != 0 && Stopwatch.GetElapsedTime(Interlocked.Read(ref _lastInputAt)).TotalSeconds > 3)
                    throw new IOException("The microphone stopped delivering audio. Reconnect it or choose another device.");
                while (Volatile.Read(ref _running) != 0)
                {
                    if (_captureBuffer.BufferedDuration.TotalMilliseconds > 150 || _outputBuffer.BufferedDuration.TotalMilliseconds > PlaybackBufferTargetMs + 150)
                        throw new AudioProcessingOverloadException();
                    int read = _samples.Read(input, pending, size - pending);
                    if (read == 0) break;
                    pending += read;
                    if (pending < size) continue;
                    pending = 0;
                    for (int i = 0; i < size; i++) input[i] *= 32768f;
                    var settings = Volatile.Read(ref _settings);
                    long started = Stopwatch.GetTimestamp();
                    FrameMeters meters = _pipeline.Process(input, output, settings);
                    maximumFrameMs = Math.Max(maximumFrameMs, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                    var comparison = Volatile.Read(ref _comparison);
                    if (comparison != null && comparison.Add(input, output))
                    {
                        Interlocked.CompareExchange(ref _comparison, null, comparison);
                        ComparisonCompleted?.Invoke(comparison);
                    }
                    inputPeak = Math.Max(inputPeak, meters.InPeak); outputPeak = Math.Max(outputPeak, meters.OutPeak);
                    for (int i = 0; i < size; i++)
                    {
                        short value = (short)Math.Clamp(output[i], short.MinValue, short.MaxValue);
                        bytes[i * 2] = (byte)value; bytes[i * 2 + 1] = (byte)(value >> 8);
                    }
                    _outputBuffer.AddSamples(bytes, 0, bytes.Length);
                    if (_outputBuffer.BufferedDuration.TotalMilliseconds >= PlaybackBufferTargetMs && Interlocked.CompareExchange(ref _playbackStarted, 1, 0) == 0)
                        _playback.Play();
                    if (uiClock.ElapsedMilliseconds >= 100)
                    {
                        if (settings.BufferMode == AudioBufferMode.Automatic)
                        {
                            var advice = _advisor.Observe(maximumFrameMs, _monitor.Underruns);
                            if (advice != null) AdviceChanged?.Invoke(advice.Value);
                        }
                        Meters?.Invoke(new MeterData(inputPeak, outputPeak, meters.Vad, meters.GateGain, settings.Bypass,
                            maximumFrameMs, _captureBuffer.BufferedDuration.TotalMilliseconds + _outputBuffer.BufferedDuration.TotalMilliseconds,
                            _monitor.Underruns));
                        inputPeak = outputPeak = 0; maximumFrameMs = 0; uiClock.Restart();
                    }
                }
            }
        }
        catch (Exception ex) { ReportFailure(ex); }
    }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        try { Stop(); }
        finally
        {
            // Attempt every release even when a disconnected endpoint rejects cleanup.
            List<Exception>? errors = null;
            foreach (var resource in new IDisposable[] { _capture, _playback, _pipeline, _inputDevice, _outputDevice, _devices, _available })
                try { resource.Dispose(); } catch (Exception ex) { (errors ??= new()).Add(ex); }
            if (errors != null) throw new AggregateException(errors);
        }
    }
}

internal sealed class AudioProcessingOverloadException : InvalidOperationException
{
    public AudioProcessingOverloadException() : base("المعالجة أو جهاز الصوت مش بيلحق الوقت الحقيقي. جرّب RNNoise أو اقفل البرامج الثقيلة.") { }
}
