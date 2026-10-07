# MASTER-BACKLOG

> **هدف:** مرجع واحد برای همه‌ی یافته‌های ممیزی.
> **آخرین به‌روزرسانی:** Phase 4 remains **IN PROGRESS / NOT restore-safe**. Approved critical path: **F1 → F2 → F3** (F1 CLOSED (`b38d9b5`); F2a CLOSED / COMMITTED / PUSHED `c9b7ccc`; F2b technically complete / UNCOMMITTED, pending commit/push; F3 OPEN). Latest hardening: **5C-2.2 — IMPLEMENTED / TESTED / INDEPENDENTLY REVIEWED / COMMITTED / PUSHED `72d77613340f2d9b88abd64ec39bcbe351156608`** (push confirmed by the user). 5C-2.1/2.2 and their historical evidence remain valid; general per-window enrollment is deferred. checkpointهای پیشین: `583a7b8` (4B-3)، `d472153` (4B-2B)، `59d0dfc` (4B-2A).
> **وضعیت:** Phase 1، Phase 2 و Phase 3 کامل‌اند؛ Phase 4 — Crash Recovery + Backup **IN PROGRESS** است و تکمیل نشده.
> نام یافته‌ها و ارجاع‌های قدیمی، سابقهٔ ممیزی‌اند؛ ستون وضعیت و توضیحات closure، نتیجهٔ فعلی را مشخص می‌کنند.

---

## منبع ۱ — AUDIT-REPORT.md

| ID    | یافته                         | فایل                       | شدت | Phase  | وضعیت                                |
| ----- | ----------------------------- | -------------------------- | --- | ------ | ------------------------------------ |
| AR-1  | نبود Transaction دور SaveSale | POSWindow                  | 🔴  | 2      | ✅                                   |
| AR-2  | EnsureCreated به جای Migrate  | DatabaseService            | 🔴  | Deferred | OPEN — approved Phase-4 scope reset |
| AR-3  | XSS در ۴ پنجره                | \*HistoryWindow            | 🟠  | ad-hoc | ✅                                   |
| AR-4  | RestoreBackup ناایمن          | BackupService              | 🟠  | 4      | ❌                                   |
| AR-5  | Unique Index InvoiceNumber    | AppDbContext               | 🟠  | 3      | ✅ یکتایی در SaleOperations؛ شرح زیر |
| AR-6  | N+1 در CreateProductTile      | POSWindow                  | 🟠  | 7      | ❌                                   |
| AR-7  | Race بررسی موجودی             | POSWindow / TransferWindow | 🟠  | 3      | ✅ فروش و انتقال عادی                |
| AR-8  | Code Signing                  | release.yml                | 🟡  | 10     | ❌                                   |
| AR-9  | API key plaintext             | AppPreferences             | 🟡  | ad-hoc | ✅                                   |
| AR-10 | admin/admin                   | AuthServiceInitializer     | 🟡  | ad-hoc | ✅                                   |
| AR-11 | SaleWindow کد مرده            | Views                      | 🟢  | ad-hoc | ✅                                   |

---

## منبع ۲ — Audit 6.A (فاز ۱)

| ID    | یافته                         | فایل:خط        | شدت | Phase  | وضعیت |
| ----- | ----------------------------- | -------------- | --- | ------ | ----- |
| 6.A1  | Revenue خام                   | POSWindow:1170 | 🔴  | 1      | ✅ B3 |
| 6.A2  | Cost از LockedCost            | POSWindow:1171 | ✅  | 1      | ✅    |
| 6.A3  | Profit خام                    | POSWindow:1172 | ✅  | 1      | ✅    |
| 6.A4  | تخفیف فقط از Profit           | POSWindow:1174 | 🔴  | 1      | ✅ B4 |
| 6.A5  | Revenue خام ذخیره             | POSWindow:1190 | 🔴  | 1      | ✅ B3 |
| 6.A6  | Profit invariant نقض          | POSWindow:1191 | 🔴  | 1      | ✅ B4 |
| 6.A7  | متغیرهای مرده                 | POSWindow:1203 | 🟡  | 1      | ✅ B6 |
| 6.A8  | Customer.TotalPurchasedAmount | POSWindow:1109 | ✅  | 1      | ✅    |
| 6.A9  | Sale.DiscountAmount غایب      | POSWindow      | 🔴  | 1      | ✅ B5 |
| 6.A10 | rounding غایب                 | POSWindow      | 🟠  | 1      | ✅ B7 |
| 6.A11 | Reversal هیچ‌جا ساخته نمی‌شود | All            | 🔴  | Future | ❌    |

