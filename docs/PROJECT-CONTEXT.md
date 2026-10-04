# ShopManager — Project Context

> **نوع سند:** index / current-state map برای onboarding کم‌هزینه.
> **قاعده:** این سند تصمیم معماری جدید یا تاریخچهٔ کامل نیست.

## 1. هدف و معماری

ShopManager یک POS و سامانهٔ مدیریت فروشگاه است.

- Stack: Avalonia 12.1.2، .NET 10، EF Core با Microsoft.EntityFrameworkCore.Sqlite 10.0.12
- Production projects: `ShopManager.Domain`, `ShopManager.Infrastructure`, `ShopManager.Desktop`
- Tests: `ShopManager.Domain.Tests`
- لایه‌ها: `Desktop` → `Infrastructure` → `Domain`
- invariant معماری: `Domain` نباید به EF Core یا Avalonia وابسته شود.

## 2. سلسله‌مراتب منبع حقیقت

به‌ترتیب اولویت:

1. source code + tests + Git
2. این سند
3. اسناد جزئی‌تر در `docs/`

اگر این سند با evidence معتبر (source، tests، Git) ناسازگار بود، **آن ناسازگاری را گزارش کنید**؛ حدس نزنید. اصلاح فقط در scope صریح مجاز است.

آخرین checkpoint پیاده‌شده: **4B-5A — IMPLEMENTED / VERIFIED / INDEPENDENTLY REVIEWED / CHECKPOINT-READY؛ UNCOMMITTED و بدون hash**.
آخرین checkpoint کد production که commit شده: `63039d2` — `feat: integrate startup restore recovery` (4B-4).
checkpointهای قبلی: `583a7b8` (4B-3)، `d472153` (4B-2B) و `59d0dfc` (4B-2A).
دستور زیر فقط تغییرات commit‌شده را نشان می‌دهد؛ برای 4B-5A که هنوز commit نشده، `git status --short` و working-tree diff را نیز بررسی کنید:
`git diff --stat d472153 HEAD -- . ':!docs' ':!AGENTS.md'`

## 3. invariantهای حیاتی

- `LockedUnitCost` snapshot فروش است و با خریدهای بعدی تغییر نمی‌کند؛ محاسبه فقط خریدهای تا تاریخ فروش را در نظر می‌گیرد.
- قیمت فروش: `SalePrice` معتبر اولویت دارد؛ در غیر این صورت از آخرین قیمت خرید و `MarkupPct` محاسبه می‌شود.
- `Revenue` مبلغ خالص پس از تخصیص تخفیف است؛ `DiscountAmount` باید ثبت شود؛ `Profit = Revenue - Cost`.
- rounding مالی با `MidpointRounding.AwayFromZero` انجام می‌شود و مجموع تخصیص تخفیف باید با تخفیف کل برابر باشد.
- موجودی انبار و مغازه از opening quantity و رویدادهای خرید، انتقال و فروش محاسبه می‌شود؛ **برای Reversal در فاز فعلی sign/effect آن را در source verify کنید** — قرارداد آن در Future Backlog است و settled تلقی نمی‌شود.
- صندوق شامل سرمایه، فروش نقدی، خرید نقدی و ledger است؛ فروش کارتی وارد صندوق نمی‌شود.
- کنترل authoritative موجودی فروش و انتقال پس از آغاز تراکنش SQLite انجام می‌شود؛ `RowVersion` پیاده نشده است.
- فروش idempotent است: `SaleOperations.OperationId` هویت عملیات، fingerprint اجباری، و `InvoiceNumber` در سطح operation یکتا است.
- همان `OperationId` و fingerprint → replay بدون اثر دوباره؛ payload متفاوت → conflict.
- `Item.IsActive` برای soft delete است؛ FKهای مالی به Item با `DeleteBehavior.Restrict` پیکربندی شده‌اند. **استثنا:** `Sale.Customer` با `SetNull` است — محدودیت‌های Restrict همه‌جا یکسان نیستند.
- هر اتصال SQLite باید `journal_mode=WAL` و `synchronous=FULL` را اعمال و read-back کند.

## 4. وضعیت roadmap

| Phase | عنوان | وضعیت |
|---|---|---|
| 1 | Business Correctness | COMPLETE |
| Pre-Phase | Cleanup S1–S3 | COMPLETE |
| 2 | Transaction Boundary | COMPLETE |
| 3 | Concurrency + Idempotency | COMPLETE تا 3C |
| 4 | Crash Recovery + Backup | **IN PROGRESS — NOT complete** |
| 5 | Audit + Security | Planned |
| 6 | Logging + Global Error | Planned |
| 7 | EF Core + Performance | Planned |
| 8 | Avalonia Reliability | Planned |
| 9 | Tests | Planned / coverage expansion |
| 10 | Release Hardening | Planned |

