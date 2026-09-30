# AUDIT-RECONCILIATION — تطبیق ممیزی با Roadmap جدید

> **هدف:** پل بین ممیزی، وضعیت فعلی، و Roadmap جدید (۱۰ Phase).
> **اصل حاکم:** هیچ Phase بدون DoD اثبات‌شده Done نیست.
> **آخرین به‌روزرسانی:** بستن مستندات Phase 3 در مبنای `da7d5b4`.
> **آخرین Commit پیاده‌سازی:** `da7d5b4` — Phase 3C؛ Phase 3 تا 3C کامل است.
> **شواهد Phase 3C:** 134 Pass / 0 Fail / 0 Skip؛ build: 0 warnings / 0 errors؛ final adversarial review: PASS.
> نتایج Phase 1/2 در بخش‌های خود، شواهد تاریخی‌اند. وضعیت فعلی این سند جایگزین نتیجه‌گیری‌های زمانی قدیمی می‌شود؛ متن `AUDIT-REPORT.md` و اسناد closure قبلی بازنویسی نشده است.

---

## ۱. Roadmap قطعی (۱۰ Phase)

| # | Phase | Scope | DoD کوتاه |
|:---:|---|---|---|
| 1 | Business Correctness | Sale, Return, Cost, Profit, Discount, Rounding, Stock | تست عددی |
| 2 | Transaction Boundary | Sale+Payment+Inventory+Cashbox+Rollback | Pre-Commit failure → Rollback؛ Post-Commit failure → حفظ Sale |
| 3 | Concurrency+Idempotency | Payment guard، SaleOperations، Stock Race با تراکنش SQLite | درخواست یکسان → replay؛ دو برداشت مستقل از stock=1 → فقط یک برنده |
| 4 | Crash Recovery+Backup | WAL, Migrate, Integrity, Restore, Encryption | Crash → بازیابی سالم |
| 5 | Audit+Security | AuditLog, Authorization, Password | هر تغییر حساس → رکورد |
| 6 | Logging+Global Error | Structured Log, Global Handler, Rolling | خطا → فایل log |
| 7 | EF Core+Performance | N+1, AsNoTracking, Index, Pagination | 100K → سریع |
| 8 | Avalonia Reliability | UI Thread, Async, Event Leak, Timer | 8 ساعت → بدون leak |
| 9 | Tests | Unit, Integration, Regression | Coverage مشخص |
| 10 | Release Hardening | Updater, Signing, Licenses, EULA | نصب تمیز |

**Scope Creep (خارج):** Cloud Sync، Multi-terminal، Multi-store، Mobile، Plugin، Telemetry، Feature Flags، API

---

## ۲. Reconciliation — یافته‌های ممیزی

### ۲.۱ جدول Master

| # | یافته | Phase | وضعیت | DoD |
|:---:|---|:---:|:---:|:---:|
| F1 | نبود Transaction دور SaveSale | 2 | ✅ | ✅ |
| F2 | EnsureCreated به جای Migrate | 4 | ❌ | ❌ |
| F3 | XSS در ۴ پنجره | خارج | ✅ | ✅ |
| F4 | RestoreBackup ناایمن | 4 | ❌ | ❌ |
| F5 | Unique Index InvoiceNumber | 3 | ✅ SaleOperations | ✅ |
| F6 | N+1 در CreateProductTile | 7 | ❌ | ❌ |
| F7 | Race بررسی موجودی | 3 | ✅ فروش/انتقال عادی | ✅ |
| F8 | Code Signing | 10 | ❌ | ❌ |
| F9 | API key plaintext | خارج | ✅ | ✅ |
| F10 | admin/admin | 5 | ✅ | ✅ |
| F11 | SaleWindow کد مرده | خارج | ✅ | ✅ |
| F12 | catch{} خالی | 6 | ❌ | ❌ |
| P1-1 | PricingCalculator SalePrice=0 | 1 | ✅ | ✅ | رفع شد (B1) |
| P1-2 | PricingCalculator ToEven rounding | 1 | ✅ | ✅ | رفع شد (B2) |
| P1-3 | POSWindow Revenue خام | 1 | ✅ | ✅ | رفع شد (B3) |
| P1-4 | POSWindow Profit invariant نقض | 1 | ✅ | ✅ | رفع شد (B4) |
| P1-5 | Sale.DiscountAmount غایب | 1 | ✅ | ✅ | رفع شد (B5) |

