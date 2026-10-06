# AI-HANDOFF — سند انتقال پروژه ShopManager

> **هدف:** این فایل مرجع کامل برای هر AI است که وارد پروژه می‌شود. قبل از هر اقدامی، این سند را کامل بخوان.
>
> **آخرین به‌روزرسانی:** Phase 4 remains **IN PROGRESS / NOT restore-safe**. Approved critical path: **F1 → F2 → F3** (F1 IMPLEMENTED / VERIFIED / COMMITTED `b38d9b5c5716a01c5e688c5d00496b5a36033d57`; F2/F3 OPEN). Latest hardening: **5C-2.2 — IMPLEMENTED / TESTED / INDEPENDENTLY REVIEWED / COMMITTED / PUSHED `72d77613340f2d9b88abd64ec39bcbe351156608`** (push confirmed by the user). 5C-2.1/2.2 and their historical evidence remain valid; general per-window enrollment is deferred. checkpointهای پیشین: `583a7b8` (4B-3)، `d472153` (4B-2B)، `59d0dfc` (4B-2A).
> **وضعیت فعلی:** Phase 1–3 کامل‌اند. Phase 4 remains **IN PROGRESS / NOT restore-safe**. Approved critical path: **F1 → F2 → F3** (F1 IMPLEMENTED / VERIFIED / COMMITTED `b38d9b5c5716a01c5e688c5d00496b5a36033d57`; F2/F3 OPEN). Latest hardening: **5C-2.2 — IMPLEMENTED / TESTED / INDEPENDENTLY REVIEWED / COMMITTED / PUSHED `72d77613340f2d9b88abd64ec39bcbe351156608`** (push confirmed by the user). 5C-2.1/2.2 and their historical evidence remain valid; general per-window enrollment is deferred.
> **مرجع قواعد اجرایی:** ابتدا [AGENTS.md](../AGENTS.md). بخش‌های تاریخی این سند دستور اجرای کار یا مجوز دست‌کاری داده نیستند.

---

## ۱. شناسنامه‌ی پروژه

| مورد                                 | مقدار                                                                                                                                                                                                                                       |
| ------------------------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| نام                                  | ShopManager — اپ POS/مدیریت فروشگاهی                                                                                                                                                                                                        |
| مسیر checkout بررسی‌شده              | `E:\Projects\ShopManager`؛ مسیر قدیمی handoff: `G:\ShopManager\ShopManager`                                                                                                                                                                 |
| DB                                   | هویت مسیر از Phase 4A-2: یک‌بار در هر پروسه تعیین و در `%LocalAppData%\ShopManager\database-location.json` ثبت می‌شود؛ marker معتبر است و در ابهام/نبود مقصد، startup با `BlockedReason` متوقف می‌شود. مسیر runtime در این کار باز نشده است |
| Backups                              | `DatabaseService.BackupFolder` کنار دیتابیس انتخاب‌شده؛ وضعیت backup عملیاتی در این کار بررسی نشده است                                                                                                                                      |
| Stack                                | Avalonia 12.1.2 / .NET 10 / EF Core + Microsoft.Data.Sqlite 10.0.12                                                                                                                                                                         |
| ساختار                               | 3 پروژهٔ production: `Domain` / `Infrastructure` / `Desktop`؛ به‌علاوه `ShopManager.Domain.Tests`                                                                                                                                           |
| IDE                                  | VS Code + PowerShell                                                                                                                                                                                                                        |
| گزارش ممیزی تاریخی                   | [AUDIT-REPORT.md](../AUDIT-REPORT.md)؛ برای وضعیت فعلی، [AUDIT-RECONCILIATION.md](AUDIT-RECONCILIATION.md) و [MASTER-BACKLOG.md](MASTER-BACKLOG.md)                                                                                         |
| آخرین verification، Phase 3C         | build: 0 Warning / 0 Error؛ tests: 134/134 passed، 0 failed / 0 skipped؛ `git diff --check`: clean؛ final adversarial review: PASS                                                                                                          |
| verification تاریخی کد production، commit `59d0dfc` | build: 0 Warning / 0 Error؛ tests: 246/246 passed، 0 failed / 0 skipped |
| historical verification checkpoint `4B-2B`، commit `d472153` (270 تست در آن source tree commit‌شده) | build غیرافزایشی solution: 0 Warning / 0 Error؛ `RestoreRecoveryServiceTests`: 42 passed؛ کل `ShopManager.Domain.Tests`: 270 passed؛ adversarial/final review: PASS بدون issue مسدودکنندهٔ Critical/High/Medium |
| historical verification checkpoint `4B-3`، پس از test hardening و پیش از commit `583a7b8` | targeted `ApplicationInstanceGuardTests`: 21 passed؛ full `ShopManager.Domain.Tests`: 291 passed؛ هر دو 0 failed / 0 skipped؛ build غیرافزایشی 0W / 0E؛ diff check exit code 0؛ review طبق تأیید کاربر: PASS WITH FINDINGS، بدون Critical یا High؛ follow-up تست بدون تغییر production |
| verification checkpoint `4B-4`، commit `63039d2` (شواهد تاریخی) | `StartupRecoveryIntegrationTests`: 29 passed؛ `RestoreRecoveryServiceTests`: 44 passed؛ `ApplicationInstanceGuardTests`: 21 passed؛ full `ShopManager.Domain.Tests`: 322 passed؛ همگی 0 failed / 0 skipped؛ build غیرافزایشی 0W / 0E؛ diff check exit code 0؛ independent adversarial review طبق تأیید کاربر: PASS WITH FINDINGS، بدون Critical یا High و بدون checkpoint-blocking finding |
| verification checkpoint `4B-5A`، commit `36f0e7f` | targeted 32/32؛ relevant 130/130؛ full 354/354 passed؛ build 0W / 0E؛ diff-check exit 0؛ independent review PASS WITH FINDINGS؛ هر دو blocking finding پیشین RESOLVED |
| verification checkpoint `4B-5B-1`، COMMITTED / PUSHED `34faeee` | targeted 36/36؛ relevant 166/166؛ full 358/358 passed؛ non-incremental build 0W / 0E؛ diff-check exit 0؛ independent review طبق تأیید کاربر: PASS WITH FINDINGS، بدون Critical/High/Medium؛ NOT restore-safe |
| verification checkpoint `4B-5B-2`, COMMITTED `287764c` | targeted 26/26; relevant 172/172; full 384/384; 0 failed / 0 skipped; non-incremental build 0W / 0E; diff-check PASS; independent re-review supplied by the user: PASS WITH FINDINGS; M1/M2 CLOSED; no Critical/High/Medium; NOT restore-safe |
| Historical verification 5C-2.1, COMMITTED / PUSHED `a8e3d02` | targeted 52/52; relevant 200/200; full 465/465; build 0W/0E; independent review recorded in the phase document |
| Historical verification 5C-2.2, COMMITTED / PUSHED `72d7761` | new 47/47; Auth 52/52; relevant 148/148; full 512/512; independent targeted 99/99; build 0W/0E; independent review PASS, no Critical/High/Medium/Low findings (user-confirmed); no rerun here |

