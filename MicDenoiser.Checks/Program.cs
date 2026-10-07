using System.Diagnostics;
using MicDenoiser;
using NAudio.Wave;

if (args.Length > 0)
{
    if (args.Length != 4 || args[0] != "--enhance" || !Enum.TryParse<DenoiserKind>(args[3], true, out var kind))
        throw new ArgumentException("Usage: --enhance input.wav output.wav DeepFilterNet3|RNNoise");
    WavEnhancer.Enhance(args[1], args[2], kind);
    return;
}

int passed = 0;
void Check(string name, Action test) { test(); Console.WriteLine("PASS " + name); passed++; }
void Require(bool value, string message) { if (!value) throw new Exception(message); }

Check("interface catalogs and XAML resource references are complete", () =>
{
    string assets = Path.Combine(AppContext.BaseDirectory, "UiValidation");
    var ar = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(assets, "ar.json")))!;
    var en = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(assets, "en.json")))!;
    Require(ar.Keys.Order().SequenceEqual(en.Keys.Order()), "A language catalog is missing keys.");
    foreach (string key in ar.Keys)
    {
        Require(!string.IsNullOrWhiteSpace(ar[key]) && !string.IsNullOrWhiteSpace(en[key]), "Empty translation: " + key);
        Require(System.Text.CompositeFormat.Parse(ar[key]).MinimumArgumentCount == System.Text.CompositeFormat.Parse(en[key]).MinimumArgumentCount,
            "Translation format arguments differ: " + key);
    }
    var xkey = System.Xml.Linq.XName.Get("Key", "http://schemas.microsoft.com/winfx/2006/xaml");
    var application = System.Xml.Linq.XDocument.Load(Path.Combine(assets, "App.xaml"));
    var staticKeys = application.Descendants().SelectMany(e => e.Attributes(xkey)).Select(a => a.Value).ToHashSet();
    foreach (string file in new[] { "MainWindow.xaml", "App.xaml" })
    {
        foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(
                     File.ReadAllText(Path.Combine(assets, file)), @"\{(StaticResource|DynamicResource) ([^}]+)\}"))
            Require(match.Groups[1].Value == "StaticResource" ? staticKeys.Contains(match.Groups[2].Value) : (ar.ContainsKey(match.Groups[2].Value) || staticKeys.Contains(match.Groups[2].Value)),
                "Unresolved XAML resource: " + match.Value);
    }
});

Check("comparison captures exactly ten seconds with aligned original audio", () =>
{
    foreach (int delay in new[] { 0, 517, 1440, 2400 })
    {
        var capture = new ComparisonCapture(480, delay);
        var actualDelay = new SampleDelay(delay);
        var raw = new float[480]; var wet = new float[480];
        int position = 0, completions = 0;
        while (!capture.IsComplete)
        {
            for (int i=0; i<480; i++) raw[i] = (position+i)%30000;
            actualDelay.Process(raw, wet);
            if (capture.Add(raw, wet)) completions++;
            position += 480;
        }
        Require(capture.SampleCount == 480000 && completions == 1, "Wrong duration/completion count.");
        Require(capture.Original.SequenceEqual(capture.Processed), "Comparison paths are misaligned.");
        for (int i=0; i<480000; i++) Require(capture.Original[i] == i%30000, "Comparison includes pre-start samples or omits audio.");
        Require(!capture.Add(raw, wet), "Completed capture accepted another frame.");
    }
});

Check("comparison cancellation discards pending capture", () =>
{
    var capture = new ComparisonCapture(480, 2400);
    capture.Cancel();
    Require(capture.IsCanceled && !capture.IsComplete && !capture.Add(new float[480], new float[480]), "Canceled recording continued.");
    bool refused = false;
    try { ComparisonResult.Create(capture); } catch (InvalidOperationException) { refused = true; }
    Require(refused, "Incomplete audio was playable.");
});

Check("BS.1770 loudness matching attenuates without changing the source", () =>
{
    var capture = new ComparisonCapture(480, 0, 48000);
    var a = new float[480]; var b = new float[480];
    for (int frame=0; frame<100; frame++)
    {
        for (int i=0; i<480; i++) { a[i]=(float)(30000*Math.Sin(2*Math.PI*1000*(frame*480+i)/48000)); b[i]=a[i]*.25f; }
        capture.Add(a, b);
    }
    var rawCopy=(float[])capture.Original.Clone();
    var result=ComparisonResult.Create(capture);
    Require(Math.Abs(result.OriginalLufs - (-3.0+20*Math.Log10(30000/32768.0)))<.12, "1 kHz reference loudness is incorrect.");
    Require(Math.Abs(result.OriginalLufs-result.ProcessedLufs-20*Math.Log10(4))<.001, "Relative loudness measurement is incorrect.");
    Require(Math.Abs(result.OriginalLufs+20*Math.Log10(result.OriginalGain)-result.ProcessedLufs-20*Math.Log10(result.ProcessedGain))<.001, "Playback levels are unmatched.");
    Require(result.OriginalGain<=1 && result.ProcessedGain<=1 && rawCopy.SequenceEqual(capture.Original), "Matching boosted or mutated source audio.");
    var before=new float[48000]; var after=new float[48000];
    Require(result.CreatePlayback(true).Read(before,0,before.Length)==48000, "Original playback is incomplete.");
    result.CreatePlayback(false).Read(after,0,after.Length);
    Require(before.Zip(after,(x,y)=>Math.Abs(x-y)).Max()<1e-6, "Proportional clips differ after matching.");
    Require(before.Max(x=>Math.Abs(x))<=.89, "Playback clipped.");
});

Check("matched playback preserves offsets, ends cleanly, and handles silence", () =>
{
    var capture=new ComparisonCapture(480,0,480);
    capture.Add(new float[480],new float[480]);
    var result=ComparisonResult.Create(capture);
    Require(double.IsNegativeInfinity(result.OriginalLufs) && double.IsFinite(result.OriginalGain), "Silent loudness produced an invalid gain.");
    var provider=result.CreatePlayback(true); var buffer=Enumerable.Repeat(123f,500).ToArray();
    Require(provider.Read(buffer,10,490)==480 && provider.Read(buffer,0,500)==0, "Provider duration/end is wrong.");
    Require(buffer.Take(10).All(x=>x==123) && buffer.Skip(490).All(x=>x==123), "Read overwrote surrounding data.");
    Require(buffer.Skip(10).Take(480).All(x=>x==0), "Silence became nonzero audio.");
});

Check("stability advisor distinguishes transient spikes from sustained overload", () =>
{
    var advisor=new StabilityAdvisor(DenoiserKind.DeepFilterNet3,AudioBufferMode.Automatic);
    for (int i=0;i<49;i++) Require(advisor.Observe(i==0?15:2,0)==null, "Premature advice.");
    Require(advisor.Observe(2,0)==StabilityAdvice.Healthy,"Transient spike was treated as overload.");
    for (int i=0;i<49;i++) advisor.Observe(12,0);
    Require(advisor.Observe(12,0)==StabilityAdvice.Lightweight,"Sustained overload did not suggest a lighter engine.");
    for (int i=0;i<50;i++) Require(advisor.Observe(12,0)==null,"Unchanged advice was repeatedly emitted.");
});