### ۲.۲ نقاط کور ۱۲ بُعدی

| # | بُعد | Phase | وضعیت |
|:---:|---|:---:|:---:|
| D1 | Backup 3-2-1 | 4 | ❌ |
| D2 | Structured Logging | 6 | ❌ |
| D3 | Alert System | Future | خارج |
| D4 | Audit Trail | 5 | ❌ |
| D5 | EULA/Privacy | 10 | ❌ |
| D6 | Tax/Legal | Business Req | 🟡 |
| D7 | Inventory Concurrency | 3 | ✅ فروش/انتقال عادی |
| D8 | Sale Transaction Boundary | 2 | ✅ |
| D9 | Idempotency | 3 | ✅ عملیات فروش |
| D10 | Time/Date edge cases | 1 | ❌ |
| D11 | UX Loading States | 8 | ❌ |
| D12 | Future-Proofing | Future | خارج |

### ۲.۳ کارهای انجام‌شده (فاز قدیمی)

| کار | Phase جدید | DoD |
|---|:---:|:---:|
| VS Code setup | خارج | ✅ |
| XSS escape | خارج | ✅ |
| MaxLength+Guard | خارج | ✅ |
| حذف DeepSeek | خارج | ✅ |
| admin/admin + FirstRun | 5 | ✅ |
| CheckLegacyAdminPassword | 5 | ✅ |
| Fix CS8602 | خارج | ✅ |
| Fix ShowConfirmDialog | خارج | ✅ |
| Fix const->readonly | خارج | ✅ |
| Watermark->PlaceholderText | خارج | ✅ |
| حذف SaleWindow | خارج | ✅ |
| حذف Models خالی | خارج | ✅ |
| حذف Hosting/Tools | خارج | ✅ |

---

## ۳. Gap Analysis

### 🔴 بحرانی (Phase 1-5)

- ~~تست محاسبات مالی~~ → Phase 1 ✅
- Sale Transaction Boundary → Phase 2 ✅؛ شواهد تاریخی در بخش ۵
- الزام قدیمی Optimistic Concurrency روی Item → هدف هم‌زمانی در Phase 3 با تراکنش SQLite تأمین شد؛ RowVersion اضافه نشده است
- Idempotency در SaveSale → Phase 3 ✅؛ در محدودهٔ OperationId/fingerprint ثبت‌شده
- Migrate() واقعی → Phase 4
- RestoreBackup ایمن → Phase 4
- Backup 3-2-1 + رمزنگاری → Phase 4
- AuditLog → Phase 5

### 🟠 مهم (Phase 6-8)

- Structured Logging + Global Handler → 6
- رفع catch{} خالی → 6
- N+1 در POS و Dashboard → 7
- AsNoTracking + Pagination → 7
- UI Thread violations → 8

### 🟡 پایین (Phase 9-10)

- Unit Tests گسترده → 9
- Integration Tests → 9
- EULA + Privacy + License → 10
- Code Signing → 10
- Updater تست → 10

---

## ۴. قالب Definition of Done

هر Phase فقط با چک‌لیست کامل Done می‌شود:

- [ ] تمام Scope پیاده‌سازی شد
- [ ] Build: 0 Warning, 0 Error
- [ ] Unit tests برای منطق جدید
- [ ] Integration test برای scenario اصلی
- [ ] Failure scenario تست شد
- [ ] Concurrency scenario تست شد (اگر مربوط)
- [ ] Regression: هیچ قابلیت قبلی نشکست
- [ ] مستندسازی در AI-HANDOFF به‌روز
- [ ] مستندسازی در AUDIT-RECONCILIATION به‌روز
- [ ] Commit/tag فقط در صورت دستور صریح کاربر؛ شرط اجرای کار مستندسازی حاضر نیست

