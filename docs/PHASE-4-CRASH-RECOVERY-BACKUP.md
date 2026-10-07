# Phase 4 — Crash Recovery + Backup

## Status

**Phase 4 — Crash Recovery + Backup: IN PROGRESS / NOT restore-safe — NOT complete.**

- Branch: `phase/4-crash-recovery-backup`.
- Latest implemented checkpoint: **F2b — IMPLEMENTED / TESTED / INDEPENDENTLY REVIEWED — PASS WITH FINDINGS; UNCOMMITTED, pending commit/push**. Final review: Critical/High/Medium = none; blocker = NO.
- Latest committed/pushed production-code checkpoint: **F2a CLOSED**, `c9b7ccc` — `feat: validate restore backup compatibility` (push confirmed by the user). Historical 5C-2.1/2.2 hardening remains valid.
- **F1 — Final Restore Boundary: IMPLEMENTED / VERIFIED / COMMITTED (`b38d9b5c5716a01c5e688c5d00496b5a36033d57`, `feat: implement final restore boundary`).** Final review PASS WITH FINDINGS, Low only, no blockers. Phase 4 remains IN PROGRESS / NOT closed; F2b is technically complete / UNCOMMITTED, pending commit/push; F3 is OPEN.
- **F2a — Backup Compatibility: CLOSED / IMPLEMENTED / TESTED / INDEPENDENTLY REVIEWED — COMMITTED / PUSHED `c9b7ccc`**, `feat: validate restore backup compatibility`. Independent review PASS WITH FINDINGS; no Critical/High/Medium production-code findings; the M1 test gap was fixed.
- **F2b — Broken-DB Recovery: IMPLEMENTED / TESTED / INDEPENDENTLY REVIEWED — PASS WITH FINDINGS; UNCOMMITTED, pending commit/push**. Final review: Critical/High/Medium = none; blocker = NO. Phase 4 remains IN PROGRESS / NOT CLOSED.
- No Phase 4 completion tag exists. The last completion tag in the repository is
  `phase-3-concurrency-idempotency-complete`.
- Documented checkpoints: **4A-1 through 4B-5C-2.2**; **5C-2.1 is COMMITTED / PUSHED; 5C-2.2 is COMMITTED / PUSHED `72d7761`**. 4A-1 through 4B-2A are committed
  (last commit `59d0dfc`); **4B-2B is committed in `d472153`; 4B-3 in `583a7b8`; 4B-4 in `63039d2`; 4B-5A in `36f0e7f`; 4B-5B-1 committed/pushed in `34faeee`**; 4B-5B-2 is committed in `287764c`; 4B-5C-1 is committed in `33e12b6`.
- Next uncompleted: **F2b commit/push**, then **F3 — Final Verification & Closure**. F1 and F2a are CLOSED; F2b is technically complete / UNCOMMITTED.
- Remaining work: approved F1/F2/F3 DoD; general per-window enrollment and lifetime architecture are deferred.

This document records checkpoints 4A-1 through 4B-2A as present in the committed source
tree at commit `59d0dfc`, 4B-2B at commit `d472153`, 4B-3 at commit `583a7b8`,
4B-4 at commit `63039d2`, verified, independently reviewed 4B-5A at commit `36f0e7f`,
4B-5B-1 committed/pushed in `34faeee`, and implemented, verified, independently reviewed
4B-5B-2 at commit `287764c`, and 4B-5C-1 at commit `33e12b6` (COMMITTED). It also records
4B-5C-2.1 as implemented, tested and independently reviewed, **COMMITTED / PUSHED `a8e3d02eb7059d5eee8504ea712f85499f9119e4`**, and 4B-5C-2.2 as **IMPLEMENTED / TESTED / INDEPENDENTLY REVIEWED — COMMITTED / PUSHED `72d7761`**.
Historical checkpoint guarantees are not extended retroactively. Phase 4 is not complete.

## Purpose and scope

Original roadmap scope included migrations, WAL, backup verification, encryption and secondary backup. The user-approved scope reset narrows closure to crash-safe backup and genuinely safe terminal restore for this single-process desktop POS. Deferred items remain OPEN; completed hardening and historical evidence are retained.

### Phase 4 checkpoint boundary (authoritative: `docs/PROJECT-CONTEXT.md`)

**4B-2B** — offline recovery engine (**implemented and verified; committed in `d472153`**):
- offline recovery engine (`RestoreRecoveryService.Recover`);
- file-level forward-completion/swap of the live DB (`shop.db` / `-wal` / `-shm`);
- operation-owned tombstones;
- SHA-256 staging fingerprint remains authoritative;
- after durable intent: **forward-complete or BLOCK — no rollback**.

