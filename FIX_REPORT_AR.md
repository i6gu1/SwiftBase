# تقرير الإصلاحات الشامل — SwiftBase v1.1.0

**التاريخ:** 23 نوفمبر 2026  
**الإصدار:** `1.1.0` (`1.1.0.0` للتجميع)  
**المستودع:** `https://github.com/i6gu1/SwiftBase.git` — الفرع `main`  
**الموقع:** `https://swiftbase-download.vercel.app`

---

## 1. الملخص التنفيذي

تم في هذا الإصدار:

1. **حذف جميع البيانات الافتراضية والتجريبية بالكامل** من التطبيق: ملفات العيّنات، ملفات الاتصال التجريبية، واجهة «Demo workspace»، مسارات التنفيذ التجريبية، وكل الشيفرات التي كانت تُنشئ جداول أو صفوفًا وهمية.
2. **تدقيق أمني كامل** أسفر عن **11 نتيجة**: 1 عالية الخطورة، 5 متوسطة، 5 منخفضة. **تم إصلاح جميع النتائج بالكامل.**
3. **اختبار الاتصال الفعلي** مع SQLite وPostgreSQL، بما في ذلك اختبارات DDL وقراءة المخطط والقراءة المجزّأة وإدراج/تعديل/حذف قيم `BLOB` بأنواعها الأصلية.
4. **بناء وتوقيع الإصدار النهائي** عبر `build.ps1 -Publish -Installer -Configuration Release`، مع التحقق من إصدارات الملفات وتجزئة SHA256.
5. **تحديث موقع التحميل** إلى v1.1.0 مع الحفاظ على الهوية البصرية: واجهة داكنة على طراز TablePlus، بكسل آرت، برتقالي/أسود، التذييل `made by the l house 2026`، والحساب `@emxomol`.
6. **دفع الكود إلى GitHub** وإعادة نشر الموقع عبر Vercel CLI.

> ملاحظة مهمة: مبنى المُثبّت القديم الذي كانت تجزئته `C85717C6...` كان مبنيًا من نسخة v1.0 قديمة بسبب استخدام `-Installer` دون `-Publish`. تم **تجاهله تمامًا**، وأُعيد بناء المُثبّت النهائي من الصفر باستخدام `-Publish -Installer`.

---

## 2. حذف البيانات الافتراضية والتجريبية

| العنصر | الحالة |
|---|---|
| `src/SwiftBase.App/Models/SampleData.cs` | **حُذف نهائيًا** |
| خاصيات `IsDemo` / `IsLive` في النماذج وعناصر الواجهة | **حُذفت بالكامل** |
| بطاقة «Demo workspace» في شاشة الترحيب | **حُذفت**، وأصبحت البطاقات اثنتين فقط: اتصال جديد وفتح ملف SQLite |
| تنفيذ استعلامات تجريبية `RunDemoAsync` | **حُذف**؛ وحدة SQL تنفّذ دائمًا على الجلسة الحقيقية |
| صفوف/جداول تجريبية في تبويبات الجداول | **حُذفت**؛ كل قراءة تمر عبر `ReadPageAsync` |
| اتصالات افتراضية عند بدء التشغيل | **حُذفت**؛ قائمة الاتصالات تبدأ فارغة وتُحمّل من الخزنة فقط |
| بيانات تجريبية على القرص | **لا تُنشأ ولا تُكتب**؛ لا توجد كلمات مرور أو حسابات افتراضية |

تم التحقق باستخدام بحث شامل في `src` عن `demo`, `Demo`, `IsDemo`, `IsLive`, `SampleData`؛ **لا توجد أي نتائج متبقية** في شيفرة C# أو XAML.

---

## 3. نتائج التدقيق والإصلاحات

إجمالي النتائج: **1 High · 5 Medium · 5 Low — تم إصلاحها جميعًا.**