---

## منبع ۳ — ۱۲ بُعدی

| ID  | بُعد                      | Phase        | وضعیت                       |
| --- | ------------------------- | ------------ | --------------------------- |
| D1  | Backup 3-2-1              | Deferred | OPEN — approved Phase-4 scope reset |
| D2  | Structured Logging        | 6            | ❌                          |
| D3  | Alert System              | Future       | خارج                        |
| D4  | Audit Trail               | 5            | ❌                          |
| D5  | EULA/Privacy              | 10           | ❌                          |
| D6  | Tax/Legal                 | Business Req | 🟡                          |
| D7  | Inventory Concurrency     | 3            | ✅ محدودهٔ فروش/انتقال عادی |
| D8  | Sale Transaction Boundary | 2            | ✅                          |
| D9  | Idempotency               | 3            | ✅ عملیات فروش              |
| D10 | Time/Date edge cases      | 1            | ❌                          |
| D11 | UX Loading States         | 8            | ❌                          |
| D12 | Future-Proofing           | Future       | خارج                        |

---

## منبع ۴ — PHASE-1 (B1-B7)

| ID  | باگ                  | فایل              | Phase | وضعیت |
| --- | -------------------- | ----------------- | ----- | ----- |
| B1  | SalePrice=0 Override | PricingCalculator | 1     | ✅    |
| B2  | ToEven rounding      | PricingCalculator | 1     | ✅    |
| B3  | Revenue خام          | POSWindow         | 1     | ✅    |
| B4  | Profit invariant     | POSWindow         | 1     | ✅    |
| B5  | DiscountAmount غایب  | POSWindow+Sale    | 1     | ✅    |
| B6  | متغیرهای مرده        | POSWindow         | 1     | ✅    |
| B7  | Rounding غایب        | POSWindow         | 1     | ✅    |

---

## منبع ۵ — PHASE-1 جانبی

| ID  | یافته                            | Phase     | وضعیت                                              |
| --- | -------------------------------- | --------- | -------------------------------------------------- |
| S1  | POSCartItem.HasDiscount          | Pre-Phase | ✅                                                 |
| S2  | POSCartItem.DiscountPct          | Pre-Phase | ✅                                                 |
| S3  | SaleCartItem مدل موازی           | Pre-Phase | ✅                                                 |
| S4  | Negative-stock guard             | 3         | ✅ کنترل authoritative داخل تراکنش                 |
| S5  | Optimistic Concurrency Token     | 3         | ✅ هدف هم‌زمانی با تراکنش SQLite؛ token پیاده نشده |
| S6  | StockAlert Warning در Fixed-only | 7         | ❌                                                 |
| S7  | Reversal Sign Contradiction      | Future    | ❌                                                 |
| S8  | Schema Drift                     | Deferred | OPEN — approved Phase-4 scope reset |

---

## نمای Phase-Centric

### Phase 1 ✅ COMPLETE

- [B1-B7] رفع شده

### Pre-Phase — Cleanup

- [x] [S1] POSCartItem.HasDiscount
- [x] [S2] POSCartItem.DiscountPct
- [x] [S3] SaleCartItem

شواهد: [PRE-PHASE-2-CLEANUP.md](PRE-PHASE-2-CLEANUP.md)

### Phase 2 — Transaction Boundary ✅ COMPLETE

- [x] [AR-1] نبود Transaction دور SaveSale — ✅
- [x] [D8] Sale Transaction Boundary — ✅