**4B-3 — app-lifetime Windows mutex (implemented and verified; committed in `583a7b8`):**
- fixed identity `Global\ShopManager.ApplicationLifetime`;
- acquired after `Velopack.Run()` returns and before `BuildAvaloniaApp()`;
- Busy → exit code 2 with zero application startup admission;
- acquisition Error → exit code 3, fail closed; no fallback, retry or waiting;
- abandoned ownership is accepted, without implying database health;
- successful guard is strongly rooted in `Program` for process lifetime;
- real multi-process harness uses unique test mutex names and no production DB/mutex.

**4B-4 — startup recovery integration (implemented, verified, independently reviewed,
checkpoint-ready; committed in `63039d2`):**
- Production order: Velopack → acquired/retained `ApplicationInstanceGuard` → Avalonia
  initialization → first desktop startup gate (`StartupRecoveryCoordinator`) →
  `DatabaseService.BlockedReason` → `CreateContext` → settings/theme → backup/timer →
  auth → normal window → existing shutdown registration.
- Only `NoIntent` or `Completed` while unarmed admits normal startup.
- `Blocked`, malformed/unreadable intent and unexpected recovery/validator errors fail
  closed: no resolver, context, settings/theme, backup/timer, auth/session, normal window
  or normal shutdown registration; startup failure is presented/logged.
- Registered identity is read directly from app-owned `database-location.json`, without
  DatabaseService resolution, SQLite or marker publication. Version 1 and a non-empty,
  fully-qualified `DataFolder` are required; canonical expected `shop.db` is compared
  with Windows `OrdinalIgnoreCase` semantics. No candidate selection or fallback.
- Validation observes the same intent consumed by `Recover`, after arming and before
  recovery artifact mutation. Live `shop.db` existence is not required: recovery can
  complete a prior tombstone transition before the resolver sees the database.
- The narrow identity-admission hook preserves 4B-2B forward-complete invariants;
  4B-3 guard behavior and the existing normal startup/shutdown body remain intact.

**Remaining completion work (approved scope reset):** F2b commit/push and F3 final verification/closure, detailed below; F1 and F2a are CLOSED. General per-window enrollment is outside the critical path.

## Checkpoints implemented (through 4B-5C-2.2; 5C-2.1 COMMITTED / PUSHED; 5C-2.2 COMMITTED / PUSHED `72d7761`)