این پاراگراف فقط به سطر «آخرین verification، Phase 3C» مربوط است: نتایج build/test/check آن سطر از اجرای ثبت‌شدهٔ Phase 3C هستند. PASS بازبینی adversarial نهایی طبق تأیید کاربر در درخواست Documentation Closure ثبت شده است؛ فایل مستقل آن در مخزن این بررسی یافت نشد. این کار فقط مستندسازی است و build/test را دوباره اجرا نمی‌کند. منشأ و نگاشت کامل شواهد در بخش Phase 3 سند [AUDIT-RECONCILIATION.md](AUDIT-RECONCILIATION.md) آمده است.

---

## ۲. معماری — ۳ لایه

```
Desktop (Viewها + سرویس‌های App)
   ↓
Infrastructure (AppDbContext + Migrations + ExcelExport)
   ↓
Domain (Entities + Enums + Helpers + Services خالص)
```

**قانون طلایی:** Domain هیچ‌وقت به EF یا Avalonia وابسته نمی‌شود. اگر شد، معماری شکسته.

---

## ۳. موجودیت‌های اصلی

| Entity          | نقش                     | نکات حیاتی                                                                         |
| --------------- | ----------------------- | ---------------------------------------------------------------------------------- |
| `Item`          | کالا                    | `ItemCode` یکتا، `SalePrice` اختیاری (Override)، `MarkupPct` پیش‌فرض ۳۰            |
| `Purchase`      | خرید از تأمین‌کننده     | `EntryType.Normal/Reversal`، `PaymentStatus`                                       |
| `Transfer`      | انتقال انبار→مغازه      | `EntryType` برای reversal                                                          |
| `Sale`          | فروش                    | `LockedUnitCost` snapshot است، هرگز تغییر نمی‌کند                                  |
| `SaleOperation` | هویت عملیات فروش        | OperationId کلید اصلی؛ InvoiceNumber یکتا در سطح عملیات؛ RequestFingerprint اجباری |
| `Customer`      | مشتری                   | `Phone` یکتا                                                                       |
| `User`          | کاربر                   | `Role` (Admin/User)، ۱۳ مجوز `Can*`                                                |
| `LoginHistory`  | Audit ورود/خروج         | `UserId` nullable                                                                  |
| `CashLedger`    | دفتر صندوق              | `AmountIn`/`AmountOut`                                                             |
| `Setting`       | تنظیمات (تک‌رکورد Id=1) | `InitialCapital`                                                                   |

---

## ۴. قواعد کسب‌وکار حیاتی (غیرقابل نقض)

1. **بهای تمام‌شده قفل‌شده**: `LockedCostCalculator` فقط خریدهای «تا تاریخ همان فروش» را در نظر می‌گیرد — نه بعدی‌ها.
2. **میانگین موزون خرید**: `CalculateCurrentAverageCost` (فقط برای نمایش).
3. **قیمت فروش**: اگر `Item.SalePrice` پر باشد → Override، وگرنه `آخرین قیمت خرید × (1 + Markup/100)`.
4. **موجودی انبار** = `OpeningWarehouseQty + ΣNormal(Purchases) − ΣNormal(Transfers)` (با reversal معکوس).
5. **موجودی مغازه** = `OpeningShopQty + ΣNormal(Transfers) − ΣNormal(Sales)`.
6. **صندوق** = `سرمایه + فروش نقدی − خرید نقدی + ورودی دفتر − خروجی دفتر`. کارتی وارد صندوق نمی‌شود.
7. **Reversal**: مدل و محاسبات آن موجودند؛ جریان ایجاد reversal هنوز Future Backlog است. طراحی مورد انتظار ردیف جدید با `EntryType=Reversal` و `ReversalOfId` است؛ قرارداد علامت Qty پیش از پیاده‌سازی باید تعیین شود.
8. **حد هشدار**: اگر `LowStockCriticalFixed` پر باشد اولویت دارد، وگرنه درصدی از `OpeningWarehouseQty + OpeningShopQty + همه خریدها`.
9. **موجودی**: `SalePersistenceService.Save` و `TransferPersistenceService.Save` کنترل authoritative را پس از آغاز تراکنش SQLite انجام می‌دهند. فروش، Qty ردیف‌های هم‌کالا را جمع می‌کند. رقابت دو برداشت از stock=1 فقط یک برنده دارد؛ این تضمین به مسیرهای تست‌شده محدود است.
10. **شماره فاکتور**: پیشنهاد معمول مانند `POS-1405-0001` از `SaleInvoiceNumberGenerator` و داده‌های Sales/SaleOperations می‌آید؛ proposal رزرو نیست. یکتایی DB روی `SaleOperations.InvoiceNumber` است، نه تک‌تک ردیف‌های Sales. برخورد قطعی در درخواست قابل‌آزادسازی، pending شکست‌خورده را آزاد می‌کند و با حفظ سبد، پیشنهاد تازه و در پرداخت بعدی OperationId تازه تولید می‌شود؛ نتیجهٔ نامعلوم قبلی همچنان مانع تغییر هویت pending است.
11. **Idempotency فروش**: همان OperationId + همان fingerprint → replay بدون اثر دوباره؛ همان OperationId + payload متفاوت → conflict. pending نامعلوم همان شناسه/snapshot را حفظ می‌کند؛ شکست قطعی قابل‌آزادسازی، اصلاح سبد را ممکن می‌کند.

