# MASTER-BACKLOG

> **هدف:** مرجع واحد برای همه‌ی یافته‌های ممیزی.
> **آخرین به‌روزرسانی:** Phase 4 همچنان **IN PROGRESS** است؛ آخرین checkpoint پیاده‌شده 4B-5B-2 **IMPLEMENTED / VERIFIED / INDEPENDENTLY REVIEWED / UNCOMMITTED** است و **NOT restore-safe** است. آخرین checkpoint کد production commit‌شده و pushed، 4B-5B-1 در `34faeee` است؛ 4B-5C آینده است؛ restore UI هنوز در scope نیست. checkpointهای پیشین: `583a7b8` (4B-3)، `d472153` (4B-2B)، `59d0dfc` (4B-2A).
> **وضعیت:** Phase 1، Phase 2 و Phase 3 کامل‌اند؛ Phase 4 — Crash Recovery + Backup **IN PROGRESS** است و تکمیل نشده.
> نام یافته‌ها و ارجاع‌های قدیمی، سابقهٔ ممیزی‌اند؛ ستون وضعیت و توضیحات closure، نتیجهٔ فعلی را مشخص می‌کنند.

---

## منبع ۱ — AUDIT-REPORT.md

| ID    | یافته                         | فایل                       | شدت | Phase  | وضعیت                                |
| ----- | ----------------------------- | -------------------------- | --- | ------ | ------------------------------------ |
| AR-1  | نبود Transaction دور SaveSale | POSWindow                  | 🔴  | 2      | ✅                                   |
| AR-2  | EnsureCreated به جای Migrate  | DatabaseService            | 🔴  | 4      | ❌                                   |
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
| D1  | Backup 3-2-1              | 4            | ❌                          |
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
| S8  | Schema Drift                     | 4         | ❌                                                 |

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
- [x] **4B-5B-2**: backup admission/timer boundary; IMPLEMENTED / VERIFIED / INDEPENDENTLY REVIEWED; **UNCOMMITTED**, **NOT restore-safe**. Final evidence: 26/26 targeted, 172/172 relevant, 384/384 full; re-review PASS WITH FINDINGS; M1/M2 CLOSED.

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
- Current sequence: **4B-5B-1 — resolver poisoning fix (`34faeee`; COMMITTED / PUSHED)**; **4B-5B-2 — backup admission/timer boundary (IMPLEMENTED / VERIFIED / INDEPENDENTLY REVIEWED / UNCOMMITTED)**; **4B-5C — unified cutoff/background/updater/multi-context completion (future)**. Production `PrepareRestore` / `Arm` remains unavailable until the complete required quiesce boundary exists. Restore UI remains future work.

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

**IMPLEMENTED / VERIFIED / INDEPENDENTLY REVIEWED / UNCOMMITTED. NOT restore-safe / NOT full quiesce.** Phase 4 remains **IN PROGRESS**.

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

**Remaining boundary:** no production cutoff / `PrepareRestore` / `Arm` wiring exists. DB/updater/shutdown/background/multi-context coordination remains for **4B-5C**. Legacy `BackupService.RestoreBackup` remains outside this guarantee; the preparation algorithm is unchanged. Backup/timer drain does not establish restore safety or full quiesce, protect the entire Prepare-to-Arm interval, or prove successful deletion of all best-effort temporary artifacts.

**Future 4B-5C integration constraints (not implemented):**

- Never wait for DB drain while holding the `TransitionGate` write lock.
- Future production flow must close admission and prove drain before `Arm`; resolver identity must be established before future `CloseAdmission`.
- In the no-lease fresh-resolution path, marker publication currently occurs after the runtime initialization lease is released and is outside DB drain. The already-admitted factory retains its existing lease. DB drain must not be presented as proof that all resolver/marker work has completed.
- 4B-5B-2 implements the backup admission/timer primitive only; 4B-5C remains unified cutoff/background/updater/multi-context completion and requires separate scope/approval. Production restore admission remains unavailable until the required complete quiesce boundary exists.

**VERIFICATION PENDING — 4B-4:** direct automated coverage مسیر واقعی `App.OnFrameworkInitializationCompleted` / `InitializeNormalDesktopStartup`؛ تست بیشتر canonicalization/alias؛ installed GUI blocked-window startup/shutdown smoke؛ startup latency/UX برای stagingهای بزرگ. پوشش entry مشترک startup به معنی پوشش مستقیم callback/بدنهٔ عادی نیست.

**VERIFICATION PENDING — غیرمسدودکنندهٔ 4B-3:** cross-user / cross-session / elevation؛ installed GUI startup/shutdown smoke؛ Velopack update/restart overlap. این موارد verified نیستند.

**باقی‌مانده (pending) — Phase 4 کامل نیست:**

- [ ] **4B-5C و بعد — future work:** restore UI wiring، quiesce/drain، shutdown redesign و حذف legacy restore path؛ هنوز پیاده نشده‌اند و نیازمند scope مستقل‌اند.
- [ ] [AR-4] جایگزینی `BackupService.RestoreBackup` قدیمی و مسیر `SettingsWindow.axaml.cs:290` با `Environment.Exit(0)`.
- [ ] [AR-2] EnsureCreated → Migrate().
- [ ] [S8] Schema Drift / migrations.
- [ ] [D1] Backup 3-2-1 + رمزنگاری + بکاپ ثانویه.
- [ ] تست‌های crash (kill وسط SaveSale)، restore کامل و بازیابی از DB خراب.

شواهد و جزئیات: [PHASE-4-CRASH-RECOVERY-BACKUP.md](PHASE-4-CRASH-RECOVERY-BACKUP.md)

### Phase 5 — Audit + Security

- [D4] Audit Trail

### Phase 6 — Logging

- [D2] Structured Logging

### Phase 7 — Performance

- [AR-6] N+1 CreateProductTile
- [S6] StockAlert Warning

### Phase 8 — Avalonia Reliability

- [D11] UX Loading States

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

### Future Backlog

- [D3] Alert System
- [D12] Future-Proofing
- [6.A11] Reversal Implementation
- [S7] Reversal Sign Contradiction

---

**پایان سند.**