شواهد: [PHASE-2-TRANSACTION-BOUNDARY.md](PHASE-2-TRANSACTION-BOUNDARY.md)

### Phase 3 — Concurrency + Idempotency ✅ COMPLETE

- [x] 3A (`45c6511`): جلوگیری از ورود مجدد به پرداخت در `POSWindow.OnPayClick`.
- [x] 3B-1 (`c3268af`): جدول `SaleOperations` با کلید یکتای `OperationId`، fingerprint اجباری و index یکتای `InvoiceNumber`؛ آماده‌سازی و اعتبارسنجی schema برای دیتابیس جدید/قدیمی.
- [x] 3B-2 (`5ff9a68`): snapshot/fingerprint، replay بدون ثبت مجدد، رد payload متفاوت، تفکیک pending قطعی/نامعلوم، بازیابی برخورد شمارهٔ فاکتور و کنترل مجموع ردیف‌های هم‌کالا.
- [x] 3C (`da7d5b4`): کنترل موجودی و ثبت انتقال در یک تراکنش؛ اثبات رقابت فروش‌های مستقل بدون تغییر production فروش؛ تفکیک شکست ثبت از خطای پس از commit.
- [x] [AR-7 / D7 / S4 / S5]: تراکنش SQLite پیش از خواندن موجودی، جایگزین الزام طراحی قدیمی RowVersion شد. **RowVersion یا optimistic concurrency token اضافه نشده است.**

**تطبیق AR-5:** یکتایی در سطح عملیات/فاکتور در `SaleOperations` اعمال می‌شود؛ `Sales.InvoiceNumber` به‌تنهایی unique نیست، چون یک فاکتور چند ردیف دارد. رکوردهای تاریخی backfill نشده‌اند؛ سرویس فروش برخورد با شماره‌های تاریخی `Sales` را نیز بررسی می‌کند. این closure ادعای اصلاح همهٔ schemaهای legacy یا اجرای `Migrate()` نیست.

**شواهد Phase 3C:** build با 0 errors / 0 warnings؛ 134/134 tests passed، 0 failed / 0 skipped؛ `git diff --check` clean؛ final adversarial review: PASS. خروجی build/test/check از اجرای ثبت‌شدهٔ Phase 3C است؛ PASS بازبینی نهایی طبق تأیید کاربر در درخواست Documentation Closure ثبت شده است. در این کار مستندسازی build/test دوباره اجرا نشده‌اند.

خلاصهٔ Phase 3 در [PHASE-3-CONCURRENCY-IDEMPOTENCY.md](PHASE-3-CONCURRENCY-IDEMPOTENCY.md) آمده است. نگاشت دقیق رفتارها به فایل‌ها و نام تست‌ها، منشأ شواهد و حدود تضمین در [AUDIT-RECONCILIATION.md](AUDIT-RECONCILIATION.md) و راهنمای ادامه در [AI-HANDOFF.md](AI-HANDOFF.md) آمده است.

**خارج از closure:** exactly-once/recovery عمومی انتقال، بازیابی pending فروش پس از restart، crash/restore و schema drift، writerهای خارجی و همهٔ مسیرهای ویرایش موجودی. تست دستی UI/چاپ و ماتریس مستقل WAL/DELETE/timeout، جزو شواهد این closure نیستند. این موارد با موفقیت تست رقابت دو اتصال مستقل یکسان نیستند.

### Phase 4 — Crash Recovery + Backup — IN PROGRESS (تکمیل‌نشده)

**Checkpoint های انجام‌شده (4A-1 تا 4B-2A: کد + تست در commit `59d0dfc`؛ 4B-2B: `d472153`؛ 4B-3: `583a7b8`؛ 4B-4: `63039d2`؛ 4B-5A: committed `36f0e7f`):**