Scope خارج از roadmap فعلی: Cloud Sync، Multi-terminal، Multi-store، Mobile، Plugin، Telemetry، Feature Flags و API.

## 5. فازها و checkpointهای بسته‌شده

- Phase 1: B1–B7؛ جزئیات در `docs/PHASE-1-BUSINESS-CORRECTNESS.md`.
- Pre-Phase: S1–S3؛ جزئیات در `docs/PRE-PHASE-2-CLEANUP.md`.
- Phase 2: transaction boundary فروش؛ پیاده‌سازی در `2399233` و `3e1e74a`؛ closure/tag به `952204a` اشاره می‌کند.
- Phase 3A: payment re-entry guard؛ commit `45c6511`.
- Phase 3B-1: SaleOperations schema foundation؛ commit `c3268af`.
- Phase 3B-2: fingerprint، replay/conflict و pending lifecycle؛ commit `5ff9a68`.
- Phase 3C: concurrency-safe inventory transfer؛ commit `da7d5b4`.
- Phase 3 closure: commit `905622c`; آخرین completion tag: `phase-3-concurrency-idempotency-complete`.
- Phase 4 completion tag وجود ندارد.

## 6. Phase 4 — وضعیت فعلی تا 4B-5A (UNCOMMITTED)

Phase 4 همچنان **IN PROGRESS — NOT complete** است.

| Checkpoint | Commit | خلاصهٔ verified |
|---|---|---|
| 4A-1 | `0944a7e` | staging، validation و atomic backup publication |
| 4A-2 | `05212da` | canonical database identity و startup blocking |
| 4A-3 | `3466604` | WAL + `synchronous=FULL` با read-back |
| 4A-4 | `22ca6fa` | backup single-flight، generation و orphan sweep |
| 4B-1 | `461670c` | non-destructive restore preparation و safety snapshot |
| 4B-2A | `59d0dfc` | durable restore intent و database admission gate |
| 4B-2B | `d472153` | offline file-level recovery engine (`RestoreRecoveryService.Recover`) |
| 4B-3 | `583a7b8` | app-lifetime Windows mutex؛ implemented / verified / committed |
| 4B-4 | `63039d2` | startup recovery integration؛ implemented / verified / independently reviewed / checkpoint-ready |
| 4B-5A | UNCOMMITTED | runtime context admission + drain؛ IMPLEMENTED / VERIFIED / INDEPENDENTLY REVIEWED / CHECKPOINT-READY؛ NOT restore-safe |

evidence تاریخی برای checkpoint قبلی `59d0dfc` (4B-2A): build با 0 errors / 0 warnings؛ tests با 246/246 passed، 0 failed، 0 skipped.

evidence تاریخی verify‌شده برای 4B-2B (commit `d472153`؛ 270 تست در آن source tree commit‌شده): `RestoreRecoveryServiceTests` 42 passed؛ کل `ShopManager.Domain.Tests` 270 passed؛ build غیرافزایشی solution با 0 warnings / 0 errors؛ adversarial/final review: PASS بدون issue مسدودکنندهٔ Critical/High/Medium.

evidence تاریخی 4B-3 پس از test hardening و پیش از commit `583a7b8`: `ApplicationInstanceGuardTests` **21 passed / 0 failed / 0 skipped**؛ کل `ShopManager.Domain.Tests` **291 passed / 0 failed / 0 skipped**؛ build غیرافزایشی solution **0 warnings / 0 errors**؛ `git diff --check` **exit code 0**؛ adversarial review طبق تأیید کاربر: **PASS WITH FINDINGS، بدون Critical یا High**. follow-up تست بدون تغییر production تکمیل شد. این نتایج مربوط به اجرای پیاده‌سازی/تست‌اند؛ در documentation sync دوباره build/test اجرا نشده است.

evidence 4B-4 (`63039d2`): `StartupRecoveryIntegrationTests` **29 passed**؛ `RestoreRecoveryServiceTests` **44 passed**؛ `ApplicationInstanceGuardTests` **21 passed**؛ کل `ShopManager.Domain.Tests` **322 passed**؛ همگی 0 failed / 0 skipped؛ build غیرافزایشی solution **0 warnings / 0 errors**؛ `git diff --check` **exit code 0**. independent adversarial review طبق تأیید کاربر: **PASS WITH FINDINGS، بدون Critical یا High و بدون checkpoint-blocking finding**. نتایج متعلق به implementation/test و review تکمیل‌شده‌اند؛ build/test و review در documentation sync تکرار نشده‌اند.
این evidence‌ها به معنی تکمیل Phase 4 یا اجرای end-to-end crash/recovery نیست.