**قاعده:** هیچ تیک بدون شاهد قابل اثبات نیست.

---

## ۵. DoD تفصیلی هر Phase

### Phase 1 — Business Correctness ✅ COMPLETE

**Scope:** Sale, Return, Cost, Profit, Discount allocation, Rounding, Stock

**DoD:**
- [x] LockedCostCalculator — ۸ تست Pass
- [x] CashboxCalculator — ۶ تست Pass
- [x] PricingCalculator — ۵ تست Pass
- [x] StockCalculator — ۶ تست Pass
- [x] StockAlertCalculator — ۵ تست Pass
- [x] Discount allocation — Σ=کل تضمین شد
- [x] Rounding AwayFromZero در همه مسیرها
- [x] Unit tests: 30/30 Pass
- [x] مستند: `docs/PHASE-1-BUSINESS-CORRECTNESS.md`

**شواهد:** 30 تست، 0W/0E، 7 باگ رفع شد (B1-B7).

### Pre-Phase — Cleanup

Cleanup یک Pre-Phase است، نه Phase شماره‌دار. حذف S1 (`POSCartItem.HasDiscount`)، S2 (`POSCartItem.DiscountPct`) و S3 (`SaleCartItem`) با شواهد build/test و بازبینی PASS تأیید و کامل شده است.

شواهد: [docs/PRE-PHASE-2-CLEANUP.md](PRE-PHASE-2-CLEANUP.md)

### Phase 2 — Transaction Boundary ✅ COMPLETE

**Scope:** Sale, Payment, Inventory, Cashbox, Cost/Profit, Rollback

**DoD:**
- [x] Sale persistence داخل یک Transaction صریح اجرا می‌شود.
- [x] شکست Persistence پیش از Commit موفق وارد مسیر Rollback می‌شود.
- [x] شکست پس از تغییرات Stageهای قبلی، فروش نیمه‌ثبت‌شده باقی نمی‌گذارد.
- [x] Sale موفق، واحد Persistence را Commit می‌کند.
- [x] Exception در SaveChanges وارد مسیر Persistence Failure / Rollback می‌شود.
- [x] Exception اصلی هنگام شکست هم‌زمان Rollback یا Cleanup حفظ می‌شود.
- [x] خطاهای ثانویه Rollback/Cleanup برای Logging حفظ می‌شوند.
- [x] DataChanged برای Transaction صریح فروش فقط پس از Commit موفق منتشر می‌شود.
- [x] خطاهای Cleanup / Notification / UI پس از Commit به‌عنوان Persistence Failure طبقه‌بندی نمی‌شوند.
- [x] جریان Sale و Commit Boundary با Stageهای شماره‌دار مستند شده است.

**Clarification:** عبارت قدیمی «شکست هر Stage → صفر تغییر DB» فقط درباره مسیر Persistence پیش از Commit موفق صدق می‌کند. پس از Commit موفق، خطاهای Post-Commit نباید فروش ثبت‌شده را Rollback کنند.

**شواهد:** [PHASE-2-TRANSACTION-BOUNDARY.md](PHASE-2-TRANSACTION-BOUNDARY.md) — Build: 0W/0E؛ Tests: 57 Passed / 0 Failed / 0 Skipped؛ Production Review: PASS؛ Test Review: PASS.

### Phase 3 — Concurrency + Idempotency ✅ COMPLETE

**Scope تکمیل‌شده:** جلوگیری از ورود مجدد پرداخت، idempotency فروش، بازیابی برخورد شمارهٔ فاکتور، کنترل مجموع ردیف‌های هم‌کالا و رقابت برداشت موجودی در فروش/انتقال عادی.

