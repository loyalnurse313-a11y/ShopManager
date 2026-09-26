# گزارش ممیزی پیش از بازآرایی — معماری و کسب‌وکار

**محل کار (Workspace):** `G:\ShopManager\ShopManager`
**راه‌حل:** `ShopManager.slnx` (۳ پروژه)
**روش:** بازرسی استاتیک و فقط‌خواندنی (هیچ فایلی تغییر نکرده است)

---

## خلاصه اجرایی (Executive Summary)

**سلامت کلی:** 🟠 High Risk (پرخطر)

اپلیکیشن POS/مدیریت فروشگاهی روی Avalonia 12.1.2، .NET 10، EF Core 10 و SQLite (حالت WAL) ساخته شده است. هسته‌ی دامنه (`ShopManager.Domain`) تمیز و مستقل است — این بزرگ‌ترین دارایی معماری است. اما عملیات مالی و موجودی **بدون هیچ تراکنش دیتابیسی** (`BeginTransaction`) انجام می‌شوند و منطق تجاری تقریباً به‌طور کامل داخل کد-پشت‌صحنه‌ی Viewها قرار دارد؛ در نتیجه شکستِ میانه‌ی یک عملیات می‌تواند وضعیت مالی/موجودی ناسازگار به‌جا بگذارد. امنیت نیز با اعتبارنامه‌ی پیش‌فرض هاردکدشده (`admin`/`[REDACTED]`) که در لاگ دیباگ هم چاپ می‌شود، مخدوش است. هیچ تستی وجود ندارد.

### ۳ ریسک بحرانی اصلی

1. **نبود تراکنش اتمیک در فروش/خرید/انتقال** — مثلاً دو `SaveChanges` در یک فروش (`SaleWindow.axaml.cs:495` و `:541`)؛ شکست بین آن‌ها ثبت ناقص مالی/موجودی می‌سازد.
2. **اعتبارنامه‌ی پیش‌فرض هاردکدشده** — `admin`/`[REDACTED]` در `AuthServiceInitializer.cs:10-11` و چاپ آن در لاگ (`App.axaml.cs:52-53`).
3. **منطق مالی/تجاری داخل کد-پشت‌صحنه‌ی Viewها** — `POSWindow.axaml.cs` (۱۶۵۷ خط) و `DashboardWindow.axaml.cs` (۱۷۴۵ خط) بدون لایه‌ی سرویس و بدون تست.

### اولین اقدام مهندسی پیشنهادی

افزودن تراکنش اتمیک (Unit of Work) حول تسویه‌ی فروش/خرید/انتقال و حذف اعتبارنامه‌ی پیش‌فرض به‌همراه اجبار تغییر رمز.

### برآورد تلاش اصلاحی

تقریباً **۳ تا ۵ هفته-نفر** برای رفع یافته‌های مهم (تراکنش‌ها، امنیت، استخراج سرویس‌ها، رفع N+1، افزودن تست‌های پایه).

**"This is a rough static-inspection estimate, not a guaranteed schedule."**

---

## الف) معماری (Architecture)

**لایه‌های واقعی:** `Desktop → Infrastructure → Domain`. جهت وابستگی درست است و Domain مستقل است.

| یافته | شدت | شواهد |
| --- | --- | --- |
| Domain خالص و مستقل (بدون ارجاع به EF/Infrastructure) | 🟢 نقطه‌قوت | `ShopManager.Domain.csproj` بدون PackageReference/ProjectReference |
| ارجاع مستقیم و تکراری Domain از Desktop (هم مستقیم و هم تراکنشی از طریق Infrastructure) | 🟢 | `ShopManager.Desktop.csproj` |
| **تمام منطق تجاری/مالی داخل Viewهاست؛ Infrastructure فقط DbContext و خروجی Excel دارد؛ هیچ لایه‌ی سرویس/مخزن (Repository/UoW) وجود ندارد** | 🟠 | `ShopManager.Infrastructure\servises\ExcelExportService.cs` تنها سرویس؛ `POSWindow`/`DashboardWindow`/`SaleWindow`/`CashboxWindow` محاسبات مالی را مستقیم انجام می‌دهند |
| الگوی سرویس‌های استاتیک؛ DI/Hosting ارجاع داده شده ولی عملاً استفاده نشده | 🟡 | `DatabaseService`/`AuthService`/`BackupService` همه `static`؛ `Microsoft.Extensions.Hosting` بدون Composition |
| **هیچ `BeginTransaction` در کل کدبیس وجود ندارد** | 🔴 | grep سراسری = صفر نتیجه |