| Checkpoint | Commit    | Change                                                                                                                                                                                                                                                                             | Tests                                                                 |
| ---------- | --------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | --------------------------------------------------------------------- |
| 4A-1       | `0944a7e` | Crash-safe SQLite backup publication: snapshot to a staging `.staging.tmp`, validate, then publish atomically (`PublishStaging`, unique name plus a single retry when the destination was concurrently claimed).                                                                   | `ShopManager.Domain.Tests/Integration/BackupPublicationTests.cs`      |
| 4A-2       | `05212da` | Canonical database identity resolved once per process, persisted to `%LocalAppData%\ShopManager\database-location.json`; startup blocked via `DatabaseService.BlockedReason` before settings, backup or auth (DB presence beats drive presence; ambiguous state blocks).           | `ShopManager.Domain.Tests/Integration/DatabasePathResolutionTests.cs` |
| 4A-3       | `3466604` | Durability contract: every EF/SQLite connection open runs `DurabilityInterceptor` → `EstablishDurability`, enforcing `journal_mode=WAL` + `synchronous=FULL` with read-back; failures propagate.                                                                                   | `ShopManager.Domain.Tests/Integration/DatabaseDurabilityTests.cs`     |
| 4A-4       | `22ca6fa` | Backup lifecycle reliability: process-wide single-flight gate (`SemaphoreSlim _backupGate`), generation-based change tracking, orphan `staging.tmp`/`-wal`/`-shm` sweep, stable error log (`backup-error.log`).                                                                    | `ShopManager.Domain.Tests/Integration/BackupLifecycleTests.cs`        |
| 4B-1       | `461670c` | Safe, non-destructive restore preparation: validate on an operation-owned copy, WAL-consistent read-only safety snapshot, Windows file-identity/hard-link guard, staging construction, and operation-owned cleanup; the live DB and the selected backup source are never modified. | `ShopManager.Domain.Tests/Integration/RestorePreparationTests.cs`     |
| 4B-2A      | `59d0dfc` | Durable restore-intent foundation: fixed app-owned `restore-intent.json`; pipeline flush → SHA-256 → atomic publish → process arm. `TransitionGate` admission lease is taken by `DatabaseService.CreateContext` so a new context cannot start while a restore owns the transition. | `ShopManager.Domain.Tests/Integration/RestoreRecoveryServiceTests.cs` |
| 4B-2B      | `d472153` | Offline file-level recovery engine (`RestoreRecoveryService.Recover`). After a durable intent the engine is forward-only: it forward-completes or BLOCKs, never rolls back. The published state **V** is "the live DB has the expected SHA-256 **and** no live `-wal` / `-shm` / `-journal` sidecar exists". Cleanup touches exact operation-owned tombstone and incoming artifacts only (no wildcard cleanup) and is plan-then-execute. Ambiguous or unsafe states fail closed and the restore stays armed. Deleting the intent is the final successful disk mutation. A missing `SafetyBackupPath` does not prevent forward completion; the safety snapshot, when present, is retained. Crash/restart and blocked-state coverage was added. | `ShopManager.Domain.Tests/Integration/RestoreRecoveryServiceTests.cs` |
| 4B-3 | `583a7b8` | App-lifetime Windows mutex `Global\ShopManager.ApplicationLifetime`; startup admission before Avalonia; Busy/Error exit codes 2/3; abandoned ownership accepted; process-lifetime root. Implemented, verified, checkpoint-ready. | `ShopManager.Domain.Tests/Integration/ApplicationInstanceGuardTests.cs`; `ShopManager.InstanceGuard.TestHost` |
| 4B-4 | `63039d2` | Startup recovery before resolver/SQLite; same-intent registered identity admission; fail-closed errors; implemented, verified, independently reviewed, checkpoint-ready. | `ShopManager.Domain.Tests/Integration/StartupRecoveryIntegrationTests.cs`; `RestoreRecoveryServiceTests.cs` |
| 4B-5A | `36f0e7f` | Runtime DbContext admission cutoff, context lifetime accounting and async drain proof; NOT restore-safe. | `ShopManager.Domain.Tests/Integration/DatabaseAdmissionDrainTests.cs` |
| 4B-5B-1 | `34faeee` (COMMITTED / PUSHED) | Resolver poisoning fix; implemented / verified / independently reviewed; NOT restore-safe. | `ShopManager.Domain.Tests/Integration/DatabaseAdmissionDrainTests.cs` |
| 4B-5B-2 | `287764c` (COMMITTED) | Backup admission/timer boundary; IMPLEMENTED / VERIFIED / INDEPENDENTLY REVIEWED; NOT restore-safe. | `ShopManager.Domain.Tests/Integration/BackupAdmissionDrainTests.cs` |
| 4B-5C-1 | COMMITTED `33e12b6` | Isolated RuntimeOperationGate; IMPLEMENTED / VERIFIED / INDEPENDENTLY REVIEWED; NOT runtime-quiescent / NOT restore-safe. | ShopManager.Domain.Tests/Integration/RuntimeOperationAdmissionTests.cs |
| 4B-5C-2.1 | COMMITTED / PUSHED `a8e3d02` | Production operation ownership/composition and Auth Login/Logout enrollment; IMPLEMENTED / TESTED / INDEPENDENTLY REVIEWED; NOT runtime-quiescent / NOT restore-safe. | ShopManager.Domain.Tests/Integration/AuthOperationEnrollmentTests.cs |
| 4B-5C-2.2 | COMMITTED / PUSHED `72d7761` | SessionTracker tick enrollment, generation/single-flight safety and conditional parent-bound Auth logout; IMPLEMENTED / TESTED / INDEPENDENTLY REVIEWED; terminal retirement/full quiescence deferred; NOT restore-safe. | ShopManager.Domain.Tests/Integration/SessionTrackerOperationEnrollmentTests.cs |

Historical declared test-method counts through 4B-2B: BackupPublication 16, DatabasePathResolution 31,
DatabaseDurability 10, BackupLifecycle 8, RestorePreparation 13, RestoreRecoveryService 42
(18 at `59d0dfc`; 42 at `d472153` after 4B-2B).
These are `[Fact]`/`[Theory]` attribute counts in the source; a `[Theory]` expands into
several executed cases, so the total executed count can be higher than the declared count.

## What is verified now

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