| بخش | Commit | نتیجه |
|---|---|---|
| 3A | `45c6511` | `_isPaymentInProgress` و غیرفعال‌کردن دکمه در `OnPayClick`؛ آزادسازی در finally |
| 3B-1 | `c3268af` | schema و اعتبارسنجی `SaleOperations` روی دیتابیس تازه و موجود |
| 3B-2 | `5ff9a68` | persistence idempotent، pending lifecycle، invoice recovery و stock aggregation |
| 3C | `da7d5b4` | انتقال اتمی و تست رقابت مستقل فروش/انتقال؛ production فروش تغییر نکرد |

`50a5427` فقط checkpoint میانی 3B-2 بود؛ مبنای closure آن نیست.

**DoD و تطبیق الزامات تاریخی:**

- [x] `SalePersistenceService.Save`: snapshot با fingerprint پایدار؛ OperationId یکسان و payload یکسان، نتیجهٔ ذخیره‌شده را بدون اثر مالی/مشتری تکراری replay می‌کند. payload متفاوت conflict است.
- [x] ثبت SaleOperations و ردیف‌های فروش/تغییرات مشتری در همان تراکنش؛ rollback قطعی و تفکیک خطای پس از commit حفظ شده‌اند.
- [x] `PendingSale`: شکست قطعیِ قابل‌آزادسازی، سبد را قابل‌اصلاح می‌کند؛ نتیجهٔ نامعلوم، شناسه و snapshot را حتی پس از شکست قطعی retry بعدی حفظ می‌کند. نتیجهٔ commit تأییدشده cache می‌شود؛ پس از reset ضروری، UI/چاپ مانع آزادسازی pending نیست.
- [x] برخورد قطعی invoice در درخواست قابل‌آزادسازی: حفظ سبد، آزادسازی درخواست شکست‌خورده و تولید پیشنهاد تازه از `Sales` و `SaleOperations`؛ پرداخت بعدی OperationId تازه دارد. اگر تلاش قبلی نتیجهٔ نامعلوم داشته باشد، هویت pending حفظ می‌شود. proposal رزرو شماره نیست؛ کنترل authoritative داخل persistence است.
- [x] اعتبارسنجی stock فروش، Qty ردیف‌های دارای ItemId یکسان را جمع می‌کند؛ ترتیب ردیف‌ها و fingerprint تغییر نمی‌کند.
- [x] بررسی constraint با کد SQLite و رکورد ذخیره‌شده انجام می‌شود؛ correctness به متن دقیق exception وابسته نیست.
- [x] `TransferPersistenceService.Save`: آغاز تراکنش پیش از خواندن کالا/خرید/انتقال، بررسی موجودی و INSERT/SaveChanges/Commit در همان تراکنش؛ اعلان `DataChanged` یک‌بار پس از commit تأییدشده. شکست قطعی rollback می‌شود و فرم پاک نمی‌شود؛ خطای UI/cleanup/notification پس از commit شکست ثبت نیست.
- [x] رقابت دو فروش با OperationId و invoice **متفاوت** روی stock=1 و رقابت دو انتقال مستقل با warehouse=1، هر کدام فقط یک برنده دارند.

**تصمیم RowVersion / S5:** الزام اولیهٔ «Item دارای RowVersion یا معادل» در scope مصوب 3C با حفاظت تراکنشی جایگزین شد. در provider فعلی Microsoft.Data.Sqlite/EF Core `10.0.12`، مسیر `BeginTransaction()` پیش‌فرض به تراکنش writer غیر deferred (`BEGIN IMMEDIATE`) می‌رسد. هر دو سرویس قبل از خواندن authoritative این تراکنش را می‌گیرند؛ writer دوم پس از آزادشدن تراکنش اول موجودی تازه را می‌خواند، یا هنگام گرفتن قفل شکست می‌خورد. تست‌های رقابت فعلی موفقیت این طراحی را اثبات کرده‌اند؛ **RowVersion، optimistic token، قفل سراسری برنامه یا تغییر schema در 3C پیاده نشده‌اند.** تغییر provider/semantics نیازمند اثبات مجدد است.

