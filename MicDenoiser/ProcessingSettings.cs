namespace MicDenoiser;

public enum AudioBufferMode { Balanced, Stable, LowLatency, Automatic }

/// <summary>All live-adjustable parameters of the processing chain.</summary>
public sealed class ProcessingSettings
{
    public DenoiserKind Engine { get; set; } = DenoiserKind.DeepFilterNet3;
    public AudioBufferMode BufferMode { get; set; } = AudioBufferMode.Balanced;
    public float NoiseReductionDb { get; set; } = 35f;
    public bool GateEnabled { get; set; }
    public bool HighPassEnabled { get; set; }
    public bool Bypass { get; set; }

    // ── Input ──────────────────────────────────────────────
    public float InputGainDb { get; set; } = 0f;

    // ── Noise reduction ────────────────────────────────────
    /// <summary>0..1 — wet/dry mix of the neural denoiser.</summary>
    public float Strength { get; set; } = 1f;

    // ── Voice-activity gate ────────────────────────────────
    /// <summary>VAD probability above which the gate opens (0..1).</summary>
    public float GateThreshold { get; set; } = 0.5f;
    /// <summary>How far the gate attenuates when closed (dB).</summary>
    public float GateDepthDb { get; set; } = 30f;
    public float GateHoldMs { get; set; } = 180f;
    public float GateReleaseMs { get; set; } = 150f;

    // ── Tone ───────────────────────────────────────────────
    public float PresenceDb { get; set; }
    public float MudCutDb { get; set; }

    // ── Dynamics ───────────────────────────────────────────
    public bool CompressorOn { get; set; }
    public float CompThresholdDb { get; set; } = -18f;
    public float CompRatio { get; set; } = 3f;

    // ── Output ─────────────────────────────────────────────
    public float OutputGainDb { get; set; }

    public static ProcessingSettings FromPreset(string key, DenoiserKind engine = DenoiserKind.DeepFilterNet3)
    {
        var s = new ProcessingSettings { Engine = engine, GateEnabled = engine == DenoiserKind.RNNoise };
        switch (key)
        {
            case "calls":
                s.GateEnabled = false; s.NoiseReductionDb = 35;
                s.CompressorOn = true; s.CompThresholdDb = -22; s.CompRatio = 2; s.PresenceDb = 1;
                break;
            case "streaming":
                s.GateEnabled = false; s.NoiseReductionDb = 35;
                s.CompressorOn = true; s.CompThresholdDb = -20; s.CompRatio = 3;
                s.PresenceDb = 2; s.MudCutDb = 1; s.OutputGainDb = 2;
                break;
            case "weakmic":
                s.GateEnabled = false; s.NoiseReductionDb = 30; s.InputGainDb = 6;
                s.CompressorOn = true; s.CompThresholdDb = -24; s.CompRatio = 2;
                break;
            case "whisper":
                s.GateEnabled = false; s.NoiseReductionDb = 25; s.InputGainDb = 3;
                s.Strength = engine == DenoiserKind.RNNoise ? .85f : 1;
                s.CompressorOn = false;
                break;
            case "natural":
                s.Strength = engine == DenoiserKind.DeepFilterNet3 ? 1f : 0.85f;
                s.NoiseReductionDb = 25f; s.GateEnabled = false;
                s.GateThreshold = 0.35f; s.GateDepthDb = 12f;
                s.PresenceDb = 0f; s.MudCutDb = 0f;
                s.CompressorOn = false; s.OutputGainDb = 0f;
                break;

            case "max":
                s.NoiseReductionDb = 50f;
                s.Strength = 1f; s.GateDepthDb = 24f;
                break;

            case "podcast":
                s.HighPassEnabled = true;
                s.Strength = 1f; s.GateThreshold = 0.45f; s.GateDepthDb = 24f;
                s.PresenceDb = 3f; s.MudCutDb = 3f;
                s.CompressorOn = true; s.CompThresholdDb = -20f; s.CompRatio = 4f; s.OutputGainDb = 4f;
                break;

            default: // "studio"
                break; // property defaults are the studio preset
        }
        return s;
    }

    public void CopyFrom(ProcessingSettings o)
    {
        Engine = o.Engine;
        BufferMode = o.BufferMode;
        NoiseReductionDb = o.NoiseReductionDb;
        GateEnabled = o.GateEnabled;
        HighPassEnabled = o.HighPassEnabled;
        Bypass = o.Bypass;
        InputGainDb = o.InputGainDb;
        Strength = o.Strength;
        GateThreshold = o.GateThreshold;
        GateDepthDb = o.GateDepthDb;
        GateHoldMs = o.GateHoldMs;
        GateReleaseMs = o.GateReleaseMs;
        PresenceDb = o.PresenceDb;
        MudCutDb = o.MudCutDb;
        CompressorOn = o.CompressorOn;
        CompThresholdDb = o.CompThresholdDb;
        CompRatio = o.CompRatio;
        OutputGainDb = o.OutputGainDb;
    }

    public ProcessingSettings Clone() => (ProcessingSettings)MemberwiseClone();

    public ProcessingSettings SanitizedClone()
    {
        var s = Clone();
        static float Bound(float v, float lo, float hi, float fallback) => float.IsFinite(v) ? Math.Clamp(v, lo, hi) : fallback;
        if (!Enum.IsDefined(s.Engine)) s.Engine = DenoiserKind.DeepFilterNet3;
        if (!Enum.IsDefined(s.BufferMode)) s.BufferMode = AudioBufferMode.Balanced;
        s.NoiseReductionDb = Bound(s.NoiseReductionDb, 10, 60, 35);
        s.Strength = Bound(s.Strength, 0, 1, 1);
        s.InputGainDb = Bound(s.InputGainDb, -12, 18, 0);
        s.OutputGainDb = Bound(s.OutputGainDb, -12, 12, 0);
        s.GateThreshold = Bound(s.GateThreshold, .15f, .85f, .5f);
        s.GateDepthDb = Bound(s.GateDepthDb, 0, 60, 30);
        s.GateHoldMs = Bound(s.GateHoldMs, 0, 1000, 180);
        s.GateReleaseMs = Bound(s.GateReleaseMs, 10, 1000, 150);
        s.PresenceDb = Bound(s.PresenceDb, 0, 6, 0);
        s.MudCutDb = Bound(s.MudCutDb, 0, 6, 0);
        s.CompThresholdDb = Bound(s.CompThresholdDb, -40, -6, -18);
        s.CompRatio = Bound(s.CompRatio, 1, 8, 3);
        return s;
    }
}
