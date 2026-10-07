using System.Diagnostics;
using MicDenoiser;
using MicDenoiser.Checks;
using NAudio.Wave;
using System.Runtime.InteropServices;

// Benchmark-only Linux RNNoise; the shipped Windows DLL is never replaced in source.
if (!OperatingSystem.IsWindows() && Environment.GetEnvironmentVariable("MICDENOISER_RNNOISE_LIBRARY") is { } linuxRn)
    NativeLibrary.SetDllImportResolver(typeof(RNNoise).Assembly, (name, assembly, searchPath) =>
        name == "rnnoise.dll" ? NativeLibrary.Load(Path.GetFullPath(linuxRn)) : IntPtr.Zero);

if (args.Length > 0)
{
    if (args.Length is < 4 or > 6 || args[0] != "--enhance" || !Enum.TryParse<DenoiserKind>(args[3], true, out var kind)
        || args.Skip(4).Any(a => a is not ("--calls-gate" or "--float")) || args.Skip(4).Distinct().Count() != args.Length - 4)
        throw new ArgumentException("Usage: --enhance input.wav output.wav DeepFilterNet3|RNNoise [--calls-gate] [--float]");
    WavEnhancer.Enhance(args[1], args[2], kind, args.Contains("--calls-gate"), args.Contains("--float"));
    return;
}

int passed = 0, skipped = 0;
foreach (var check in CheckCatalog.Create())
{
    if (check.WindowsOnly && !OperatingSystem.IsWindows()) { Console.WriteLine("SKIP " + check.Name); skipped++; continue; }
    check.Run(); Console.WriteLine("PASS " + check.Name); passed++;
}
Console.WriteLine($"{passed} checks passed; {skipped} skipped. Live WASAPI audio needs separate Windows validation.");