**تصمیم F5 / AR-5:** کلید اصلی `SaleOperations.OperationId` و index یکتای `SaleOperations.InvoiceNumber` هویت عملیات/فاکتور را محافظت می‌کنند. `Sales.InvoiceNumber` به‌تنهایی unique نیست؛ چند ردیف یک فاکتور مجاز است. آماده‌سازی schema، ساختار غیرcanonical را رد می‌کند و داده‌های تاریخی را backfill یا بازنویسی نمی‌کند. کنترل برخورد با فاکتورهای تاریخی در سرویس فروش انجام می‌شود. یکتایی سراسری همهٔ داده‌های legacy و جایگزینی `EnsureCreated` با migrations ادعا نشده‌اند.

**نگاشت شواهد به تست‌های موجود در HEAD:**

| فایل تست | تست‌های مرتبط و آنچه اثبات می‌کنند |
|---|---|
| [SaleOperationSchemaTests.cs](../ShopManager.Domain.Tests/Integration/SaleOperationSchemaTests.cs) | `SqliteEnforcesRequiredIdentityConstraints`، `PreparationIsRepeatableAndPreservesOperations`، `HistoricalMultilineSalesNeedNoBackfillOrSchemaChanges`، تست‌های `Noncanonical*` و `TwoIndependentConnectionsPrepareTheSameDatabase`: قیود هویت، آماده‌سازی تکرارپذیر/هم‌زمان و حفظ تاریخچه |
| [SaleRequestTests.cs](../ShopManager.Domain.Tests/Integration/SaleRequestTests.cs) | `EveryMeaningfulInputAndLineOrderAffectsFingerprint`، `FingerprintIsCultureIndependentAndNormalizesEquivalentInput`، `SnapshotDefensivelyCopiesLines`: snapshot/fingerprint؛ `UncertainAttemptCannotBeDiscardedAfterDefinitiveRetryFailure` و `CompletedSaleReleasesPendingBeforeFallibleUi`: lifecycle |
| [SalePersistenceTests.cs](../ShopManager.Domain.Tests/Integration/SalePersistenceTests.cs) | `ReplayPreservesMultilineSaleCustomerAndNotifications`، `ReplayDoesNotRevalidateConsumedStockOrSaveChanges`، `ChangedRequestIsConflictBeforeAnyMutation` و `SameOperationOnIndependentConnectionsCreatesOnlyOneSale`: replay/conflict و عدم اثر تکراری |
| همان فایل | `ActualCommitThenExceptionResolvesFromFreshContextAndRetryIsReplay`، `VerificationReadFailurePreservesOriginalErrorAndLaterRetryIsSafe`، `ConstraintResolutionUsesStoredIdentityWithoutExceptionMessage`: commit نامعلوم و بررسی رکورد واقعی |
| همان فایل | `InvoiceCollisionReleasesPendingPreservesCartAndNextPaymentPersistsExactlyOnce`: شناسه و شمارهٔ تازه از مولد production، حفظ سبد و دقیقاً یک ثبت؛ `DuplicateItemLinesUseCombinedStockWithoutMergingPersistedLines`: کنترل مجموع موجودی |
| همان فایل | `DifferentOperationsCompetingForLastUnitPersistOnlyTheWinner`: دو اتصال فایل‌محور مستقل، شناسه/فاکتور متفاوت، stock=1؛ صفرشدن stock و نبود اثر مالی/مشتری/SaleOperation بازنده؛ پیش از تغییر production انتقال اجرا و موفق شد |
| [TransferPersistenceTests.cs](../ShopManager.Domain.Tests/Integration/TransferPersistenceTests.cs) | `IndependentTransfersCompetingForLastUnitPersistOnlyTheWinner`: warehouse=1، دو انتقال 1، دقیقاً یک ثبت، warehouse=0 و shop=1 |
| همان فایل | `FailureAfterRealInsertRollsBackCompletelyAndDoesNotClearForm`: INSERT واقعی سپس شکست، rollback کامل و حفظ فرم، شامل شکست rollback صریح؛ `SuccessPreservesFieldsAndNotifiesOnceAfterVisibleCommit`: فیلدها و اعلان پس از commit قابل‌مشاهده از اتصال مستقل |
| همان فایل | `PostCommitFailureDoesNotBecomePersistenceFailure` و `CommitExceptionReportsUncertaintyWithoutRetry`: جداسازی UI/cleanup/notification و گزارش commit نامعلوم بدون retry خودکار |