- [x] 4A-1 (`0944a7e`): انتشار اتمیک بکاپ SQLite — staging، اعتبارسنجی و publish ایمن.
- [x] 4A-2 (`05212da`): هویت canonical دیتابیس و توقف امن startup پیش از settings/backup/auth.
- [x] 4A-3 (`3466604`): قرارداد دوام SQLite — `journal_mode=WAL` + `synchronous=FULL` با read-back.
- [x] 4A-4 (`22ca6fa`): قابلیت اطمینان چرخهٔ حیات بکاپ — single-flight، ردیابی نسل تغییرات، پاک‌سازی staging یتیم و لاگ خطا.
- [x] 4B-1 (`461670c`): آماده‌سازی بازیابی امن پیش از تعویض — اعتبارسنجی، اسنپ‌شات ایمنی WAL-سازگار و گارد هویت فایل/hard-link؛ دیتابیس زنده دست‌نخورده می‌ماند.
- [x] 4B-2A (`59d0dfc`): بنیاد intent بازیابی ماندگار — flush → SHA-256 → انتشار اتمیک intent → مسلح‌سازی؛ gate پذیرش دیتابیس.
- [x] 4B-2B (`d472153`): offline file-level recovery engine (`RestoreRecoveryService.Recover`) — پس از intent ماندگار forward-only (forward-complete یا BLOCK، هرگز rollback)؛ وضعیت منتشرشده با SHA-256 مورد انتظار دیتابیس زنده و نبودن sidecarهای زندهٔ `-wal`/`-shm`/`-journal` تأیید می‌شود؛ فقط tombstone/incoming artifactهای دقیقاً operation-owned (بدون wildcard)؛ cleanup plan-then-execute؛ وضعیت مبهم/ناایمن fail-closed و مسلح؛ حذف intent آخرین mutation موفق؛ نبودن `SafetyBackupPath` مانع forward completion نیست.

- [x] 4B-3 (`583a7b8`): app-lifetime Windows mutex `Global\ShopManager.ApplicationLifetime`؛ acquisition پس از `Velopack.Run()` و پیش از `BuildAvaloniaApp()`؛ Busy/Error → exit code 2/3 بدون startup admission؛ abandoned ownership پذیرفته؛ guard موفق برای عمر process strongly rooted؛ harness واقعی چندprocess بدون production DB/mutex. **Implemented / verified / checkpoint-ready**.
- [x] 4B-4 (`63039d2`): startup recovery integration; implemented / verified / independently reviewed / checkpoint-ready. Evidence and residual verification are recorded below; Phase 4 remains IN PROGRESS.
- [x] 4B-5A (`36f0e7f`): runtime DbContext admission + context drain only; implemented / verified / independently reviewed / committed; NOT restore-safe.
- [x] 4B-5B-1 (`34faeee`; COMMITTED / PUSHED): resolver poisoning fix; implemented / verified / independently reviewed; NOT restore-safe.
- [x] **4B-5B-2**: backup admission/timer boundary; IMPLEMENTED / VERIFIED / INDEPENDENTLY REVIEWED; **COMMITTED `287764c`**, **NOT restore-safe**. Final evidence: 26/26 targeted, 172/172 relevant, 384/384 full; re-review PASS WITH FINDINGS; M1/M2 CLOSED.
- [x] **4B-5C-1 — COMMITTED `33e12b6`**: isolated RuntimeOperationGate primitive; implemented / verified / independently reviewed; NOT runtime-quiescent / NOT restore-safe. Final evidence: targeted 29/29, relevant 118/118, full 413/413 PASS, 0 failed/skipped; build 0 warnings/errors; diff-check PASS; user-confirmed review PASS WITH FINDINGS, M1/M2 re-reviewed CLOSED; M3 stress linearizability remains a test gap.
- [x] **4B-5C-2.1 — IMPLEMENTED / TESTED / INDEPENDENTLY REVIEWED; COMMITTED / PUSHED `a8e3d02`:** production operation ownership/composition and Auth enrollment only; F1/M1 CLOSED, L3 added; targeted 52/52, relevant 200/200, full 465/465 PASS; no runtime quiescence or restore safety.
- [x] **4B-5C-2.2 — IMPLEMENTED / TESTED / INDEPENDENTLY REVIEWED; COMMITTED / PUSHED `72d7761`:** SessionTracker tick enrollment and generation/single-flight safety; Stop is not completion proof; conditional parent-bound Auth logout protects replacement sessions without nested admission; cleanup uncertainty remains fail-closed. New tests 47/47, Auth 52/52, relevant 148/148, full 512/512, independent targeted review run 99/99 PASS; build 0 warnings/errors; independent review PASS with no Critical/High/Medium/Low findings (user-confirmed); diff-check PASS. Terminal producer retirement/full quiescence deferred; NOT restore-safe.
- [ ] **F1 → F2 → F3:** approved critical path (F1 CLOSED (`b38d9b5`); F2a CLOSED / COMMITTED / PUSHED `c9b7ccc`; F2b technically complete / UNCOMMITTED, pending commit/push; F3 OPEN); general per-window enrollment is deferred to reliability backlog.