- Production file: `ShopManager.Desktop/Services/RuntimeOperationGate.cs`.
- Tests: `ShopManager.Domain.Tests/Integration/RuntimeOperationAdmissionTests.cs`.
- Isolated, instance-based primitive with states `Open` / `Closed` / `FaultedClosed`; atomic admission and operation-lifetime accounting; `NonInteractive` / `Interactive` classification and Interactive Busy; one closure owner; asynchronous drain; controlled reopen; exact-once/idempotent successful completion; fail-closed unproven completion.
- No production callers or integration, DB capability, `AsyncLocal`, singleton, or production cutoff. No lock is held across await, callbacks, DB or other I/O; signalling uses asynchronous continuations.
- Independent review supplied by the user: **PASS WITH FINDINGS**. **M1 and M2 fixed and re-reviewed CLOSED**; this sync records that disposition and does not claim a new independent review.
- **M1 CLOSED:** deterministic tests reject stale/foreign owner identity when the current closure is drained (`outstanding == 0`) but remains Closed; only its current owner can reopen. The foreign-owner test uses test-only reflection to construct a non-issued handle without changing production code.
- **M2 CLOSED:** two controlled orderings plus a concurrent Barrier-started Dispose-versus-first-`MarkCompletionUnproven` race allow only whole terminal outcomes: completed with zero outstanding and no fault, or FaultedClosed with one outstanding. The mixed outcome FaultedClosed plus zero outstanding is explicitly rejected.
- **M3 — residual test gap / VERIFICATION PENDING:** broad contended/stress linearizability coverage. No iteration-count stress test was added.
- Final implementation/follow-up evidence: targeted **29/29 PASS**; relevant admission/concurrency regression **118/118 PASS** (`DatabaseAdmissionDrainTests`, `BackupAdmissionDrainTests`, `SalePersistenceTests`, `TransferPersistenceTests`); full suite **413/413 PASS, 0 failed / 0 skipped**; non-incremental solution build **0 warnings / 0 errors**; `git diff --check` **PASS**, with separate whitespace checks for untracked files.
- These test/build results were executed during implementation/follow-up, not rerun in this documentation-only sync.
- Completion boundaries and proven cleanup remain caller responsibilities. No automatic self-drain detection, implicit nesting/join, or DB authorization is provided. **5C-1 does not establish runtime quiescence or restore safety.**
- Next increment recorded at the 5C-1 checkpoint: **4B-5C-2 — production operation enrollment and producer retirement**, requiring separate scope/approval. Restore UI, unified quiescence, shutdown/updater coordination and legacy restore removal remain future integration.

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

Historical verified 4B-4 evidence (checkpoint committed in `63039d2`; recorded implementation/test results):

- `StartupRecoveryIntegrationTests` → **29 passed, 0 failed, 0 skipped**.
- `RestoreRecoveryServiceTests` → **44 passed, 0 failed, 0 skipped**.
- `ApplicationInstanceGuardTests` → **21 passed, 0 failed, 0 skipped**.
- Full `ShopManager.Domain.Tests` → **322 passed, 0 failed, 0 skipped**.
- `dotnet build ShopManager.slnx --no-incremental -v:m` → **0 warnings, 0 errors**.
- `git diff --check` → **exit code 0**.
- Independent adversarial review → **PASS WITH FINDINGS**; no Critical or High findings,
  no checkpoint-blocking finding. Review disposition and verified invariants were
  supplied by the user for this documentation sync; review was not rerun here.
- Ordering coverage uses the shared `App.RunDesktopStartup` entry with real resolver
  and context operations. Direct automated coverage of the actual framework callback
  and normal startup body remains **VERIFICATION PENDING**, as recorded below.

Historical 4B-3 evidence after test hardening, recorded before commit `583a7b8`:

- Targeted `ApplicationInstanceGuardTests` → **21 passed, 0 failed, 0 skipped**.
- Full `ShopManager.Domain.Tests` → **291 passed, 0 failed, 0 skipped**.
- `dotnet build ShopManager.slnx --no-incremental -v:m` → **0 warnings, 0 errors**.
- `git diff --check` → **exit code 0**.
- Adversarial review → **PASS WITH FINDINGS**, no Critical or High defect;
  review disposition supplied by the user for this documentation sync.
- Test-hardening follow-up completed without production changes: exact mutex identity,
  Busy/Error exit-code mapping and contender proof after wrong-thread disposal.

These are the recorded implementation/test results, not a new build/test run during
documentation sync. Residual verification items below are not checkpoint blockers.

Historical verified evidence for checkpoint 4B-2B (implementation commit `d472153`;
270 tests in that committed source tree):

- `RestoreRecoveryServiceTests` → **42 passed**.
- Full `ShopManager.Domain.Tests` → **270 passed**.
- Non-incremental solution build → **0 Warning(s), 0 Error(s)**.
- Adversarial / final review → **PASS**, with no blocking Critical/High/Medium issue.