**ثبت verification در پایان Phase 3C:**

| بررسی | نتیجه | منشأ شاهد |
|---|---|---|
| `dotnet build ShopManager.slnx --no-restore` | 0 errors / 0 warnings | خروجی اجرای Phase 3C ثبت‌شده در همین گفتگو؛ commit نهایی `da7d5b4` |
| `dotnet test ShopManager.slnx --no-build --no-restore` | 134/134 passed؛ 0 failed / 0 skipped | خروجی اجرای کامل Phase 3C؛ شامل 10 مورد جدید نسبت به 124 تست پایان 3B-2 |
| `git diff --check` | clean | بررسی پایان پیاده‌سازی Phase 3C؛ هشدار تبدیل LF/CRLF دو فایل جدید در بررسی جداگانه، خطای whitespace نبود |
| Final adversarial review | PASS | تأیید صریح کاربر در درخواست Phase 3 Documentation Closure؛ فایل گزارش مستقل در مخزن این بررسی یافت نشد |

در این کار documentation-only، build/test یا بازبینی adversarial دوباره اجرا نشده‌اند. تطبیق read-only کد و نام تست‌ها با HEAD و self-review مستندات انجام شد؛ ادعای screenshot دیتابیس یا تست دستی UI/چاپ نداریم.

**حدود closure / ریسک‌های عمداً باز:**

- pending فروش در حافظه است؛ بازیابی خودکار آن پس از restart یا تضمین عمومی exactly-once پس از crash جزو شواهد Phase 3 نیست.
- انتقال رکورد idempotency ندارد؛ exception هنگام commit با `TransferCommitUncertainException` گزارش می‌شود و UI درخواست بررسی سابقه می‌کند. ثبت مجدد خودکار انجام نمی‌شود؛ recovery عمومی انتقال پیاده نشده است.
- تست رقابت دو اتصال مستقل، تأیید محصول multi-terminal/multi-store یا ایمنی همهٔ writerهای خارجی و تمام مسیرهای ویرایش موجودی نیست.
- ماتریس مستقل WAL/DELETE/timeout، قطع برق، UI تعاملی و چاپ فیزیکی در این closure اجرا نشده‌اند. تست‌های خطا تزریقی‌اند.
- `EnsureCreated`، آماده‌سازی legacy و index ترکیبی legacy روی Sales همچنان وجود دارند؛ schema drift، migrations، crash recovery و backup/restore امن به Phase 4 تعلق دارند. ساخت SaleOperations در 3B-1 به معنی بسته‌شدن F2/S8 نیست.
- Reversal implementation و ناسازگاری علامت آن همچنان Future Backlog هستند.

### Phase 4 — Crash Recovery + Backup

**Scope:** Migrate(), WAL, Backup verification, Encryption, External backup, Crash

**DoD:**
- [ ] EnsureCreated → Migrate()
- [ ] DB از صفر → همه Migrations اجرا
- [ ] Backup: کپی + Integrity Check + Restore
- [ ] Backup رمزنگاری (DPAPI/AES)
- [ ] Backup ثانویه روی USB
- [ ] تست: kill وسط SaveSale → DB سالم
- [ ] تست: Restore از backup → همه داده
- [ ] تست: DB خراب → از backup بازیابی

**شواهد:** سناریوی Crash + Recovery کامل.

### Phase 5 — Audit + Security

**Scope:** AuditLog, Authorization, Sensitive operations, Password policy

