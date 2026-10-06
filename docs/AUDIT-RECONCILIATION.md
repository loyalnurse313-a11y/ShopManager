# AUDIT-RECONCILIATION — تطبیق ممیزی با Roadmap جدید

> **هدف:** پل بین ممیزی، وضعیت فعلی، و Roadmap جدید (۱۰ Phase).
> **اصل حاکم:** هیچ Phase بدون DoD اثبات‌شده Done نیست.
> **آخرین به‌روزرسانی:** بستن مستندات Phase 3 در `905622c` (3C در `da7d5b4`)؛ Phase 4 remains **IN PROGRESS / NOT restore-safe**. Approved critical path: **F1 → F2 → F3** (F1 IMPLEMENTED / VERIFIED, uncommitted; F2/F3 OPEN). Latest hardening: **5C-2.2 — IMPLEMENTED / TESTED / INDEPENDENTLY REVIEWED / COMMITTED / PUSHED `72d77613340f2d9b88abd64ec39bcbe351156608`** (push confirmed by the user). 5C-2.1/2.2 and their historical evidence remain valid; general per-window enrollment is deferred. checkpointهای پیشین: `583a7b8` (4B-3)، `d472153` (4B-2B)، `59d0dfc` (4B-2A).
> **Latest production checkpoint:** 4B-5C-2.2, **COMMITTED / PUSHED `72d77613340f2d9b88abd64ec39bcbe351156608`** (push confirmed by the user; no push performed in this docs-only update). 5C-2.1 remains valid in `a8e3d02`; historical evidence is retained.
> **شواهد Phase 3C:** 134 Pass / 0 Fail / 0 Skip؛ build: 0 warnings / 0 errors؛ final adversarial review: PASS.
> **وضعیت فعلی:** Phase 1–3 کامل‌اند. Phase 4 remains **IN PROGRESS / NOT restore-safe**. Approved critical path: **F1 → F2 → F3** (F1 IMPLEMENTED / VERIFIED, uncommitted; F2/F3 OPEN). Latest hardening: **5C-2.2 — IMPLEMENTED / TESTED / INDEPENDENTLY REVIEWED / COMMITTED / PUSHED `72d77613340f2d9b88abd64ec39bcbe351156608`** (push confirmed by the user). 5C-2.1/2.2 and their historical evidence remain valid; general per-window enrollment is deferred.
> نتایج Phase 1/2 در بخش‌های خود، شواهد تاریخی‌اند. وضعیت فعلی این سند جایگزین نتیجه‌گیری‌های زمانی قدیمی می‌شود؛ متن `AUDIT-REPORT.md` و اسناد closure قبلی بازنویسی نشده است.

---

## ۱. Roadmap قطعی (۱۰ Phase)

|  #  | Phase                   | Scope                                                      | DoD کوتاه                                                         |
| :-: | ----------------------- | ---------------------------------------------------------- | ----------------------------------------------------------------- |
|  1  | Business Correctness    | Sale, Return, Cost, Profit, Discount, Rounding, Stock      | تست عددی                                                          |
|  2  | Transaction Boundary    | Sale+Payment+Inventory+Cashbox+Rollback                    | Pre-Commit failure → Rollback؛ Post-Commit failure → حفظ Sale     |
|  3  | Concurrency+Idempotency | Payment guard، SaleOperations، Stock Race با تراکنش SQLite | درخواست یکسان → replay؛ دو برداشت مستقل از stock=1 → فقط یک برنده |
|  4  | Crash Recovery+Backup | F1 boundary, F2 compatibility/broken-DB recovery, F3 verification | Genuinely safe restore; evidence required |
|  5  | Audit+Security          | AuditLog, Authorization, Password                          | هر تغییر حساس → رکورد                                             |
|  6  | Logging+Global Error    | Structured Log, Global Handler, Rolling                    | خطا → فایل log                                                    |
|  7  | EF Core+Performance     | N+1, AsNoTracking, Index, Pagination                       | 100K → سریع                                                       |
|  8  | Avalonia Reliability    | UI Thread, Async, Event Leak, Timer                        | 8 ساعت → بدون leak                                                |
|  9  | Tests                   | Unit, Integration, Regression                              | Coverage مشخص                                                     |
| 10  | Release Hardening       | Updater, Signing, Licenses, EULA                           | نصب تمیز                                                          |

**Scope Creep (خارج):** Cloud Sync، Multi-terminal، Multi-store، Mobile، Plugin، Telemetry، Feature Flags، API

---

## ۲. Reconciliation — یافته‌های ممیزی

### ۲.۱ جدول Master

|  #   | یافته                             | Phase |        وضعیت        | DoD |
| :--: | --------------------------------- | :---: | :-----------------: | :-: | ----------- |
|  F1  | نبود Transaction دور SaveSale     |   2   |         ✅          | ✅  |
|  F2  | EnsureCreated به جای Migrate      | Deferred | OPEN — approved scope reset | Deferred |
|  F3  | XSS در ۴ پنجره                    | خارج  |         ✅          | ✅  |
|  F4  | RestoreBackup ناایمن              |   4   |         ❌          | ❌  |
|  F5  | Unique Index InvoiceNumber        |   3   |  ✅ SaleOperations  | ✅  |
|  F6  | N+1 در CreateProductTile          |   7   |         ❌          | ❌  |
|  F7  | Race بررسی موجودی                 |   3   | ✅ فروش/انتقال عادی | ✅  |
|  F8  | Code Signing                      |  10   |         ❌          | ❌  |
|  F9  | API key plaintext                 | خارج  |         ✅          | ✅  |
| F10  | admin/admin                       |   5   |         ✅          | ✅  |
| F11  | SaleWindow کد مرده                | خارج  |         ✅          | ✅  |
| F12  | catch{} خالی                      |   6   |         ❌          | ❌  |
| P1-1 | PricingCalculator SalePrice=0     |   1   |         ✅          | ✅  | رفع شد (B1) |
| P1-2 | PricingCalculator ToEven rounding |   1   |         ✅          | ✅  | رفع شد (B2) |
| P1-3 | POSWindow Revenue خام             |   1   |         ✅          | ✅  | رفع شد (B3) |
| P1-4 | POSWindow Profit invariant نقض    |   1   |         ✅          | ✅  | رفع شد (B4) |
| P1-5 | Sale.DiscountAmount غایب          |   1   |         ✅          | ✅  | رفع شد (B5) |

