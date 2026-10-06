using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;

namespace MicDenoiser;

/// <summary>Embedded translation catalogs shared by labels and live status messages.</summary>
internal static class UiStrings
{
    private static Dictionary<string, string> _strings = Load("ar");
    public static string Language { get; private set; } = "ar";
    private static Dictionary<string, string> Load(string language)
    {
        using var stream = typeof(UiStrings).Assembly.GetManifestResourceStream($"MicDenoiser.Localization.{language}.json")
            ?? throw new InvalidDataException("Missing UI translation catalog.");
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)!;
    }
    public static void Apply(Window window, string language)
    {
        Language = language == "en" ? "en" : "ar";
        _strings = Load(Language);
        foreach (var pair in _strings) window.Resources[pair.Key] = pair.Value;
        window.FlowDirection = Language == "ar" ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        window.Language = System.Windows.Markup.XmlLanguage.GetLanguage(Language);
    }
    public static string Get(string key, params object[] args) =>
        string.Format(CultureInfo.GetCultureInfo(Language), _strings[key], args);
    public static string ErrorDetail(string message)
    {
        if (Language != "en") return message;
        foreach (var pair in Load("ar"))
            if (pair.Key.StartsWith("Error", StringComparison.Ordinal) && pair.Value == message)
                return _strings[pair.Key];
        return message; // Native and Windows library diagnostics retain their original wording.
    }
}