---

## ۵. قواعد پروژه (فاز ۱ به بعد)

قواعد اجرایی جاری در [AGENTS.md](../AGENTS.md) است. راهنمای زیر باید در محدودهٔ task و مجوز کاربر تفسیر شود؛ هیچ دستور قدیمی backup/build مجوز تغییر داده یا اجرای تست در کار read-only نیست.

### 🔒 امنیت

**XSS**: `HtmlEncoder.Encode` همیشه **بیرونی‌ترین لایه** است.

```csharp
// ✅ درست
HtmlEncoder.Encode(PersianNumber.ToPersianDigits(input))

// ❌ غلط — entity خراب می‌شود
PersianNumber.ToPersianDigits(HtmlEncoder.Encode(input))
```

**MaxLength**: در XAML فقط UI را محدود می‌کند → **گارد سمت منطق اجباری است**:

```csharp
if (text.Length > N) { StatusText.Text = "..."; return; }
```

**رمز**: هیچ hardcode. `admin/admin` حذف شد. کاربر اول در `FirstRunSetupWindow` ساخته می‌شود.

### 🗄️ دیتابیس

- **`DeleteBehavior.Restrict`** روی FK‌های مالی تاریخی.
- **Soft Delete**: `Item.IsActive = false` (نه حذف فیزیکی).
- **Transaction**: هر عملیات چندمرحله‌ای مالی داخل `BeginTransaction`.
- **`AsNoTracking()`** برای کوئری‌های read-only مناسب است؛ پوشش سراسری آن ادعا نشده و بهینه‌سازی عمومی Phase 7 باز است.
- **RowVersion پیاده نشده است.** الزام طراحی قدیمی با تراکنش writer SQLite پیش از خواندن موجودی و تست‌های دو اتصال مستقل تأمین شده است؛ تغییر provider نیازمند بررسی مجدد این تضمین است.

### 💾 بکاپ و بازیابی

- **D4 - قاعده بحرانی**: بعد از هر `Copy-Item` از `masterbak`، اجباری:
  ```powershell
  Set-ItemProperty "...\shop.db" -Name IsReadOnly -Value $false
  ```
  وگرنه SQLite خطای `attempt to write a readonly database` می‌دهد.
- **بدون wildcard**: هرگز `Remove-Item "shop.db*"` (باگ D1).
- **Copy نه Move**: بازیابی از masterbak با `Copy-Item` (باگ D2).

### 🧪 تست DB

- تست‌های integration فعلی دیتابیس فایل‌محور موقت و ایزوله می‌سازند؛ دادهٔ عملیاتی ShopManager یا backup کاربر را استفاده/تغییر نمی‌دهند.
- دستور قدیمی استفاده از `masterbak` مربوط به رویهٔ دستی تاریخی بود و روش اجرای تست‌های فعلی نیست. وجود، تازگی یا سیاست نگهداری آن در این closure تأیید نشده است؛ کار backup/restore نیازمند scope صریح Phase 4 است.
- هش legacy SHA256: `Base64(SHA256(password + salt))` بدون پیشوند `v2:`.

### 📝 Build و Commit

- برای تغییر production، Quality Gate در AGENTS.md لازم است: build با 0 Warning / 0 Error و عبور تست‌ها. در این task documentation-only، طبق دستور کاربر فقط بررسی diff و تطبیق اسناد انجام می‌شود.
- commit، tag و push بدون دستور صریح انجام نمی‌شوند.

---

## ۶. روش ادامهٔ کار

قواعد ارتباط و workflow از AGENTS.md پیروی می‌کنند: توضیح فنی فارسی، گزارش شواهد واقعی، تفکیک POSSIBLE / NOT FOUND / VERIFICATION PENDING و رعایت scope مصوب. ممیزی و بازبینی مستقل read-only هستند. نقش‌ها و مجوز agentها فقط در AGENTS.md تعریف می‌شوند؛ در هر لحظه فقط یک agent مجاز به تغییر working tree است و سایر agentها فقط بازبینی read-only انجام می‌دهند. هیچ نقشی به‌تنهایی، بدون scope و approval صریح، مجوز تغییر کد نمی‌دهد.

---

## ۷. وضعیت فازها

جدول جاری بر مبنای roadmap ده‌مرحله‌ای است؛ شماره‌گذاری قدیمی پایین فقط سابقه است.

| Phase     | عنوان                     | وضعیت فعلی                                                                                                                 |
| --------- | ------------------------- | -------------------------------------------------------------------------------------------------------------------------- |
| 1         | Business Correctness      | ✅ COMPLETE؛ شواهد تاریخی در سند Phase 1                                                                                   |
| Pre-Phase | Cleanup محدود S1/S2/S3    | ✅؛ مرحلهٔ شماره‌دار نیست                                                                                                  |
| 2         | Transaction Boundary      | ✅ COMPLETE؛ شواهد تاریخی در سند Phase 2                                                                                   |
| 3         | Concurrency + Idempotency | ✅ COMPLETE تا 3C؛ پیاده‌سازی نهایی `da7d5b4` / بستن رسمی `905622c`                                                        |
| 4 | Crash Recovery + Backup | Phase 4 remains **IN PROGRESS / NOT restore-safe**. Approved critical path: **F1 → F2 → F3** (F1 IMPLEMENTED / VERIFIED / COMMITTED `b38d9b5c5716a01c5e688c5d00496b5a36033d57`; F2/F3 OPEN). Latest hardening: **5C-2.2 — IMPLEMENTED / TESTED / INDEPENDENTLY REVIEWED / COMMITTED / PUSHED `72d77613340f2d9b88abd64ec39bcbe351156608`** (push confirmed by the user). 5C-2.1/2.2 and their historical evidence remain valid; general per-window enrollment is deferred. |
| 5         | Audit + Security          | برنامه‌ریزی‌شده                                                                                                            |
| 6         | Logging + Global Error    | برنامه‌ریزی‌شده                                                                                                            |
| 7         | EF Core + Performance     | برنامه‌ریزی‌شده                                                                                                            |
| 8         | Avalonia Reliability      | برنامه‌ریزی‌شده                                                                                                            |
| 9         | Tests                     | توسعهٔ پوشش؛ 384/384 passed برای 4B-5B-2 (COMMITTED `287764c`)؛ تاریخی: 358/358 passed برای 4B-5B-1 (`34faeee`; COMMITTED / PUSHED)؛ تاریخی: 354/354 برای 4B-5A (`36f0e7f`)؛ تاریخی: 322 در 4B-4، 291 در 4B-3، 270 در `d472153` و 246 در `59d0dfc`؛ این شواهد به معنی اتمام Phase 9 نیست |
| 10        | Release Hardening         | برنامه‌ریزی‌شده                                                                                                            |