Check("stability advisor accounts for new gaps and existing buffering", () =>
{
    foreach (var mode in new[]{AudioBufferMode.Automatic,AudioBufferMode.Stable})
    {
        var advisor=new StabilityAdvisor(DenoiserKind.RNNoise,mode);
        for (int i=0;i<49;i++) advisor.Observe(2,2);
        Require(advisor.Observe(2,2)==(mode==AudioBufferMode.Stable?StabilityAdvice.CheckDevice:StabilityAdvice.MoreBuffer),"Incorrect gap advice.");
        for (int i=0;i<49;i++) advisor.Observe(2,2);
        Require(advisor.Observe(2,2)==StabilityAdvice.Healthy,"Old gaps were counted again.");
    }
});

Check("use-case presets preserve quiet speech and survive saved configuration", () =>
{
    foreach (var engine in Enum.GetValues<DenoiserKind>())
        foreach (string key in new[]{"calls","streaming","weakmic","whisper"})
        {
            var settings=ProcessingSettings.FromPreset(key,engine);
            settings.BufferMode=AudioBufferMode.Automatic;
            var roundtrip=System.Text.Json.JsonSerializer.Deserialize<ProcessingSettings>(System.Text.Json.JsonSerializer.Serialize(settings))!.SanitizedClone();
            Require(!roundtrip.GateEnabled && roundtrip.Engine==engine && roundtrip.BufferMode==AudioBufferMode.Automatic,"Preset/configuration lost engine or voice-gate choice.");
            if(key=="whisper") Require(!roundtrip.CompressorOn && roundtrip.NoiseReductionDb<=25,"Whisper preset is excessively aggressive.");
        }
});

Check("parametric EQ reaches its specified gain without changing neutral audio", () =>
{
    var eq=new ParametricEqualizer(); var s=new ProcessingSettings { EqEnabled=true, EqPresenceHz=1000, EqPresenceGainDb=6 };
    var signal=new float[480]; double inputEnergy=0, outputEnergy=0;
    for(int frame=0;frame<150;frame++)
    {
        for(int i=0;i<480;i++) signal[i]=(float)(2000*Math.Sin(2*Math.PI*1000*(frame*480+i)/48000));
        if(frame>=100) inputEnergy+=signal.Sum(x=>(double)x*x);
        eq.Process(signal,s);
        if(frame>=100) outputEnergy+=signal.Sum(x=>(double)x*x);
    }
    Require(Math.Abs(10*Math.Log10(outputEnergy/inputEnergy)-6)<.05,"EQ band gain is incorrect.");
    var neutral=new ParametricEqualizer(); s=new ProcessingSettings { EqEnabled=true };
    for(int frame=0;frame<20;frame++)
    {
        for(int i=0;i<480;i++) signal[i]=(float)(2000*Math.Sin((frame*480+i)*.13));
        var original=(float[])signal.Clone(); neutral.Process(signal,s);
        Require(signal.Zip(original,(a,b)=>Math.Abs(a-b)).Max()<.01,"Neutral EQ colors the input.");
    }
});

Check("EQ live changes remain finite and bypass is gradual", () =>
{
    var eq=new ParametricEqualizer(); var s=new ProcessingSettings(); var x=new float[480]; float previous=0; double jump=0;
    for(int frame=0;frame<250;frame++)
    {
        if(frame==30) { s.EqEnabled=true; s.EqLowGainDb=9; s.EqLowQ=3; }
        if(frame==80) { s.EqLowHz=400; s.EqLowGainDb=-9; s.EqLowQ=.35f; }
        if(frame==130) { s.EqEnabled=false; }
        if(frame==180) { s.EqEnabled=true; s.EqLowHz=40; s.EqLowGainDb=9; s.EqLowQ=3; }
        for(int i=0;i<480;i++) x[i]=(float)(2000*Math.Sin(2*Math.PI*120*(frame*480+i)/48000));
        eq.Process(x,s);
        foreach(float value in x) { Require(float.IsFinite(value) && Math.Abs(value)<20000,"EQ sweep is unstable."); jump=Math.Max(jump,Math.Abs(value-previous)); previous=value; }
    }
    Require(jump<600,$"EQ change produced a discontinuity of {jump:0.0} PCM units.");
});

Check("target EQ response agrees with a measured filter and bypass", () =>
{
    var s=new ProcessingSettings { EqEnabled=true, EqPresenceGainDb=6, EqPresenceHz=1000 };
    var curve=EqualizerResponse.Curve(s,1024);
    int index=(int)Math.Round(Math.Log(1000/40.0)/Math.Log(400)*1023);
    Require(Math.Abs(curve[index]-6)<.05,"Displayed response is incorrect.");
    s.EqEnabled=false;
    Require(EqualizerResponse.Curve(s).All(v=>Math.Abs(v)<1e-8),"Disabled EQ remains on the graph.");
    s.HighPassEnabled=true; s.HighPassHz=100;
    var hp=EqualizerResponse.Curve(s);
    Require(hp[0]<-15 && Math.Abs(hp[^1])<.01,"High-pass response is incorrect.");
});

Check("de-esser selectively attenuates highs while retaining low fundamentals", () =>
{
    double Render(double frequency,bool enabled)
    {
        var deess=new DeEsser();var s=new ProcessingSettings { DeEsserEnabled=enabled, DeEsserThresholdDb=-36, DeEsserMaxReductionDb=6 };
        var x=new float[480]; double energy=0;
        for(int frame=0;frame<150;frame++)
        {
            for(int i=0;i<480;i++) x[i]=(float)(12000*Math.Sin(2*Math.PI*frequency*(frame*480+i)/48000));
            var copy=(float[])x.Clone(); deess.Process(x,s);
            Require(x.All(float.IsFinite),"De-esser produced invalid audio.");
            if(!enabled) Require(x.SequenceEqual(copy),"Disabled de-esser changed input.");
            if(frame>=100) energy+=x.Sum(v=>(double)v*v);
        }
        return energy;
    }
    double high=10*Math.Log10(Render(9000,true)/Render(9000,false));
    double low=10*Math.Log10(Render(200,true)/Render(200,false));
    Require(high<-2 && high>-7,$"Sibilant band reduction is wrong: {high:0.00} dB.");
    Require(Math.Abs(low)<.1,$"Low fundamental was affected: {low:0.00} dB.");
});