### 4B-5A — database admission + context drain

**IMPLEMENTED / VERIFIED / INDEPENDENTLY REVIEWED / CHECKPOINT-READY — UNCOMMITTED.** Phase 4 remains **IN PROGRESS**.

- Guaranteed ONLY: atomic runtime DbContext admission cutoff; tracking admitted runtime contexts until successful cleanup; asynchronous drain proof; owner-controlled reopen; fail-closed cleanup faults.
- **NOT restore-safe:** no proof of backup drain, background/timer drain, updater drain, complete multi-context business-operation drain or full quiesce. No production permission/caller for `PrepareRestore` / `Arm` is introduced; no production caller of `CloseAdmission` exists in this checkpoint. No restore UI wiring.
- Verified implementation evidence supplied for this documentation sync: `DatabaseAdmissionDrainTests` **32/32 passed**; relevant DB/resolution/recovery/startup tests **130/130 passed**; full `ShopManager.Domain.Tests` **354/354 passed**; non-incremental solution build **0 warnings / 0 errors**; implementation `git diff --check` **exit 0**. Tests/build/review were not rerun during this docs-only step.
- Independent review: **PASS WITH FINDINGS**. Both previous blocking findings are **RESOLVED**: concurrent cleanup ownership is established before EF cleanup; already-admitted fresh initialization reuses the existing admission instead of taking a second independent lease.
- **MEDIUM — OPEN BEFORE PRODUCTION `CloseAdmission` / 4B-5B:** a no-lease `EnsureResolved` resolution path can still permanently publish `_blockedReason` if fresh initialization encounters admission-closed. Non-blocking for 4B-5A because it adds no production `CloseAdmission` caller; **must be resolved before production cutoff is introduced**.
- **LOW:** if the cleanup-success callback itself throws, cleanup state may remain in-progress/fail-closed; the current gate callback has no expected throw path.
- **LOW:** AppDbContext disposal semantics are stricter: concurrent disposal is rejected; a later disposal after cleanup failure rethrows the original failure.
- Future sequence: **4B-5B — backup admission/timer boundary**; **4B-5C — unified cutoff/background/updater/multi-context completion**. Each needs separate scope/approval. Production `PrepareRestore` / `Arm` remains unavailable until the complete required quiesce boundary exists. Restore UI remains future work.

**قراردادهای حیاتی Phase 4 برای کار بعدی:**
- **Startup recovery (4B-4):** پس از mutex و Avalonia، نخستین gate در startupِ desktop پیش از resolver/SQLite اجرا می‌شود. فقط `NoIntent` یا `Completed` در حالت unarmed اجازهٔ ادامه می‌دهد؛ `Blocked` و خطاهای غیرمنتظره بدون resolver/context/settings/theme/backup/timer/auth/session/normal window/normal shutdown registration متوقف می‌شوند. registered identity روی همان intent مصرف‌شدهٔ `Recover`، پس از arming و پیش از mutation بررسی می‌شود؛ validator از DatabaseService یا SQLite استفاده نمی‌کند و live مفقود پس از tombstone می‌تواند پیش از resolver بازیابی شود. invariantهای 4B-2B و رفتار 4B-3 حفظ شده‌اند.
- **App-lifetime mutex (4B-3):** نام ثابت `Global\ShopManager.ApplicationLifetime`؛ acquisition پس از `Velopack.Run()` و پیش از `BuildAvaloniaApp()`؛ Busy → exit code 2 با صفر startup admission؛ acquisition Error → exit code 3 و fail-closed؛ abandoned ownership پذیرفته می‌شود بدون ادعای سلامت DB؛ guard موفق در `Program` برای عمر process strongly rooted است. harness چندprocess واقعی فقط از mutexهای یکتای تست استفاده می‌کند و production DB/mutex را مصرف نمی‌کند.
- **Startup fail-closed:** اگر marker نامعتبر/خراب باشد، مسیر ثبت‌شده در دسترس نباشد، `shop.db` در مسیر ثبت‌شده نباشد، یا چند دیتابیس هم‌زمان و نامشخص وجود داشته باشد، `DatabaseService.BlockedReason` startup را پیش از settings/backup/auth متوقف می‌کند. نصب تازه بدون marker (که سیاست قدیمی را دنبال می‌کند) در این محدوده نیست.
- **Admission gate:** `DatabaseService.CreateContext` قفل خواندن `RestoreRecoveryService.EnterDatabaseAdmission` را می‌گیرد؛ اگر restore مسلح باشد، ساخت context تازه fail-closed مسدود می‌شود.
- **Runtime context drain (4B-5A):** `DatabaseAdmissionGate` پذیرش contextهای runtime را اتمیک می‌بندد و lease را تا cleanup موفق `AppDbContext` نگه می‌دارد؛ اثبات drain این contextها، تضمین پایان همهٔ عملیات کسب‌وکار یا full quiesce نیست.
- **Durability:** شکست `journal_mode=WAL` یا `synchronous=FULL` از `DurabilityInterceptor` باید propagate شود؛ نباید silently نادیده گرفته شود.
- **Recovery engine (4B-2B، `d472153`):** `RestoreRecoveryService.Recover` پس از intent ماندگار فقط forward-only است. وضعیت منتشرشده (V) یعنی SHA-256 مورد انتظار دیتابیس زنده **و** نبودن sidecarهای زندهٔ `-wal` / `-shm` / `-journal`. وضعیت مبهم یا ناایمن fail-closed می‌شود و restore مسلح می‌ماند.