**خلاصهٔ Phase 3 برای handoff:**

- 3A، `45c6511`: guard ورود مجدد پرداخت در `OnPayClick`؛ پوشش UI تعاملی مستقل ادعا نشده است.
- 3B-1، `c3268af`: SaleOperations و آماده‌سازی/اعتبارسنجی تکرارپذیر schema؛ دادهٔ تاریخی بدون backfill حفظ می‌شود.
- 3B-2، `5ff9a68`: fingerprint پایدار، replay/conflict، pending قطعی/نامعلوم، اصلاح چرخهٔ پس از commit، invoice recovery و duplicate-item aggregation. `50a5427` checkpoint میانی بود.
- 3C، `da7d5b4`: تراکنش انتقال پیش از خواندن موجودی، rollback قطعی و اعلان یک‌باره پس از commit تأییدشده؛ فرم در شکست قطعی حفظ می‌شود. تست رقابت دو فروش مستقل پیش از تغییر انتقال موفق شد؛ منطق production فروش تغییر نکرد.
- تست‌های کلیدی: `DifferentOperationsCompetingForLastUnitPersistOnlyTheWinner` در `SalePersistenceTests` و `IndependentTransfersCompetingForLastUnitPersistOnlyTheWinner` در `TransferPersistenceTests`؛ هر دو با اتصال‌های مستقل فایل‌محور. تست‌های schema، fingerprint، replay، invoice collision و fault injection در همان مجموعهٔ integration ثبت شده‌اند؛ نگاشت تفصیلی در reconciliation است.

**مرزهای باز:** pending فروش در حافظه است و پس از restart خودکار بازیابی نمی‌شود. انتقال idempotency عمومی ندارد؛ commit نامعلوم به‌صورت صریح گزارش می‌شود و retry خودکار ندارد. UI/چاپ فیزیکی، ماتریس مستقل WAL/DELETE/timeout، crash/restore و تضمین همهٔ writerهای خارجی جزو verification این closure نیستند. `EnsureCreated` و مسیرهای legacy، از جمله index ترکیبی Sales، هنوز وجود دارند؛ safe backup/restore remains OPEN under F1/F2/F3; general migrations/schema drift is OPEN / DEFERRED after the approved scope reset. این وضعیت تناقضی با closure محدود Phase 3 نیست.

**خلاصهٔ Phase 4 (IN PROGRESS — تکمیل نشده):**

- 4A-1، `0944a7e`: انتشار اتمیک بکاپ SQLite (staging + اعتبارسنجی + publish ایمن).
- 4A-2، `05212da`: هویت canonical دیتابیس و توقف امن startup پیش از settings/backup/auth.
- 4A-3، `3466604`: قرارداد دوام SQLite (WAL + synchronous=FULL با read-back روی هر اتصال).
- 4A-4، `22ca6fa`: قابلیت اطمینان چرخهٔ حیات بکاپ (single-flight، generation، sweep staging یتیم، backup-error.log).
- 4B-1، `461670c`: آماده‌سازی بازیابی امن و غیرمخرب (اعتبارسنجی روی کپی، اسنپ‌شات ایمنی WAL-سازگار، گارد hard-link)؛ دیتابیس زنده دست‌نخورده.
- 4B-2A، `59d0dfc`: بنیاد intent بازیابی ماندگار (`RestoreRecoveryService`) و gate پذیرش دیتابیس در `DatabaseService.CreateContext`.
- 4B-2B، `d472153`: موتور بازیابی آفلاین در سطح فایل (`RestoreRecoveryService.Recover`). پس از intent ماندگار فقط forward-only است (forward-complete یا BLOCK، هرگز rollback). وضعیت منتشرشده با SHA-256 مورد انتظار دیتابیس زنده و نبودن sidecarهای زندهٔ `-wal`/`-shm`/`-journal` تأیید می‌شود. فقط tombstone/incoming artifactهای دقیقاً operation-owned پاک می‌شوند (بدون wildcard) و cleanup به‌صورت plan-then-execute است. وضعیت مبهم/ناایمن fail-closed است و restore مسلح می‌ماند. حذف intent آخرین mutation موفق روی disk است. نبودن `SafetyBackupPath` مانع forward completion نیست و اسنپ‌شات ایمنی در صورت وجود حفظ می‌شود. پوشش crash/restart و blocked-state اضافه شد. evidence: `RestoreRecoveryServiceTests` 42 passed؛ کل `ShopManager.Domain.Tests` 270 passed؛ build غیرافزایشی 0W/0E؛ adversarial/final review: PASS.

- 4B-3, `583a7b8` - **implemented / verified / committed**: app-lifetime Windows mutex `Global\ShopManager.ApplicationLifetime`؛ acquisition پس از `Velopack.Run()` و پیش از `BuildAvaloniaApp()`؛ Busy → exit code 2 با صفر startup admission؛ acquisition Error → exit code 3 و fail-closed؛ abandoned ownership پذیرفته بدون ادعای سلامت DB؛ guard موفق در `Program` برای عمر process strongly rooted. harness واقعی چندprocess از نام‌های یکتای تست استفاده می‌کند و production DB/mutex را مصرف نمی‌کند. شواهد 21/291 passed و 0W/0E در جدول بالا ثبت شده‌اند؛ در این documentation sync build/test دوباره اجرا نشده است.

