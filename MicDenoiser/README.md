# MicDenoiser — DeepFilterNet3

تطبيق Windows x64 / WPF / .NET 8 لعزل ضوضاء المايك في الوقت الحقيقي.
DeepFilterNet3 هو المحرّك الافتراضي، وRNNoise متاح كوضع خفيف.
دي ترقية للمشروع المرفق؛ الوصول لجودة Krisp محتاج مقارنة على تسجيلات فعلية، مش مجرد تبديل النموذج.

## التشغيل على ويندوز

1. Windows 10/11 x64، وثبّت VB-Cable من https://vb-audio.com/Cable/ ثم أعد التشغيل.
2. لو بتشغّل نسخة المصدر: ثبّت .NET 8 SDK. مكتبة RNNoise الأصلية قد تحتاج Visual C++ Redistributable x64.
3. من جذر المشروع شغّل:

```powershell
powershell -File scripts/build.ps1
cd MicDenoiser
dotnet run -c Release --no-build
```

اختار المايك الحقيقي، والمخرج `CABLE Input`، ومحرك العزل، ثم ابدأ.
في Discord / Zoom / OBS اختار `CABLE Output` كمايك.
تغيير المحرك يحتاج إيقاف المعالجة أولًا. باقي الإعدادات تتحدّث بين الإطارات.
لتقييم العزل اقفل عزل الضوضاء الإضافي في برنامج المكالمات، واستخدم سماعات لتجنب رجوع صوت السماعات للمايك.

لإنشاء نسخة تحتوي .NET runtime:

```powershell
powershell -File scripts/build.ps1 -Publish
```

الناتج في `artifacts/windows`. انقل المجلد كله، بما فيه ملفات DLL ومجلد models.

## المحرّكات والبوابة

- **DeepFilterNet3:** نموذج جاهز 48kHz، بتمريرة واحدة. بوابة الكلام متوقفة افتراضيًا.
- **RNNoise:** أخف، مع احتمال الكلام الأصلي من RNNoise.
- تفعيل البوابة مع DeepFilterNet يشغّل RNNoise ككاشف كلام موازي على الدخل فقط؛ صوته المعالَج لا يدخل الخرج.
- القيمة العائدة من DeepFilterNet هي local SNR، ولا تُستخدم كاحتمال كلام.
- تم إلغاء تمريرتي RNNoise لتجنب المبالغة في العزل. إعداد Cascade القديم يُتجاهل عند تحميل JSON.
- إعداد استوديو يبدأ بدون high-pass أو EQ أو compressor أو تضخيم خرج. المؤثرات متاحة اختياريًا.
- الإعدادات السابقة تُحفظ وتُقرأ في `%AppData%/MicDenoiser/settings.json`؛ لو لديك ملف قديم، اختار استوديو لإعادة تطبيق الإعدادات الجديدة.

## مسار الصوت والتأخير

```text
WASAPI capture (صيغة الجهاز)
  → buffer محدود → عامل معالجة مستقل → mono / إعادة أخذ العينات إلى 48kHz
  → تضخيم الدخل → high-pass 70Hz اختياري → محرّك العزل
  → مزج dry/wet بعد محاذاة التأخير → look-ahead 20ms
  → بوابة اختيارية → EQ اختياري → compressor اختياري → limiter
  → WASAPI output → CABLE Input
```

النموذج المحدد يضيف 30ms؛ مع look-ahead يصبح التأخير الخوارزمي 50ms لـDeepFilterNet3 و30ms لـRNNoise.
**هذه ليست أرقام التأخير الكلي.** buffers والأجهزة وإعادة أخذ العينات وVB-Cable تضيف تأخيرًا.
مسار المقارنة bypass له نفس التأخير الخوارزمي، ويستخدم انتقالًا عبر إطار واحد بدل تبديل فجائي.
قوة العزل مزج مع صوت أصلي محاذى زمنيًا؛ تقليلها يُرجع جزءًا من الضوضاء.

الواجهة تعرض أبطأ زمن معالجة إطار خلال فترة التحديث، والصوت الموجود في buffers، وعدد نقص بيانات الخرج.
الإطار مدته 10ms؛ لو زمن المعالجة يتجاوزها باستمرار، جرّب RNNoise.
التطبيق يوقف الصوت برسالة لو التراكم يتجاوز 100ms، بدل إسقاط الصوت بصمت أو ترك التأخير يزيد.

## إعادة بناء DeepFilterNet DLL

مكتبة Windows x64 والنموذج مرفقان. لإعادة البناء من المصدر تحتاج Rust وVisual Studio Build Tools مع Desktop development with C++:

```powershell
powershell -File scripts/build-native.ps1
powershell -File scripts/build.ps1
```

Rust مثبت على 1.90.0، ومصدر DeepFilterNet مثبت على commit محدد، والاعتماديات في Cargo.lock.
الربط يستخدم wrapper C صغير يعيد أخطاء التحميل والمعالجة إلى C#، ولا يمرر local SNR إلى بوابة الكلام.
مدخل ومخرج المكتبة مصفوفتان منفصلتان، مع تحويل PCM16 float إلى المجال الطبيعي للنموذج وإرجاعه.
SHA-256 للنموذج يُراجع قبل تحميله. نصوص التراخيص في licenses.

## اختبارات المعالجة ومقارنة التسجيلات

```powershell
dotnet run --project MicDenoiser.Checks -c Release
```

الاختبارات تشمل محاذاة dry/wet، وتأخير bypass، وتحميل النموذج، والصمت، وعزل ضوضاء ثابتة، والتحقق من سلامة الملف.
تعمل اختبارات نواة المعالجة على Linux أيضًا عند بناء المكتبة المحلية:

```bash
cargo build --manifest-path MicDenoiser/native-src/Cargo.toml --release --locked -j 3
dotnet run --project MicDenoiser.Checks -c Release
```

لمقارنة ملف صوتي: استخدم WAV بصيغة mono، 48kHz، PCM16. الأمر يحافظ على عدد العينات ويعوّض التأخير الخوارزمي؛ لا يكتب فوق ملف موجود:

```powershell
dotnet run --project MicDenoiser.Checks -c Release -- --enhance noisy.wav deepfilter.wav DeepFilterNet3
dotnet run --project MicDenoiser.Checks -c Release -- --enhance noisy.wav rnnoise.wav RNNoise
```

اختبر الكلام المصري والهمس وأول الكلمات مع مراوح وكيبورد وموسيقى ومتحدث آخر في الخلفية.
ساوِ مستوى الصوت عند المقارنة، وقارن مع Krisp على نفس الدخل.
عزل المتحدث المستهدف، وإلغاء صدى السماعات، وإزالة صدى الغرفة ليست وظائف مثبتة بهذه النسخة.
اختبارات الملفات وبناء WPF لا تغني عن اختبار مايك وVB-Cable فعليين على ويندوز.