---

## ب) ساختار پروژه/راه‌حل (Structure)

| یافته | شدت | شواهد |
| --- | --- | --- |
| فایل‌های خیلی بزرگ (God Class) در Viewها | 🟠 | `DashboardWindow.axaml.cs` (۱۷۴۵ خط)، `POSWindow.axaml.cs` (۱۶۵۷ خط)، `SaleWindow.axaml.cs` (۵۸۱ خط)، `CashboxWindow.axaml.cs` (۴۶۷ خط) |
| سرویس‌های چندمسئولیتی (God Object) | 🟠 | `DatabaseService` (۵ نگرانی: مسیر، DbContext، مایگریشن، WAL، لاگ)، `AuthService` (احراز هویت + audit + مجوز + حالت جلسه)، `BackupService` (ماندگاری + زمان‌بندی + Process.Start) |
| نام‌گذاری نادرست فایل | 🟢 | `ShopManager.Domain\Enums\Class1.cs` در واقع `enum PaymentStatus` است |
| غلط املایی پوشه‌ها | 🟢 | `Infrastructure\servises` (باید Services)، `Desktop\model` (کوچک) در برابر `Desktop\Models` (خالی) |
| هیچ تستی وجود ندارد | 🟠 | هیچ فایل/پروژه `*Test*` و هیچ بسته‌ی xunit/nunit/MSTest یافت نشد |

---

## ج) داده/دیتابیس (Data/Database)

`AppDbContext` با ۹ `DbSet`. پیکربندی Fluent:

| یافته | شدت | شواهد |
| --- | --- | --- |
| ایندکس‌های یکتا صحیح | 🟢 | `Item.ItemCode`، `Customer.Phone`، `User.Username` |
| **`Sale.InvoiceNumber` ایندکس غیریکتا دارد → جلوگیری از شماره‌فاکتور تکراری در سطح DB وجود ندارد** | 🔴 | `AppDbContext.cs` — `HasIndex(e => e.InvoiceNumber)` بدون `IsUnique()` |
| Delete Behavior محافظت‌کننده | 🟢 | `Restrict` روی `Item → Purchases/Transfers/Sales`؛ `SetNull` روی `Sale → Customer` |
| FKهای خودارجاع `ReversalOf` بدون `OnDelete` صریح | 🟡 | موجودیت‌های `Purchase`/`Sale`/`Transfer` |
| ناسازگاری دقت decimal | 🟢 | `Item.MarkupPct` با `HasPrecision(5,4)` ولی مقدار پیش‌فرض `30m` |
| **چند `SaveChanges` در یک عملیات تجاری بدون تراکنش** | 🔴 | `SaleWindow.axaml.cs:495` و `:541` |
| **موجودی منفی در Domain/DB اعمال نمی‌شود** (موجودی مشتق‌شده است) | 🟠 | `StockCalculator` فقط محاسبه می‌کند؛ هیچ قید DB/کد برای جلوگیری از فروش بیش از موجودی نیست |
| WAL فقط در لایه Desktop تنظیم می‌شود، نه Infrastructure | 🟡 | `DatabaseService.cs:80-85` (`PRAGMA journal_mode=WAL; synchronous=NORMAL;`) |
| **رانش اسکیما با ADO.NET خام در کنار EF Migrations (دو منبع حقیقت)** | 🟠 | `DatabaseService.cs:119-123` (`ALTER TABLE ... ADD COLUMN`) |
| مسیر متفاوت دیتابیس بین Runtime و Design-time | 🟡 | Runtime: `G:\ShopManager-Data\shop.db`؛ `AppDbContextFactory.cs:17`: `AppContext.BaseDirectory\shop.db` |
| وابستگی به درایو `G:` هاردکدشده | 🟡 | `DatabaseService.cs` (`PrimaryDataPath = @"G:\ShopManager-Data"`) |
| `Setting` تک‌رکوردی (Id=1) بدون قید DB | 🟢 | `Setting.cs` — فقط کامنت، نه Constraint |
| `synchronous=NORMAL` دوام را با سرعت معامله می‌کند | 🟡 | `DatabaseService.cs:80-85` |