Check("compressor has a continuous soft knee and configurable attack", () =>
{
    Require(Math.Abs(Compressor.GainCurveDb(-6,-18,4,6)+9)<.001,"Steady ratio is incorrect.");
    Require(Compressor.GainCurveDb(-24,-18,4,6)==0,"Below-knee audio was compressed.");
    foreach(float level in new[]{-21f,-15f}) Require(Math.Abs(Compressor.GainCurveDb(level-.001f,-18,4,6)-Compressor.GainCurveDb(level+.001f,-18,4,6))<.002,"Soft knee is discontinuous.");
    float Attack(float milliseconds)
    {
        var comp=new Compressor(); var signal=Enumerable.Repeat(12000f,480).ToArray();
        comp.Process(signal,signal.Length,-30,4,48000,milliseconds,120,6);
        return comp.GainReductionDb;
    }
    Require(Attack(1)>Attack(100)+2,"Attack control does not affect the transient.");
});

Check("output protection respects each configured ceiling and reports reduction", () =>
{
    foreach(float ceilingDb in new[]{-.3f,-.5f,-3f,-6f})
    {
        using var pipeline=new AudioPipeline(new DelayedIdentityEngine(0));
        var s=new ProcessingSettings { OutputCeilingDb=ceilingDb, OutputGainDb=12 };
        var input=Enumerable.Repeat(15000f,480).ToArray(); var output=new float[480]; FrameMeters meters=default;
        for(int i=0;i<30;i++) meters=pipeline.Process(input,output,s);
        float ceiling=MathF.Pow(10,ceilingDb/20)*32768;
        Require(output.All(x=>float.IsFinite(x) && Math.Abs(x)<=ceiling+1),"Output exceeded the sample ceiling.");
        Require(meters.LimiterReductionDb>1 && meters.OutRms<=meters.OutPeak+.001,"Metered reduction/RMS is incorrect.");
    }
});

Check("professional settings and exchanged profiles are bounded and preserve routing choices", () =>
{
    var settings=new ProcessingSettings { EqLowHz=float.NaN, EqAirQ=float.PositiveInfinity, EqBodyGainDb=100, CompAttackMs=0, CompReleaseMs=99999, OutputCeilingDb=1, DeEsserHz=-30 };
    var s=settings.SanitizedClone();
    Require(s.EqLowHz==120 && s.EqAirQ==.7f && s.EqBodyGainDb==9 && s.CompAttackMs==1 && s.CompReleaseMs==1000 && s.OutputCeilingDb==-.3f && s.DeEsserHz==2500,"Professional settings escaped bounds.");
    var current=new ProcessingSettings { Engine=DenoiserKind.RNNoise, BufferMode=AudioBufferMode.Stable, Bypass=true };
    var saved=ProcessingSettings.FromPreset("voiceover"); saved.GateEnabled=true;
    var loaded=EffectsProfile.Read(EffectsProfile.Serialize(saved),current,true);
    Require(loaded.Engine==current.Engine && loaded.BufferMode==current.BufferMode && loaded.Bypass && !loaded.GateEnabled,"Profile changed processing ownership/routing or activated a live VAD.");
    Require(loaded.EqEnabled && loaded.CompKneeDb==6 && loaded.DeEsserEnabled,"Profile lost effects.");
    bool invalid=false; try { EffectsProfile.Read("{\"Settings\":{}}",current,false); } catch(ArgumentException) { invalid=true; }
    Require(invalid,"Untagged settings were accepted as an effects profile.");
    var copied=new ProcessingSettings(); copied.CopyFrom(saved);
    Require(System.Text.Json.JsonSerializer.Serialize(copied)==System.Text.Json.JsonSerializer.Serialize(saved),"CopyFrom lost an effect setting.");
});

Check("natural singing preset keeps sustained notes out of the denoiser and gate", () =>
{
    foreach(var engine in Enum.GetValues<DenoiserKind>())
    {
        var singing=ProcessingSettings.FromPreset("singing",engine);
        Require(singing.Strength==0 && !singing.GateEnabled && !singing.HighPassEnabled && !singing.DeEsserEnabled,"Singing preset imposes speech suppression.");
    }
    using var pipeline=new AudioPipeline(new DelayedIdentityEngine(1440));
    var s=ProcessingSettings.FromPreset("singing"); s.CompressorOn=false;
    var input=new float[480]; var output=new float[480]; int delay=(int)Math.Round(pipeline.AlgorithmicDelayMs*48);
    for(int frame=0;frame<100;frame++)
    {
        for(int i=0;i<480;i++) input[i]=(float)(3000*Math.Sin(2*Math.PI*220*(frame*480+i)/48000));
        pipeline.Process(input,output,s);
        for(int i=0;i<480;i++)
        {
            int pos=frame*480+i-delay; float expected=pos<0?0:(float)(3000*Math.Sin(2*Math.PI*220*pos/48000));
            Require(Math.Abs(output[i]-expected)<.01,"Sustained dry note lost samples or was colored.");
        }
    }
});

Check("WAV export retains captured levels, precision format and sample count", () =>
{
    var capture=new ComparisonCapture(480,0,960);
    var raw=Enumerable.Repeat(6000f,480).ToArray(); var wet=Enumerable.Repeat(1500f,480).ToArray();
    capture.Add(raw,wet); capture.Add(raw,wet); var result=ComparisonResult.Create(capture);
    string path=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N")+".wav");
    try
    {
        foreach(bool original in new[]{true,false})
        {
            result.SaveWaveFile(path,original);
            using var reader=new WaveFileReader(path);
            Require(reader.WaveFormat.SampleRate==48000 && reader.WaveFormat.Channels==1 && reader.WaveFormat.BitsPerSample==32
                && reader.WaveFormat.Encoding==WaveFormatEncoding.IeeeFloat,"Export format is incorrect.");
            Require(reader.Length==960*4,"WAV lost or added samples.");
            var samples=new float[960]; Require(reader.ToSampleProvider().Read(samples,0,960)==960,"WAV cannot be read back.");
            Require(samples.All(x=>Math.Abs(x-(original?6000f:1500f)/32768)<1e-7),"Export applied preview loudness/fades or changed capture levels.");
        }
    }
    finally { File.Delete(path); }
});

Check("live parameter fades settle to exact silence without subnormal tails", () =>
{
    var gain=new SmoothedValue(); gain.SetTarget(1); gain.Next(); gain.SetTarget(0);
    float value=1;
    for(int sample=0;sample<96000;sample++) value=gain.Next();
    Require(value==0,"A completed fade retained a subnormal tail.");
});

Check("professional effects do not allocate on a warmed audio worker", () =>
{
    using var pipeline=new AudioPipeline(new DelayedIdentityEngine(0));
    var s=ProcessingSettings.FromPreset("broadcast"); var input=Enumerable.Repeat(1000f,480).ToArray(); var output=new float[480];
    s.ReverbEnabled=s.EchoEnabled=s.ChorusEnabled=true;
    for(int frame=0;frame<100;frame++) pipeline.Process(input,output,s);
    long before=GC.GetAllocatedBytesForCurrentThread();
    for(int frame=0;frame<100;frame++) pipeline.Process(input,output,s);
    Require(GC.GetAllocatedBytesForCurrentThread()==before,"Effects allocate memory on the audio worker.");
});

