# AI-HANDOFF — سند انتقال پروژه ShopManager

> **هدف:** این فایل مرجع کامل برای هر AI است که وارد پروژه می‌شود. قبل از هر اقدامی، این سند را کامل بخوان.
>
> **آخرین به‌روزرسانی:** به‌روزرسانی وضعیت Phase 4 تا checkpoint `4B-2B`؛ checkpoint کد production: `d472153`؛ checkpoint قبلی: `59d0dfc`؛ commitهای بین `59d0dfc` و `d472153` فقط مستندات/قواعد عملیاتی مخزن (AGENTS.md) را تغییر داده‌اند.
> **وضعیت فعلی:** Phase 1، Phase 2 و Phase 3 تا 3C کامل‌اند. Phase 4 — Crash Recovery + Backup **IN PROGRESS** است و تکمیل نشده (checkpoint ها تا `4B-2B`، commit `d472153`).
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
| verification checkpoint `4B-2B`، commit `d472153` (HEAD فعلی شامل این 270 تست است) | build غیرافزایشی solution: 0 Warning / 0 Error؛ `RestoreRecoveryServiceTests`: 42 passed؛ کل `ShopManager.Domain.Tests`: 270 passed؛ adversarial/final review: PASS بدون issue مسدودکنندهٔ Critical/High/Medium |

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
| 4         | Crash Recovery + Backup   | 🔄 IN PROGRESS؛ checkpoint ها تا `4B-2B` (4B-2A در `59d0dfc`؛ 4B-2B در `d472153`)؛ باقی‌مانده: integration بعدی Phase 4 (startup، UI، shutdown، mutex، حذف legacy restore) و بقیهٔ DoD |
| 5         | Audit + Security          | برنامه‌ریزی‌شده                                                                                                            |
| 6         | Logging + Global Error    | برنامه‌ریزی‌شده                                                                                                            |
| 7         | EF Core + Performance     | برنامه‌ریزی‌شده                                                                                                            |
| 8         | Avalonia Reliability      | برنامه‌ریزی‌شده                                                                                                            |
| 9         | Tests                     | توسعهٔ پوشش؛ وجود 270 تست در HEAD (پس از 4B-2B در `d472153`؛ 246 تست در `59d0dfc`) به معنی اتمام این Phase نیست                                                             |
| 10        | Release Hardening         | برنامه‌ریزی‌شده                                                                                                            |

**خلاصهٔ Phase 3 برای handoff:**

- 3A، `45c6511`: guard ورود مجدد پرداخت در `OnPayClick`؛ پوشش UI تعاملی مستقل ادعا نشده است.
- 3B-1، `c3268af`: SaleOperations و آماده‌سازی/اعتبارسنجی تکرارپذیر schema؛ دادهٔ تاریخی بدون backfill حفظ می‌شود.
- 3B-2، `5ff9a68`: fingerprint پایدار، replay/conflict، pending قطعی/نامعلوم، اصلاح چرخهٔ پس از commit، invoice recovery و duplicate-item aggregation. `50a5427` checkpoint میانی بود.
- 3C، `da7d5b4`: تراکنش انتقال پیش از خواندن موجودی، rollback قطعی و اعلان یک‌باره پس از commit تأییدشده؛ فرم در شکست قطعی حفظ می‌شود. تست رقابت دو فروش مستقل پیش از تغییر انتقال موفق شد؛ منطق production فروش تغییر نکرد.
- تست‌های کلیدی: `DifferentOperationsCompetingForLastUnitPersistOnlyTheWinner` در `SalePersistenceTests` و `IndependentTransfersCompetingForLastUnitPersistOnlyTheWinner` در `TransferPersistenceTests`؛ هر دو با اتصال‌های مستقل فایل‌محور. تست‌های schema، fingerprint، replay، invoice collision و fault injection در همان مجموعهٔ integration ثبت شده‌اند؛ نگاشت تفصیلی در reconciliation است.

**مرزهای باز:** pending فروش در حافظه است و پس از restart خودکار بازیابی نمی‌شود. انتقال idempotency عمومی ندارد؛ commit نامعلوم به‌صورت صریح گزارش می‌شود و retry خودکار ندارد. UI/چاپ فیزیکی، ماتریس مستقل WAL/DELETE/timeout، crash/restore و تضمین همهٔ writerهای خارجی جزو verification این closure نیستند. `EnsureCreated` و مسیرهای legacy، از جمله index ترکیبی Sales، هنوز وجود دارند؛ migrations/schema drift/backup در Phase 4 بازند. این وضعیت تناقضی با closure محدود Phase 3 نیست.