**نکته:** ۴ مایگریشن (`InitialCreate`، `AddMarkupPctToItem`، `AddCustomerAndInvoice`، `AddUsersAndLoginHistory`) همگی افزایشی (additive) هستند و گام تخریبی/داده‌ای ندارند.

---

## د) وابستگی‌ها (Dependencies)

| یافته | شدت | شواهد |
| --- | --- | --- |
| بسته‌های تکراری EF | 🟡 | `Microsoft.EntityFrameworkCore.Sqlite` و `Tools` در هر دو پروژه Desktop و Infrastructure (نسخه 10.0.12) |
| بسته‌ی بلااستفاده | 🟡 | `DeepSeek.NET 1.2.0` — `AiAssistantService.cs` مستقیماً با `HttpClient` خام به `api.deepseek.com` می‌زند و از کتابخانه استفاده نمی‌کند |
| ناسازگاری نسخه | 🟡 | `AvaloniaUI.DiagnosticsSupport 2.2.3` در برابر Avalonia 12.1.2 |
| ابزار CI بدون پین نسخه | 🟡 | `release.yml` → `dotnet tool install -g vpk` بدون نسخه |
| فقدان بسته‌های تست | 🟠 | هیچ بسته‌ی تستی در هیچ csproj نیست |

---

## هـ) مشکلات بحرانی (Critical Problems)

1. 🔴 **ثبت ناقص مالی/موجودی در اثر نبود تراکنش** — `SaleWindow.axaml.cs:495` و `:541` دو `SaveChanges` جداگانه؛ بدون `BeginTransaction` شکست میانه، فاکتور نیمه‌نوشته و موجودی/مالی ناسازگار باقی می‌گذارد.
2. 🔴 **اعتبارنامه‌ی پیش‌فرض هاردکدشده و لاگ‌شده** — `AuthServiceInitializer.cs:10-11` (`admin`/`[REDACTED]`)، بازبررسی همان literal در `LoginWindow.axaml.cs:176-177`، و چاپ در `App.axaml.cs:52-53`.
3. 🟠 **شماره‌فاکتور تکراری ممکن است** — ایندکس `InvoiceNumber` یکتا نیست و تولید شماره در سمت کلاینت با خواندن «آخرین» انجام می‌شود (مسابقه/race).
4. 🟠 **فروش بیش از موجودی جلوگیری نمی‌شود** — موجودی مشتق‌شده است و هیچ قیدی در Domain/DB وجود ندارد.
5. 🟠 **دو منبع حقیقت برای اسکیما** — `ALTER TABLE` خام در `DatabaseService.cs:119-123` در کنار EF Migrations.
6. 🟠 **منطق مالی در Viewهای بسیار بزرگ** — `POSWindow.axaml.cs` (۱۶۵۷ خط) و `DashboardWindow.axaml.cs` (۱۷۴۵ خط) بدون تست.

---

## و) بدهی فنی (Technical Debt)

- **منطق تجاری تکراری:** تولید شماره‌فاکتور هم در `SaleWindow.axaml.cs:36-71` و هم در `POSWindow.axaml.cs:1615-1647` پیاده شده است.
- **منطق تجاری داخل UI:** تسویه‌ی فروش، محاسبه‌ی سود/هزینه/درآمد، تخصیص تخفیف، تجمیع صندوق و داشبورد همگی در کد-پشت‌صحنه است.
- **سرویس‌های استاتیک جفت‌شده:** `DatabaseService`/`AuthService`/`BackupService` قابل تزریق/تست نیستند.
- **اعتبارسنجی ناکافی:** ورودی‌ها با `try/catch` خاموش یا بدون اعتبارسنجی مرکزی پردازش می‌شوند.
- **کد مرده (با اطمینان):** `AiAssistantService` و فیلد `DeepSeekApiKey` بلااستفاده‌اند.
- **الگوهای شکننده:** ساخت connection string با الحاق رشته (`DatabaseService.cs:75`).

تمایز: موارد فوق عمدتاً **بدهی فنی ایجادکننده‌ی ریسک عملیاتی/تجاری** هستند، نه صرفاً ناراحتی نگهداری.

---

## ز) امنیت (Security)