Check("fast singing creates a model-free path with no algorithmic delay", () =>
{
    var s=new ProcessingSettings { FastSinging=true, GateEnabled=true, Strength=1 };
    var clean=s.SanitizedClone();
    Require(clean.Strength==0 && !clean.GateEnabled,"Fast singing retains speech suppression.");
    using var engine=AudioEngineFactory.Create(clean);
    Require(engine is DirectVoiceEngine && engine.DelaySamples==0,"Fast singing creates a neural engine.");
    using var pipeline=new AudioPipeline(new DirectVoiceEngine());
    var input=Enumerable.Range(0,480).Select(i=>(float)(4000*Math.Sin(i*.03)+1000)).ToArray();var output=new float[480];
    pipeline.Process(input,output,clean);
    Require(pipeline.AlgorithmicDelayMs==0 && input.Zip(output).All(pair=>Math.Abs(pair.First-pair.Second)<.01),"Direct voice has look-ahead or changes dry input.");
    s=ProcessingSettings.FromPreset("vocalroom");s.FastSinging=true;
    var copy=new ProcessingSettings();copy.CopyFrom(s.SanitizedClone());
    var roundtrip=System.Text.Json.JsonSerializer.Deserialize<ProcessingSettings>(System.Text.Json.JsonSerializer.Serialize(copy))!;
    Require(roundtrip.FastSinging && roundtrip.ReverbEnabled,"Saved fast singing loses mode or effects.");
    Require(AudioEngineFactory.PlaybackTargetMs(new ProcessingSettings { FastSinging=true })==30
        && AudioEngineFactory.PlaybackTargetMs(new ProcessingSettings { FastSinging=true, BufferMode=AudioBufferMode.LowLatency })==20
        && AudioEngineFactory.PlaybackTargetMs(new ProcessingSettings())==60,"Fast mode buffer targets are incorrect.");
});

Check("raw source and app gain peaks are measured independently", () =>
{
    using var pipeline=new AudioPipeline(new DirectVoiceEngine());
    var s=new ProcessingSettings { FastSinging=true, InputGainDb=6 };
    var input=Enumerable.Repeat(24000f,480).ToArray();var output=new float[480];
    var m=pipeline.Process(input,output,s);
    Require(Math.Abs(m.RawInputPeak-24000f/32768)<.001 && m.InPeak>1.4,"Gain overflow cannot be distinguished from source level.");
    var diagnostics=new AudioDiagnostics();diagnostics.Observe(m.RawInputPeak,m.InPeak,0,1,0);
    Require(diagnostics.Observe(m.RawInputPeak,m.InPeak,0,1,0).Concern==AudioConcern.InputGain,"App gain is blamed on the source.");
});

Check("diagnostics distinguish source, gain, processing, gaps and output pressure", () =>
{
    var d=new AudioDiagnostics();DiagnosticSnapshot x=default;
    for(int i=0;i<10;i++)x=d.Observe(.3f,.3f,0,1,0);
    Require(x.Concern==AudioConcern.NoEvidence,"Healthy audio is diagnosed as broken.");
    d.Observe(.3f,.3f,0,20,0);
    Require(d.Observe(.3f,.3f,0,1,0).Concern==AudioConcern.NoEvidence,"An isolated slow frame is called sustained load.");
    for(int i=0;i<5;i++)x=d.Observe(.3f,.3f,0,12,2);
    Require(x.Concern==AudioConcern.ProcessingLoad && x.WorstFrameMs==20,"Sustained load or worst frame is lost.");
    for(int i=0;i<25;i++)x=d.Observe(.3f,.3f,0,1,2);
    Require(x.Concern==AudioConcern.NoEvidence && x.Underruns==2 && x.SlowWindows==6,"Old evidence does not expire or counters reset.");
    Require(d.Observe(.3f,.3f,0,1,3).Concern==AudioConcern.OutputGaps,"A new output gap is missed.");
    for(int i=0;i<2;i++)x=d.Observe(1,.4f,0,1,3);
    Require(x.Concern==AudioConcern.SourceLevel,"A hot source is hidden by app attenuation.");
    d=new AudioDiagnostics();for(int i=0;i<5;i++)x=d.Observe(.3f,.3f,8,1,0);
    Require(x.Concern==AudioConcern.OutputPressure,"Heavy output limiting is undetected.");
    var advisor=new StabilityAdvisor(DenoiserKind.DeepFilterNet3,AudioBufferMode.Automatic,true);
    for(int i=0;i<50;i++)advisor.Observe(12,0);
    Require(advisor.Current==StabilityAdvice.CheckDevice,"Direct voice recommends switching an inactive model.");
});

Check("diagnostics use bounded state and reject nonfinite telemetry", () =>
{
    var d=new AudioDiagnostics();for(int i=0;i<100;i++)d.Observe(.2f,.2f,0,1,0);
    long before=GC.GetAllocatedBytesForCurrentThread();
    for(int i=0;i<10000;i++)d.Observe(.2f,.2f,0,1,0);
    Require(GC.GetAllocatedBytesForCurrentThread()==before,"Diagnostics allocate on the audio worker.");
    bool rejected=false;try { d.Observe(float.NaN,0,0,0,0); }catch(ArgumentOutOfRangeException) { rejected=true; }
    Require(rejected,"Invalid telemetry enters the evidence report.");
});

Check("A/B renders identical input with compensated delays and matched preview levels", () =>
{
    var source=Enumerable.Range(0,25001).Select(i=>(float)(3000*Math.Sin(2*Math.PI*1000*i/48000))).ToArray();
    var untouched=(float[])source.Clone();
    var a=new ProcessingSettings { Strength=0 };var b=new ProcessingSettings { FastSinging=true, OutputGainDb=6 };
    var result=AbComparison.Render(source,a,b,factory:s=>s.FastSinging?new DirectVoiceEngine():new DelayedIdentityEngine(480));
    Require(source.SequenceEqual(untouched) && result.Original.Length==source.Length && result.Processed.Length==source.Length,"A/B mutates input or changes duration.");
    Require(source.Zip(result.Original).All(p=>Math.Abs(p.First-p.Second)<.05),"A rendering is misaligned.");
    float ratio=MathF.Pow(10,6f/20);
    Require(source.Zip(result.Processed).All(p=>Math.Abs(p.First*ratio-p.Second)<.05),"B rendering is misaligned or gain is wrong.");
    Require(Math.Abs((result.OriginalLufs+20*Math.Log10(result.OriginalGain))-(result.ProcessedLufs+20*Math.Log10(result.ProcessedGain)))<.001,
        "A louder preset remains louder in preview.");
    Require(result.OriginalGain<=1 && result.ProcessedGain<=1,"A/B loudness matching boosts audio.");
});

