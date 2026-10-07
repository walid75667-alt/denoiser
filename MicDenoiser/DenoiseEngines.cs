using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace MicDenoiser;

public enum DenoiserKind { DeepFilterNet3, RNNoise }

public interface IDenoiseEngine : IDisposable
{
    string Name { get; }
    int FrameSize { get; }
    int DelaySamples { get; }
    void Configure(ProcessingSettings settings) { }
    // Samples use the existing pipeline's PCM16 float range. Null means no VAD.
    float? Process(float[] input, float[] output);
}

/// <summary>No native model or VAD; frame size is retained for WASAPI and effects.</summary>
public sealed class DirectVoiceEngine : IDenoiseEngine
{
    public string Name => "Direct voice";
    public int FrameSize => 480;
    public int DelaySamples => 0;
    public float? Process(float[] input, float[] output)
    { Array.Copy(input, output, FrameSize); return null; }
    public void Dispose() { }
}

public static class AudioEngineFactory
{
    public static IDenoiseEngine Create(ProcessingSettings settings) => settings.FastSinging
        ? new DirectVoiceEngine() : settings.Engine == DenoiserKind.DeepFilterNet3 ? new DeepFilterNetEngine() : new RNNoiseEngine();
    public static int PlaybackTargetMs(ProcessingSettings settings) => settings.FastSinging
        ? settings.BufferMode switch { AudioBufferMode.Stable => 60, AudioBufferMode.LowLatency => 20, _ => 30 }
        : settings.BufferMode switch { AudioBufferMode.Stable => 100, AudioBufferMode.LowLatency => 30, _ => 60 };
}

public sealed class RNNoiseEngine : IDenoiseEngine
{
    private readonly RNNoise _state = new();
    public string Name => "RNNoise";
    public int FrameSize => RNNoise.FrameSize;
    public int DelaySamples => RNNoise.FrameSize;
    public float? Process(float[] input, float[] output)
    {
        Array.Copy(input, output, FrameSize);
        return _state.ProcessFrame(output);
    }
    public void Dispose() => _state.Dispose();
}

public sealed class DeepFilterNetEngine : IDenoiseEngine
{
    public const string ModelSha256 = "c94d91f70911001c946e0fabb4aa9adc37045f45a03b56008cb0c8244cb63616";
    private readonly DfHandle _state;
    private readonly float[] _input;
    private readonly float[] _output;
    private float _attenuation = 35f;
    public string Name => "DeepFilterNet3";
    public int FrameSize { get; }
    public int DelaySamples { get; }