**شواهد تاریخی 4B-2B (commit `d472153`؛ 270 تست در آن source tree commit‌شده؛ evidence قدیمی 246/246 مربوط به `59d0dfc` است):** `RestoreRecoveryServiceTests` 42 passed؛ کل `ShopManager.Domain.Tests` 270 passed؛ build غیرافزایشی solution با 0 warnings / 0 errors؛ adversarial/final review: PASS بدون issue مسدودکنندهٔ Critical/High/Medium. این evidence تکمیل Phase 4 را ادعا نمی‌کند.

**شواهد تاریخی 4B-3 پس از test hardening، پیش از commit `583a7b8`:** targeted `ApplicationInstanceGuardTests` 21 passed؛ full `ShopManager.Domain.Tests` 291 passed (هر دو 0 failed / 0 skipped)؛ build غیرافزایشی 0 warnings / 0 errors؛ `git diff --check` exit code 0؛ adversarial review طبق تأیید کاربر: **PASS WITH FINDINGS، بدون Critical یا High**؛ follow-up تست بدون تغییر production تکمیل شد. شواهد زمان verification حفظ شده‌اند؛ در documentation sync build/test دوباره اجرا نشده است.

**4B-4 — implemented / verified / independently reviewed / checkpoint-ready؛ committed `63039d2`:** recovery پیش از resolver/SQLite؛ admission فقط `NoIntent` یا `Completed` در حالت unarmed؛ توقف fail-closed برای `Blocked` و خطای غیرمنتظره. registered identity روی همان intent مصرف‌شده، پس از arming و پیش از mutation، بدون DatabaseService/SQLite بررسی می‌شود؛ live مفقود پس از tombstone پیش از resolver قابل بازیابی است. invariantهای 4B-2B و رفتار 4B-3 حفظ شده‌اند.

**شواهد 4B-4:** targeted startup/recovery/guard به‌ترتیب **29 / 44 / 21 passed**؛ full `ShopManager.Domain.Tests` **322 passed**؛ همگی 0 failed / 0 skipped؛ build غیرافزایشی **0 warnings / 0 errors**؛ diff-check **exit code 0**؛ independent adversarial review طبق تأیید کاربر: **PASS WITH FINDINGS، بدون Critical یا High و بدون checkpoint-blocking finding**. این نتایج در documentation sync دوباره اجرا نشده‌اند.

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

**Next uncompleted work (approved scope reset):** F2b commit/push, then F3 final verification/closure (F1 and F2a CLOSED; F2b technically complete / UNCOMMITTED). General per-window enrollment is deferred to reliability backlog; source implementation needs separate scope/approval.

**Final restore constraints (F1 — now implemented, verified and committed in `b38d9b5c5716a01c5e688c5d00496b5a36033d57`; retained as design constraints):**

