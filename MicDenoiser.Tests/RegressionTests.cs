using MicDenoiser.Checks;
using Xunit;

namespace MicDenoiser.Tests;

// Native/DSP checks include timing measurements: keep test execution sequential.
[CollectionDefinition("Audio", DisableParallelization = true)]
public sealed class AudioCollection;

[Collection("Audio")]
public sealed class RegressionTests
{
    public static IEnumerable<object[]> PortableCases => CheckCatalog.Create().Where(c => !c.WindowsOnly).Select(c => new object[] { c.Name });
    [Theory]
    [MemberData(nameof(PortableCases))]
    public void PortableRegression(string name) => CheckCatalog.Create().Single(c => c.Name == name).Run();

    [WindowsFact]
    public void RealWindowsRNNoiseLatency() => CheckCatalog.Create().Single(c => c.Name == "pinned Windows RNNoise DLL has 960 sample impulse delay").Run();
    [WindowsFact]
    public void RealWindowsRNNoiseAndDeepFilterVad() => CheckCatalog.Create().Single(c => c.Name == "RNNoise fallback and DeepFilterNet optional sidechain VAD").Run();
}

public sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute() { if (!OperatingSystem.IsWindows()) Skip = "Requires the actual pinned Windows rnnoise.dll."; }
}
