namespace MicDenoiser;

public static class SuppressionLevel
{
    public static bool Matches(string level, ProcessingSettings current) =>
        System.Text.Json.JsonSerializer.Serialize(Create(level, current)) == System.Text.Json.JsonSerializer.Serialize(current.SanitizedClone());
    public static ProcessingSettings Create(string level, ProcessingSettings current)
    {
        if (level is not ("light" or "balanced" or "strong")) throw new ArgumentException("Unknown isolation level.");
        var result = ProcessingSettings.FromPreset(level == "light" ? "natural" : "calls",
            level == "light" ? DenoiserKind.RNNoise : DenoiserKind.DeepFilterNet3);
        result.BufferMode = current.BufferMode; result.InputGainDb = current.InputGainDb;
        result.OutputGainDb = current.OutputGainDb; result.Bypass = current.Bypass; result.Muted = current.Muted;
        if (level == "strong") { result.NoiseReductionDb = 50; result.GateDepthDb = 12; result.GateThreshold = .4f; }
        return result.SanitizedClone();
    }
}