Historical evidence for the earlier production-code checkpoint `59d0dfc` (4B-2A;
kept as checkpoint evidence only):

- `dotnet build ShopManager.slnx -v:m` → **Build succeeded. 0 Warning(s), 0 Error(s).**
- `dotnet test ShopManager.slnx` → **Failed: 0, Passed: 246, Skipped: 0, Total: 246** (`net10.0`).

Source-confirmed facts:

- The checkpoint symbols above exist at commit `59d0dfc`: `BackupService.RunExclusive` /
  `TryRunExclusive` / `CreateForcedBackup` / `CreateSmartBackup` / `TryCreateSmartBackup` /
  `PrepareRestore`; `DatabaseService.BlockedReason` / `EstablishDurability` /
  `DurabilityInterceptor`; `RestoreRecoveryService.Arm` / `ReadIntent` / `ValidateIntent` /
  `PublishIntent` / `IsArmed` / `EnterDatabaseAdmission` / `OverrideAppOwnedRootForTests`.
- `App.axaml.cs` consults `DatabaseService.BlockedReason` and verifies a non-creating
  connection **before** settings, backup, and auth initialization.
- Historical at `d472153`: the restore intent was **not yet consumed in production**; no production caller of
  `RestoreRecoveryService.Arm`, `ReadIntent`, `PublishIntent`, or `Recover` existed outside
  the service itself (checked at `d472153`); `DatabaseService.CreateContext` only took the
  admission lease.
- At `59d0dfc`, `RestoreRecoveryService` documented the real database/WAL/SHM swap and
  tombstone as later work. Commit `d472153` (4B-2B) implements that file-level engine
  (`Recover`); startup recovery wiring is now implemented in committed 4B-4 (`63039d2`) through
  `StartupRecoveryCoordinator`, before the resolver and SQLite startup.
- The legacy `BackupService.RestoreBackup` (a `File.Copy`-based implementation) still
  exists and is still invoked from `Views/SettingsWindow.axaml.cs:290` together with
  `Environment.Exit(0)`.

## Pending work (Phase 4 is NOT complete)

- [x] **4B-2B**: offline recovery engine — file-level forward-completion/swap of
      `shop.db` / `-wal` / `-shm` with operation-owned tombstones. SHA-256 staging
      fingerprint remains authoritative; after durable intent, forward-complete or BLOCK,
      never rollback. Implemented and verified (evidence above); committed in `d472153`.
- [x] **4B-3**: app-lifetime mutex — implemented, verified, checkpoint-ready;
      committed in `583a7b8`, with historical evidence and residual verification items retained.
- [x] **4B-4**: startup recovery wiring/order and consumption of restore intent;
      implemented, verified, independently reviewed, checkpoint-ready; committed in **`63039d2`**.
- [x] **4B-5A**: runtime context admission + drain only; implemented, verified, independently reviewed; committed in **`36f0e7f`**, **NOT restore-safe**.
- [x] **4B-5B-1**: resolver poisoning fix; implemented, verified, independently reviewed; **COMMITTED / PUSHED `34faeee`**, **NOT restore-safe**.
- [x] **4B-5B-2**: backup admission/timer boundary; IMPLEMENTED / VERIFIED / INDEPENDENTLY REVIEWED; **COMMITTED `287764c`**, **NOT restore-safe**. Final evidence: 26/26 targeted, 172/172 relevant, 384/384 full; re-review PASS WITH FINDINGS; M1/M2 CLOSED.
- [x] **4B-5C-1 — COMMITTED `33e12b6`**: isolated RuntimeOperationGate primitive; implemented / verified / independently reviewed; NOT runtime-quiescent / NOT restore-safe. Final evidence: targeted 29/29, relevant 118/118, full 413/413 PASS, 0 failed/skipped; build 0 warnings/errors; diff-check PASS; user-confirmed review PASS WITH FINDINGS, M1/M2 re-reviewed CLOSED; M3 stress linearizability remains a test gap.
- [x] **4B-5C-2.1 — IMPLEMENTED / TESTED / INDEPENDENTLY REVIEWED; COMMITTED / PUSHED `a8e3d02`:** production operation ownership/composition and Auth enrollment only; F1/M1 CLOSED, L3 added; targeted 52/52, relevant 200/200, full 465/465 PASS; no runtime quiescence or restore safety.
- [x] **4B-5C-2.2 — IMPLEMENTED / TESTED / INDEPENDENTLY REVIEWED; COMMITTED / PUSHED `72d7761`:** SessionTracker tick enrollment and generation/single-flight safety; Stop is not completion proof; conditional parent-bound Auth logout protects replacement sessions without nested admission; cleanup uncertainty remains fail-closed. New tests 47/47, Auth 52/52, relevant 148/148, full 512/512, independent targeted review run 99/99 PASS; build 0 warnings/errors; independent review PASS with no Critical/High/Medium/Low findings (user-confirmed); diff-check PASS. Terminal producer retirement/full quiescence deferred; NOT restore-safe.
- [ ] **F1 → F2 → F3:** approved completion path below; F1 is IMPLEMENTED / VERIFIED / COMMITTED (`b38d9b5c5716a01c5e688c5d00496b5a36033d57`; evidence in the F1 status block below); F2b is technically complete / UNCOMMITTED, pending commit/push; F3 remains OPEN.