Check("A/B cancellation releases the active engine and skips the second render", () =>
{
    var cancellation=new CancellationTokenSource();var engine=new CancelingEngine(cancellation);int created=0;bool canceled=false;
    try { AbComparison.Render(new float[48000],new(),new(),cancellation.Token,s=>{created++;return engine;}); }
    catch(OperationCanceledException) { canceled=true; }
    Require(canceled && created==1 && engine.Disposed,"Canceled rendering leaks an engine or starts B.");
    bool rejected=false;try { AbComparison.Render(new[]{float.NaN},new(),new()); }catch(ArgumentException) { rejected=true; }
    Require(rejected,"Invalid A/B input initializes a native engine.");
});

Check("snapshot and imported effects preserve the local singing path and live gate ownership", () =>
{
    var snapshot=ProcessingSettings.FromPreset("broadcast");snapshot.GateEnabled=true;
    var current=new ProcessingSettings { FastSinging=true, Engine=DenoiserKind.RNNoise, BufferMode=AudioBufferMode.LowLatency };
    var loaded=EffectsProfile.Read(EffectsProfile.Serialize(snapshot),current,true);
    Require(loaded.FastSinging && loaded.Strength==0 && !loaded.GateEnabled && loaded.Engine==current.Engine
        && loaded.BufferMode==current.BufferMode && loaded.EqEnabled && loaded.CompressorOn,"Snapshot changes live routing or loses effects.");
});

Check("new effects remain transparent for existing settings and old profiles", () =>
{
    var s=System.Text.Json.JsonSerializer.Deserialize<ProcessingSettings>("{\"Strength\":0.5}")!;
    Require(!s.ReverbEnabled && !s.EchoEnabled && !s.ChorusEnabled,"An old configuration enables new effects.");
    var fx=new SpatialEffects(); var audio=Enumerable.Range(0,480).Select(i=>(float)(12000*Math.Sin(i*.1))).ToArray();
    var original=(float[])audio.Clone();
    for(int frame=0;frame<200;frame++) { Array.Copy(original,audio,480); fx.Process(audio,s); Require(audio.SequenceEqual(original),"Disabled effects change dry audio."); }
});

Check("echo repeats at the selected interval and feedback decays to silence", () =>
{
    var s=new ProcessingSettings { EchoEnabled=true, EchoMix=.5f, EchoDelayMs=100, EchoFeedback=.5f };
    var fx=new SpatialEffects(); var block=new float[480]; var result=new float[48000];
    for(int frame=0;frame<100;frame++)
    {
        Array.Clear(block); if(frame==0) block[0]=12000;
        fx.Process(block,s); Array.Copy(block,0,result,frame*480,480);
    }
    Require(result[0]==12000 && result.Take(4800).Skip(1).All(x=>x==0),"Echo changes the onset or arrives early.");
    Require(Math.Abs(result[4800]-6000)<.001,"Echo repeat timing/level is incorrect.");
    Require(result[9600]>0 && result[9600]<result[4800],"Feedback does not create a decaying repeat.");
    for(int frame=0;frame<4000;frame++) { Array.Clear(block); fx.Process(block,s); Require(block.All(float.IsFinite),"Echo feedback becomes nonfinite."); }
    Require(block.All(x=>x==0),"Echo feedback never settles to exact silence.");
});

Check("reverb has pre-delay and a longer decay retains more tail energy", () =>
{
    double Tail(float decay)
    {
        var fx=new SpatialEffects();var s=new ProcessingSettings { ReverbEnabled=true, ReverbMix=.5f, ReverbPreDelayMs=30, ReverbDecaySeconds=decay };
        var block=new float[480]; double energy=0;
        for(int frame=0;frame<400;frame++)
        {
            Array.Clear(block);if(frame==0) block[0]=12000;
            fx.Process(block,s);
            for(int i=0;i<480;i++)
            {
                int index=frame*480+i;
                Require(float.IsFinite(block[i]),"Reverb tail is invalid.");
                if(index>0 && index<1440+1499) Require(block[i]==0,"Reverb arrives before its pre-delay and first reflection.");
                if(index>=1440+1499 && index<1440+1499+1) Require(Math.Abs(block[i])>1,"First reflection is missing.");
                if(index>24000) energy+=(double)block[i]*block[i];
            }
        }
        for(int frame=0;frame<2600;frame++) { Array.Clear(block); fx.Process(block,s); }
        Require(block.All(x=>Math.Abs(x)<1e-8f),"Room tail does not decay.");
        return energy;
    }
    double shortTail=Tail(.4f),longTail=Tail(1.8f);
    Require(shortTail>0 && longTail>shortTail*4,"Decay control does not extend the tail.");
});

Check("chorus modulates a delayed voice without changing dry onset", () =>
{
    var fx=new SpatialEffects();var s=new ProcessingSettings { ChorusEnabled=true, ChorusMix=.25f, ChorusRateHz=1, ChorusDepthMs=5 };
    var block=new float[480];double difference=0;float peak=0;
    for(int frame=0;frame<200;frame++)
    {
        for(int i=0;i<480;i++) block[i]=(float)(8000*Math.Cos(2*Math.PI*220*(frame*480+i)/48000));
        fx.Process(block,s);
        if(frame==0) Require(Math.Abs(block[0]-6000)<.01,"Chorus delays the dry onset.");
        for(int i=0;i<480;i++)
        {
            Require(float.IsFinite(block[i]),"Chorus output is invalid.");peak=Math.Max(peak,Math.Abs(block[i]));
            if(frame>10) difference+=Math.Abs(block[i]-8000*Math.Cos(2*Math.PI*220*(frame*480+i)/48000));
        }
    }
    Require(difference>100000 && peak<=8000.01,"Chorus lacks modulation or has uncontrolled gain.");
});

Check("effect switches and delay moves fade smoothly while running", () =>
{
    var fx=new SpatialEffects();var s=new ProcessingSettings();var block=new float[480];float previous=1000,maxStep=0;
    for(int frame=0;frame<600;frame++)
    {
        if(frame==100) { s.ReverbEnabled=s.EchoEnabled=s.ChorusEnabled=true;s.ReverbMix=s.EchoMix=s.ChorusMix=.5f; }
        if(frame>=250 && frame<300) { s.EchoDelayMs=frame%2==0?40:1000;s.ReverbPreDelayMs=frame%2==0?0:100; }
        if(frame==400) s.ReverbEnabled=s.EchoEnabled=s.ChorusEnabled=false;
        Array.Fill(block,1000);fx.Process(block,s);
        foreach(float sample in block) { Require(float.IsFinite(sample),"Live effects became invalid.");maxStep=Math.Max(maxStep,Math.Abs(sample-previous));previous=sample; }
    }
    Require(maxStep<15,"An effect control produces an abrupt jump: "+maxStep);
    Require(Math.Abs(previous-1000)<.01,"Disabling effects does not restore dry audio.");
});

