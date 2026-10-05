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

آخرین checkpoint پیاده‌شده: **4B-5C-2.1 — IMPLEMENTED / TESTED / INDEPENDENTLY REVIEWED — NOT COMMITTED YET؛ NOT runtime-quiescent / NOT restore-safe**. production operation ownership/composition و Auth enrollment اضافه شده‌اند؛ گام بعدی **4B-5C-2.2 — SessionTracker enrollment/retirement** با audit/scope مستقل است.
آخرین checkpoint کد production که commit شده: `33e12b6` — `feat: add runtime operation admission gate` (4B-5C-1). push تأییدشدهٔ قبلی: 4B-5B-1 در `34faeee`.
checkpointهای قبلی: `583a7b8` (4B-3)، `d472153` (4B-2B) و `59d0dfc` (4B-2A).
دستور زیر checkpointهای commit‌شده را نشان می‌دهد؛ 4B-5C-1 در `33e12b6` commit شده است:
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

## 6. Phase 4 — وضعیت فعلی تا 4B-5C-2.1 (NOT COMMITTED YET؛ آخرین checkpoint کد production commit‌شده: 4B-5C-1 در `33e12b6`)

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
| 4B-5A | `36f0e7f` | runtime context admission + drain؛ IMPLEMENTED / VERIFIED / INDEPENDENTLY REVIEWED / COMMITTED؛ NOT restore-safe |
| 4B-5B-1 | `34faeee` (COMMITTED / PUSHED) | resolver poisoning fix؛ IMPLEMENTED / VERIFIED / INDEPENDENTLY REVIEWED؛ NOT restore-safe |
| 4B-5B-2 | `287764c` (COMMITTED) | backup admission/timer boundary; IMPLEMENTED / VERIFIED / INDEPENDENTLY REVIEWED; NOT restore-safe |
| 4B-5C-1 | COMMITTED `33e12b6` | isolated RuntimeOperationGate primitive; IMPLEMENTED / VERIFIED / INDEPENDENTLY REVIEWED; NOT runtime-quiescent / NOT restore-safe |
| 4B-5C-2.1 | NOT COMMITTED YET | production operation ownership/composition and Auth enrollment; IMPLEMENTED / TESTED / INDEPENDENTLY REVIEWED; NOT runtime-quiescent / NOT restore-safe |

evidence تاریخی برای checkpoint قبلی `59d0dfc` (4B-2A): build با 0 errors / 0 warnings؛ tests با 246/246 passed، 0 failed، 0 skipped.

evidence تاریخی verify‌شده برای 4B-2B (commit `d472153`؛ 270 تست در آن source tree commit‌شده): `RestoreRecoveryServiceTests` 42 passed؛ کل `ShopManager.Domain.Tests` 270 passed؛ build غیرافزایشی solution با 0 warnings / 0 errors؛ adversarial/final review: PASS بدون issue مسدودکنندهٔ Critical/High/Medium.

evidence تاریخی 4B-3 پس از test hardening و پیش از commit `583a7b8`: `ApplicationInstanceGuardTests` **21 passed / 0 failed / 0 skipped**؛ کل `ShopManager.Domain.Tests` **291 passed / 0 failed / 0 skipped**؛ build غیرافزایشی solution **0 warnings / 0 errors**؛ `git diff --check` **exit code 0**؛ adversarial review طبق تأیید کاربر: **PASS WITH FINDINGS، بدون Critical یا High**. follow-up تست بدون تغییر production تکمیل شد. این نتایج مربوط به اجرای پیاده‌سازی/تست‌اند؛ در documentation sync دوباره build/test اجرا نشده است.

evidence 4B-4 (`63039d2`): `StartupRecoveryIntegrationTests` **29 passed**؛ `RestoreRecoveryServiceTests` **44 passed**؛ `ApplicationInstanceGuardTests` **21 passed**؛ کل `ShopManager.Domain.Tests` **322 passed**؛ همگی 0 failed / 0 skipped؛ build غیرافزایشی solution **0 warnings / 0 errors**؛ `git diff --check` **exit code 0**. independent adversarial review طبق تأیید کاربر: **PASS WITH FINDINGS، بدون Critical یا High و بدون checkpoint-blocking finding**. نتایج متعلق به implementation/test و review تکمیل‌شده‌اند؛ build/test و review در documentation sync تکرار نشده‌اند.
این evidence‌ها به معنی تکمیل Phase 4 یا اجرای end-to-end crash/recovery نیست.

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

**Remaining boundary:** no production cutoff / `PrepareRestore` / `Arm` wiring exists. DB/updater/shutdown/background/multi-context coordination remains for **4B-5C-2 and later integration**. Legacy `BackupService.RestoreBackup` remains outside this guarantee; the preparation algorithm is unchanged. Backup/timer drain does not establish restore safety or full quiesce, protect the entire Prepare-to-Arm interval, or prove successful deletion of all best-effort temporary artifacts.

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

**IMPLEMENTED / TESTED / INDEPENDENTLY REVIEWED — NOT COMMITTED YET.** Phase 4 remains **IN PROGRESS**; runtime quiescence and restore safety are **NOT established**.

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

- **F2 — OPEN FOLLOW-UP:** new Auth admission/Busy/fault exceptions can escape existing callers. Caller hardening belongs to later SessionTracker/UI/lifecycle enrollment; it was not changed here.
- **F3 — OPEN RESIDUAL:** a replayed historical DatabaseService resolution cleanup diagnostic may be attributed to a current operation; normal production reachability remains **VERIFICATION PENDING**. This checkpoint does not fix or redesign diagnostic provenance.
- **F4 — KNOWN/INTENTIONAL:** LogoutCore may swallow a cleanup-unproven diagnostic while the operation gate remains FaultedClosed.