| یافته | شدت | شواهد |
| --- | --- | --- |
| اعتبارنامه‌ی پیش‌فرض هاردکدشده | 🔴 | `AuthServiceInitializer.cs:10-11`؛ `LoginWindow.axaml.cs:176-177` |
| چاپ رمز پیش‌فرض در لاگ | 🟠 | `App.axaml.cs:52-53` |
| کلید API دیپ‌سیک به‌صورت متن ساده | 🟡 | `AppPreferences.cs` (`DeepSeekApiKey`) → `preferences.json` (کد مرده) |
| قفل brute-force فقط در UI و process-local | 🟡 | `LoginWindow.axaml.cs` (`_failedAttempts`/`_isLocked`) — با ری‌استارت برنامه دور زده می‌شود |
| مکانیزم آپدیت بدون پین/امضای صریح و بدون rollback خودکار | 🟡 | `UpdateService.cs:133-140` (`GithubSource(RepoUrl, accessToken: null, ...)`) |
| رمزنگاری قوی رمز عبور | 🟢 نقطه‌قوت | `PasswordHasher.cs` (PBKDF2-SHA256، ۱۰۰هزار تکرار، salt تصادفی ۱۶ بایتی، `FixedTimeEquals`) |
| جلوگیری از XSS در فاکتور HTML | 🟢 نقطه‌قوت | `HtmlEncoder.Encode` در `SaleInvoiceHtmlBuilder.cs` |
| SQL Injection | 🟢 ایمن | EF با کوئری پارامتری؛ `ALTER TABLE` فقط با شناسه‌های هاردکد |
| هش ضعیف قدیمی SHA256 فقط در ورود lazy ارتقا می‌یابد | 🟡 | `AuthService.cs:62-77` |

---

## ح) کارایی (Performance)

| یافته | شدت | شواهد/مکانیزم |
| --- | --- | --- |
| I/O همزمان روی UI Thread | 🟠 | همه Viewها `SaveChanges`/`ToList` همزمان؛ `PreferencesService.Save`/`StoreSettingsService.Save`/`ErrorHandler.LogError` با `File.WriteAllText`/`AppendAllText` |
| کوئری N+1 | 🟠 | `PurchaseWindow.axaml.cs:222-233` (یک زیرکوئری `Items.Where(...)` به‌ازای هر ردیف)؛ `TransferWindow.axaml.cs:262-274` |
| DbContext جدید به‌ازای هر tile/result | 🟠 | `POSWindow.axaml.cs:551-558` (هر tile) و `:406-409` (هر نتیجه جستجو) |
| بارگذاری مکرر جدول‌های کامل | 🟠 | `db.Sales.ToList()`/`db.Purchases.ToList()`/`db.Transfers.ToList()` در اکثر Viewها و چند بار در `POSWindow` |
| `CreateContext` هر بار `EnsureCreated` + PRAGMA اجرا می‌کند | 🟡 | `DatabaseService.cs:59-93` |
| نشت رویداد/تایمر | 🟡 | `MainWindow.axaml.cs:80` (اشتراک `StateChanged` بدون لغو اشتراک)، `:73-78` (تایمر بدون توقف)، `WindowHelper.OpenMaximized` (اشتراک lambda در هر فراخوانی) |

---

## ط) فرصت‌های بازآرایی (Refactoring Opportunities)

- 🟢 **کم‌ریسک:** تغییر نام `Class1.cs` به `PaymentStatus.cs`؛ اصلاح پوشه‌ی `servises`؛ حذف ارجاع مستقیم تکراری Domain از Desktop؛ حذف پوشه‌ی خالی `Models`.
- 🟡 **ریسک متوسط:** استخراج سرویس‌های `SaleService`/`PurchaseService`/`TransferService`/`CashboxService`/`DashboardService`/`InventoryService`؛ تقسیم `DatabaseService` به `DbContextFactory`/`SchemaMigrator`/`AppPaths`؛ متمرکزسازی تولید شماره‌فاکتور؛ حذف کد مرده.
- 🔴 **پرخطر:** تراکنشی‌کردن فروش/خرید/انتقال؛ انتقال منطق تجاری از Viewها؛ متمرکزسازی احراز هویت/قفل در `AuthService`؛ اصلاح رانش اسکیما.

برای تغییرات پرخطر، اجزای متأثر: `SaleWindow`/`POSWindow`/`PurchaseWindow`/`TransferWindow`/`CashboxWindow`/`DashboardWindow` + `DatabaseService` + `AppDbContext`. پیش از تغییر، تست‌های رگرسیون مالی/موجودی و backup لازم است.

