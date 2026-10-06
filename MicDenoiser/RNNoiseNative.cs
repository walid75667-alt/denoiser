using System.Runtime.InteropServices;

namespace MicDenoiser;

/// <summary>Thin wrapper over the native rnnoise.dll (48 kHz mono, 480-sample frames).</summary>
public sealed class RNNoise : IDisposable
{
    public const int FrameSize = 480;

    private const string Dll = "rnnoise.dll";

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr rnnoise_create(IntPtr model);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    private static extern void rnnoise_destroy(IntPtr state);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    private static extern float rnnoise_process_frame(IntPtr state, float[] output, float[] input);

    private IntPtr _state;

    public RNNoise()
    {
        try
        {
            _state = rnnoise_create(IntPtr.Zero);
        }
        catch (DllNotFoundException ex)
        {
            throw new InvalidOperationException(
                "تعذّر تحميل rnnoise.dll. تأكد أن الملف موجود بجانب البرنامج، " +
                "وأن حزمة Microsoft Visual C++ Redistributable (x64) مثبّتة.\n" + ex.Message);
        }
        catch (BadImageFormatException)
        {
            throw new InvalidOperationException("rnnoise.dll بصيغة 64-bit، لذا يجب تشغيل البرنامج بصيغة x64.");
        }

        if (_state == IntPtr.Zero)
            throw new InvalidOperationException("فشل إنشاء محرّك RNNoise.");
    }

    /// <summary>
    /// Denoise one 480-sample frame in place (floats in 16-bit range).
    /// Returns the voice-activity probability (0..1) for the frame.
    /// </summary>
    public float ProcessFrame(float[] frame) => rnnoise_process_frame(_state, frame, frame);

    public void Dispose()
    {
        if (_state != IntPtr.Zero)
        {
            rnnoise_destroy(_state);
            _state = IntPtr.Zero;
        }
    }
}