| # | الخطورة | المشكلة | الإصلاح | الحالة |
|---:|---|---|---|---|
| 1 | **عالية** | TLS يشفّر حركة قاعدة البيانات دون التحقق من هوية الخادم | `VerifyFull` افتراضيًا مع التحقق من المرجع واسم المضيف لكلٍّ من PostgreSQL وMySQL | ✅ |
| 2 | متوسطة | حقن الصيغ في تصدير CSV | تعطيل محفّزات الصيغ بإضافة علامة اقتباس قبل `= + - @` وTab وCR | ✅ |
| 3 | متوسطة | توليد INSERT غير آمن وخاص بـ SQL عام | توليد عبر مزوّد البيانات مع معرّفات وقيم حرفية صحيحة لكل محرك | ✅ |
| 4 | متوسطة | تسريب جلسات وسباق عند تبديل قاعدة البيانات | إلغاء + جيل + تحرير آمن ونسخ الإعداد قبل التبديل | ✅ |
| 5 | متوسطة | الشبكة تحفظ نصوص العرض بدل القيم الأصلية → تعطب BLOB/Timestamp | فصل القيم الأصلية `object?` عن نص العرض | ✅ |
| 6 | متوسطة | مفاتيح مضيف SSH تُوثَّق صامتةً عند أول اتصال وتتسابق | عملية ذرية بقفل عام وميوتكس، وثقة صريحة اختيارية فقط | ✅ |
| 7 | منخفضة | كلمة مرور ظاهرة بعد استيراد رابط الاتصال | تعتيم كلمة المرور فور الاستيراد | ✅ |
| 8 | منخفضة | سجل أعطال غير محدد وغير مقيد الحجم | تعقيم + ACL للمستخدم الحالي + تدوير 256 KB | ✅ |
| 9 | منخفضة | رمز Vercel OIDC منتهٍ في `web/.env.local` | حذف الملف نهائيًا | ✅ |
| 10 | منخفضة | فشل فك تشفير DPAPI صامت + نسخ `.bak` قديمة | منع الكتابة عند الفشل + حذف النسخة القديمة | ✅ |
| 11 | منخفضة | استثناءات غير معالجة عند حذف اتصال | حذف غير متزامن مع `try/catch` وفصل منتظر | ✅ |

---

## 4. تفاصيل الإصلاحات الأمنية

### 4.1 مصادقة خادم قاعدة البيانات (عالية الخطورة)

**قبل الإصلاح:** كان PostgreSQL يستخدم `SslMode.Require` وMySQL يستخدم `MySqlSslMode.Required`؛ أي تشفير بلا تحقق من الشهادة أو اسم المضيف، مما يسمح بهجوم MITM بشهادة مزيفة.

**بعد الإصلاح:**

- `PostgresProvider.cs`: `SslMode.VerifyFull` افتراضيًا، مع `RootCertificate` اختياري و`CheckCertificateRevocation = true`.
- `MySqlProvider.cs`: `MySqlSslMode.VerifyFull` افتراضيًا، مع `SslCa` اختياري و`SkipCertificateRevocationCheck = false`.
- أُضيفت خاصيات `AllowUnverifiedTls` و`RootCertificatePath` إلى `ConnectionConfig` مع حفظها في الخزنة.
- `AllowUnverifiedTls` **افتراضه `false`** ويظهر في الواجهة كخيار خطير صريح، ولا يُقبل مع مسار CA في الوقت نفسه.
- عند استخدام نفق SSH، تُعطَّل TLS على واجهة loopback فقط لأن قناة SSH نفسها مشفّرة ومصدَّقة بمفتاح المضيف؛ ولا تُستخدم شهادة قاعدة البيانات على `127.0.0.1`.
- `IncludeErrorDetail = false` في PostgreSQL لتقليل تسريب تفاصيل الاستعلام في رسائل الخطأ.

**النتيجة:** الاتصالات المباشرة تفشل مغلقةً (fail closed) إذا تعذّر التحقق من الشهادة، ما لم يختر المستخدم صراحةً تعطيل التحقق.

---

### 4.2 تصدير CSV آمن

في `QueryTabViewModel.EscapeCsv()`:

- يُضاف `'` قبل أي قيمة تبدأ بـ `=`, `+`, `-`, `@`, Tab أو CR.
- يُطبَّق الحماية على **العناوين والخلايا** معًا.
- تُحافظ على اقتباس CSV القياسي بعد التعقيم.

**النتيجة:** لا تُنفَّذ صيغ مثل `=HYPERLINK(...)` أو `=cmd|...` عند فتح الملف في Excel أو LibreOffice.

---

### 4.3 تصدير INSERT صحيح لكل محرك

- أُضيف إلى `IDatabaseProvider`:
  - `QuoteIdentifier(string identifier)`
  - `FormatSqlLiteral(object? value)`
- PostgreSQL: `"identifier"` مع مضاعفة `"`، و`'\\x...'::bytea` للبايتات.
- MySQL: `` `identifier` `` مع مضاعفة `` ` ``، و`X'...'` للبايتات، ومضاعفة الشرطة العكسية قبل مضاعفة `'` لتفادي كسر النص الحرفي.
- SQLite: `"identifier"` مع مضاعفة `"`، و`X'...'` للبايتات.
- القيم الزمنية تُكتب بصيغة `O` القابلة للقراءة العكسية، والأرقام بثقافة invariant، و`NULL` من `DBNull`.
- لم يعد التصدير يمرّر نصوص العرض (`ValueFormatter.FormatExport`) إلى SQL.