    public DeepFilterNetEngine(string? modelPath = null)
    {
        modelPath ??= Path.Combine(AppContext.BaseDirectory, "models", "DeepFilterNet3_onnx.tar.gz");
        if (!File.Exists(modelPath))
            throw new FileNotFoundException("نموذج DeepFilterNet3 غير موجود. شغّل scripts/build-native.ps1 ثم أعد البناء.", modelPath);
        using (var stream = File.OpenRead(modelPath))
        {
            if (!Convert.ToHexString(SHA256.HashData(stream)).Equals(ModelSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("ملف نموذج DeepFilterNet3 تالف أو مختلف عن النسخة المعتمدة.");
        }
        try
        {
            if (Native.AbiVersion() != 2) throw new InvalidOperationException("حدّث مجلد البرنامج كاملًا؛ إصدار مكتبة DeepFilterNet لا يطابق التطبيق.");
            _state = Native.Create(modelPath, _attenuation);
        }
        catch (EntryPointNotFoundException ex)
        {
            throw new InvalidOperationException("مكتبة DeepFilterNet قديمة. فك ضغط النسخة الجديدة في مجلد جديد وشغّل البرنامج منه.", ex);
        }
        catch (DllNotFoundException ex)
        {
            throw new InvalidOperationException("مكتبة DeepFilterNet غير موجودة. شغّل scripts/build-native.ps1 وأعد بناء التطبيق، أو اختر RNNoise.", ex);
        }
        catch (BadImageFormatException ex)
        {
            throw new InvalidOperationException("مكتبة DeepFilterNet لازم تكون مبنية لويندوز x64.", ex);
        }
        if (_state.IsInvalid) { _state.Dispose(); throw Error(); }
        FrameSize = checked((int)Native.FrameLength(_state));
        DelaySamples = checked((int)Native.DelaySamples(_state));
        if (Native.SampleRate(_state) != 48000 || FrameSize != RNNoise.FrameSize || DelaySamples % FrameSize != 0)
        {
            _state.Dispose();
            throw new InvalidDataException("النموذج لا يطابق إعدادات 48kHz وإطارات 10ms المطلوبة.");
        }
        _input = new float[FrameSize];
        _output = new float[FrameSize];
    }

    public void Configure(ProcessingSettings settings)
    {
        float limit = Math.Clamp(settings.NoiseReductionDb, 10f, 60f);
        if (limit == _attenuation) return;
        // Move by <= 1 dB per 10 ms frame rather than changing the spectral floor abruptly.
        float next = _attenuation + Math.Clamp(limit - _attenuation, -1f, 1f);
        if (Native.SetAttenuation(_state, next) != 0) throw Error();
        _attenuation = next;
    }

    public float? Process(float[] input, float[] output)
    {
        if (input.Length != FrameSize || output.Length != FrameSize)
            throw new ArgumentException("Invalid DeepFilterNet frame length.");
        for (int i = 0; i < FrameSize; i++) _input[i] = input[i] / 32768f;
        if (Native.Process(_state, _input, _output) != 0) throw Error();
        for (int i = 0; i < FrameSize; i++)
        {
            if (!float.IsFinite(_output[i])) throw new InvalidDataException("DeepFilterNet returned invalid audio.");
            output[i] = _output[i] * 32768f;
        }
        return null; // Native local SNR must never be interpreted as VAD.
    }

    private static Exception Error()
    {
        var bytes = new byte[2048];
        Native.LastError(bytes, (nuint)bytes.Length);
        int end = Array.IndexOf(bytes, (byte)0);
        return new InvalidOperationException("DeepFilterNet: " + Encoding.UTF8.GetString(bytes, 0, end < 0 ? bytes.Length : end));
    }
    public void Dispose() => _state.Dispose();

    private sealed class DfHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public DfHandle() : base(true) { }
        protected override bool ReleaseHandle() { Native.Destroy(handle); return true; }
    }
    private static class Native
    {
        private const string Dll = "micdenoiser_df";
        [DllImport(Dll, EntryPoint = "md_df_abi_version", CallingConvention = CallingConvention.Cdecl)]
        public static extern uint AbiVersion();
        [DllImport(Dll, EntryPoint = "md_df_set_attenuation", CallingConvention = CallingConvention.Cdecl)]
        public static extern int SetAttenuation(DfHandle state, float attenuation);
        [DllImport(Dll, EntryPoint = "md_df_create", CallingConvention = CallingConvention.Cdecl)]
        public static extern DfHandle Create([MarshalAs(UnmanagedType.LPUTF8Str)] string path, float attenuation);
        [DllImport(Dll, EntryPoint = "md_df_destroy", CallingConvention = CallingConvention.Cdecl)]
        public static extern void Destroy(IntPtr state);
        [DllImport(Dll, EntryPoint = "md_df_frame_length", CallingConvention = CallingConvention.Cdecl)]
        public static extern nuint FrameLength(DfHandle state);
        [DllImport(Dll, EntryPoint = "md_df_sample_rate", CallingConvention = CallingConvention.Cdecl)]
        public static extern nuint SampleRate(DfHandle state);
        [DllImport(Dll, EntryPoint = "md_df_delay_samples", CallingConvention = CallingConvention.Cdecl)]
        public static extern nuint DelaySamples(DfHandle state);
        [DllImport(Dll, EntryPoint = "md_df_process", CallingConvention = CallingConvention.Cdecl)]
        public static extern int Process(DfHandle state, [In] float[] input, [Out] float[] output);
        [DllImport(Dll, EntryPoint = "md_df_last_error", CallingConvention = CallingConvention.Cdecl)]
        public static extern void LastError([Out] byte[] output, nuint capacity);
    }
}