**DoD:**
- [ ] جدول AuditLog (UserId, Timestamp, Action, EntityType, EntityId, OldValue, NewValue)
- [ ] ثبت تغییر قیمت Item
- [ ] ثبت تغییر موجودی
- [ ] ثبت حذف (Soft Delete)
- [ ] ثبت تغییر کاربر/رمز
- [ ] هیچ رمز hardcode
- [ ] Password policy ≥ ۸ کاراکتر + حرف + عدد
- [ ] Rate limit (۵ تلاش → قفل)
- [ ] تست: تغییر قیمت → رکورد در AuditLog

**شواهد:** Query روی AuditLog بعد از عملیات نمونه.

### Phase 6 — Logging + Global Error Handling

**Scope:** Structured Logging, Global Handler, Rolling File

**DoD:**
- [ ] Global AppDomain.UnhandledException handler
- [ ] Global TaskScheduler.UnobservedTaskException handler
- [ ] Log به فایل با rolling روزانه
- [ ] فرمت: Timestamp | Level | User | Operation | Exception | StackTrace
- [ ] حذف catch{} خالی (همه → ErrorHandler.LogError)
- [ ] مسیر: G:\ShopManager-Data\logs\
- [ ] تست: Exception دستی → فایل log

**شواهد:** محتوای فایل log.

### Phase 7 — EF Core + Performance

**Scope:** N+1, AsNoTracking, DbContext, Index, Pagination

**DoD:**
- [ ] CreateProductTile → ۳ کوئری (نه ۹۰)
- [ ] Historyها → Pagination (100 ردیف)
- [ ] کوئری‌های read-only → AsNoTracking
- [ ] DbContext کوتاه‌عمر
- [ ] Index روی (DateGregorian, PaymentStatus)
- [ ] تست: POS ۳۰ کالا → < ۱ ثانیه
- [ ] تست: History ۱۰K ردیف → < ۵۰۰ms

**شواهد:** زمان‌سنجی + profiler.

### Phase 8 — Avalonia Reliability

**Scope:** UI Thread, Async, Event Leak, Timer Lifecycle

**DoD:**
- [ ] همه I/O روی background thread
- [ ] هیچ async void خارج از event handler
- [ ] همه Event Subscriptions → Unsubscribe در dispose
- [ ] همه Timers → Stop در close
- [ ] تست: ۸ ساعت باز → صفر leak
- [ ] تست: ۵۰ بار باز/بسته → صفر leak

**شواهد:** Task Manager قبل/بعد.

### Phase 9 — Tests

**Scope:** Unit, Integration, Regression

**DoD:**
- [ ] Coverage Domain.Services ≥ ۸۰٪
- [ ] Coverage Domain.Entities ≥ ۵۰٪
- [ ] Integration Test هر Service
- [ ] Regression فازهای ۱-۸
- [ ] dotnet test → ۰ fail

**شواهد:** Coverage report + خروجی.

### Phase 10 — Release Hardening

**Scope:** Updater, Signing, Licenses, EULA, Privacy

**DoD:**
- [ ] EULA.md
- [ ] PRIVACY.md
- [ ] LICENSES.md
- [ ] Updater تست (n → n+1)
- [ ] Code Signing (اگر گواهی)
- [ ] README کامل
- [ ] نصب روی ویندوز تمیز

**شواهد:** Screenshot نصب + signtool verify.

---

## ۶. Workflow هر Phase
AUDIT → وضعیت فعلی (CONFIRMED/POSSIBLE/NOT FOUND)
↓
SCOPE → داخل/خارج
↓
DoD → چک‌لیست قابل اثبات
↓
IMPL → پیاده‌سازی
↓
TEST → تست + شواهد
↓
REVIEW → مطابقت با DoD
↓
COMMIT → فقط با دستور صریح کاربر؛ tag نیز نیازمند دستور است

text

**اگر هر مرحله fail → برگرد به قبل. هیچ Phase نیمه‌کاره commit نمی‌شود.**

---

## ۷. وضعیت فعلی