### ۲.۲ نقاط کور ۱۲ بُعدی

|  #  | بُعد                      |    Phase     |        وضعیت        |
| :-: | ------------------------- | :----------: | :-----------------: |
| D1  | Backup 3-2-1              | Deferred | OPEN — approved scope reset |
| D2  | Structured Logging        |      6       |         ❌          |
| D3  | Alert System              |    Future    |        خارج         |
| D4  | Audit Trail               |      5       |         ❌          |
| D5  | EULA/Privacy              |      10      |         ❌          |
| D6  | Tax/Legal                 | Business Req |         🟡          |
| D7  | Inventory Concurrency     |      3       | ✅ فروش/انتقال عادی |
| D8  | Sale Transaction Boundary |      2       |         ✅          |
| D9  | Idempotency               |      3       |   ✅ عملیات فروش    |
| D10 | Time/Date edge cases      |      1       |         ❌          |
| D11 | UX Loading States         |      8       |         ❌          |
| D12 | Future-Proofing           |    Future    |        خارج         |

### ۲.۳ کارهای انجام‌شده (فاز قدیمی)

| کار                        | Phase جدید | DoD |
| -------------------------- | :--------: | :-: |
| VS Code setup              |    خارج    | ✅  |
| XSS escape                 |    خارج    | ✅  |
| MaxLength+Guard            |    خارج    | ✅  |
| حذف DeepSeek               |    خارج    | ✅  |
| admin/admin + FirstRun     |     5      | ✅  |
| CheckLegacyAdminPassword   |     5      | ✅  |
| Fix CS8602                 |    خارج    | ✅  |
| Fix ShowConfirmDialog      |    خارج    | ✅  |
| Fix const->readonly        |    خارج    | ✅  |
| Watermark->PlaceholderText |    خارج    | ✅  |
| حذف SaleWindow             |    خارج    | ✅  |
| حذف Models خالی            |    خارج    | ✅  |
| حذف Hosting/Tools          |    خارج    | ✅  |

---

## ۳. Gap Analysis

### 🔴 بحرانی (Phase 1-5)

- ~~تست محاسبات مالی~~ → Phase 1 ✅
- Sale Transaction Boundary → Phase 2 ✅؛ شواهد تاریخی در بخش ۵
- الزام قدیمی Optimistic Concurrency روی Item → هدف هم‌زمانی در Phase 3 با تراکنش SQLite تأمین شد؛ RowVersion اضافه نشده است
- Idempotency در SaveSale → Phase 3 ✅؛ در محدودهٔ OperationId/fingerprint ثبت‌شده
- Real migrations (AR-2/S8) → deferred backlog after approved Phase-4 scope reset; not completed.
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

| بخش  | Commit    | نتیجه                                                                           |
| ---- | --------- | ------------------------------------------------------------------------------- |
| 3A   | `45c6511` | `_isPaymentInProgress` و غیرفعال‌کردن دکمه در `OnPayClick`؛ آزادسازی در finally |
| 3B-1 | `c3268af` | schema و اعتبارسنجی `SaleOperations` روی دیتابیس تازه و موجود                   |
| 3B-2 | `5ff9a68` | persistence idempotent، pending lifecycle، invoice recovery و stock aggregation |
| 3C   | `da7d5b4` | انتقال اتمی و تست رقابت مستقل فروش/انتقال؛ production فروش تغییر نکرد           |

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

| فایل تست                                                                                           | تست‌های مرتبط و آنچه اثبات می‌کنند                                                                                                                                                                                                                                                                      |
| -------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| [SaleOperationSchemaTests.cs](../ShopManager.Domain.Tests/Integration/SaleOperationSchemaTests.cs) | `SqliteEnforcesRequiredIdentityConstraints`، `PreparationIsRepeatableAndPreservesOperations`، `HistoricalMultilineSalesNeedNoBackfillOrSchemaChanges`، تست‌های `Noncanonical*` و `TwoIndependentConnectionsPrepareTheSameDatabase`: قیود هویت، آماده‌سازی تکرارپذیر/هم‌زمان و حفظ تاریخچه               |
| [SaleRequestTests.cs](../ShopManager.Domain.Tests/Integration/SaleRequestTests.cs)                 | `EveryMeaningfulInputAndLineOrderAffectsFingerprint`، `FingerprintIsCultureIndependentAndNormalizesEquivalentInput`، `SnapshotDefensivelyCopiesLines`: snapshot/fingerprint؛ `UncertainAttemptCannotBeDiscardedAfterDefinitiveRetryFailure` و `CompletedSaleReleasesPendingBeforeFallibleUi`: lifecycle |
| [SalePersistenceTests.cs](../ShopManager.Domain.Tests/Integration/SalePersistenceTests.cs)         | `ReplayPreservesMultilineSaleCustomerAndNotifications`، `ReplayDoesNotRevalidateConsumedStockOrSaveChanges`، `ChangedRequestIsConflictBeforeAnyMutation` و `SameOperationOnIndependentConnectionsCreatesOnlyOneSale`: replay/conflict و عدم اثر تکراری                                                  |
| همان فایل                                                                                          | `ActualCommitThenExceptionResolvesFromFreshContextAndRetryIsReplay`، `VerificationReadFailurePreservesOriginalErrorAndLaterRetryIsSafe`، `ConstraintResolutionUsesStoredIdentityWithoutExceptionMessage`: commit نامعلوم و بررسی رکورد واقعی                                                            |
| همان فایل                                                                                          | `InvoiceCollisionReleasesPendingPreservesCartAndNextPaymentPersistsExactlyOnce`: شناسه و شمارهٔ تازه از مولد production، حفظ سبد و دقیقاً یک ثبت؛ `DuplicateItemLinesUseCombinedStockWithoutMergingPersistedLines`: کنترل مجموع موجودی                                                                  |
| همان فایل                                                                                          | `DifferentOperationsCompetingForLastUnitPersistOnlyTheWinner`: دو اتصال فایل‌محور مستقل، شناسه/فاکتور متفاوت، stock=1؛ صفرشدن stock و نبود اثر مالی/مشتری/SaleOperation بازنده؛ پیش از تغییر production انتقال اجرا و موفق شد                                                                           |
| [TransferPersistenceTests.cs](../ShopManager.Domain.Tests/Integration/TransferPersistenceTests.cs) | `IndependentTransfersCompetingForLastUnitPersistOnlyTheWinner`: warehouse=1، دو انتقال 1، دقیقاً یک ثبت، warehouse=0 و shop=1                                                                                                                                                                           |
| همان فایل                                                                                          | `FailureAfterRealInsertRollsBackCompletelyAndDoesNotClearForm`: INSERT واقعی سپس شکست، rollback کامل و حفظ فرم، شامل شکست rollback صریح؛ `SuccessPreservesFieldsAndNotifiesOnceAfterVisibleCommit`: فیلدها و اعلان پس از commit قابل‌مشاهده از اتصال مستقل                                              |
| همان فایل                                                                                          | `PostCommitFailureDoesNotBecomePersistenceFailure` و `CommitExceptionReportsUncertaintyWithoutRetry`: جداسازی UI/cleanup/notification و گزارش commit نامعلوم بدون retry خودکار                                                                                                                          |