## 7. کار بعدی Phase 4

### 4B-2B — پیاده‌سازی و verify شده؛ commit `d472153`

- offline recovery engine (`RestoreRecoveryService.Recover`)؛
- file-level forward-completion/swap وضعیت زندهٔ DB؛
- tombstoneها و incoming artifactهای دقیقاً operation-owned؛ بدون wildcard cleanup؛
- cleanup به‌صورت plan-then-execute؛
- **SHA-256 staging fingerprint** مرجع باقی می‌ماند؛
- پس از intent ماندگار: **forward-complete یا BLOCK — هرگز rollback**؛
- حذف intent آخرین mutation موفق روی disk است؛
- نبودن `SafetyBackupPath` مانع forward completion نیست؛ اسنپ‌شات ایمنی در صورت وجود حفظ می‌شود؛
- پوشش crash/restart و blocked-state اضافه شده است.

**خارج از 4B-2B:** app-lifetime mutex در **4B-3 (`583a7b8`)** commit شده و startup recovery در **4B-4 (`63039d2`)** پیاده، verify و independently reviewed شده است. **4B-5B / 4B-5C و بعد — آینده و پیاده‌نشده:** restore UI wiring؛ quiesce/drain؛ shutdown redesign؛ حذف legacy production restore path.

### Phase 4 — remaining DoD (هنوز باز)

- AR-2: `EnsureCreated` → `Migrate()`.
- S8: بررسی schema/migration drift. **POSSIBLE:** پوشش migration/schema ممکن است از runtime schema عقب باشد؛ پیش از هر اقدام در AR-2/S8 تأیید شود.
- D1: backup 3-2-1، encryption و secondary backup.
- تست: kill وسط `SaveSale` → DB سالم؛
- تست: restore کامل از backup → همهٔ داده؛
- تست: DB خراب → بازیابی از backup.

### Phase 4 — subsequent integration (4B-5B / 4B-5C و بعد؛ هنوز باز)

- **4B-5B / 4B-5C — future work:** checkpoint بعدی نیازمند scope و approval مستقل است؛ restore UI wiring، quiesce/drain و shutdown redesign هنوز پیاده نشده‌اند.
- فراخوانی موتور swap آفلاین از restore UI/flow برنامه همچنان باز است؛ مصرف intent موجود در startup از 4B-4 وجود دارد.
- جایگزینی مسیر legacy `BackupService.RestoreBackup` و مسیر `Environment.Exit(0)` در `Views/SettingsWindow.axaml.cs`.
- رفع محدودیت‌های Phase 4 شناخته‌شده در بخش ۹.

### Phase 5 به بعد

Audit Trail، structured logging، performance، UX reliability و release hardening طبق roadmap.

## 8. ریسک‌ها و مرزهای باز