---

## ی) نقشه راه پیشنهادی (Recommended Roadmap)

| فاز | هدف | اجزای متأثر | دلیل ترتیب | تلاش تقریبی | ریسک |
| --- | --- | --- | --- | --- | --- |
| ۱ | تراکنش اتمیک مالی/موجودی | `SaleWindow`/`POSWindow`/`PurchaseWindow`/`TransferWindow` | درستی داده/مالی | ~۱ هفته | 🔴 |
| ۲ | یکتایی شماره‌فاکتور + جلوگیری از فروش بیش از موجودی | `AppDbContext`/`SaleWindow`/`POSWindow` | صحت/ریسک خرابی | ~۳ روز | 🟠 |
| ۳ | حذف اعتبارنامه پیش‌فرض و لاگ آن | `AuthServiceInitializer`/`App.axaml.cs`/`LoginWindow` | امنیت | ~۱ روز | 🟢 |
| ۴ | استخراج سرویس‌های کاربردی از Viewها | Viewهای فروش/خرید/صندوق/داشبورد | مسدودکننده معماری | ~۱–۲ هفته | 🔴 |
| ۵ | تست‌پذیری (تست‌های واحد سرویس‌ها + Domain) | `ShopManager.Domain.Services` + سرویس‌های جدید | پایداری | ~۱ هفته | 🟡 |
| ۶ | رفع N+1 و I/O غیرهمزمان | `PurchaseWindow`/`TransferWindow`/`POSWindow` | کارایی | ~۳–۵ روز | 🟡 |
| ۷ | نگهداری (نام‌گذاری، کد مرده، پوشه‌ها) | سراسری | نگهداری | ~۲ روز | 🟢 |
| ۸ | موارد ظاهری | — | آخر | — | 🟢 |

برآوردها بر پایه‌ی بازرسی استاتیک است.

---

## ک) نقاط قوت (Strengths)

- **هسته‌ی Domain تمیز:** `ShopManager.Domain` بدون هیچ بسته/ارجاع خارجی؛ موجودیت‌ها + ماشین‌حساب‌های خالص (`StockCalculator`، `PricingCalculator`، `LockedCostCalculator`، `CashboxCalculator`، `StockAlertCalculator`).
- **هش امن رمز عبور:** PBKDF2-SHA256 با salt و `FixedTimeEquals` (`PasswordHasher.cs`).
- **الگوی برگشت (Reversal) غیرمخرّب:** اصلاحات به‌صورت ردیف جدید `EntryType.Reversal` ثبت می‌شوند، نه ویرایش درجا.
- **حذف محافظت‌شده:** `Restrict` روی حذف `Item` از تاریخچه‌ی مالی/موجودی محافظت می‌کند؛ soft-delete (`IsActive`).
- **قیدهای یکتا** روی `ItemCode`/`Phone`/`Username`.
- **XSS:** کدگذاری HTML در فاکتور.
- **آپدیت با backup:** `UpdateService.ApplyUpdatesAndRestart` پیش از اعمال، backup اجباری می‌گیرد.
- **ردپای ورود/خروج:** `LoginHistory` برای audit احراز هویت.

---

## ناورداهای حیاتی کسب‌وکار (Business Invariants)

| ناوردا | وضعیت |
| --- | --- |
| ۱. موجودی هرگز منفی نشود (مگر با قاعده صریح) | **اجرا نمی‌شود** — موجودی مشتق‌شده؛ هیچ قید Domain/DB |
| ۲. جمع پرداخت برابر مبلغ فاکتور باشد | **اجرا نمی‌شود** — وضعیت‌های Cash/Credit/Card و تخصیص تخفیف وجود دارد اما هیچ بررسی برابری کل یافت نشد؛ "Cannot be determined from static inspection." |
| ۳. فروش بیش از یک‌بار ثبت نشود | **اجرا نمی‌شود** — قید یکتا وجود ندارد؛ تولید شماره سمت کلاینت با race |
| ۴. استراتژی backup/recovery پیش از مایگریشن‌های مخرب | **تا حدی** — مایگریشن‌ها فقط additive هستند؛ backup فقط پیش از آپدیت Velopack |
| ۵. ردپای تغییرات مالی | **اجرا نمی‌شود** — فقط `LoginHistory`؛ هیچ audit تغییر داده برای موجودیت‌های تجاری |
| ۶. تراکنش مالی ناموفق حالت ناسازگار نگذارد | **اجرا نمی‌شود** — هیچ `BeginTransaction` وجود ندارد |
| ۷. یکتایی شناسه‌ی فاکتور | **اجرا نمی‌شود** — ایندکس `InvoiceNumber` غیریکتا |
| ۸. حرکت موجودی با تراکنش متناظر باشد | **تا حدی** — موجودی از movementها مشتق می‌شود، اما صحت آن اعتبارسنجی نمی‌شود |