Check("vocal effects are limited at output and global bypass restores aligned dry audio", () =>
{
    using var pipeline=new AudioPipeline(new DelayedIdentityEngine(0));
    var s=new ProcessingSettings { Strength=0, ReverbEnabled=true, EchoEnabled=true, ChorusEnabled=true,
        ReverbMix=.5f, EchoMix=.5f, ChorusMix=.5f, EchoFeedback=.7f, OutputGainDb=12, OutputCeilingDb=-3 };
    var input=Enumerable.Repeat(24000f,480).ToArray();var output=new float[480];
    float ceiling=32768*MathF.Pow(10,-3f/20);
    for(int frame=0;frame<300;frame++) { pipeline.Process(input,output,s);Require(output.All(x=>float.IsFinite(x) && Math.Abs(x)<=ceiling+.1f),"An effect bypasses the output ceiling."); }
    s.Bypass=true;
    for(int frame=0;frame<300;frame++) pipeline.Process(input,output,s);
    Require(output.All(x=>Math.Abs(x-Limiter.Process(24000,ceiling))<.1f),"Global bypass retains effects or output gain.");
});

Check("vocal presets and effect settings survive copy, save and bounded profile load", () =>
{
    foreach(string key in new[]{"vocalroom","vocalhall","slapback"})
    {
        var preset=ProcessingSettings.FromPreset(key,DenoiserKind.RNNoise);
        Require(preset.Strength==0 && !preset.GateEnabled && preset.Engine==DenoiserKind.RNNoise,"An effect preset imposes speech suppression on singing.");
        Require(preset.ReverbEnabled || preset.EchoEnabled,"Vocal preset lacks an effect.");
    }
    var s=new ProcessingSettings { ReverbEnabled=true, EchoEnabled=true, ChorusEnabled=true,
        ReverbMix=.31f, ReverbDecaySeconds=2.5f, ReverbPreDelayMs=70, ReverbDamping=.7f,
        EchoMix=.4f, EchoDelayMs=710, EchoFeedback=.6f, ChorusMix=.2f, ChorusRateHz=2, ChorusDepthMs=8 };
    var copy=new ProcessingSettings();copy.CopyFrom(s);
    var loaded=EffectsProfile.Read(EffectsProfile.Serialize(copy),new ProcessingSettings(),false);
    Require(System.Text.Json.JsonSerializer.Serialize(s)==System.Text.Json.JsonSerializer.Serialize(loaded),"Effect save/load or copy loses settings.");
    s.EchoDelayMs=float.NaN;s.EchoFeedback=10;s.ReverbDecaySeconds=0;s.ReverbMix=float.PositiveInfinity;s.ChorusDepthMs=100;
    var bounded=s.SanitizedClone();
    Require(bounded.EchoDelayMs==180 && bounded.EchoFeedback==.7f && bounded.ReverbDecaySeconds==.2f && bounded.ReverbMix==.15f && bounded.ChorusDepthMs==10,"Unsafe effect settings escape bounds.");
});

Check("output starvation and recovery avoid abrupt zero-fill clicks", () =>
{
    byte[] Tone(short value, int samples)
    {
        var bytes = new byte[samples * 2];
        for (int i = 0; i < samples; i++) System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(i * 2, 2), value);
        return bytes;
    }
    var old = new BufferedWaveProvider(new WaveFormat(48000, 16, 1));
    var block = Tone(12000, 480); old.AddSamples(block, 0, block.Length);
    var buffer = new byte[960]; old.Read(buffer, 0, buffer.Length); short before = BitConverter.ToInt16(buffer, 958);
    old.Read(buffer, 0, buffer.Length);
    Require(Math.Abs(before - BitConverter.ToInt16(buffer, 0)) == 12000, "Original discontinuity was not reproduced.");

    var source = new BufferedWaveProvider(new WaveFormat(48000, 16, 1));
    var output = new StablePlaybackProvider(source, 10);
    source.AddSamples(block, 0, block.Length); output.Read(buffer, 0, buffer.Length);
    before = BitConverter.ToInt16(buffer, 958);
    output.Read(buffer, 0, buffer.Length);
    for (int i = 0; i < buffer.Length; i += 2)
    {
        short sample = BitConverter.ToInt16(buffer, i);
        Require(Math.Abs(sample - before) <= 51, "Starvation caused a click-sized sample jump."); before = sample;
    }
    Require(before == 0 && output.Underruns == 1, "Gap did not decay to silence/count once.");
    output.Read(buffer, 0, buffer.Length);
    Require(output.Underruns == 1, "A single gap was counted repeatedly.");
    block = Tone(-12000, 480); source.AddSamples(block, 0, block.Length);
    output.Read(buffer, 0, buffer.Length);
    for (int i = 0; i < buffer.Length; i += 2)
    {
        short sample = BitConverter.ToInt16(buffer, i);
        Require(Math.Abs(sample - before) <= 51, "Recovery caused an abrupt sample jump."); before = sample;
    }
    Require(before == -12000, "Recovery did not return to full level.");
});

Check("playback rebuffering preserves queued input and respects offsets", () =>
{
    var source = new BufferedWaveProvider(new WaveFormat(48000, 16, 1));
    var output = new StablePlaybackProvider(source, 60);
    var data = new byte[480 * 2]; source.AddSamples(data, 0, data.Length);
    var destination = Enumerable.Repeat((byte)0xa5, 1000).ToArray();
    output.Read(destination, 7, 960);
    Require(source.BufferedBytes == 960 && output.Underruns == 0, "Startup consumed undersized queued input or counted an underrun.");
    Require(destination.Take(7).All(v => v == 0xa5) && destination.Skip(967).All(v => v == 0xa5), "Playback overwrote bytes outside requested range.");
    for (int i = 0; i < 5; i++) source.AddSamples(data, 0, data.Length);
    output.Read(destination, 7, 960);
    Require(source.BufferedBytes == 4800, "Buffered input did not resume at target occupancy.");
});

Check("gain and compressor changes are continuous across frames", () =>
{
    using var pipeline = new AudioPipeline(new DelayedIdentityEngine(0));
    var input = Enumerable.Repeat(1000f, 480).ToArray(); var output = new float[480];
    var settings = new ProcessingSettings();
    for (int frame = 0; frame < 20; frame++) pipeline.Process(input, output, settings);
    float previous = output[^1], largestJump = 0;
    settings.InputGainDb = settings.OutputGainDb = 12;
    for (int frame = 0; frame < 20; frame++)
    {
        pipeline.Process(input, output, settings);
        foreach (float sample in output) { largestJump = Math.Max(largestJump, Math.Abs(sample - previous)); previous = sample; }
    }
    Require(largestJump < 100 && output[^1] > 14000, $"Gain transition jumped {largestJump:0.0} samples or did not reach target.");
    settings.CompressorOn = true; settings.CompThresholdDb = -40;
    for (int frame = 0; frame < 30; frame++) pipeline.Process(input, output, settings);
    previous = output[^1]; settings.CompressorOn = false;
    largestJump = 0;
    for (int frame = 0; frame < 20; frame++)
    {
        pipeline.Process(input, output, settings);
        foreach (float sample in output) { largestJump = Math.Max(largestJump, Math.Abs(sample - previous)); previous = sample; }
    }
    Require(largestJump < 100, "Disabling the compressor jumped the output level.");
});

