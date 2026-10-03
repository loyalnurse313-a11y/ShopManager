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

آخرین production-code checkpoint ثبت‌شده (committed) در این context: `59d0dfc` — `feat: add durable restore intent foundation`.
checkpoint `4B-2B` در working tree پیاده‌سازی و verify شده است ولی هنوز commit نشده؛ تا commit شدن، HEAD محسوب نمی‌شود و hash ندارد.
برای بررسی اینکه پس از آن چه چیزی غیر از docs/AGENTS.md تغییر کرده است، از Git verify کنید:
`git diff --stat 59d0dfc HEAD -- . ':!docs' ':!AGENTS.md'`

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

## 6. Phase 4 — وضعیت فعلی تا 4B-2B

Phase 4 همچنان **IN PROGRESS — NOT complete** است.

| Checkpoint | Commit | خلاصهٔ verified |
|---|---|---|
| 4A-1 | `0944a7e` | staging، validation و atomic backup publication |
| 4A-2 | `05212da` | canonical database identity و startup blocking |
| 4A-3 | `3466604` | WAL + `synchronous=FULL` با read-back |
| 4A-4 | `22ca6fa` | backup single-flight، generation و orphan sweep |
| 4B-1 | `461670c` | non-destructive restore preparation و safety snapshot |
| 4B-2A | `59d0dfc` | durable restore intent و database admission gate |
| 4B-2B | _(commit نشده؛ hash ثبت نشده)_ | offline file-level recovery engine (`RestoreRecoveryService.Recover`)؛ پیاده‌سازی و verify شده در working tree |

evidence تاریخی برای production-code checkpoint `59d0dfc` (commit شده): build با 0 errors / 0 warnings؛ tests با 246/246 passed، 0 failed، 0 skipped.

evidence verify‌شدهٔ working tree برای 4B-2B (تا commit شدن، HEAD نیست): `RestoreRecoveryServiceTests` 42 passed؛ کل `ShopManager.Domain.Tests` 270 passed؛ build غیرافزایشی solution با 0 warnings / 0 errors؛ adversarial/final review: PASS بدون issue مسدودکنندهٔ Critical/High/Medium.
این evidence‌ها به معنی تکمیل Phase 4 یا اجرای end-to-end crash/recovery نیست.

**قراردادهای حیاتی Phase 4 برای کار بعدی:**
- **Startup fail-closed:** اگر marker نامعتبر/خراب باشد، مسیر ثبت‌شده در دسترس نباشد، `shop.db` در مسیر ثبت‌شده نباشد، یا چند دیتابیس هم‌زمان و نامشخص وجود داشته باشد، `DatabaseService.BlockedReason` startup را پیش از settings/backup/auth متوقف می‌کند. نصب تازه بدون marker (که سیاست قدیمی را دنبال می‌کند) در این محدوده نیست.
- **Admission gate:** `DatabaseService.CreateContext` قفل خواندن `RestoreRecoveryService.EnterDatabaseAdmission` را می‌گیرد؛ اگر restore مسلح باشد، ساخت context تازه fail-closed مسدود می‌شود.
- **Durability:** شکست `journal_mode=WAL` یا `synchronous=FULL` از `DurabilityInterceptor` باید propagate شود؛ نباید silently نادیده گرفته شود.
- **Recovery engine (4B-2B، working tree):** `RestoreRecoveryService.Recover` پس از intent ماندگار فقط forward-only است. وضعیت منتشرشده (V) یعنی SHA-256 مورد انتظار دیتابیس زنده **و** نبودن sidecarهای زندهٔ `-wal` / `-shm` / `-journal`. وضعیت مبهم یا ناایمن fail-closed می‌شود و restore مسلح می‌ماند.

## 7. کار بعدی Phase 4

### 4B-2B — پیاده‌سازی و verify شده در working tree (commit نشده)

- offline recovery engine (`RestoreRecoveryService.Recover`)؛
- file-level forward-completion/swap وضعیت زندهٔ DB؛
- tombstoneها و incoming artifactهای دقیقاً operation-owned؛ بدون wildcard cleanup؛
- cleanup به‌صورت plan-then-execute؛
- **SHA-256 staging fingerprint** مرجع باقی می‌ماند؛
- پس از intent ماندگار: **forward-complete یا BLOCK — هرگز rollback**؛
- حذف intent آخرین mutation موفق روی disk است؛
- نبودن `SafetyBackupPath` مانع forward completion نیست؛ اسنپ‌شات ایمنی در صورت وجود حفظ می‌شود؛
- پوشش crash/restart و blocked-state اضافه شده است.

**عمداً خارج از 4B-2B و هنوز کار آینده** (این‌ها integration بعدی Phase 4 هستند و پیاده نشده‌اند): App startup wiring/order و مصرف intent؛ app-lifetime mutex؛ SettingsWindow/UI integration؛ shutdown/quiesce integration؛ حذف legacy production restore path.

### Phase 4 — remaining DoD (هنوز باز)

- AR-2: `EnsureCreated` → `Migrate()`.
- S8: بررسی schema/migration drift. **POSSIBLE:** پوشش migration/schema ممکن است از runtime schema عقب باشد؛ پیش از هر اقدام در AR-2/S8 تأیید شود.
- D1: backup 3-2-1، encryption و secondary backup.
- تست: kill وسط `SaveSale` → DB سالم؛
- تست: restore کامل از backup → همهٔ داده؛
- تست: DB خراب → بازیابی از backup.

### Phase 4 — subsequent integration (پس از 4B-2B؛ هنوز باز)

- اتصال recovery به startup و مصرف restore intent.
- فراخوانی موتور swap آفلاین از restore flow برنامه (خود موتور در 4B-2B پیاده شده است؛ فراخوانی آن از برنامه در این بخش و هنوز باز است).
- جایگزینی مسیر legacy `BackupService.RestoreBackup` و مسیر `Environment.Exit(0)` در `Views/SettingsWindow.axaml.cs`.
- رفع محدودیت‌های Phase 4 شناخته‌شده در بخش ۹.

### Phase 5 به بعد

Audit Trail، structured logging، performance، UX reliability و release hardening طبق roadmap.

## 8. ریسک‌ها و مرزهای باز

- restore intent در production هنوز end-to-end مصرف نمی‌شود: موتور 4B-2B (`Recover`) وجود دارد اما هیچ caller production (startup/UI/shutdown) آن را فراخوانی نمی‌کند؛ مسیر legacy restore هنوز در UI است.
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