- Never wait for DB drain while holding the `TransitionGate` write lock.
- Future production flow must close admission and prove drain before `Arm`; resolver identity must be established before future `CloseAdmission`.
- In the no-lease fresh-resolution path, marker publication currently occurs after the runtime initialization lease is released and is outside DB drain. The already-admitted factory retains its existing lease. DB drain must not be presented as proof that all resolver/marker work has completed.
- Existing 5C-2.1/2.2 hardening and evidence are retained. F1/F2/F3 is the approved critical path; general per-window enrollment/lifetime architecture is deferred. Terminal restore boundary is implemented by F1 (committed in `b38d9b5c5716a01c5e688c5d00496b5a36033d57`, verified); the swap itself remains startup-recovery work; F2b is technically complete / UNCOMMITTED, pending commit/push; F3 remains OPEN.
- Keep cutoff closed through terminal restore exit. Timeout or cleanup uncertainty => do not `Arm`; after persistent intent exists, or publication is uncertain, normal DB work must not resume.
- Ordering: runtime/DB cutoff and drain → `PrepareRestore` with backup admission still open → backup cutoff and drain → release SQLite pools → `Arm` → restore-specific exit → actual swap at next startup. Updater Apply/Restart and queued lifecycle transitions must not race restore.

**VERIFICATION PENDING — 4B-4:** direct automated coverage مسیر واقعی `App.OnFrameworkInitializationCompleted` / `InitializeNormalDesktopStartup`؛ تست بیشتر canonicalization/alias؛ installed GUI blocked-window startup/shutdown smoke؛ startup latency/UX برای stagingهای بزرگ. پوشش entry مشترک startup به معنی پوشش مستقیم callback/بدنهٔ عادی نیست.

**VERIFICATION PENDING — غیرمسدودکنندهٔ 4B-3:** cross-user / cross-session / elevation؛ installed GUI startup/shutdown smoke؛ Velopack update/restart overlap. این موارد verified نیستند.

**Approved Phase-4 DoD — F1 CLOSED; F2a CLOSED; F2b technically complete, pending commit/push; F3 OPEN:**

- [x] **F1 — Final Restore Boundary — F1 IMPLEMENTED / VERIFIED / COMMITTED (`b38d9b5c5716a01c5e688c5d00496b5a36033d57`); final independent review PASS WITH FINDINGS (Low only; Critical/High/Medium 0; no blockers); targeted 31 passed, relevant 307 passed, full 547 passed (0 failed / 0 skipped), non-incremental build 0 warnings / 0 errors, git diff --check PASS. No live DB swap occurs in the current process; the actual swap remains startup recovery work; the terminal restore boundary is fail-closed. B1/B2 review findings were fixed and re-reviewed; remaining Low findings are accepted residual risks. F2b is technically complete / UNCOMMITTED, pending commit/push; F3 remains OPEN; Phase 4 remains IN PROGRESS / NOT closed.** Scope: replace legacy SettingsWindow restore; terminal single-flight; block relevant new work; stop relevant producers; drain existing runtime/DB/backup work; ordered preparation, pool release, persistent intent and restore-specific exit; startup recovery performs the swap.
- [ ] **F2 — Backup Compatibility + Broken-DB Recovery:** prove application compatibility; bounded recovery when the current DB is broken; preserve damaged/original files fail-closed. **F2a CLOSED / IMPLEMENTED / TESTED / INDEPENDENTLY REVIEWED (PASS WITH FINDINGS), COMMITTED / PUSHED `c9b7ccc` — `feat: validate restore backup compatibility`; F2b IMPLEMENTED / TESTED / INDEPENDENTLY REVIEWED — PASS WITH FINDINGS; technically complete / UNCOMMITTED, pending commit/push.** See `PHASE-4-CRASH-RECOVERY-BACKUP.md`.

**F2b checkpoint (2026-10-07) — IMPLEMENTED / TESTED / INDEPENDENTLY REVIEWED — PASS WITH FINDINGS; UNCOMMITTED, pending commit/push.** Final independent-review disposition supplied by the user: Critical/High/Medium = none; blocker = NO. F1 and F2a are CLOSED; F3 remains OPEN; Phase 4 remains IN PROGRESS / NOT CLOSED / NOT restore-safe.