**VERIFICATION PENDING — غیرمسدودکنندهٔ 4B-3:** cross-user / cross-session / elevation؛ installed GUI startup/shutdown smoke؛ Velopack update/restart overlap. هیچ‌کدام verified نیستند.

**4B-4 — implemented / verified / independently reviewed / checkpoint-ready؛ committed `63039d2`:** نخستین desktop startup gate پس از mutex و Avalonia، recovery را پیش از resolver/SQLite اجرا می‌کند؛ فقط `NoIntent` یا `Completed` در حالت unarmed ادامه می‌دهد. `Blocked` و خطاهای غیرمنتظره بدون resolver/context/settings/theme/backup/timer/auth/session/normal window/normal shutdown registration متوقف می‌شوند. registered identity روی همان intent مصرف‌شده، پس از arming و پیش از mutation، بدون DatabaseService/SQLite بررسی می‌شود؛ live مفقود پس از tombstone پیش از resolver بازیابی می‌شود. invariantهای 4B-2B و رفتار 4B-3 حفظ شده‌اند. شواهد در جدول بالا ثبت‌اند؛ build/test و review در documentation sync تکرار نشده‌اند.

### 4B-5A — database admission + context drain

**IMPLEMENTED / VERIFIED / INDEPENDENTLY REVIEWED / COMMITTED — `36f0e7f`.** Phase 4 remains **IN PROGRESS**.

Checkpoint `36f0e7f` (`feat: add database admission and drain gate`) is pushed to `origin/phase/4-crash-recovery-backup` (push confirmed by the user; local remote-tracking ref matches).

- Guaranteed ONLY: atomic runtime DbContext admission cutoff; tracking admitted runtime contexts until successful cleanup; asynchronous drain proof; owner-controlled reopen; fail-closed cleanup faults.
- **NOT restore-safe:** no proof of backup drain, background/timer drain, updater drain, complete multi-context business-operation drain or full quiesce. No production permission/caller for `PrepareRestore` / `Arm` is introduced; no production caller of `CloseAdmission` exists in this checkpoint. No restore UI wiring.
- Verified implementation evidence supplied for this documentation sync: `DatabaseAdmissionDrainTests` **32/32 passed**; relevant DB/resolution/recovery/startup tests **130/130 passed**; full `ShopManager.Domain.Tests` **354/354 passed**; non-incremental solution build **0 warnings / 0 errors**; implementation `git diff --check` **exit 0**. Tests/build/review were not rerun during this docs-only step.
- Independent review: **PASS WITH FINDINGS**. Both previous blocking findings are **RESOLVED**: concurrent cleanup ownership is established before EF cleanup; already-admitted fresh initialization reuses the existing admission instead of taking a second independent lease.
- **Historical MEDIUM at 4B-5A — RESOLVED in 4B-5B-1 (`34faeee`; COMMITTED / PUSHED):** no-lease fresh `EnsureResolved` could permanently cache admission-closed in `_resolved` / `_blockedReason`. The committed 4B-5A checkpoint retains this historical finding; the independently reviewed resolver fix committed in `34faeee` leaves temporary rejection retryable. No production cutoff caller has been introduced.
- **LOW:** if the cleanup-success callback itself throws, cleanup state may remain in-progress/fail-closed; the current gate callback has no expected throw path.
- **LOW:** AppDbContext disposal semantics are stricter: concurrent disposal is rejected; a later disposal after cleanup failure rethrows the original failure.
- Historical 5A/5B sequence remains valid. Current approved critical path: **F1 → F2 → F3**; general lifetime/enrollment work is deferred and production restore activation is not established.

### 4B-5B-1 — resolver poisoning fix

**IMPLEMENTED / VERIFIED / INDEPENDENTLY REVIEWED / COMMITTED / PUSHED — `34faeee`. NOT restore-safe.** Phase 4 remains **IN PROGRESS**. Commit/push confirmed by the user; local HEAD and `origin/phase/4-crash-recovery-backup` contain `34faeee`.

- Typed `DatabaseAdmissionClosedException` identifies temporary `Closed` admission only; no message matching or broad `InvalidOperationException` handling.
- Temporary no-lease `EnsureResolved` rejection no longer poisons `_resolved` / `_blockedReason`; retry after `Reopen` succeeds.
- Real resolution/initialization/cleanup failures and `FaultedClosed` remain fail-closed. Already-admitted fresh factory initialization retains its original admission ownership; new factories after cutoff remain rejected.
- No production `CloseAdmission`, `PrepareRestore` or `Arm` caller was added in 5B-1. Its backup/timer primitive successor is now implemented in 5B-2; unified production/update/shutdown/restore/UI integration remains pending for 5C and later.
- Recorded implementation evidence: targeted `DatabaseAdmissionDrainTests` **36/36 passed**; relevant admission/resolution/durability/recovery/startup tests **166/166 passed**; full `ShopManager.Domain.Tests` **358/358 passed**; non-incremental solution build **0 warnings / 0 errors**; implementation `git diff --check` **exit 0**. These checks were executed during implementation, not rerun during this docs-only sync.
- Independent review supplied by the user: **PASS WITH FINDINGS; no Critical / High / Medium findings**. This sync records that review; it does not claim a new independent review.

**Residual LOW findings:**

1. Unresolved path getters can temporarily throw admission-closed.
2. An empty directory may be created before admission rejection.
3. Minor test gaps remain: cached resolution while `Closed`, the `FaultedClosed` resolver path, and concurrent retry / `Reopen`. These cases are **VERIFICATION PENDING**, not claimed as covered.

### 4B-5B-2 — backup admission/timer boundary

**IMPLEMENTED / VERIFIED / INDEPENDENTLY REVIEWED / COMMITTED. NOT restore-safe / NOT full quiesce.** Phase 4 remains **IN PROGRESS**.

Checkpoint commit: `287764c2e89d428ac658e55589a5900defe24397` — `feat: add backup admission and drain boundary`. This post-commit reconciliation records COMMITTED status only; it does not claim a push or new test/build/review execution.