**Final implementation/follow-up evidence:**

- Targeted `AuthOperationEnrollmentTests`: **52/52 PASS**.
- Relevant Auth/admission/resolution regressions: **200/200 PASS** (`AuthOperationEnrollmentTests`, `RuntimeOperationAdmissionTests`, `DatabaseAdmissionDrainTests`, `DatabasePathResolutionTests`, `DatabaseDurabilityTests`, `BackupAdmissionDrainTests`).
- Full suite: **465/465 PASS, 0 failed / 0 skipped**.
- Non-incremental solution build: **0 warnings / 0 errors**; `git diff --check`: **PASS**.
- Test/build evidence was executed during implementation/follow-up, not rerun during this docs-only step.

**Explicit non-guarantees:** SessionTracker and LoginWindow are NOT enrolled; producer retirement is NOT implemented; F2/F3 remain open; no runtime quiescence or restore safety is established. Earlier checkpoint guarantees and evidence remain historical and are not retroactively upgraded.

**Next planned increment:** **4B-5C-2.2 — SessionTracker enrollment/retirement**, subject to its own audit/scope gate.

**Future 4B-5C integration constraints (not implemented):**

- Never wait for DB drain while holding the `TransitionGate` write lock.
- Future production flow must close admission and prove drain before `Arm`; resolver identity must be established before future `CloseAdmission`.
- In the no-lease fresh-resolution path, marker publication currently occurs after the runtime initialization lease is released and is outside DB drain. The already-admitted factory retains its existing lease. DB drain must not be presented as proof that all resolver/marker work has completed.
- 4B-5B-2 implements the backup admission/timer primitive only; 4B-5C-1 supplied the isolated operation-lifetime primitive without production enrollment at that checkpoint. 4B-5C-2.1 now adds production ownership/composition and Auth enrollment only; 4B-5C-2.2 SessionTracker enrollment/retirement is next planned. Producer retirement, unified cutoff/background/updater/multi-context completion remain future work requiring separate scope/approval. Production restore admission remains unavailable until the required complete quiesce boundary exists.

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

**خارج از 4B-2B:** app-lifetime mutex در **4B-3 (`583a7b8`)** commit شده و startup recovery در **4B-4 (`63039d2`)** پیاده، verify و independently reviewed شده است. **4B-5C-2 و بعد — integration آینده و پیاده‌نشده:** restore UI wiring؛ quiesce/drain؛ shutdown redesign؛ حذف legacy production restore path.

### Phase 4 — remaining DoD (هنوز باز)

- AR-2: `EnsureCreated` → `Migrate()`.
- S8: بررسی schema/migration drift. **POSSIBLE:** پوشش migration/schema ممکن است از runtime schema عقب باشد؛ پیش از هر اقدام در AR-2/S8 تأیید شود.
- D1: backup 3-2-1، encryption و secondary backup.
- تست: kill وسط `SaveSale` → DB سالم؛
- تست: restore کامل از backup → همهٔ داده؛
- تست: DB خراب → بازیابی از backup.

### Phase 4 — subsequent integration (4B-5C-2 و بعد؛ هنوز باز)

- **4B-5C-2.1 — IMPLEMENTED / TESTED / INDEPENDENTLY REVIEWED؛ NOT COMMITTED YET:** فقط production operation ownership/composition و Auth enrollment؛ producer retirement هنوز پیاده نشده است.
- **4B-5C-2.2 — next planned:** SessionTracker enrollment/retirement با audit/scope gate مستقل؛ LoginWindow enrollment، caller hardening، restore UI wiring، quiesce/drain و shutdown redesign همچنان بازند.
- فراخوانی موتور swap آفلاین از restore UI/flow برنامه همچنان باز است؛ مصرف intent موجود در startup از 4B-4 وجود دارد.
- جایگزینی مسیر legacy `BackupService.RestoreBackup` و مسیر `Environment.Exit(0)` در `Views/SettingsWindow.axaml.cs`.
- رفع محدودیت‌های Phase 4 شناخته‌شده در بخش ۹.

### Phase 5 به بعد

Audit Trail، structured logging، performance، UX reliability و release hardening طبق roadmap.

## 8. ریسک‌ها و مرزهای باز

- **4B-3 — VERIFICATION PENDING، غیرمسدودکنندهٔ checkpoint:** رفتار cross-user / cross-session / elevation؛ installed GUI startup/shutdown smoke؛ رفتار overlap در Velopack update/restart. هیچ‌کدام verified گزارش نمی‌شوند.
- **4B-4 — VERIFICATION PENDING، غیرمسدودکنندهٔ checkpoint:** direct automated coverage مسیر واقعی `App.OnFrameworkInitializationCompleted` / `InitializeNormalDesktopStartup`؛ پوشش بیشتر path canonicalization/alias؛ installed GUI blocked-window startup/shutdown smoke؛ startup latency/UX برای stagingهای بزرگ. پوشش entry مشترک `App.RunDesktopStartup` معادل پوشش مستقیم callback و بدنهٔ عادی نیست.
- startup اکنون intent موجود را مصرف می‌کند؛ restore end-to-end، UI wiring، quiesce/drain، shutdown redesign و حذف legacy restore هنوز پیاده نشده‌اند و مربوط به 4B-5C و بعد هستند.
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