- Evidence from the implementation/fix session (not rerun in this docs-only step): targeted recovery/startup **88/88**, relevant F1/F2a/F2b regression **325/325**, full suite **589/589**, all with 0 failed / 0 skipped; `dotnet build ShopManager.slnx --no-incremental`: **0 warnings / 0 errors**; `git diff --check`: **exit 0**.
- Final H1 fix: Incoming alone no longer proves recovery resume or bypasses pristine safety. Incoming beside the old regular live DB blocks before any destructive move; legitimate tombstone-backed resume remains supported.
- Final M2 fix: blocked-startup remains displayable when backup enumeration/access/I/O fails; it shows a concise backup-unavailable message and Restore remains unavailable.
- Accepted residual findings: an external writer outside ShopManager may mutate DB/sidecars during byte-level preservation; in-process gates do not cover external writers. The blocked-startup backup enumeration error path has no automated Avalonia UI test. Remaining Low UI/message findings are non-blocking and may be verified in F3.
- Manual blocked-startup UI smoke: **VERIFICATION PENDING for F3**; not performed. Remaining unrelated stress/crash verification is deferred to F3. No F2b commit hash or push is claimed.

- [ ] **F3 — Final Verification & Closure:** integration/end-to-end restore, real process-crash/kill where required, UI smoke, full tests, build **0 warnings / 0 errors**, independent review and final documentation reconciliation.

The user-approved scope reset is intentional, not abandonment of restore safety. No current evidence proves per-window enrollment mandatory. General enrollment/lifetime architecture moves to deferred reliability backlog. AR-2/S8 (migrations/schema drift) and D1 (encryption/secondary backup/3-2-1) remain OPEN but are deferred from this closure; F2 compatibility is still mandatory. Completed 5C-2.1/2.2 remain valid hardening. See [approved F1 → F2 → F3 plan](PHASE-4-CRASH-RECOVERY-BACKUP.md#approved-completion-plan) and [deferred backlog](MASTER-BACKLOG.md#deferred-work-from-phase-4-scope-reset). This docs-only approval does not authorize source implementation.

### Phase 5 — Audit + Security

- [D4] Audit Trail

### Phase 6 — Logging

- [D2] Structured Logging

### Phase 7 — Performance

- [AR-6] N+1 CreateProductTile
- [S6] StockAlert Warning

### Phase 8 — Avalonia Reliability

- [D11] UX Loading States
- [ ] Deferred from Phase 4: general per-window operation enrollment (Login/Users/Items/Cashbox/POS/Transfer/Reports/etc.) and general UI/Auth caller hardening; no current evidence proves enrollment mandatory for safe terminal restore.
- [ ] General operation-lifetime/producer-retirement architecture and broader shutdown/updater reliability; restore-specific coordination remains mandatory in F1. Retain completed 5C-2.1/2.2 hardening and evidence.

### Phase 9 — Tests

- گسترش Coverage

### Phase 10 — Release Hardening

- [AR-8] Code Signing
- [D5] EULA/Privacy

### Ad-hoc (خارج از Phase)

- [AR-3] XSS (فاز ۱ قدیمی)
- [AR-9] API key (حذف شد)
- [AR-10] admin/admin (فاز ۱)
- [AR-11] SaleWindow (فاز ۲ قدیمی)

### Deferred work from Phase-4 scope reset

These items remain OPEN; deferral is user-approved scope reduction, not implementation or closure.

- [ ] **AR-2/S8:** `EnsureCreated → Migrate()`, DB-from-zero migration coverage and general schema/migration drift. Audit current runtime versus migration schema before implementation. F2 backup compatibility is not deferred.
- [ ] **D1:** backup encryption (DPAPI/AES), secondary USB backup and broader 3-2-1 policy.
- General per-window enrollment/lifetime work is tracked under **Phase 8 — Avalonia Reliability** above; it is outside the F1/F2/F3 critical path.

### Future Backlog

- [D3] Alert System
- [D12] Future-Proofing
- [6.A11] Reversal Implementation
- [S7] Reversal Sign Contradiction

---

**پایان سند.**
