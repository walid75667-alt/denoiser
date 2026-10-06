namespace MicDenoiser;

/// <summary>All live-adjustable parameters of the processing chain.</summary>
public sealed class ProcessingSettings
{
    public DenoiserKind Engine { get; set; } = DenoiserKind.DeepFilterNet3;
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
            case "natural":
                s.Strength = 0.85f; s.GateThreshold = 0.35f; s.GateDepthDb = 12f;
                s.PresenceDb = 0f; s.MudCutDb = 0f;
                s.CompressorOn = false; s.OutputGainDb = 0f;
                break;

            case "max":
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
}