Check("persisted settings cannot inject nonfinite/extreme audio parameters", () =>
{
    var s = new ProcessingSettings { Engine = (DenoiserKind)99, BufferMode = (AudioBufferMode)99,
        Strength = float.NaN, NoiseReductionDb = float.PositiveInfinity, InputGainDb = 1000, OutputGainDb = float.NaN }.SanitizedClone();
    Require(s.Engine == DenoiserKind.DeepFilterNet3 && s.BufferMode == AudioBufferMode.Balanced &&
        s.Strength == 1 && s.NoiseReductionDb == 35 && s.InputGainDb == 18 && s.OutputGainDb == 0, "Invalid settings were not bounded.");
});

Check("live resampling survives fragmented capture and empty gaps", () =>
{
    foreach (int rate in new[] { 16000, 44100, 96000 })
    {
        float[] Render(bool fragmented)
        {
            const int frames = 8891;
            var source = new BufferedWaveProvider(WaveFormat.CreateIeeeFloatWaveFormat(rate, 2)) { ReadFully = false };
            var mono = new DownmixSampleProvider(source.ToSampleProvider());
            var resampler = new StreamingResamplingSampleProvider(mono, () => source.BufferedBytes / 8, 48000);
            var output = new float[480]; var result = new List<float>();
            void Drain()
            {
                int n;
                while ((n = resampler.Read(output, 0, output.Length)) > 0) result.AddRange(output.Take(n));
                Require(resampler.Read(output, 0, output.Length) == 0, "Capture starvation synthesized extra audio.");
            }
            for (int offset = 0; offset < frames;)
            {
                int count = Math.Min(fragmented ? 13 + offset % 733 : frames, frames - offset);
                var samples = new float[count * 2];
                for (int i = 0; i < count; i++)
                {
                    samples[i * 2] = .2f * MathF.Sin((offset + i) * .11f);
                    samples[i * 2 + 1] = .1f * MathF.Cos((offset + i) * .07f);
                }
                var bytes = new byte[samples.Length * 4];
                Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
                source.AddSamples(bytes, 0, bytes.Length);
                offset += count; Drain();
            }
            int expected = (int)(frames * 48000.0 / rate);
            Require(result.Count >= expected - 32 && result.Count <= expected + 1, "Resampling changed the stream duration.");
            return result.ToArray();
        }
        var bulk = Render(false); var fragmented = Render(true);
        Require(bulk.Length == fragmented.Length, "Capture chunk size changed output duration.");
        for (int i = 0; i < bulk.Length; i++)
            Require(Math.Abs(bulk[i] - fragmented[i]) < .0001f, "Capture gaps changed the resampled waveform.");
    }
});

Check("sample delays across frame boundaries", () =>
{
    foreach (int delay in new[] { 0, 17, 480, 960, 1440, 2400 })
    {
        var ring = new SampleDelay(delay); var x = new float[480]; var y = new float[480];
        for (int frame = 0; frame < 10; frame++)
        {
            for (int i = 0; i < x.Length; i++) x[i] = frame * 480 + i + 1;
            ring.Process(x, y);
            for (int i = 0; i < y.Length; i++)
                Require(y[i] == Math.Max(0, frame * 480 + i + 1 - delay), "Incorrect sample delay.");
        }
    }
});

Check("wet/dry alignment for a delayed engine", () =>
{
    using var dry = new AudioPipeline(new DelayedIdentityEngine(1440));
    using var mixed = new AudioPipeline(new DelayedIdentityEngine(1440));
    using var wet = new AudioPipeline(new DelayedIdentityEngine(1440));
    var input = new float[480]; var a = new float[480]; var b = new float[480]; var c = new float[480];
    for (int frame = 0; frame < 20; frame++)
    {
        for (int i = 0; i < 480; i++) input[i] = 2000 * MathF.Sin((frame * 480 + i) * 0.1f);
        dry.Process(input, a, new ProcessingSettings { Strength = 0 });
        mixed.Process(input, b, new ProcessingSettings { Strength = 0.37f });
        wet.Process(input, c, new ProcessingSettings { Strength = 1 });
        for (int i = 0; i < 480; i++)
            Require(Math.Abs(a[i] - b[i]) < 0.001f && Math.Abs(a[i] - c[i]) < 0.001f, "Dry and wet paths are misaligned.");
    }
});

Check("bypass preserves latency and the raw waveform", () =>
{
    using var pipeline = new AudioPipeline(new DelayedIdentityEngine(1440));
    var input = new float[480]; var output = new float[480];
    int delay = (int)(pipeline.AlgorithmicDelayMs * 48);
    for (int frame = 0; frame < 20; frame++)
    {
        for (int i = 0; i < 480; i++) input[i] = Signal(frame * 480 + i);
        pipeline.Process(input, output, new ProcessingSettings { Bypass = true });
        for (int i = 0; i < 480; i++)
        {
            int source = frame * 480 + i - delay;
            Require(Math.Abs(output[i] - (source < 0 ? 0 : Signal(source))) < 0.001f, "Bypass path is not aligned.");
        }
    }
});

Check("DeepFilterNet native loading, metadata, and silence", () =>
{
    using var engine = new DeepFilterNetEngine();
    Require(engine.FrameSize == 480 && engine.DelaySamples == 1440, "Unexpected pinned model metadata.");
    var input = new float[480]; var output = new float[480];
    for (int frame = 0; frame < 30; frame++)
    {
        Require(engine.Process(input, output) is null, "SNR was exposed as VAD.");
        Require(output.All(v => float.IsFinite(v) && Math.Abs(v) < 0.01f), "Silence produced invalid/non-silent output.");
    }
});

Check("DeepFilterNet processes and suppresses stationary noise", () =>
{
    using var pipeline = new AudioPipeline(new DeepFilterNetEngine());
    var input = new float[480]; var output = new float[480]; var random = new Random(75667);
    double inputEnergy = 0, outputEnergy = 0; var times = new List<double>();
    for (int frame = 0; frame < 400; frame++)
    {
        for (int i = 0; i < 480; i++) input[i] = (float)(random.NextDouble() * 2 - 1) * 2000;
        long begin = Stopwatch.GetTimestamp();
        var meters = pipeline.Process(input, output, new ProcessingSettings());
        double elapsed = Stopwatch.GetElapsedTime(begin).TotalMilliseconds;
        Require(meters.Vad is null, "Disabled gate unexpectedly ran a voice detector.");
        Require(output.All(float.IsFinite), "Invalid processed audio.");
        if (frame < 100) continue;
        times.Add(elapsed);
        for (int i = 0; i < 480; i++) { inputEnergy += input[i] * input[i]; outputEnergy += output[i] * output[i]; }
    }
    double ratio = Math.Sqrt(outputEnergy / inputEnergy);
    Require(ratio < 0.75, $"Stationary noise not reduced: RMS ratio {ratio:0.000}.");
    times.Sort();
    string reduction = ratio == 0 ? "output is silent" : $"RMS reduction {20 * Math.Log10(ratio):0.0} dB";
    Console.WriteLine($"  Noise-only check: {reduction}; frame mean {times.Average():0.00} ms, p95 {times[(int)(times.Count * .95)]:0.00} ms (this host only).");
});