**ثبت verification در پایان Phase 3C:**

| بررسی                                                  | نتیجه                                | منشأ شاهد                                                                                                |
| ------------------------------------------------------ | ------------------------------------ | -------------------------------------------------------------------------------------------------------- |
| `dotnet build ShopManager.slnx --no-restore`           | 0 errors / 0 warnings                | خروجی اجرای Phase 3C ثبت‌شده در همین گفتگو؛ commit نهایی پیاده‌سازی 3C: `da7d5b4`                        |
| `dotnet test ShopManager.slnx --no-build --no-restore` | 134/134 passed؛ 0 failed / 0 skipped | خروجی اجرای کامل Phase 3C؛ شامل 10 مورد جدید نسبت به 124 تست پایان 3B-2                                  |
| `git diff --check`                                     | clean                                | بررسی پایان پیاده‌سازی Phase 3C؛ هشدار تبدیل LF/CRLF دو فایل جدید در بررسی جداگانه، خطای whitespace نبود |
| Final adversarial review                               | PASS                                 | تأیید صریح کاربر در درخواست Phase 3 Documentation Closure؛ فایل گزارش مستقل در مخزن این بررسی یافت نشد   |

در این کار documentation-only، build/test یا بازبینی adversarial دوباره اجرا نشده‌اند. تطبیق read-only کد و نام تست‌ها با HEAD و self-review مستندات انجام شد؛ ادعای screenshot دیتابیس یا تست دستی UI/چاپ نداریم.

**حدود closure / ریسک‌های عمداً باز:**

- pending فروش در حافظه است؛ بازیابی خودکار آن پس از restart یا تضمین عمومی exactly-once پس از crash جزو شواهد Phase 3 نیست.
- انتقال رکورد idempotency ندارد؛ exception هنگام commit با `TransferCommitUncertainException` گزارش می‌شود و UI درخواست بررسی سابقه می‌کند. ثبت مجدد خودکار انجام نمی‌شود؛ recovery عمومی انتقال پیاده نشده است.
- تست رقابت دو اتصال مستقل، تأیید محصول multi-terminal/multi-store یا ایمنی همهٔ writerهای خارجی و تمام مسیرهای ویرایش موجودی نیست.
- ماتریس مستقل WAL/DELETE/timeout، قطع برق، UI تعاملی و چاپ فیزیکی در این closure اجرا نشده‌اند. تست‌های خطا تزریقی‌اند.
- `EnsureCreated`، آماده‌سازی legacy و index ترکیبی legacy روی Sales همچنان وجود دارند؛ crash recovery and safe backup/restore remain in Phase 4; general schema drift/migrations remain OPEN / DEFERRED after the approved scope reset. ساخت SaleOperations در 3B-1 به معنی بسته‌شدن F2/S8 نیست.
- Reversal implementation و ناسازگاری علامت آن همچنان Future Backlog هستند.

### Phase 4 — Crash Recovery + Backup 🔄 IN PROGRESS

**Approved scope:** crash-safe backup and safe terminal restore for the single-process desktop POS, completed through F1 → F2 → F3. Original migration/encryption/secondary-backup items remain OPEN in deferred backlog.

**Checkpoint های انجام‌شده (4A-1 تا 4B-2A: کد + تست در commit `59d0dfc`؛ 4B-2B: `d472153`؛ 4B-3: `583a7b8`؛ 4B-4: `63039d2`؛ 4B-5A: committed `36f0e7f`):**