**النتيجة:** سكربت التصدير يعكس نوع المحرك ولا يمكن حقنه عبر قيمة تحتوي على `\` أو `'`.

---

### 4.4 تبديل قاعدة البيانات

في `MainViewModel.SwitchDatabaseAsync()`:

- `CancellationTokenSource` لكل تبديل، مع إلغاء السابق.
- عدّاد جيل `_sessionGeneration` لمنع عملية قديمة من الكتابة فوق جلسة أحدث.
- نسخ الإعداد عبر `CloneForDatabase(database)` بدل تعديل الإعداد الحيّ قبل النجاح.
- الجلسة الجديدة والقديمة تُحرَّران في `finally` بعد الالتزام الناجح.
- الفشل يعيد `SelectedDatabase` ولا يترك جلسة أو نفق SSH مفتوحًا.
- حذف الاتصال النشط أصبح `DeleteConnectionAsync` مع فصل مُنتظر (`force: true`).

---

### 4.5 الحفاظ على أنواع القيم في الشبكة

- أُضيف سجل `CellData(string? Display, object? Typed)`.
- `CellViewModel` و`RowViewModel` يحتفظان بـ `TypedValue` و`OriginalTypedValue` منفصلين عن نص العرض.
- `Snapshot()` / `OriginalSnapshot()` / `ChangedSnapshot()` تعيد `Dictionary<string, object?>` بدل نصوص.
- `TableChange.Values` و`OriginalValues` أصبحتا قيمًا مكتوبة.
- معاينة BLOB في الواجهة تبقى مختصرة للعرض فقط، ولا تُستخدم أبدًا في UPDATE/DELETE.

**النتيجة:** حفظ صف يحتوي على BLOB أكبر من 24 بايت أو طابع زمني بكسور ثانية لم يعد يفشل أو يتلف البيانات.

---

### 4.6 مفاتيح مضيف SSH

- `SshHostKeyStore.VerifyOrTrust()` أصبح عملية ذرية واحدة تحت قفل عام لكل مسار + `Mutex` مسمّى، لمنع تسابق نسخ التطبيق أو الخيوط.
- لا توجد ثقة تلقائية عند أول استخدام؛ يجب تفعيل `TrustNewHostKey` صراحةً لكل اتصال.
- البصمة تُربط بخوارزمية المفتاح: `"{HostKeyName}:{FingerPrintSHA256}"` لمنع هجوم تخفيض نوع المفتاح.
- تغيير المفتاح يُرفض دائمًا مع رسالة واضحة تتضمن البصمة المتوقعة والمستلمة.
- فشل قراءة الملف أو غياب البصمة يُعدّ فشلًا مغلقًا (fail closed).

---

### 4.7 العناصر منخفضة الخطورة

| المشكلة | المعالجة |
|---|---|
| الرابط المستورد يُبقي كلمة المرور ظاهرة | `RedactConnectionUrl()` يحوّلها إلى `***` فور الاستيراد، وتظهر رسالة «password hidden» |
| سجل الأعطال | تعقيم أسماء الحقول الحساسة وروابط `user:pass@host`، وتقييد الطول، وACL للمستخدم الحالي، وتدوير عند 256 KB إلى `crash.log.old` مع تقييد صلاحيات الأرشيف أيضًا |
| `web/.env.local` | حُذف؛ لا توجد رموز OIDC في الملفات أو في Git |
| فشل فك DPAPI | علم `LastDecryptFailed`، منع `SaveAll` من الكتابة، رسالة واضحة في الواجهة، تغليف أخطاء `Protect`، وحذف `connections.json.bak` القديم |
| حذف الاتصال | `try/catch` + رسالة خطأ + انتظار `DisconnectAsync(force: true)` بدل fire-and-forget |

---

## 5. اختبارات الاتصال بقواعد البيانات

تم تشغيل برنامج اختبار مستقل يشير إلى `SwiftBase.Core` الفعلي، على نسخة من قاعدة بيانات SQLite موجودة محليًا وعلى عنقود PostgreSQL مؤقت معزول على المنفذ `55432` بشهادة موقّعة ذاتيًا و`subjectAltName=DNS:localhost,IP:127.0.0.1`. استخدم الاختبار `VerifyFull` مع `RootCertificatePath`، وليس تعطيل التحقق.

### 5.1 SQLite

```text
Open: OK (SQLite)
Test: True · Connected
Databases: main
DDL: OK
Schema: 4 columns in main.swiftbase_probe
Insert: True · Saved 1 change
ReadPage: 1 row(s), total 1
BLOB round-trip: True (0001FEFF41)
Update: True · Saved 1 change
BLOB update round-trip: True
Delete: True · Saved 1 change
```