## Approved completion plan

The scope reset is intentional and user-approved, not abandonment of restore safety. **No current evidence proves per-window operation enrollment mandatory for safe restore.** Existing 5C-2.1 Auth and 5C-2.2 SessionTracker hardening, code and historical evidence remain valid. F1/F2/F3 here name completion increments, not historical audit finding IDs. Source implementation still requires its own approved scope.

### F1 — Final Restore Boundary

- [x] Replace the legacy SettingsWindow `RestoreBackup`/`Environment.Exit(0)` path with terminal restore single-flight.
- [x] Establish registered DB identity, block relevant new work, stop relevant producers, and asynchronously drain existing runtime/DB work. Active contexts held across UI dialogs must settle or restore must abort; do not force-dispose them.
- [x] Healthy-DB ordering: runtime/DB admission cutoff and drain → `PrepareRestore` while backup admission is still open → backup admission cutoff and drain (accepted/queued work and timer/lifecycle retirement) → release SQLite pools → `Arm` persistent restore intent → terminal restore-specific exit.
- [x] Keep cutoff closed through terminal restore exit. Timeout or cleanup uncertainty => do not `Arm`. Once persistent intent exists, or publication is uncertain, normal DB work must not resume.
- [x] Prevent updater Apply/Restart and queued lifecycle transitions from racing restore; suppress normal shutdown DB/logout/backup work during terminal restore.
- [x] Startup recovery performs the actual swap before resolver/SQLite admission. Do not resume normal work on the restored DB in the exiting process. No general UI operation-lifetime framework is required by this plan.

**F1 status — IMPLEMENTED / VERIFIED / COMMITTED (`b38d9b5c5716a01c5e688c5d00496b5a36033d57`, `feat: implement final restore boundary`):**

- **Review:** independent adversarial review found B1/B2; both were fixed and re-reviewed. Final targeted re-review: **PASS WITH FINDINGS** — Critical 0, High 0, Medium 0, Low only, **no blockers**. B3/B4/B5 from the first review were deliberately outside the fix round.
- **Evidence (from the implementation/fix turns and the final review; not rerun in this docs-only step):** targeted F1 tests **31 passed**; relevant regression **307 passed**; full suite **547 passed, 0 failed, 0 skipped**; non-incremental build **0 warnings / 0 errors**; `git diff --check` **PASS**.
- **B1 (fixed):** terminal-failure cleanup and logging are individually best-effort and `exit(1)` is in `finally`, so a cleanup/logging exception cannot skip exit; no admission is reopened and no normal DB work resumes.
- **B2 (fixed):** a busy interactive operation is refused before any cutoff state changes (`BusyInteractive`, restore ownership released, no exit, admissions untouched); `AlreadyClosed`/`FaultedClosed` and every post-cutoff failure remain terminal. The Settings restore button is re-enabled only when the call returns with an exception.
- **Boundary properties:** no live DB swap occurs in the current process; the actual restore remains startup-recovery work at next launch; the terminal restore boundary is fail-closed (timeout/cleanup uncertainty => no `Arm`; after intent exists or publication is uncertain, normal DB work does not resume).
- **Accepted residual risks (Low):** (1) a microsecond window between taking restore-exit ownership and releasing it on `BusyInteractive` can cancel one concurrent shutdown/update-apply request (user can repeat); (2) `BusyInteractive` has no production producer yet, so the abandon path is reachable only via tests until interactive enrollment exists; (3) the B1 regression test covers the logging-throws case only, the CloseAdmission catch-alls are verified by inspection; (4) if `Arm` publishes the intent and then fails, the process exits 1 and the next startup applies the restore (designed terminal semantics, covered by test).
- **Not claimed:** F2 (backup compatibility, broken-DB recovery) and F3 (end-to-end/process-crash/UI-smoke verification, closure) are OPEN. F1 does not make Phase 4 restore-safe or complete.