**ناوردای اضافی کشف‌شده:** صحت زنجیره‌ی برگشت (`ReversalOfId`) — **تا حدی اجرا می‌شود** (مکانیزم وجود دارد، ولی اعتبارسنجی صریح نیست).

---

## جدول یافته‌های اولویت‌بندی‌شده (Prioritized Findings)

| مشکل | شدت | ریسک | تلاش تقریبی | اولویت |
| --- | --- | --- | --- | --- |
| نبود تراکنش در فروش/خرید/انتقال | 🔴 | بالا | ~۱ هفته | ۱ |
| اعتبارنامه پیش‌فرض هاردکد + لاگ | 🔴 | بالا | ~۱ روز | ۲ |
| شماره‌فاکتور تکراری ممکن | 🟠 | بالا | ~۲ روز | ۳ |
| فروش بیش از موجودی بدون قید | 🟠 | بالا | ~۲ روز | ۴ |
| دو منبع حقیقت اسکیما (ALTER TABLE + Migrations) | 🟠 | متوسط | ~۳ روز | ۵ |
| منطق مالی در Viewهای بزرگ بدون تست | 🟠 | بالا | ~۱–۲ هفته | ۶ |
| نبود تست | 🟠 | متوسط | ~۱ هفته | ۷ |
| N+1 / I/O همزمان UI | 🟠 | متوسط | ~۳–۵ روز | ۸ |
| قفل brute-force فقط در UI | 🟡 | متوسط | ~۱ روز | ۹ |
| بسته‌های تکراری/بلااستفاده/نامنطبق | 🟡 | پایین | ~نیم روز | ۱۰ |

---

## فرضیات و محدودیت‌ها (Assumptions & Limitations)

- **Assumption:** شماره‌فاکتور باید یکتا باشد (بر پایه‌ی الگوی `INV-1405-0001` و ایندکس آن)؛ الزام صریح تجاری تأیید نشده است.
- **Cannot determine from static inspection:** رفتار دقیق هم‌زمانی/race در SQLite WAL هنگام دو نوشتن هم‌زمان، و اینکه آیا هر checkout واقعاً اتمیک است (هیچ API تراکنشی استفاده نشده، پس نیست).
- **Requires runtime testing:** مسیر fallback دیتابیس بدون درایو `G:`، رفتار `synchronous=NORMAL` در قطع برق، و مکانیزم عملی Velopack.
- **Requires business confirmation:** الزام یکتایی فاکتور، قواعد مجاز موجودی منفی/نسیه، و نیاز به audit تغییر داده‌های مالی.
- **Requires production data:** اندازه‌ی واقعی جداول و شدت تأثیر N+1/بارگذاری کامل جدول‌ها.

---

## سوالات باز پیش از بازآرایی (Open Questions)

1. آیا یکتایی شماره‌فاکتور الزام سخت‌گیرانه است؟ (قید DB لازم؟)
2. آیا موجودی منفی تحت شرایطی مجاز است (مثلاً فروش نسیه/پیش‌فروش)؟
3. آیا ثبت‌های مالی نیازمند audit تغییر (چه کسی/چه زمانی/چه چیزی) هستند؟
4. سیاست backup پیش از مایگریشن‌های آینده چیست؟
5. آیا آپدیت باید امضای بسته را تأیید کند یا به Velopack اعتماد کافی است؟
6. آیا `DeepSeekApiKey`/`AiAssistantService` حذف شوند یا به ذخیره‌سازی محافظت‌شده‌ی OS منتقل شوند؟
7. حد پذیرش خطای هم‌زمانی/قطع برق برای تراکنش‌های مالی چقدر است؟

---

**Audit complete. No code was modified.**