- **4B-3 — VERIFICATION PENDING، غیرمسدودکنندهٔ checkpoint:** رفتار cross-user / cross-session / elevation؛ installed GUI startup/shutdown smoke؛ رفتار overlap در Velopack update/restart. هیچ‌کدام verified گزارش نمی‌شوند.
- **4B-4 — VERIFICATION PENDING، غیرمسدودکنندهٔ checkpoint:** direct automated coverage مسیر واقعی `App.OnFrameworkInitializationCompleted` / `InitializeNormalDesktopStartup`؛ پوشش بیشتر path canonicalization/alias؛ installed GUI blocked-window startup/shutdown smoke؛ startup latency/UX برای stagingهای بزرگ. پوشش entry مشترک `App.RunDesktopStartup` معادل پوشش مستقیم callback و بدنهٔ عادی نیست.
- startup اکنون intent موجود را مصرف می‌کند؛ restore end-to-end، UI wiring، quiesce/drain، shutdown redesign و حذف legacy restore هنوز پیاده نشده‌اند و مربوط به 4B-5B / 4B-5C و بعد هستند.
- pending فروش فقط در حافظه است و recovery خودکار پس از restart ندارد؛ انتقال در حال حاضر هیچ ثبت idempotency بر اساس OperationId/Fingerprint ندارد — و commit نامعلوم retry خودکار نمی‌گیرد.
- crash/restore کامل، UI/چاپ فیزیکی و writerهای خارجی در evidence فعلی ادعا نشده‌اند.
- **Reversal:** جریان ساخت reversal پیاده نشده و قرارداد sign آن حل‌نشده/موکول‌شده است. محاسبات پشتیبان آن می‌توانند روی علامت‌های اثبات‌نشده تکیه کنند؛ **پیش از تکیه بر رفتار reversal، آن را در source verify کنید.**

## 9. ایمنی database / backup / restore

**محدودیت‌های طراحی implementation:**
- تهیهٔ restore باید non-destructive باشد: validation، staging و safety snapshot؛ دیتابیس زنده و backup انتخاب‌شده نباید در preparation تغییر کنند.
- پس از durable intent، رفتار **forward-complete یا BLOCK** است؛ هرگز rollback نشود.
- هرگز restore/copy روی دیتابیس عملیاتی را بدون scope و approval صریح انجام ندهید.

**اقدامات manual عملیاتی:**
- `masterbak` فقط context تاریخی/دستی است، نه restore procedure جاری؛ هرگز آن را تغییر ندهید.
- از wildcard (مثل `shop.db*`) و `Remove-Item` مخرب پرهیز کنید.
- هرگز از `Move-Item` برای restore استفاده نکنید.
- پس از هر copy از backup، `IsReadOnly` باید false شود.
- هیچ backup را به‌صورت خودسرانه روی دیتابیس عملیاتی کپی نکنید؛ scope و approval صریح لازم است.
- تست‌های integration باید از database و folder موقت و ایزوله استفاده کنند.

## 10. workflow و gateها

`AGENTS.md` مرجع انحصاری workflow، نقش agentها، gateها، الزامات تست و مجوزهای تغییر است. این سند آن‌ها را خلاصه یا بازتعریف نمی‌کند.

## 11. مسیر اسناد عمیق‌تر

| نیاز | سند |
|---|---|
| قواعد اجرایی | `AGENTS.md` |
| نقشهٔ کامل backlog | `docs/MASTER-BACKLOG.md` |
| تطبیق audit و وضعیت closure | `docs/AUDIT-RECONCILIATION.md` |
| جزئیات Phase 4 | `docs/PHASE-4-CRASH-RECOVERY-BACKUP.md` |
| جزئیات Phase 3 | `docs/PHASE-3-CONCURRENCY-IDEMPOTENCY.md` |
| جزئیات Phase 1 و 2 | `docs/PHASE-1-BUSINESS-CORRECTNESS.md` و `docs/PHASE-2-TRANSACTION-BOUNDARY.md` |
| handoff تاریخی و مرزهای شناخته‌شده | `docs/AI-HANDOFF.md` |

**موقعیت کد serviceها:** `BackupService`، `DatabaseService`، `RestoreRecoveryService` و persistence services (مثل `SalePersistenceService`، `TransferPersistenceService`) در `ShopManager.Desktop/Services/` قرار دارند.

## 12. نگهداری این سند

این فایل باید کوتاه، current-state و link-oriented بماند: تاریخچهٔ کامل، فهرست یافته‌ها، log تصمیمات، test output تفصیلی و توضیح implementation را اینجا کپی نکنید.
اگر source، tests یا Git تغییر کرد، ابتدا facts را verify کنید و فقط بخش‌های لازم را به‌روزرسانی کنید؛ هر مورد نامطمئن را با `POSSIBLE`، `PENDING` یا `VERIFICATION PENDING` برچسب بزنید.