### F2 — Backup Compatibility + Broken-DB Recovery

F2 is split into **F2a (backup compatibility)** and **F2b (broken-DB recovery)**. F2 is technically complete; F2a is CLOSED / COMMITTED / PUSHED and F2b remains UNCOMMITTED, pending commit/push.

**F2a status — CLOSED / IMPLEMENTED / TESTED / INDEPENDENTLY REVIEWED; COMMITTED / PUSHED `c9b7ccc` — `feat: validate restore backup compatibility`:**

- **Implemented:** `BackupService.ValidateApplicationCompatibility`, called from `ValidateBackupForRestore` on the stabilized private restore candidate over the existing read-only validation connection, after the SQLite integrity check and before `BackupValidated`, the safety snapshot and staging. An incompatible backup fails with `InvalidDataException` before any live-DB restore preparation; the live DB/WAL, the candidate and the restore artifacts are untouched.
- **Contract:** required tables/columns are derived from the `AppDbContext` EF relational model (`Model.GetRelationalModel().Tables`, no database connection). Every model table must exist as a real table (not a view) with all model columns; names are matched case-insensitively. Extra tables/columns are allowed. If `SaleOperations` exists, all its model columns are required.
- **Only exemptions:** `Users.CanPOS`, `Users.CanViewFinance`, `Users.MustChangePassword`, `Sales.CardTerminal`, `Sales.DiscountAmount`, and an absent `SaleOperations` table. The five columns live in `DatabaseService.LegacyUpgradeColumns` (with `SaleOperationsTable`), which `EnsureSchemaWithAdoNet` startup repair also reads; a guard test fails if an exemption stops matching a real model table/column.
- **Review:** independent review **PASS WITH FINDINGS** — no Critical/High/Medium production-code findings; **M1** (present `SaleOperations` missing a model column) was a test gap and is **fixed** by an added test case. Low findings L1–L5 are accepted/deferred (see below).
- **Evidence (from the implementation/review/follow-up turns; not rerun in this docs-only step):** targeted `RestorePreparationTests` **23 passed**; full suite **557 passed, 0 failed, 0 skipped**; non-incremental build **0 warnings / 0 errors**; `git diff --check` exit 0.
- **Deferred / not claimed by F2a:** newer-version detection; column type/constraint validation (validation is by table and column name only); data/FK/canonical-shape validation. Review Low findings L1–L5 were not addressed in this increment and remain accepted/deferred. **F2b — broken-DB recovery — is technically complete / UNCOMMITTED, pending commit/push.** F2a does not make Phase 4 restore-safe in all failure modes.

**F2a checklist:**

- [x] Prove the selected backup is compatible with this application before arming; SQLite integrity plus a nonempty `sqlite_master` is insufficient application-compatibility evidence. (F2a: table/column compatibility against the current model, as scoped above.)

**F2b checkpoint (2026-10-07) — IMPLEMENTED / TESTED / INDEPENDENTLY REVIEWED — PASS WITH FINDINGS; UNCOMMITTED, pending commit/push.** Final independent-review disposition supplied by the user: Critical/High/Medium = none; blocker = NO. F1 and F2a are CLOSED; F3 remains OPEN; Phase 4 remains IN PROGRESS / NOT CLOSED / NOT restore-safe.

- Evidence from the implementation/fix session (not rerun in this docs-only step): targeted recovery/startup **88/88**, relevant F1/F2a/F2b regression **325/325**, full suite **589/589**, all with 0 failed / 0 skipped; `dotnet build ShopManager.slnx --no-incremental`: **0 warnings / 0 errors**; `git diff --check`: **exit 0**.
- Final H1 fix: Incoming alone no longer proves recovery resume or bypasses pristine safety. Incoming beside the old regular live DB blocks before any destructive move; legitimate tombstone-backed resume remains supported.
- Final M2 fix: blocked-startup remains displayable when backup enumeration/access/I/O fails; it shows a concise backup-unavailable message and Restore remains unavailable.
- Accepted residual findings: an external writer outside ShopManager may mutate DB/sidecars during byte-level preservation; in-process gates do not cover external writers. The blocked-startup backup enumeration error path has no automated Avalonia UI test. Remaining Low UI/message findings are non-blocking and may be verified in F3.
- Manual blocked-startup UI smoke: **VERIFICATION PENDING for F3**; not performed. Remaining unrelated stress/crash verification is deferred to F3. No F2b commit hash or push is claimed.

**F2b checklist (technical work complete; commit/push pending):**

