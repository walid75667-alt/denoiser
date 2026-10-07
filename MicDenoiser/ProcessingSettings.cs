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
    public bool FastSinging { get; set; }

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

    public float HighPassHz { get; set; } = 70;
    public bool EqEnabled { get; set; }
    public float EqLowHz { get; set; } = 120;
    public float EqLowGainDb { get; set; }
    public float EqLowQ { get; set; } = .8f;
    public float EqBodyHz { get; set; } = 450;
    public float EqBodyGainDb { get; set; }
    public float EqBodyQ { get; set; } = 1;
    public float EqPresenceHz { get; set; } = 3000;
    public float EqPresenceGainDb { get; set; }
    public float EqPresenceQ { get; set; } = .9f;
    public float EqAirHz { get; set; } = 8500;
    public float EqAirGainDb { get; set; }
    public float EqAirQ { get; set; } = .7f;
    public bool DeEsserEnabled { get; set; }
    public float DeEsserHz { get; set; } = 5500;
    public float DeEsserThresholdDb { get; set; } = -24;
    public float DeEsserMaxReductionDb { get; set; } = 6;

    // ── Dynamics ───────────────────────────────────────────
    public bool CompressorOn { get; set; }
    public float CompThresholdDb { get; set; } = -18f;
    public float CompRatio { get; set; } = 3f;

    public float CompAttackMs { get; set; } = 8;
    public float CompReleaseMs { get; set; } = 120;
    public float CompKneeDb { get; set; }
    public float CompMakeupDb { get; set; }
    public float OutputCeilingDb { get; set; } = -.5f;

    // ── Output ─────────────────────────────────────────────
    public float OutputGainDb { get; set; }

    public bool ReverbEnabled { get; set; }
    public float ReverbMix { get; set; } = .15f;
    public float ReverbDecaySeconds { get; set; } = .8f;
    public float ReverbPreDelayMs { get; set; } = 20;
    public float ReverbDamping { get; set; } = .55f;
    public bool EchoEnabled { get; set; }
    public float EchoMix { get; set; } = .15f;
    public float EchoDelayMs { get; set; } = 180;
    public float EchoFeedback { get; set; } = .25f;
    public bool ChorusEnabled { get; set; }
    public float ChorusMix { get; set; } = .15f;
    public float ChorusRateHz { get; set; } = .8f;
    public float ChorusDepthMs { get; set; } = 3;

    public static ProcessingSettings FromPreset(string key, DenoiserKind engine = DenoiserKind.DeepFilterNet3)
    {
        var s = new ProcessingSettings { Engine = engine, GateEnabled = engine == DenoiserKind.RNNoise };
        switch (key)
        {
            case "vocalroom":
            case "vocalhall":
            case "slapback":
                s.CopyFrom(FromPreset("singing", engine));
                if (key == "slapback")
                { s.EchoEnabled = true; s.EchoDelayMs = 100; s.EchoMix = .16f; s.EchoFeedback = .12f; }
                else
                {
                    s.ReverbEnabled = true;
                    s.ReverbDecaySeconds = key == "vocalhall" ? 1.8f : .6f;
                    s.ReverbMix = key == "vocalhall" ? .22f : .12f;
                    s.ReverbPreDelayMs = key == "vocalhall" ? 35 : 12;
                }
                break;
            case "singing":
                s.GateEnabled = false; s.Strength = 0; s.NoiseReductionDb = 10;
                s.CompressorOn = true; s.CompThresholdDb = -16; s.CompRatio = 1.5f;
                s.CompAttackMs = 25; s.CompReleaseMs = 200; s.CompKneeDb = 6;
                break;
            case "broadcast":
                s.GateEnabled = false; s.HighPassEnabled = true; s.HighPassHz = 80;
                s.EqEnabled = true; s.EqLowGainDb = 1.5f; s.EqBodyGainDb = -2;
                s.EqPresenceGainDb = 1.5f; s.EqAirGainDb = .5f;
                s.CompressorOn = true; s.CompThresholdDb = -22; s.CompRatio = 3;
                s.CompAttackMs = 12; s.CompReleaseMs = 150; s.CompKneeDb = 6; s.CompMakeupDb = 2;
                s.DeEsserEnabled = true; s.DeEsserMaxReductionDb = 4;
                break;
            case "voiceover":
                s.GateEnabled = false; s.NoiseReductionDb = 25; s.HighPassEnabled = true;
                s.EqEnabled = true; s.EqBodyGainDb = -1; s.EqPresenceGainDb = 1;
                s.CompressorOn = true; s.CompThresholdDb = -20; s.CompRatio = 2;
                s.CompAttackMs = 20; s.CompReleaseMs = 180; s.CompKneeDb = 6;
                s.DeEsserEnabled = true; s.DeEsserMaxReductionDb = 3;
                break;
            case "calls":
                s.GateEnabled = true; s.NoiseReductionDb = 35;
                s.GateThreshold = .35f; s.GateDepthDb = 6; s.GateHoldMs = 300; s.GateReleaseMs = 200;
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
        FastSinging = o.FastSinging;
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
        HighPassHz = o.HighPassHz;
        EqEnabled = o.EqEnabled;
        EqLowHz = o.EqLowHz;
        EqLowGainDb = o.EqLowGainDb;
        EqLowQ = o.EqLowQ;
        EqBodyHz = o.EqBodyHz;
        EqBodyGainDb = o.EqBodyGainDb;
        EqBodyQ = o.EqBodyQ;
        EqPresenceHz = o.EqPresenceHz;
        EqPresenceGainDb = o.EqPresenceGainDb;
        EqPresenceQ = o.EqPresenceQ;
        EqAirHz = o.EqAirHz;
        EqAirGainDb = o.EqAirGainDb;
        EqAirQ = o.EqAirQ;
        DeEsserEnabled = o.DeEsserEnabled;
        DeEsserHz = o.DeEsserHz;
        DeEsserThresholdDb = o.DeEsserThresholdDb;
        DeEsserMaxReductionDb = o.DeEsserMaxReductionDb;
        CompAttackMs = o.CompAttackMs;
        CompReleaseMs = o.CompReleaseMs;
        CompKneeDb = o.CompKneeDb;
        CompMakeupDb = o.CompMakeupDb;
        OutputCeilingDb = o.OutputCeilingDb;

        ReverbEnabled = o.ReverbEnabled; ReverbMix = o.ReverbMix;
        ReverbDecaySeconds = o.ReverbDecaySeconds; ReverbPreDelayMs = o.ReverbPreDelayMs;
        ReverbDamping = o.ReverbDamping;
        EchoEnabled = o.EchoEnabled; EchoMix = o.EchoMix; EchoDelayMs = o.EchoDelayMs; EchoFeedback = o.EchoFeedback;
        ChorusEnabled = o.ChorusEnabled; ChorusMix = o.ChorusMix; ChorusRateHz = o.ChorusRateHz; ChorusDepthMs = o.ChorusDepthMs;

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
        s.HighPassHz = Bound(s.HighPassHz, 40, 200, 70);
        s.EqLowHz = Bound(s.EqLowHz, 40, 400, 120);
        s.EqBodyHz = Bound(s.EqBodyHz, 200, 1800, 450);
        s.EqPresenceHz = Bound(s.EqPresenceHz, 1000, 6000, 3000);
        s.EqAirHz = Bound(s.EqAirHz, 4000, 14000, 8500);
        s.DeEsserHz = Bound(s.DeEsserHz, 2500, 9000, 5500);
        s.DeEsserThresholdDb = Bound(s.DeEsserThresholdDb, -48, -6, -24);
        s.DeEsserMaxReductionDb = Bound(s.DeEsserMaxReductionDb, 0, 12, 6);
        s.CompAttackMs = Bound(s.CompAttackMs, 1, 100, 8);
        s.CompReleaseMs = Bound(s.CompReleaseMs, 20, 1000, 120);
        s.CompKneeDb = Bound(s.CompKneeDb, 0, 12, 0);
        s.CompMakeupDb = Bound(s.CompMakeupDb, 0, 12, 0);
        s.OutputCeilingDb = Bound(s.OutputCeilingDb, -6, -0.3f, -0.5f);
        s.EqLowGainDb = Bound(s.EqLowGainDb, -9, 9, 0);
        s.EqLowQ = Bound(s.EqLowQ, 0.35f, 3, 0.8f);
        s.EqBodyGainDb = Bound(s.EqBodyGainDb, -9, 9, 0);
        s.EqBodyQ = Bound(s.EqBodyQ, 0.35f, 3, 1);
        s.EqPresenceGainDb = Bound(s.EqPresenceGainDb, -9, 9, 0);
        s.EqPresenceQ = Bound(s.EqPresenceQ, 0.35f, 3, 0.9f);
        s.EqAirGainDb = Bound(s.EqAirGainDb, -9, 9, 0);
        s.EqAirQ = Bound(s.EqAirQ, 0.35f, 3, 0.7f);
        s.ReverbMix = Bound(s.ReverbMix, 0, .5f, .15f);
        s.ReverbDecaySeconds = Bound(s.ReverbDecaySeconds, .2f, 3, .8f);
        s.ReverbPreDelayMs = Bound(s.ReverbPreDelayMs, 0, 100, 20);
        s.ReverbDamping = Bound(s.ReverbDamping, .1f, .85f, .55f);
        s.EchoMix = Bound(s.EchoMix, 0, .5f, .15f);
        s.EchoDelayMs = Bound(s.EchoDelayMs, 40, 1000, 180);
        s.EchoFeedback = Bound(s.EchoFeedback, 0, .7f, .25f);
        s.ChorusMix = Bound(s.ChorusMix, 0, .5f, .15f);
        s.ChorusRateHz = Bound(s.ChorusRateHz, .1f, 3, .8f);
        s.ChorusDepthMs = Bound(s.ChorusDepthMs, 1, 10, 3);
        if (s.FastSinging) { s.Strength = 0; s.GateEnabled = false; }
        return s;
    }
}
