using System.Text.Json;

namespace MicDenoiser;

/// <summary>Device-free effects exchange. Routing, engine, buffer and mute state stay local.</summary>
public static class EffectsProfile
{
    private sealed class Document
    {
        public string Format { get; set; } = "";
        public int Version { get; set; }
        public ProcessingSettings? Settings { get; set; }
    }
    public static string Serialize(ProcessingSettings settings) => JsonSerializer.Serialize(new Document
    { Format = "MicDenoiser.Effects", Version = 1, Settings = settings.SanitizedClone() }, new JsonSerializerOptions { WriteIndented = true });
    public static ProcessingSettings Read(string json, ProcessingSettings current, bool processing)
    {
        if (json.Length > 65536) throw new ArgumentException("Effects profile is too large.");
        var document = JsonSerializer.Deserialize<Document>(json);
        if (document?.Format != "MicDenoiser.Effects" || document.Version != 1 || document.Settings == null)
            throw new ArgumentException("Unsupported effects profile.");
        var result = document.Settings.SanitizedClone();
        result.Engine = current.Engine; result.BufferMode = current.BufferMode; result.Bypass = current.Bypass; result.Muted = current.Muted;
        result.FastSinging = current.FastSinging;
        return result.SanitizedClone();
    }
}