Check("model integrity errors are actionable", () =>
{
    string path = Path.GetTempFileName();
    try
    {
        File.WriteAllText(path, "invalid model");
        try { using var engine = new DeepFilterNetEngine(path); throw new Exception("Corrupt model accepted."); }
        catch (InvalidDataException) { }
    }
    finally { File.Delete(path); }
});

Check("DeepFilterNet attenuation control retains more detail at lower limits", () =>
{
    using var mild = new DeepFilterNetEngine(); using var strong = new DeepFilterNetEngine();
    var a = new ProcessingSettings { NoiseReductionDb = 10 }; var b = new ProcessingSettings { NoiseReductionDb = 60 };
    var input = new float[480]; var x = new float[480]; var y = new float[480]; var random = new Random(75667);
    double mildEnergy = 0, strongEnergy = 0;
    for (int frame = 0; frame < 200; frame++)
    {
        for (int i = 0; i < input.Length; i++) input[i] = (float)(random.NextDouble() * 2 - 1) * 2000;
        mild.Configure(a); strong.Configure(b); mild.Process(input, x); strong.Process(input, y);
        if (frame < 100) continue;
        for (int i = 0; i < x.Length; i++) { mildEnergy += x[i] * x[i]; strongEnergy += y[i] * y[i]; }
    }
    Require(mildEnergy > strongEnergy * 20 && mildEnergy > 0, "Native attenuation control did not change suppression strength.");
});
if (OperatingSystem.IsWindows())
{
    Check("RNNoise fallback and DeepFilterNet optional sidechain VAD", () =>
    {
        var input = new float[480]; var output = new float[480];
        using (var engine = new RNNoiseEngine())
        {
            var vad = engine.Process(input, output);
            Require(vad is >= 0f and <= 1f && output.All(float.IsFinite), "RNNoise returned invalid audio or VAD.");
        }
        using var pipeline = new AudioPipeline(new DeepFilterNetEngine());
        var meters = pipeline.Process(input, output, new ProcessingSettings { GateEnabled = true });
        Require(meters.Vad is >= 0f and <= 1f && output.All(float.IsFinite), "Parallel VAD did not run.");
    });
}
else Console.WriteLine("SKIP Windows RNNoise binary and optional sidechain VAD (Windows DLL).");
Console.WriteLine($"{passed} checks passed. Live Windows audio and speech quality need separate validation.");

static float Signal(int sample) => 1000 * MathF.Sin(sample * .13f);

sealed class DelayedIdentityEngine(int delay) : IDenoiseEngine
{
    private readonly SampleDelay _delay = new(delay);
    public string Name => "test";
    public int FrameSize => 480;
    public int DelaySamples => delay;
    public float? Process(float[] input, float[] output) { _delay.Process(input, output); return null; }
    public void Dispose() { }
}

static class WavEnhancer
{
    public static void Enhance(string inputPath, string outputPath, DenoiserKind kind)
    {
        if (Path.GetFullPath(inputPath) == Path.GetFullPath(outputPath)) throw new ArgumentException("Use a different output file.");
        if (File.Exists(outputPath)) throw new IOException("Output exists; choose a new filename.");
        using var reader = new BinaryReader(File.OpenRead(inputPath));
        string FourCc() => new(reader.ReadChars(4));
        if (FourCc() != "RIFF") throw new InvalidDataException("Expected RIFF WAV.");
        reader.ReadUInt32();
        if (FourCc() != "WAVE") throw new InvalidDataException("Expected WAVE.");
        byte[]? data = null; bool validFormat = false;
        while (reader.BaseStream.Position + 8 <= reader.BaseStream.Length)
        {
            string id = FourCc(); uint size = reader.ReadUInt32(); long end = reader.BaseStream.Position + size;
            if (end > reader.BaseStream.Length) throw new InvalidDataException("Truncated WAV.");
            if (id == "fmt ")
            {
                if (size < 16) throw new InvalidDataException("Invalid WAV format.");
                ushort format = reader.ReadUInt16(), channels = reader.ReadUInt16(); uint rate = reader.ReadUInt32();
                reader.ReadUInt32(); reader.ReadUInt16(); ushort bits = reader.ReadUInt16();
                validFormat = format == 1 && channels == 1 && rate == 48000 && bits == 16;
            }
            if (id == "data") data = reader.ReadBytes(checked((int)size));
            reader.BaseStream.Position = end + (size % 2);
        }
        if (!validFormat || data is null || data.Length % 2 != 0)
            throw new InvalidDataException("Use 48kHz mono PCM16 WAV.");
        using var pipeline = new AudioPipeline(kind == DenoiserKind.DeepFilterNet3 ? new DeepFilterNetEngine() : new RNNoiseEngine());
        int samples = data.Length / 2, delay = (int)(pipeline.AlgorithmicDelayMs * 48);
        var input = new float[480]; var output = new float[480]; var result = new short[samples];
        long begin = Stopwatch.GetTimestamp();
        for (int offset = 0; offset < samples + delay; offset += 480)
        {
            for (int i = 0; i < 480; i++) input[i] = offset + i < samples ? BitConverter.ToInt16(data, (offset + i) * 2) : 0;
            pipeline.Process(input, output, new ProcessingSettings { Engine = kind });
            for (int i = 0; i < 480; i++)
            {
                int index = offset + i - delay;
                if (index >= 0 && index < samples) result[index] = (short)Math.Clamp(output[i], short.MinValue, short.MaxValue);
            }
        }
        using var writer = new BinaryWriter(new FileStream(outputPath, FileMode.CreateNew));
        void Cc(string value) => writer.Write(System.Text.Encoding.ASCII.GetBytes(value));
        Cc("RIFF"); writer.Write(36 + samples * 2); Cc("WAVE"); Cc("fmt "); writer.Write(16);
        writer.Write((ushort)1); writer.Write((ushort)1); writer.Write(48000); writer.Write(96000);
        writer.Write((ushort)2); writer.Write((ushort)16); Cc("data"); writer.Write(samples * 2);
        foreach (short value in result) writer.Write(value);
        Console.WriteLine($"Enhanced {samples / 48000.0:0.00}s using {pipeline.EngineName} in {Stopwatch.GetElapsedTime(begin).TotalSeconds:0.00}s; compensated {delay / 48.0:0}ms delay.");
    }
}

sealed class CancelingEngine(CancellationTokenSource cancellation) : IDenoiseEngine
{
    private int _calls;
    public bool Disposed { get; private set; }
    public string Name => "Cancellation test";
    public int FrameSize => 480;
    public int DelaySamples => 0;
    public float? Process(float[] input,float[] output)
    {
        Array.Copy(input,output,480);
        if(++_calls==12)cancellation.Cancel();
        return null;
    }
    public void Dispose() => Disposed=true;
}
