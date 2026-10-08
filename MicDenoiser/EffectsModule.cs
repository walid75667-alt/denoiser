namespace MicDenoiser;

/// <summary>Reset one strip without changing routing, mute, bypass or other effects.</summary>
public static class EffectsModule
{
    public static ProcessingSettings Reset(string module, ProcessingSettings current)
    {
        string[] names = module switch
        {
            "input" => [nameof(current.InputGainDb), nameof(current.HighPassEnabled), nameof(current.HighPassHz)],
            "output" => [nameof(current.OutputGainDb), nameof(current.OutputCeilingDb)],
            "eq" => [nameof(current.PresenceDb), nameof(current.MudCutDb)],
            "compressor" or "deesser" or "gate" or "reverb" or "echo" or "chorus" => [],
            _ => throw new ArgumentException("Unknown effect module.", nameof(module))
        };
        string? prefix = module switch { "eq" => "Eq", "compressor" => "Comp", "deesser" => "DeEsser", "gate" => "Gate", "reverb" => "Reverb", "echo" => "Echo", "chorus" => "Chorus", _ => null };
        var result = current.SanitizedClone(); var defaults = new ProcessingSettings();
        foreach (var property in typeof(ProcessingSettings).GetProperties())
            if (names.Contains(property.Name) || prefix != null && property.Name.StartsWith(prefix, StringComparison.Ordinal))
                property.SetValue(result, property.GetValue(defaults));
        return result.SanitizedClone();
    }
}