| مورد | مقدار |
|---|---|
| فازهای کامل (Framework جدید) | 3 (Phase 1, Phase 2, Phase 3 تا 3C) |
| Unique Index / F5 | بسته‌شده در سطح SaleOperations؛ محدودیت legacy در بخش ۵ |
| فاز بعدی | Phase 4 — Crash Recovery + Backup؛ شروع نشده |
| فازهای حذف‌شده از Roadmap | Alert، Cloud، Multi-terminal |
| کارهای قدیمی ثبت‌شده در بخش ۲.۳ | ۱۳؛ این عدد شمارندهٔ کل تغییرات جدید نیست |
| آخرین مجموعهٔ تست تأییدشده | 134 Passed / 0 Failed / 0 Skipped |
| Build | 0W / 0E |
| Git | Phase 3 implementation at `da7d5b4`؛ ویرایش مستندات حاضر بدون commit/push |

**Phase 3 — Concurrency + Idempotency تا 3C کامل و تأیید شده است. نقطه شروع بعدی طبق roadmap موجود: Phase 4 — Crash Recovery + Backup؛ ابتدا AUDIT و تعیین scope.**

---

## ۸. Decision Log

| تاریخ | تصمیم | دلیل |
|---|---|---|
| Closure در مبنای `da7d5b4` | Phase 3 تا 3C COMPLETE | build 0W/0E، 134/134 تست، diff check و تأیید final adversarial review؛ منشأ شواهد در بخش ۵ |
| Closure در مبنای `da7d5b4` | جایگزینی الزام RowVersion با طراحی تراکنشی SQLite | خواندن authoritative پس از آغاز تراکنش writer و تست رقابت مستقل؛ token پیاده نشده |
| Closure در مبنای `da7d5b4` | تفکیک هویت عملیات از ردیف‌های فاکتور | uniqueness در SaleOperations؛ حفظ فاکتور چندردیفی و تاریخچه؛ بدون ادعای اصلاح عمومی legacy schema |
| 1405/07/08 | Phase 2 — Transaction Boundary COMPLETE | DoD، Build/Test، Production Review و Test Review تأیید شدند |
| 1405/07/08 | تفکیک Cleanup به Pre-Phase (نه Phase شماره‌دار) | جلوگیری از تداخل شماره‌گذاری با Transaction Boundary |
| 1405/07/07 | Framework جدید (۱۲ بُعد + Audit + Reconciliation + Roadmap) | جلوگیری از Scope Creep |
| 1405/07/07 | Alert System حذف | معماری زود است |
| 1405/07/07 | Logging در Phase 6 (نه 2.5) | Transaction خودش تست‌پذیر |
| 1405/07/07 | EULA/Privacy در Phase 10 | Release فقط |
| 1405/07/07 | Tax/Legal = Business Requirement | Scope question |
| 1405/07/07 | DoD اجباری | جلوگیری از پیشرفت کاذب |
| 1405/07/07 | Phase 1 COMPLETE | DoD 100% پاس شد |
| 1405/07/07 | FluentAssertions استفاده نشود | لایسنس تجاری Xceed |
| 1405/07/07 | Reversal خارج از Scope | فیچر پیاده نشده، Backlog |
| 1405/07/07 | Customer TotalPurchasedAmount بدون تغییر | خط 1109 خالص بود |

---

## ۹. Future Backlog

### Reversal Sign Contradiction

- `Purchase.cs` comment: "برای برگشت، منفی"
- `StockCalculator.cs:23,29,48,53` formula: assumes positive
- Risk: two-negatives bug if Reversal implemented with negative Qty
- Decision needed before implementing Reversal feature
- Refs: Phase 1 Audit (N1), Phase 1 Step 3 (test evidence)

### کد مرده (Pre-Phase — Cleanup)

- [x] S1 / S2: `POSCartItem.HasDiscount` / `DiscountPct` — حذف تأییدشده
- [x] S3: `SaleCartItem` — حذف تأییدشده

شواهد: [docs/PRE-PHASE-2-CLEANUP.md](PRE-PHASE-2-CLEANUP.md)

---

**پایان سند. Phase 3 بسته شد؛ نقطه شروع برنامه‌ریزی‌شده: Phase 4 — Crash Recovery + Backup.**