- [x] Provide a bounded recovery entry when the current DB is broken, without admitting normal DB consumers. Failure to obtain a valid live safety snapshot must not be silently bypassed.
- [x] Preserve damaged/original DB and relevant sidecar files before replacement; uncertainty must fail closed. The existing engine's tolerance of a missing safety snapshot does not itself satisfy preservation.

### F3 — Final Verification & Closure

- [ ] Integration/end-to-end restore verification: admission cutoff/drain, preparation ordering, persistent intent, terminal exit, startup recovery and restored application data; cover incompatible backups, broken DB and fail-closed failures.
- [ ] Real process-crash/kill verification where required, including kill during `SaveSale` and restart across critical restore boundaries. Exception/reset simulations alone do not establish process-crash behavior.
- [ ] UI smoke verification, including pending dialogs, queued lifecycle transitions, updater exclusion, restore-specific shutdown and broken-startup recovery.
- [ ] Full tests pass; solution build has **0 warnings / 0 errors**; independent review passes; final documentation reconciliation records only obtained evidence.

## Phase 4 DoD — approved scope reset

| Completion increment | Current status |
| --- | --- |
| F1 — Final Restore Boundary | **CLOSED / IMPLEMENTED / VERIFIED / COMMITTED (`b38d9b5c5716a01c5e688c5d00496b5a36033d57`); final review PASS WITH FINDINGS, Low only, no blockers** |
| F2 — Backup Compatibility + Broken-DB Recovery | F2a **CLOSED / COMMITTED / PUSHED `c9b7ccc`**; F2b **IMPLEMENTED / TESTED / INDEPENDENTLY REVIEWED — PASS WITH FINDINGS; UNCOMMITTED, pending commit/push** |
| F3 — Final Verification & Closure | **OPEN / VERIFICATION PENDING**; manual blocked-startup UI smoke pending |

**Original DoD disposition:** backup publication/integrity portions remain implemented; end-to-end restore, complete restored DB data, broken-DB recovery and crash verification remain mandatory under F1/F2/F3. `EnsureCreated → Migrate()`, DB-from-zero migration coverage, general schema drift (AR-2/S8), encryption, secondary USB backup and 3-2-1 (D1) move to [deferred backlog](MASTER-BACKLOG.md#deferred-work-from-phase-4-scope-reset), OPEN and not completed. Narrow backup compatibility remains in F2.

General per-window enrollment (Login/Users/Items/Cashbox/POS/Transfer/Reports/etc.), general producer-retirement/lifetime architecture and broader shutdown/updater redesign move to deferred reliability backlog. Only restore-boundary safety coordination is required now.

**Phase 4 closure evidence is NOT produced.** F1 and F2a are CLOSED; F2b evidence is recorded above and is technically complete / UNCOMMITTED, pending commit/push; F3 remains OPEN. Historical checkpoint tests/builds/reviews are retained; none were rerun in this docs-only update. Phase 4 remains **IN PROGRESS / NOT restore-safe / NOT closed** until F2b commit/push and the required F3 closure evidence are complete.

## Residual risks and limitations

- **4B-3 — VERIFICATION PENDING (not checkpoint blockers):** cross-user / cross-session /
  elevation behavior; installed GUI startup/shutdown smoke; Velopack update/restart
  overlap behavior. None is represented as verified by the harness or review.
- **4B-4 — VERIFICATION PENDING (not checkpoint blockers):** direct automated coverage
  of real `App.OnFrameworkInitializationCompleted` / `InitializeNormalDesktopStartup`;
  additional path canonicalization/alias test coverage; installed GUI blocked-window
  startup/shutdown smoke; startup latency/UX for large recovery staging files.
- Startup consumes existing restore intents. F1 and F2a are CLOSED; F2b is technically complete / UNCOMMITTED, pending commit/push; F3 remains OPEN; checkpoint readiness does not close Phase 4. General lifetime/shutdown architecture is deferred; restore-boundary safety remains mandatory.
- Historical (pre-F1): the legacy restore path was still the one the UI called. F1 (committed in `b38d9b5c5716a01c5e688c5d00496b5a36033d57`) replaces it with the terminal restore boundary; F2b is technically complete / UNCOMMITTED, pending commit/push; F3 remains OPEN.
- Durability is enforced on each connection open; this is not a claim about power-loss
  behavior under every filesystem.
- Integration tests use temporary, isolated databases and folders; the operational
  database and user backups are not used or modified.

## Closure

**Phase 4: IN PROGRESS / NOT restore-safe — do not mark complete.** Close only after the approved F1/F2/F3 DoD has implementation, verification and independent-review evidence.