- [x] 4A-1 (`0944a7e`): انتشار اتمیک بکاپ SQLite — staging، اعتبارسنجی و publish ایمن.
- [x] 4A-2 (`05212da`): هویت canonical دیتابیس و توقف امن startup.
- [x] 4A-3 (`3466604`): قرارداد دوام SQLite (WAL + synchronous=FULL).
- [x] 4A-4 (`22ca6fa`): قابلیت اطمینان چرخهٔ حیات بکاپ (single-flight، generation، پاک‌سازی staging یتیم، لاگ خطا).
- [x] 4B-1 (`461670c`): آماده‌سازی بازیابی امن پیش از تعویض (اعتبارسنجی، اسنپ‌شات ایمنی WAL-سازگار، گارد hard-link)؛ دیتابیس زنده دست‌نخورده.
- [x] 4B-2A (`59d0dfc`): بنیاد intent بازیابی ماندگار (flush → SHA-256 → انتشار اتمیک → مسلح‌سازی) و gate پذیرش دیتابیس.
- [x] 4B-2B (`d472153`): موتور بازیابی آفلاین در سطح فایل (`RestoreRecoveryService.Recover`) — پس از intent ماندگار forward-only (forward-complete یا BLOCK، هرگز rollback)؛ وضعیت منتشرشده با SHA-256 مورد انتظار دیتابیس زنده و نبودن sidecarهای زندهٔ `-wal`/`-shm`/`-journal` تأیید می‌شود؛ فقط tombstone/incoming artifactهای دقیقاً operation-owned (بدون wildcard)؛ cleanup plan-then-execute؛ وضعیت مبهم/ناایمن fail-closed و مسلح؛ حذف intent آخرین mutation موفق؛ نبودن `SafetyBackupPath` مانع forward completion نیست. app-lifetime mutex خارج از 4B-2B بود و اکنون در 4B-3 verify شده است. **هنوز باز:** F1 restore boundary/legacy-path removal, F2 compatibility/broken-DB recovery and F3 verification remain OPEN; general shutdown/lifetime architecture is deferred.
- [x] 4B-3 (`583a7b8`): app-lifetime Windows mutex `Global\ShopManager.ApplicationLifetime`؛ acquisition پس از `Velopack.Run()` و پیش از `BuildAvaloniaApp()`؛ Busy/Error → exit code 2/3 بدون startup admission؛ abandoned ownership پذیرفته، بدون ادعای سلامت DB؛ guard موفق برای عمر process strongly rooted؛ harness واقعی چندprocess بدون production DB/mutex. **Implemented / verified / checkpoint-ready**.
- [x] 4B-4 (`63039d2`): startup recovery integration; implemented / verified / independently reviewed / checkpoint-ready. Evidence and residual verification are recorded below; Phase 4 remains IN PROGRESS.
- [x] 4B-5A (`36f0e7f`): runtime DbContext admission + context drain only; implemented / verified / independently reviewed / committed; NOT restore-safe.
- [x] 4B-5B-1 (`34faeee`; COMMITTED / PUSHED): resolver poisoning fix; implemented / verified / independently reviewed; NOT restore-safe.
- [x] **4B-5B-2**: backup admission/timer boundary; IMPLEMENTED / VERIFIED / INDEPENDENTLY REVIEWED; **COMMITTED `287764c`**, **NOT restore-safe**. Final evidence: 26/26 targeted, 172/172 relevant, 384/384 full; re-review PASS WITH FINDINGS; M1/M2 CLOSED.
- [x] **4B-5C-1 — COMMITTED `33e12b6`**: isolated RuntimeOperationGate primitive; implemented / verified / independently reviewed; NOT runtime-quiescent / NOT restore-safe. Final evidence: targeted 29/29, relevant 118/118, full 413/413 PASS, 0 failed/skipped; build 0 warnings/errors; diff-check PASS; user-confirmed review PASS WITH FINDINGS, M1/M2 re-reviewed CLOSED; M3 stress linearizability remains a test gap.
- [x] **4B-5C-2.1 — IMPLEMENTED / TESTED / INDEPENDENTLY REVIEWED; COMMITTED / PUSHED `a8e3d02`:** production operation ownership/composition and Auth enrollment; historical evidence retained below.
- [x] **4B-5C-2.2 — IMPLEMENTED / TESTED / INDEPENDENTLY REVIEWED; COMMITTED / PUSHED `72d7761`:** SessionTracker tick enrollment, generation/single-flight safety and conditional parent-bound Auth logout; Stop is not completion proof; no nested Auth admission; cleanup uncertainty remains fail-closed. New 47/47, Auth 52/52, relevant 148/148, full 512/512 and independent targeted review run 99/99 PASS; build 0 warnings/errors; independent review PASS with no Critical/High/Medium/Low findings (user-confirmed); diff-check PASS.
- [ ] **F1 → F2 → F3:** approved critical path (F1 IMPLEMENTED / VERIFIED, uncommitted; F2/F3 OPEN); general per-window enrollment is deferred to reliability backlog.

**Approved Phase-4 DoD — F1 implemented/verified; F2 and F3 remain OPEN:**

- [x] **F1 — Final Restore Boundary — F1 IMPLEMENTED / VERIFIED (uncommitted working tree); final independent review PASS WITH FINDINGS (Low only; Critical/High/Medium 0; no blockers); targeted 31 passed, relevant 307 passed, full 547 passed (0 failed / 0 skipped), non-incremental build 0 warnings / 0 errors, git diff --check PASS. No live DB swap occurs in the current process; the actual swap remains startup recovery work; the terminal restore boundary is fail-closed. B1/B2 review findings were fixed and re-reviewed; remaining Low findings are accepted residual risks. F2 and F3 remain OPEN; Phase 4 remains IN PROGRESS / NOT closed.** Scope: replace legacy SettingsWindow restore; terminal single-flight; block relevant new work; stop relevant producers; drain existing runtime/DB/backup work; ordered preparation, pool release, persistent intent and restore-specific exit; startup recovery performs the swap.
- [ ] **F2 — Backup Compatibility + Broken-DB Recovery:** prove application compatibility; bounded recovery when the current DB is broken; preserve damaged/original files fail-closed.
- [ ] **F3 — Final Verification & Closure:** integration/end-to-end restore, real process-crash/kill where required, UI smoke, full tests, build **0 warnings / 0 errors**, independent review and final documentation reconciliation.