**خلاصهٔ Phase 4 (IN PROGRESS — تکمیل نشده):**

- 4A-1، `0944a7e`: انتشار اتمیک بکاپ SQLite (staging + اعتبارسنجی + publish ایمن).
- 4A-2، `05212da`: هویت canonical دیتابیس و توقف امن startup پیش از settings/backup/auth.
- 4A-3، `3466604`: قرارداد دوام SQLite (WAL + synchronous=FULL با read-back روی هر اتصال).
- 4A-4، `22ca6fa`: قابلیت اطمینان چرخهٔ حیات بکاپ (single-flight، generation، sweep staging یتیم، backup-error.log).
- 4B-1، `461670c`: آماده‌سازی بازیابی امن و غیرمخرب (اعتبارسنجی روی کپی، اسنپ‌شات ایمنی WAL-سازگار، گارد hard-link)؛ دیتابیس زنده دست‌نخورده.
- 4B-2A، `59d0dfc`: بنیاد intent بازیابی ماندگار (`RestoreRecoveryService`) و gate پذیرش دیتابیس در `DatabaseService.CreateContext`.
- 4B-2B، `d472153`: موتور بازیابی آفلاین در سطح فایل (`RestoreRecoveryService.Recover`). پس از intent ماندگار فقط forward-only است (forward-complete یا BLOCK، هرگز rollback). وضعیت منتشرشده با SHA-256 مورد انتظار دیتابیس زنده و نبودن sidecarهای زندهٔ `-wal`/`-shm`/`-journal` تأیید می‌شود. فقط tombstone/incoming artifactهای دقیقاً operation-owned پاک می‌شوند (بدون wildcard) و cleanup به‌صورت plan-then-execute است. وضعیت مبهم/ناایمن fail-closed است و restore مسلح می‌ماند. حذف intent آخرین mutation موفق روی disk است. نبودن `SafetyBackupPath` مانع forward completion نیست و اسنپ‌شات ایمنی در صورت وجود حفظ می‌شود. پوشش crash/restart و blocked-state اضافه شد. evidence: `RestoreRecoveryServiceTests` 42 passed؛ کل `ShopManager.Domain.Tests` 270 passed؛ build غیرافزایشی 0W/0E؛ adversarial/final review: PASS.

**باقی‌مانده (pending):** integration بعدی Phase 4 که در 4B-2B **نیست** و هنوز پیاده نشده: startup wiring/order و مصرف intent، app-lifetime mutex، UI، shutdown/quiesce، و حذف legacy restore path. موتور 4B-2B هنوز از هیچ caller production فراخوانی نمی‌شود. سپس: جایگزینی `BackupService.RestoreBackup` قدیمی و مسیر `SettingsWindow.axaml.cs:290` با `Environment.Exit(0)` (AR-4)؛ `EnsureCreated → Migrate()` (AR-2)؛ Schema Drift (S8)؛ Backup 3-2-1 + رمزنگاری (D1)؛ تست‌های crash/restore. جزئیات و مرزبندی کامل: [PHASE-4-CRASH-RECOVERY-BACKUP.md](PHASE-4-CRASH-RECOVERY-BACKUP.md).

مرحلهٔ بعد مطابق roadmap موجود **Phase 4 — Crash Recovery + Backup** است و تکمیل آن (integration بعدی Phase 4 و بقیهٔ DoD؛ 4B-2B در `d472153` پیاده شده است) نیازمند scope و approval مستقل است. این handoff مجوز اجرای خودکار آن تغییرات نیست.

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
| `Services/RestoreRecoveryService.cs`                     | Phase 4B-2A: intent بازیابی ماندگار (`Arm`/`ReadIntent`/`IsArmed`) و gate پذیرش دیتابیس؛ Phase 4B-2B (`d472153`): موتور آفلاین `Recover` و tombstoneهای operation-owned؛ هنوز از هیچ caller production فراخوانی نمی‌شود |
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

**پایان سند. Phase 1–3 بسته‌اند؛ Phase 4 — Crash Recovery + Backup در حال انجام است (checkpoint تا `4B-2B`؛ 4B-2A در `59d0dfc` و 4B-2B در `d472153`) و تکمیل نشده. باقی‌ماندهٔ integration بعدی Phase 4 نیازمند scope و approval مستقل است.**
