namespace MicDenoiser;

/// <summary>Offline renders of identical captured input; fresh engines, aligned onsets and bounded data.</summary>
public static class AbComparison
{
    public static ComparisonResult Render(float[] source, ProcessingSettings a, ProcessingSettings b,
        CancellationToken cancellation = default, Func<ProcessingSettings, IDenoiseEngine>? factory = null)
    {
        if (source.Length < 1 || source.Length > 480000 || source.Any(x => !float.IsFinite(x)))
            throw new ArgumentException("Invalid A/B source audio.");
        factory ??= AudioEngineFactory.Create;
        float[] RenderOne(ProcessingSettings settings)
        {
            cancellation.ThrowIfCancellationRequested();
            settings = settings.SanitizedClone(); settings.Bypass = false;
            using var pipeline = new AudioPipeline(factory(settings));
            int size = pipeline.FrameSize, delay = (int)Math.Round(pipeline.AlgorithmicDelayMs * 48);
            var input = new float[size]; var output = new float[size]; var result = new float[source.Length];
            for (int warm = 0; warm < 10; warm++) { cancellation.ThrowIfCancellationRequested(); pipeline.Process(input, output, settings); }
            for (int start = 0; start < source.Length + delay; start += size)
            {
                cancellation.ThrowIfCancellationRequested(); Array.Clear(input);
                int count = Math.Min(size, Math.Max(0, source.Length - start));
                if (count > 0) Array.Copy(source, start, input, 0, count);
                pipeline.Process(input, output, settings);
                for (int i = 0; i < size; i++)
                {
                    int index = start + i - delay;
                    if (index >= 0 && index < result.Length) result[index] = output[i];
                }
            }
            return result;
        }
        var first = RenderOne(a); var second = RenderOne(b);
        cancellation.ThrowIfCancellationRequested();
        return ComparisonResult.Create(first, second);
    }
}