The user-approved scope reset is intentional, not abandonment of restore safety. No current evidence proves per-window enrollment mandatory. General enrollment/lifetime architecture moves to deferred reliability backlog. AR-2/S8 (migrations/schema drift) and D1 (encryption/secondary backup/3-2-1) remain OPEN but are deferred from this closure; F2 compatibility is still mandatory. Completed 5C-2.1/2.2 remain valid hardening. See [approved F1 → F2 → F3 plan](PHASE-4-CRASH-RECOVERY-BACKUP.md#approved-completion-plan) and [deferred backlog](MASTER-BACKLOG.md#deferred-work-from-phase-4-scope-reset). This docs-only approval does not authorize source implementation.

**شواهد تاریخی Phase 4:** شاهد commit `59d0dfc`: build 0W/0E و 246/246 تست در اجرای کامل. شاهد verify‌شده برای 4B-2B (commit `d472153`؛ 270 تست در آن source tree commit‌شده): `RestoreRecoveryServiceTests` 42 passed؛ کل `ShopManager.Domain.Tests` 270 passed؛ build غیرافزایشی solution با 0W/0E؛ adversarial/final review: PASS بدون issue مسدودکنندهٔ Critical/High/Medium. مسیر بازگردانی قدیمی `BackupService.RestoreBackup` هنوز از `SettingsWindow.axaml.cs:290` با `Environment.Exit(0)` فراخوانی می‌شود. **شاهد سناریوی کامل Crash + Recovery هنوز تولید نشده است.** جزئیات: [PHASE-4-CRASH-RECOVERY-BACKUP.md](PHASE-4-CRASH-RECOVERY-BACKUP.md).

**شواهد تاریخی 4B-3 پس از test hardening، پیش از commit `583a7b8`:** `ApplicationInstanceGuardTests` 21 passed؛ کل `ShopManager.Domain.Tests` 291 passed (هر دو 0 failed / 0 skipped)؛ build غیرافزایشی 0 warnings / 0 errors؛ `git diff --check` exit code 0؛ adversarial review طبق تأیید کاربر: **PASS WITH FINDINGS، بدون Critical یا High**؛ follow-up تست بدون تغییر production تکمیل شد. نتایج متعلق به اجرای پیاده‌سازی/تست‌اند؛ در documentation sync build/test دوباره اجرا نشده است.

**4B-4 — implemented / verified / independently reviewed / checkpoint-ready؛ committed `63039d2`:** recovery پیش از resolver/SQLite؛ ادامه فقط برای `NoIntent` یا `Completed` در حالت unarmed. `Blocked` و خطاهای غیرمنتظره بدون resolver/context/settings/theme/backup/timer/auth/session/normal window/normal shutdown registration fail-closed هستند. registered identity روی همان intent مصرف‌شده، پس از arming و پیش از mutation، بدون DatabaseService/SQLite بررسی می‌شود؛ live مفقود پس از tombstone پیش از resolver قابل بازیابی است. invariantهای forward-complete فاز 4B-2B و رفتار guard فاز 4B-3 حفظ شده‌اند.

**شواهد 4B-4:** `StartupRecoveryIntegrationTests` **29 passed**؛ `RestoreRecoveryServiceTests` **44 passed**؛ `ApplicationInstanceGuardTests` **21 passed**؛ full `ShopManager.Domain.Tests` **322 passed**؛ همگی 0 failed / 0 skipped؛ build غیرافزایشی **0 warnings / 0 errors**؛ diff-check **exit code 0**؛ independent adversarial review طبق تأیید کاربر: **PASS WITH FINDINGS، بدون Critical یا High و بدون checkpoint-blocking finding**. نتایج implementation/test و review تکمیل‌شده ثبت شده‌اند؛ در documentation sync دوباره اجرا نشده‌اند.

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
- Historical sequence recorded through 4B-5C-1: **4B-5B-1 — resolver poisoning fix (`34faeee`; COMMITTED / PUSHED)**; **4B-5B-2 — backup admission/timer boundary (IMPLEMENTED / VERIFIED / INDEPENDENTLY REVIEWED / COMMITTED `287764c`)**; **4B-5C-1 — isolated operation-lifetime primitive (IMPLEMENTED / VERIFIED / INDEPENDENTLY REVIEWED — COMMITTED `33e12b6`)**; **4B-5C-2 — production operation enrollment and producer retirement (next planned)**. Production `PrepareRestore` / `Arm` remains unavailable until the complete required quiesce boundary exists. Restore UI remains future work.

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

### 4B-5C-1 — isolated operation-lifetime primitive

**IMPLEMENTED / VERIFIED / INDEPENDENTLY REVIEWED — COMMITTED `33e12b6`. NOT runtime-quiescent / NOT restore-safe.** Phase 4 remains **IN PROGRESS**.

Checkpoint commit: `33e12b6e52e01a307e7ab474f26945447116db46` — `feat: add runtime operation admission gate`. This post-commit reconciliation records COMMITTED status only; it does not claim a push or new test/build/review execution.

- Production: `ShopManager.Desktop/Services/RuntimeOperationGate.cs`; tests: `ShopManager.Domain.Tests/Integration/RuntimeOperationAdmissionTests.cs`.
- States `Open` / `Closed` / `FaultedClosed`; operation-lifetime accounting, Interactive Busy, closure ownership, async drain, controlled reopen and fail-closed unproven completion.
- No production callers/integration, DB capability, `AsyncLocal`, singleton or production cutoff. No runtime quiescence or restore safety; no automatic self-drain detection.
- Independent review supplied by the user: **PASS WITH FINDINGS**; **M1 and M2 fixed and re-reviewed CLOSED**. M1 now proves stale/foreign identity rejection with zero outstanding; M2 covers Dispose versus the first unproven report, with both controlled orderings and a concurrent start, rejecting FaultedClosed plus zero outstanding.
- **M3 — residual test gap / VERIFICATION PENDING:** broad contended/stress linearizability coverage.
- Final evidence: targeted **29/29 PASS**; relevant regression **118/118 PASS**; full suite **413/413 PASS, 0 failed / 0 skipped**; non-incremental build **0 warnings / 0 errors**; `git diff --check` **PASS**. Test/build evidence comes from completed implementation/follow-up; review closure is user-confirmed. No test/build or independent review was rerun in this documentation-only sync.
- Next increment recorded at the 5C-1 checkpoint: **4B-5C-2 — production operation enrollment and producer retirement**, requiring separate scope/approval. Full details: [Phase 4 checkpoint](PHASE-4-CRASH-RECOVERY-BACKUP.md#4b-5c-1--isolated-operation-lifetime-primitive).

### 4B-5C-2.1 — production operation ownership/composition and Auth enrollment

**IMPLEMENTED / TESTED / INDEPENDENTLY REVIEWED — COMMITTED / PUSHED `a8e3d02eb7059d5eee8504ea712f85499f9119e4`** (push confirmed by the user). Phase 4 remains **IN PROGRESS**; runtime quiescence and restore safety are **NOT established**.

**Implemented scope:**

- Production files: `ShopManager.Desktop/Services/RuntimeOperations.cs`, `ShopManager.Desktop/Services/AuthService.cs`, `ShopManager.Desktop/Services/DatabaseService.cs`.
- Tests: `ShopManager.Domain.Tests/Integration/AuthOperationEnrollmentTests.cs`.
- `RuntimeOperations` owns one shared production `RuntimeOperationGate` and supplies explicit owner/view composition bindings; isolated runtimes support tests without contaminating production state. Stale/completed/faulted and concurrent/reentrant binding use is rejected; an owner cannot complete while its binding is in use.
- Standalone Auth Login/Logout owns exactly one operation lifetime through context disposal. Enrolled Auth borrows an explicit parent binding without nested admission or completing its parent. `RecordFailedLogin` and its secondary context remain within the parent Login lifetime.
- Internal `DatabaseContextCleanupUnprovenException` preserves factory/fresh-initialization body and cleanup diagnostics without coupling DatabaseService to RuntimeOperationGate, RuntimeOperations or AuthService; identity/blocking behavior remains fail-closed.
- Admission rejection precedes DB/session/history/event side effects. Auth mutation has a non-blocking Busy claim. Proven cleanup of business/auth/query/save failures, including unknown commit outcomes, does not itself fault operation accounting.
- No `AsyncLocal`, ambient authority, DB capability/bypass, or production Close/Drain/Reopen caller was added.

**Review reconciliation (user-confirmed independent review; no new review performed in this documentation sync):**

- **F1 HIGH — CLOSED:** Login tracks publication by this attempt. A later subscriber/cleanup failure returns Success=true with a warning and preserves its published session/history; it does not fake rollback or re-fire events. Before-publication failure remains Success=false, even if an older session exists. Subscriber failure alone is not cleanup uncertainty; unproven cleanup still leaves the gate FaultedClosed and its outstanding lease retained. Combined subscriber/cleanup failures preserve both diagnostics.
- **M1 test false-positive — CLOSED:** reentrancy observations/results/exceptions are captured inside the callback and asserted after Login returns; coverage proves exactly-once callback execution, Busy rejection, no extra DB work, outer success, cleanup/claim release, a subsequent legitimate Auth operation and healthy final accounting.
- **L3 coverage added:** older session + pre-publication failure + cleanup failure remains Success=false, preserves the older session and fault-closes the gate with outstanding retained.
- Final independent-review disposition supplied by the user: **F1 CLOSED; no new Critical/High; M1 CLOSED**.

**Residual findings:**

- **F2 — OPEN FOLLOW-UP:** at 5C-2.1, new Auth admission/Busy/fault exceptions could escape existing callers. 5C-2.2 handles the SessionTracker path; general UI/Auth caller hardening remains deferred.
- **F3 — OPEN RESIDUAL:** a replayed historical DatabaseService resolution cleanup diagnostic may be attributed to a current operation; normal production reachability remains **VERIFICATION PENDING**. This checkpoint does not fix or redesign diagnostic provenance.
- **F4 — KNOWN/INTENTIONAL:** LogoutCore may swallow a cleanup-unproven diagnostic while the operation gate remains FaultedClosed.

**Final implementation/follow-up evidence:**

- Targeted `AuthOperationEnrollmentTests`: **52/52 PASS**.
- Relevant Auth/admission/resolution regressions: **200/200 PASS** (`AuthOperationEnrollmentTests`, `RuntimeOperationAdmissionTests`, `DatabaseAdmissionDrainTests`, `DatabasePathResolutionTests`, `DatabaseDurabilityTests`, `BackupAdmissionDrainTests`).
- Full suite: **465/465 PASS, 0 failed / 0 skipped**.
- Non-incremental solution build: **0 warnings / 0 errors**; `git diff --check`: **PASS**.
- Test/build evidence was executed during implementation/follow-up, not rerun during this docs-only step.

**Historical non-guarantees at the 5C-2.1 checkpoint:** SessionTracker and LoginWindow were NOT enrolled; producer retirement was NOT implemented; F2/F3 remained open; no runtime quiescence or restore safety was established. Earlier checkpoint guarantees and evidence remain historical and are not retroactively upgraded.

**Next increment recorded at the 5C-2.1 checkpoint:** **4B-5C-2.2 — SessionTracker enrollment/retirement**. The implemented narrower boundary and its remaining deferrals are recorded below.

### 4B-5C-2.2 — SessionTracker tick enrollment and generation/single-flight safety

**IMPLEMENTED / TESTED / INDEPENDENTLY REVIEWED — COMMITTED / PUSHED `72d7761`.** Phase 4 remains **IN PROGRESS / NOT restore-safe**; full runtime quiescence is **NOT established**.

**Implemented scope:** `ShopManager.Desktop/Services/SessionTracker.cs`, `ShopManager.Desktop/Services/AuthService.cs`, and new `ShopManager.Domain.Tests/Integration/SessionTrackerOperationEnrollmentTests.cs`. RuntimeOperations, RuntimeOperationGate, DatabaseService and UI/lifecycle source were not changed in this increment.

- One accepted tick owns one NonInteractive operation through tracker DB work and proven context cleanup, conditional enrolled Auth logout, synchronous UserDeactivated notification and generation-scoped self-stop, followed by owner disposal. Producer bookkeeping settles after owner disposal. Stale-generation callbacks cannot become accepted; accepted ticks are single-flight.
- Stop revokes generation authority and initiates timer teardown; it does **not** prove accepted work complete. Accepted work remains accounted until proven completion. Start during Starting/Stopping is Busy; no pending Start/latest-wins or terminal retirement API was added. No DB/Auth/callback/timer teardown or drain wait occurs under the producer lock; self-stop does not wait for its own lease.
- Conditional Auth logout borrows an explicit parent binding with **no nested Auth admission and no AsyncLocal**. Under the Auth mutation claim it compares immutable session-snapshot reference identity before effects. Replacement sessions are protected, including logout/relogin by the same user; mutable user fields are not session identity.
- Tracker/Auth cleanup uncertainty remains **fail-closed**: the responsible operation's unproven lease stays outstanding and the gate stays FaultedClosed. A fault from another operation does not turn this tick's proven cleanup into an unproven lease. Timer teardown uncertainty faults the producer and is not automatically a runtime-operation cleanup fault.

**Evidence from completed implementation and user-confirmed independent review (not rerun in this docs-only step):**

- New SessionTracker tests: **47/47 PASS**; Auth regression: **52/52 PASS**.
- Relevant regression: **148/148 PASS** (RuntimeOperationAdmission, DatabaseAdmissionDrain, DatabasePathResolution, DatabaseDurability and BackupAdmissionDrain tests).
- Full suite: **512/512 PASS, 0 failed / 0 skipped**.
- Independent targeted review run: **99/99 PASS**; independent review: **PASS, no Critical/High/Medium/Low findings** (supplied by the user).
- Non-incremental solution build: **0 warnings / 0 errors**; implementation `git diff --check`: **PASS**. This docs step reruns only diff/working-tree integrity checks, not build/tests/review.

**Residuals / non-guarantees:** terminal producer retirement and full quiescence remain deferred. F2 general UI/Auth caller hardening remains open; SessionTracker exception handling does not close it for other callers. F3 historical resolution-cleanup diagnostic provenance remains **VERIFICATION PENDING**. F4 LogoutCore may swallow its cleanup diagnostic, while the same parent lease remains unproven and the gate stays FaultedClosed. Synchronous notification coverage does not include queued UI transitions. LoginWindow/MainWindow/general lifecycle, history-write serialization, shutdown/updater coordination and restore activation/safety are not established by this increment.

**Next uncompleted work (approved scope reset):** F2 → F3 (F1 implemented/verified, uncommitted). General per-window enrollment is deferred to reliability backlog; source implementation needs separate scope/approval.

**Final restore constraints (F1 — now implemented and verified, uncommitted; retained as design constraints):**

- Never wait for DB drain while holding the `TransitionGate` write lock.
- Future production flow must close admission and prove drain before `Arm`; resolver identity must be established before future `CloseAdmission`.
- In the no-lease fresh-resolution path, marker publication currently occurs after the runtime initialization lease is released and is outside DB drain. The already-admitted factory retains its existing lease. DB drain must not be presented as proof that all resolver/marker work has completed.
- Existing 5C-2.1/2.2 hardening and evidence are retained. F1/F2/F3 is the approved critical path; general per-window enrollment/lifetime architecture is deferred. Terminal restore boundary is implemented by F1 (uncommitted, verified); the swap itself remains startup-recovery work; F2/F3 remain OPEN.
- Keep cutoff closed through terminal restore exit. Timeout or cleanup uncertainty => do not `Arm`; after persistent intent exists, or publication is uncertain, normal DB work must not resume.
- Ordering: runtime/DB cutoff and drain → `PrepareRestore` with backup admission still open → backup cutoff and drain → release SQLite pools → `Arm` → restore-specific exit → actual swap at next startup. Updater Apply/Restart and queued lifecycle transitions must not race restore.

**VERIFICATION PENDING — 4B-4:** direct automated coverage مسیر واقعی `App.OnFrameworkInitializationCompleted` / `InitializeNormalDesktopStartup`؛ تست بیشتر path canonicalization/alias؛ installed GUI blocked-window startup/shutdown smoke؛ startup latency/UX برای stagingهای بزرگ. پوشش entry مشترک startup ادعای پوشش مستقیم callback/بدنهٔ عادی نیست.

**VERIFICATION PENDING — غیرمسدودکنندهٔ 4B-3:** cross-user / cross-session / elevation؛ installed GUI startup/shutdown smoke؛ Velopack update/restart overlap. این موارد verified نیستند.

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

**Scope:** UI Thread, Async, Event Leak, Timer Lifecycle; deferred general per-window operation enrollment/lifetime work from Phase 4 (Login/Users/Items/Cashbox/POS/Transfer/Reports/etc.). Restore-specific safety remains in F1; existing 5C-2.1/2.2 hardening is retained.

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

| مورد                            | مقدار                                                                                                         |
| ------------------------------- | ------------------------------------------------------------------------------------------------------------- |
| فازهای کامل (Framework جدید)    | 3 (Phase 1, Phase 2, Phase 3 تا 3C)                                                                           |
| Unique Index / F5               | بسته‌شده در سطح SaleOperations؛ محدودیت legacy در بخش ۵                                                       |
| فاز بعدی | Phase 4 remains **IN PROGRESS / NOT restore-safe**. Approved critical path: **F1 → F2 → F3** (F1 IMPLEMENTED / VERIFIED, uncommitted; F2/F3 OPEN). Latest hardening: **5C-2.2 — IMPLEMENTED / TESTED / INDEPENDENTLY REVIEWED / COMMITTED / PUSHED `72d77613340f2d9b88abd64ec39bcbe351156608`** (push confirmed by the user). 5C-2.1/2.2 and their historical evidence remain valid; general per-window enrollment is deferred. |
| فازهای حذف‌شده از Roadmap       | Alert، Cloud، Multi-terminal                                                                                  |
| کارهای قدیمی ثبت‌شده در بخش ۲.۳ | ۱۳؛ این عدد شمارندهٔ کل تغییرات جدید نیست                                                                     |
| آخرین مجموعهٔ تست تأییدشده | 4B-5C-2.2 (COMMITTED / PUSHED `72d7761`): new **47/47 PASS**؛ Auth **52/52 PASS**؛ relevant **148/148 PASS**؛ full **512/512 PASS**؛ independent targeted review run **99/99 PASS** (user-confirmed)؛ تاریخی: 4B-5C-2.1 (COMMITTED / PUSHED `a8e3d02`): full **465/465 PASS**؛ targeted **52/52 PASS**؛ relevant **200/200 PASS**؛ 0 failed / 0 skipped؛ تاریخی: 4B-5C-1 (COMMITTED `33e12b6`): full **413/413 PASS**؛ targeted **29/29 PASS**؛ relevant **118/118 PASS**؛ 0 failed / 0 skipped؛ تاریخی: 4B-5B-2 (COMMITTED `287764c`): full **384/384**؛ targeted **26/26**؛ relevant **172/172**؛ 0 failed / 0 skipped؛ تاریخی: 4B-5B-1 (`34faeee`; COMMITTED / PUSHED): full **358/358**؛ targeted **36/36**؛ relevant **166/166**؛ تاریخی 4B-5A: full 354/354، targeted 32/32، relevant 130/130؛ تاریخی: 322 در 4B-4، 291 در 4B-3، 270 در `d472153` و 246 در `59d0dfc` |
| Build | 4B-5C-2.2 (COMMITTED / PUSHED `72d7761`): non-incremental **0 warnings / 0 errors**؛ diff-check **PASS**؛ independent review **PASS, no Critical/High/Medium/Low findings** (user-confirmed)؛ تاریخی: 4B-5C-2.1 (COMMITTED / PUSHED `a8e3d02`): non-incremental **0 warnings / 0 errors**؛ diff-check **PASS**؛ تاریخی: 4B-5C-1 (COMMITTED `33e12b6`): non-incremental **0 warnings / 0 errors**؛ diff-check **PASS**؛ تاریخی: 4B-5B-2 (COMMITTED `287764c`): non-incremental **0 warnings / 0 errors**؛ تاریخی: 4B-5B-1 (`34faeee`; COMMITTED / PUSHED): build غیرافزایشی solution با **0 warnings / 0 errors**؛ شواهد تاریخی checkpointهای قبلی حفظ شده‌اند |
| Git | Latest production hardening: **4B-5C-2.2 COMMITTED / PUSHED `72d77613340f2d9b88abd64ec39bcbe351156608`** (user-confirmed); 5C-2.1: `a8e3d02`; historical checkpoints/evidence retained. This docs-only update is uncommitted. |

**Phase 1–3 بسته‌اند.** Phase 4 remains **IN PROGRESS / NOT restore-safe**. Approved critical path: **F1 → F2 → F3** (F1 IMPLEMENTED / VERIFIED, uncommitted; F2/F3 OPEN). Latest hardening: **5C-2.2 — IMPLEMENTED / TESTED / INDEPENDENTLY REVIEWED / COMMITTED / PUSHED `72d77613340f2d9b88abd64ec39bcbe351156608`** (push confirmed by the user). 5C-2.1/2.2 and their historical evidence remain valid; general per-window enrollment is deferred. سایر اقلام DoD بازند؛ جزئیات در [PHASE-4-CRASH-RECOVERY-BACKUP.md](PHASE-4-CRASH-RECOVERY-BACKUP.md).

---

## ۸. Decision Log

**2026-10-06 — User-approved Phase-4 SCOPE RESET:** critical path F1 → F2 → F3; general per-window enrollment/lifetime work → deferred reliability backlog; AR-2/S8 and D1 remain OPEN / DEFERRED. Safety invariants and existing 5C-2.1/2.2 evidence are retained. Documentation only; no implementation, build/test, commit/push/tag or Phase-4 completion is authorized by this update.

| تاریخ                      | تصمیم                                                       | دلیل                                                                                              |
| -------------------------- | ----------------------------------------------------------- | ------------------------------------------------------------------------------------------------- |
| مبنای `59d0dfc`            | Phase 4 checkpoint ها تا 4B-2A پیاده و در commit `59d0dfc` تأیید شدند | build 0W/0E و اجرای کامل 246/246 تست در commit `59d0dfc`؛ Phase 4 همچنان IN PROGRESS است |
| مبنای `59d0dfc`            | Restore intent ماندگار و gate پذیرش دیتابیس (4B-2A)         | fail-closed؛ تعویض واقعی DB/WAL/SHM و tombstone به 4B-2B و startup recovery به integration بعدی Phase 4 موکول شد |
| Closure در مبنای `905622c` | Phase 3 تا 3C COMPLETE                                      | build 0W/0E، 134/134 تست، diff check و تأیید final adversarial review؛ منشأ شواهد در بخش ۵        |
| Closure در مبنای `905622c` | جایگزینی الزام RowVersion با طراحی تراکنشی SQLite           | خواندن authoritative پس از آغاز تراکنش writer و تست رقابت مستقل؛ token پیاده نشده                 |
| Closure در مبنای `905622c` | تفکیک هویت عملیات از ردیف‌های فاکتور                        | uniqueness در SaleOperations؛ حفظ فاکتور چندردیفی و تاریخچه؛ بدون ادعای اصلاح عمومی legacy schema |
| 1405/07/08                 | Phase 2 — Transaction Boundary COMPLETE                     | DoD، Build/Test، Production Review و Test Review تأیید شدند                                       |
| 1405/07/08                 | تفکیک Cleanup به Pre-Phase (نه Phase شماره‌دار)             | جلوگیری از تداخل شماره‌گذاری با Transaction Boundary                                              |
| 1405/07/07                 | Framework جدید (۱۲ بُعد + Audit + Reconciliation + Roadmap) | جلوگیری از Scope Creep                                                                            |
| 1405/07/07                 | Alert System حذف                                            | معماری زود است                                                                                    |
| 1405/07/07                 | Logging در Phase 6 (نه 2.5)                                 | Transaction خودش تست‌پذیر                                                                         |
| 1405/07/07                 | EULA/Privacy در Phase 10                                    | Release فقط                                                                                       |
| 1405/07/07                 | Tax/Legal = Business Requirement                            | Scope question                                                                                    |
| 1405/07/07                 | DoD اجباری                                                  | جلوگیری از پیشرفت کاذب                                                                            |
| 1405/07/07                 | Phase 1 COMPLETE                                            | DoD 100% پاس شد                                                                                   |
| 1405/07/07                 | FluentAssertions استفاده نشود                               | لایسنس تجاری Xceed                                                                                |
| 1405/07/07                 | Reversal خارج از Scope                                      | فیچر پیاده نشده، Backlog                                                                          |
| 1405/07/07                 | Customer TotalPurchasedAmount بدون تغییر                    | خط 1109 خالص بود                                                                                  |

---

## ۹. Future Backlog

Phase-4 scope reset: general per-window enrollment/lifetime work → Phase 8 reliability; AR-2/S8 migrations/schema drift and D1 encryption/secondary backup remain OPEN / DEFERRED. See [deferred backlog](MASTER-BACKLOG.md#deferred-work-from-phase-4-scope-reset).

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

**Phase 1–3 بسته‌اند.** Phase 4 remains **IN PROGRESS / NOT restore-safe**. Approved critical path: **F1 → F2 → F3** (F1 IMPLEMENTED / VERIFIED, uncommitted; F2/F3 OPEN). Latest hardening: **5C-2.2 — IMPLEMENTED / TESTED / INDEPENDENTLY REVIEWED / COMMITTED / PUSHED `72d77613340f2d9b88abd64ec39bcbe351156608`** (push confirmed by the user). 5C-2.1/2.2 and their historical evidence remain valid; general per-window enrollment is deferred. سایر اقلام DoD بازند؛ جزئیات در [PHASE-4-CRASH-RECOVERY-BACKUP.md](PHASE-4-CRASH-RECOVERY-BACKUP.md).