مثال السكربت المولّد:

```sql
INSERT INTO "main"."swiftbase_probe" ("name", "payload", "created_at")
VALUES ('typed ''blob'' test', X'0001FEFF41', '2026-11-23T12:34:56.0000000Z');
```

### 5.2 PostgreSQL

```text
Open: OK (PostgreSQL)
Test: True · Connected
Databases: postgres, swiftbase_test
DDL: OK
Schema: 4 columns in public.swiftbase_probe
Insert: True · Saved 1 change
ReadPage: 1 row(s), total 1
BLOB round-trip: True (0001FEFF41)
Update: True · Saved 1 change
BLOB update round-trip: True
Delete: True · Saved 1 change
```

مثال السكربت المولّد:

```sql
INSERT INTO "public"."swiftbase_probe" ("name", "payload", "created_at")
VALUES ('typed ''blob'' test', '\x0001FEFF41'::bytea, '2026-11-23T12:34:56.0000000Z');
```

تم إيقاف عنقود PostgreSQL المؤقت بعد الاختبار، و**لم يتم تعديل أو إيقاف** خدمة PostgreSQL المثبتة `postgresql-x64-14`.

---

## 6. التحقق من البناء والتشغيل

| الفحص | النتيجة |
|---|---|
| `dotnet build SwiftBase.slnx -c Debug` | نجح — 0 تحذير، 0 خطأ |
| `dotnet build SwiftBase.slnx -c Release` | نجح — 0 تحذير، 0 خطأ |
| تشغيل التطبيق فعليًا | `Responding=True`، لا نوافذ أعطال |
| سجل الأعطال قبل/بعد التشغيل | 18 / 18 — **لا إدخالات جديدة** |
| إصدار التطبيق المنشور | `ProductVersion: 1.1.0`، `FileVersion: 1.1.0.0` |
| إصدار المُثبّت | `ProductVersion: 1.1.0`، `FileVersion: 1.1.0.0` |
| أمر البناء النهائي | `powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -Publish -Installer -Configuration Release` |

### المُثبّت النهائي

| الخاصية | القيمة |
|---|---|
| المسار | `artifacts\installer\SwiftBase-Setup.exe` |
| نسخة الموقع | `web\public\downloads\SwiftBase-Setup.exe` |
| الحجم | **70,264,063 بايت** (نحو 67 MB) |
| SHA256 | **98161D5301038A6F5E15AD73569453C5873A9CE194D8ADC278724CDA19D40658** |

تم التحقق من تطابق تجزئة النسختين في `artifacts` و`web/public`، ثم نسخ الملف إلى `web/dist/downloads` عبر `bun run --cwd web build`.

---

## 7. فحوصات إضافية سلبية

- لم يتم العثور على SQL injection في مسارات التوليد الأساسية؛ الاستعلامات العادية تستخدم معاملات ومعرّفات مقتبسة.
- لم يتم العثور على XSS في الموقع؛ لا يوجد `innerHTML` أو `eval` أو `document.write`.
- لم يتم العثور على بيانات اعتماد حقيقية مضمنة في الشيفرة المتتبّعة.
- لا توجد ملفات `.env` أو رموز Vercel متبقية في المشروع.

---

## 8. النشر والتسليم

- **GitHub:** دُفع الإصدار إلى `main` في المستودع `i6gu1/SwiftBase`.
- **الموقع:** أُعيد النشر من مجلد `web` عبر Vercel CLI باستخدام `vercel --prod --yes` على المشروع `i6gu1s-projects/swiftbase-download`.
- **رابط الموقع:** `https://swiftbase-download.vercel.app`
- **رابط التحميل:** `https://swiftbase-download.vercel.app/downloads/SwiftBase-Setup.exe`
- **الهوية البصرية:** لم يتغير التصميم؛ لوحة داكنة، بكسل آرت، لمسات برتقالية، التذييل `made by the l house 2026`، ورابط Instagram `@emxomol`.

---

## 9. الخلاصة

SwiftBase v1.1.0 الآن:

- بلا أي بيانات تجريبية أو اتصالات افتراضية.
- بلا واحدة من ثغرات التدقيق الحادية عشرة.
- يعمل مع SQLite وPostgreSQL باتصال مشفّر ومُصدَّق، وبحفظ دقيق للقيم المكتوبة.
- مُوقَّع بإصدار واضح وتجزئة SHA256 قابلة للتحقق.
- مُحدَّث على GitHub وعلى موقع التحميل.

**انتهى التقرير.**