- Recorded final implementation evidence: targeted `BackupAdmissionDrainTests` **26/26**; relevant backup/preparation + DB admission/recovery/startup regressions **172/172**; full suite **384/384**; **0 failed / 0 skipped**; non-incremental solution build **0 warnings / 0 errors**; implementation `git diff --check` **PASS**. Tests/build were executed during implementation and the Medium fixes, not rerun during this documentation-only sync.
- Independent re-review supplied by the user: **PASS WITH FINDINGS**; **M1 CLOSED**, **M2 CLOSED**; **no remaining Critical / High / Medium findings**. This sync records that result, not a new independent review.
- **M1 CLOSED:** recoverable interval/factory/activation failures retire any candidate and leave no invalid current timer installed; later backup/timer operations remain usable. Unproven retirement/accounting completion remains fail-closed. `RestartAutoBackupTimer` now propagates recoverable setup errors; existing production callers in `App.axaml.cs` and `SettingsWindow.axaml.cs` catch them.
- **M2 CLOSED:** service tests use an isolated execution-context coordinator, restore only their own `DataChanged` subscription and prior tracking state after worker completion, and preserve the original assertion when cleanup fails. Production subscription semantics are unchanged.

**Guarantees within the backup/timer boundary:**

- Atomic backup admission before waiting, resolution or I/O; post-cutoff requests are rejected before side effects.
- FIFO logical execution-turn token; accepted queued work remains counted through completion/cleanup.
- No coordinator execution lock is held across SQLite/file I/O; state locks are short.
- Timer busy ticks skip instead of queueing; closed/stale ticks also skip, with epoch/current-session protection.
- Timer retirement and drain are provable through callback/disposal completion, including retired sessions and lifecycle work.
- `Initialize` and `PrepareRestore` share the same serialized boundary; wrapper/core calls do not double-admit.
- Owner-controlled drain/reopen; timeout/cancellation does not reopen admission.

**Residual LOW findings — VERIFICATION PENDING:**

1. `Reopen` does not restart the timer; a later explicit restart is required.
2. Accounting corruption can leave queued waiters wedged.
3. An invariant-path timer-thread exception remains unguarded.
4. The `AsyncLocal` reentrancy marker may flow into child tasks.
5. A theoretical close-lifecycle fault-ordering window exists before `_fault` is recorded.

**Remaining test gaps — VERIFICATION PENDING:**

- No concurrent Admit-vs-`CloseAdmission` stress test.
- No blocking `Run`-in-transfer-gap test.
- No recoverable timer-failure test while an active timer exists.
- No close-lifecycle failure-ordering test.
- The accounting-corruption test reflects private `_accepted`.
- Fixture worker aggregation has no independent timeout.

**Remaining boundary:** no production cutoff / `PrepareRestore` / `Arm` wiring exists. Restore-specific cutoff/drain and lifecycle coordination remain for **F1**. Legacy `BackupService.RestoreBackup` remains outside this guarantee; the preparation algorithm is unchanged. Backup/timer drain does not establish restore safety or full quiesce, protect the entire Prepare-to-Arm interval, or prove successful deletion of all best-effort temporary artifacts.

**Final restore constraints (F1 — now implemented, verified and committed in `b38d9b5c5716a01c5e688c5d00496b5a36033d57`; retained as design constraints):**

- Never wait for DB drain while holding the `TransitionGate` write lock.
- Future production flow must close admission and prove drain before `Arm`; resolver identity must be established before future `CloseAdmission`.
- In the no-lease fresh-resolution path, marker publication currently occurs after the runtime initialization lease is released and is outside DB drain. The already-admitted factory retains its existing lease. DB drain must not be presented as proof that all resolver/marker work has completed.
- Existing 5C-2.1/2.2 hardening and evidence are retained. F1/F2/F3 is the approved critical path; general per-window enrollment/lifetime architecture is deferred. Terminal restore boundary is implemented by F1 (committed in `b38d9b5c5716a01c5e688c5d00496b5a36033d57`, verified); the swap itself remains startup-recovery work; F2/F3 remain OPEN.
- Keep cutoff closed through terminal restore exit. Timeout or cleanup uncertainty => do not `Arm`; after persistent intent exists, or publication is uncertain, normal DB work must not resume.
- Ordering: runtime/DB cutoff and drain → `PrepareRestore` with backup admission still open → backup cutoff and drain → release SQLite pools → `Arm` → restore-specific exit → actual swap at next startup. Updater Apply/Restart and queued lifecycle transitions must not race restore.

**VERIFICATION PENDING — 4B-4:** direct automated coverage مسیر واقعی `App.OnFrameworkInitializationCompleted` / `InitializeNormalDesktopStartup`؛ تست بیشتر path canonicalization/alias؛ installed GUI blocked-window startup/shutdown smoke؛ startup latency/UX برای stagingهای بزرگ. پوشش entry مشترک `App.RunDesktopStartup` ادعای پوشش مستقیم callback/بدنهٔ عادی نیست.

**Approved Phase-4 DoD — F1 implemented/verified; F2 and F3 remain OPEN:**

- [x] **F1 — Final Restore Boundary — F1 IMPLEMENTED / VERIFIED / COMMITTED (`b38d9b5c5716a01c5e688c5d00496b5a36033d57`); final independent review PASS WITH FINDINGS (Low only; Critical/High/Medium 0; no blockers); targeted 31 passed, relevant 307 passed, full 547 passed (0 failed / 0 skipped), non-incremental build 0 warnings / 0 errors, git diff --check PASS. No live DB swap occurs in the current process; the actual swap remains startup recovery work; the terminal restore boundary is fail-closed. B1/B2 review findings were fixed and re-reviewed; remaining Low findings are accepted residual risks. F2 and F3 remain OPEN; Phase 4 remains IN PROGRESS / NOT closed.** Scope: replace legacy SettingsWindow restore; terminal single-flight; block relevant new work; stop relevant producers; drain existing runtime/DB/backup work; ordered preparation, pool release, persistent intent and restore-specific exit; startup recovery performs the swap.
- [ ] **F2 — Backup Compatibility + Broken-DB Recovery:** prove application compatibility; bounded recovery when the current DB is broken; preserve damaged/original files fail-closed.
- [ ] **F3 — Final Verification & Closure:** integration/end-to-end restore, real process-crash/kill where required, UI smoke, full tests, build **0 warnings / 0 errors**, independent review and final documentation reconciliation.

The user-approved scope reset is intentional, not abandonment of restore safety. No current evidence proves per-window enrollment mandatory. General enrollment/lifetime architecture moves to deferred reliability backlog. AR-2/S8 (migrations/schema drift) and D1 (encryption/secondary backup/3-2-1) remain OPEN but are deferred from this closure; F2 compatibility is still mandatory. Completed 5C-2.1/2.2 remain valid hardening. See [approved F1 → F2 → F3 plan](PHASE-4-CRASH-RECOVERY-BACKUP.md#approved-completion-plan) and [deferred backlog](MASTER-BACKLOG.md#deferred-work-from-phase-4-scope-reset). This docs-only approval does not authorize source implementation.

Current checkpoint: **5C-2.2 COMMITTED / PUSHED `72d7761`**, with push confirmed by the user. This handoff authorizes no source changes; the approved reset is docs-only.

<details>
<summary>جدول تاریخی شماره‌گذاری قدیمی؛ snapshot مورخ 1405/07/07، دیگر وضعیت فعلی نیست</summary>

| فاز | عنوان                                |     وضعیت     |
| :-: | ------------------------------------ | :-----------: |
|  ۰  | زیرساخت VS Code                      |      ✅       |
|  ۱  | امنیت (XSS + API key + admin)        |      ✅       |
|  ۲  | پاک‌سازی کد مرده (`SaleWindow`)      |    ⏳ بعدی    |
| ۲.۵ | رفع `catch{}` خالی                   |      ⏳       |
| ۲.۷ | Structured Logging + Crash Reporting |      ⏳       |
|  ۳  | بهینه‌سازی داده (N+1 + Pagination)   |      ⏳       |
| ۳.۵ | بهینه‌سازی داشبورد                   |      ⏳       |
|  ۴  | RestoreBackup + `SettingsWindow:290` |      ⏳       |
| ۴.۵ | Backup 3-2-1 + Encryption            |      ⏳       |
|  ۵  | Migrate + Race + Denormalized Stock  |      ⏳       |
| ۵.۵ | Audit Trail                          |      ⏳       |
|  ۶  | Code Signing                         | 🔒 نیاز گواهی |
|  ۷  | تست‌های واحد Domain                  |      ⏳       |
|  ۸  | Archiving + Backup بهبود             |      ⏳       |
| ۸.۵ | Alert System                         |      ⏳       |
| ۰.۵ | EULA + Privacy + License             |      ⏳       |

</details>

---

## ۸. منشور ۱۲ بُعدی کیفیت (چک‌لیست هر تغییر)

قبل از هر تغییر، این ۱۲ سؤال را بپرس:

1. **عملکرد**: آیا واقعاً همان کار را می‌کند؟
2. **امنیت**: آیا XSS/SQLi/Authorization را تضعیف می‌کند؟
3. **عملکرد/مقیاس**: با ۱۰۰K رکورد هنوز سریع است؟
4. **قابلیت اطمینان**: اگر برق برود وسط این تغییر چه می‌شود؟
5. **قابلیت نگهداری**: ۶ ماه دیگر کسی می‌فهمد؟
6. **نظارت**: اگر خطا رخ دهد، چطور می‌فهمم؟
7. **پشتیبان‌گیری**: اگر دیسک بمیرد، داده بازیابی می‌شود؟
8. **تاریخ/زمان**: ساعت سیستم روی این اثر دارد؟
9. **صحت کسب‌وکار**: قانون مالیاتی رعایت می‌شود؟
10. **UX**: کاربر بدون آموزش می‌فهمد؟
11. **انطباق**: حریم خصوصی / مجوز نرم‌افزار رعایت است؟
12. **آینده‌نگری**: اگر ۲ شعبه بشود کار می‌کند؟

---

## ۹. Backlog تاریخی با شماره‌گذاری قدیمی

این بخش برای حفظ سابقه نگه داشته شده است؛ unchecked بودن متن قدیمی به معنی بازبودن همان کار در HEAD نیست. حذف SaleWindow/Models و cleanupهای ثبت‌شده در reconciliation انجام شده‌اند. موارد دیگر این فهرست بدون audit تازه، تأییدشده یا بسته تلقی نمی‌شوند. برای اولویت و شمارهٔ Phase فقط [MASTER-BACKLOG.md](MASTER-BACKLOG.md) و reconciliation جاری ملاک‌اند؛ از این بخش scope جدید استخراج نکنید.

### فاز ۲ — پاک‌سازی

- حذف `SaleWindow.axaml` + `.axaml.cs` (~۵۸۴ خط)
- حذف پوشه‌ی خالی `Models`
- حذف بسته‌های تکراری در `Desktop.csproj` (اگر در `Infrastructure` هستند)
- حذف `Microsoft.Extensions.Hosting` (اگر بلااستفاده)

### فاز ۲.۵ — Server-side Validation

- `PurchaseWindow` → `SupplierNote` (۵۰۰)
- `TransferWindow` → `Note` (۵۰۰) — قبلاً انجام شد
- `UserEditWindow` → `FullName` (۲۰۰) + `Username` (۵۰) — قبلاً انجام شد

### Excel Formula Injection (فاز ۲)

- `TransferHistoryWindow.axaml.cs:299` → `t.Note`
- `LoginHistoryWindow.axaml.cs:348,349,356` → `h.Username`, `h.FullName`, `h.Note`
- `ExcelExportService.cs` → همه‌ی `.Value = string`

### فاز ۴ — RestoreBackup

- `SettingsWindow.axaml.cs:290` → `Environment.Exit(0)` بعد از Restore → `Shutdown()`

### سیاست Legacy (بازبینی آینده)

- کاربران با هش قدیمی روی ورود موفق **خودکار ارتقا** می‌یابند (گزینه A)
- بدون اجبار تغییر رمز
- اگر روزی سیاست سخت‌گیرانه‌تر خواستی: تعمیم `CheckLegacyAdminPassword` به همه‌ی `NeedsUpgrade`

### D4 — بازیابی از ReadOnly

- بعد از هر `Copy-Item` از `masterbak`، `Set-ItemProperty IsReadOnly $false` **اجباری**

---

## ۱۰. فایل‌های کلیدی (نقشه سریع)

| فایل                                                     | نقش                                                                                                                        |
| -------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------- |
| `Services/DatabaseService.cs`                            | ساخت DbContext، EnsureCreated، آماده‌سازی legacy و اعتبارسنجی SaleOperations؛ جایگزین کامل EF migrations نیست              |
| `Services/SaleRequest.cs`                                | snapshot/fingerprint و lifecycle PendingSale                                                                               |
| `Services/SalePersistenceService.cs`                     | تراکنش فروش، idempotency و کنترل authoritative موجودی                                                                      |
| `Services/SaleInvoiceNumberGenerator.cs`                 | پیشنهاد شمارهٔ تازه با خواندن Sales و SaleOperations                                                                       |
| `Services/TransferPersistenceService.cs`                 | تراکنش انتقال، نتیجهٔ commit و خطاهای پس از commit                                                                         |
| `Services/AuthService.cs`                                | Login + auto-upgrade hash                                                                                                  |
| `Services/AuthServiceInitializer.cs`                     | `HasAnyUser`, `CreateInitialAdmin`, `CheckLegacyAdminPassword`                                                             |
| `Services/PasswordHasher.cs`                             | PBKDF2 v2 + SHA256 legacy                                                                                                  |
| `Services/BackupService.cs`                              | بکاپ خودکار (staging + publish ایمن + single-flight) و آماده‌سازی بازیابی امن (4B-1)؛ `RestoreBackup` قدیمی هنوز موجود است |
| `Services/RestoreRecoveryService.cs`                     | Phase 4B-2A: intent بازیابی ماندگار (`Arm`/`ReadIntent`/`IsArmed`) و gate پذیرش دیتابیس؛ Phase 4B-2B (`d472153`): موتور آفلاین `Recover` و tombstoneهای operation-owned؛ از 4B-4 در startup توسط coordinator فراخوانی می‌شود؛ hook محدود same-intent identity admission |
| `Services/StartupRecoveryCoordinator.cs`                 | 4B-4 (`63039d2`): recovery admission پیش از resolver/SQLite و registered identity validation؛ توقف fail-closed |
| `Services/HtmlEncoder.cs`                                | escape HTML                                                                                                                |
| `Services/SaleInvoiceHtmlBuilder.cs`                     | سازنده HTML فاکتور                                                                                                         |
| `Views/FirstRunSetupWindow.axaml.cs`                     | راه‌اندازی اولیه (جدید)                                                                                                    |
| `Views/POSWindow.axaml.cs`                               | guard پرداخت و مرز اجرای pending/UI                                                                                        |
| `Views/TransferWindow.axaml.cs`                          | اعتبارسنجی ورودی و مرز persistence/UI انتقال                                                                               |
| `Views/DashboardWindow.axaml.cs`                         | داشبورد                                                                                                                    |
| `ShopManager.Infrastructure/Persistence/AppDbContext.cs` | مدل EF و قیود SaleOperations                                                                                               |
| `ShopManager.Domain.Tests/Integration/`                  | تست‌های schema، request، sale persistence و transfer persistence                                                           |
| `App.axaml.cs`                                           | سوییچ بین FirstRunSetup / Login                                                                                            |

---

## ۱۱. سابقهٔ ابزار AI؛ جایگزین‌شده با AGENTS.md

توصیه‌های زیر متعلق به handoff قدیمی‌اند و ابزار/مجوز جاری پروژه را تعیین نمی‌کنند. نقش فعلی Repository Agent و Reviewer در AGENTS.md تعریف شده است.

- **ابزار اصلی:** DeepSeek Chat
- **پیشنهاد:** نصب Cline در VS Code + اتصال به DeepSeek API برای ریفکتور مقیاس‌دار
- **قاعده:** برای کارهای حساس (`SaveSale`, `PasswordHasher`, Migration) → فقط با چت
- **قاعده:** برای ریفکتور تکراری (۲۴ فایل XSS) → Cline

---

## ۱۲. هشدارهای مهم

### ⚠️ قبل از هر تغییر

- ابتدا scope کار و AGENTS.md؛ production در این task مستندسازی frozen است.
- آخرین شواهد build/test را از اجرای واقعی گزارش کنید؛ read-only/docs-only به معنی اجرای مجدد برنامه یا دست‌کاری دیتابیس نیست.
- اگر کار مجاز به دیتابیس/backup عملیاتی نیاز دارد، مسیر و پیش‌نیازهای آن باید جداگانه بررسی شوند؛ این closure چنین دسترسی‌ای ندارد.

### ⚠️ بعد از هر تغییر

- verification متناسب با scope؛ برای این closure، `git diff --check`، کنترل فهرست فایل‌ها و self-review مستندات.
- وضعیت را فقط پس از وجود شواهد به‌روز کنید؛ نتیجهٔ Phase 3 را به recovery/backup یا پوشش همهٔ مسیرهای موجودی تعمیم ندهید.

### ⚠️ ممنوع مطلق

- `Remove-Item "shop.db*"` (wildcard)
- `Move-Item` برای بازیابی از بکاپ
- ویرایش `masterbak`
- حذف بی‌تأیید فایل‌های کد
- `Environment.Exit` در مسیرهایی که `ShutdownRequested` باید اجرا شود

---

**Phase 1–3 بسته‌اند.** Phase 4 remains **IN PROGRESS / NOT restore-safe**. Approved critical path: **F1 → F2 → F3** (F1 IMPLEMENTED / VERIFIED / COMMITTED `b38d9b5c5716a01c5e688c5d00496b5a36033d57`; F2/F3 OPEN). Latest hardening: **5C-2.2 — IMPLEMENTED / TESTED / INDEPENDENTLY REVIEWED / COMMITTED / PUSHED `72d77613340f2d9b88abd64ec39bcbe351156608`** (push confirmed by the user). 5C-2.1/2.2 and their historical evidence remain valid; general per-window enrollment is deferred. سایر اقلام DoD بازند؛ جزئیات در [PHASE-4-CRASH-RECOVERY-BACKUP.md](PHASE-4-CRASH-RECOVERY-BACKUP.md).
