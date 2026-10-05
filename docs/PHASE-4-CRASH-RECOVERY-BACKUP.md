# Phase 4 — Crash Recovery + Backup

## Status

**Phase 4 — Crash Recovery + Backup: IN PROGRESS — NOT complete.**

- Branch: `phase/4-crash-recovery-backup`.
- Latest implemented checkpoint: **4B-5C-1 — IMPLEMENTED / VERIFIED / INDEPENDENTLY REVIEWED — NOT COMMITTED; NOT runtime-quiescent / NOT restore-safe**. The latest committed production checkpoint is 4B-5B-2 (`287764c`); the previously confirmed pushed checkpoint is 4B-5B-1 (`34faeee`).
- Latest committed production-code checkpoint: `287764c` — `feat: add backup admission and drain boundary` (4B-5B-2). Earlier checkpoints: `34faeee` (4B-5B-1; committed/pushed), `36f0e7f` (4B-5A), `63039d2` (4B-4), `583a7b8` (4B-3), `d472153` (4B-2B), `59d0dfc` (4B-2A); commits between `59d0dfc` and `d472153` changed documentation and repository operating rules only.
- No Phase 4 completion tag exists. The last completion tag in the repository is
  `phase-3-concurrency-idempotency-complete`.
- Documented checkpoints: **4A-1 through 4B-5C-1**. 4A-1 through 4B-2A are committed
  (last commit `59d0dfc`); **4B-2B is committed in `d472153`; 4B-3 in `583a7b8`; 4B-4 in `63039d2`; 4B-5A in `36f0e7f`; 4B-5B-1 committed/pushed in `34faeee`**; 4B-5B-2 is committed in `287764c`.
- Next planned: **4B-5C-2 — production operation enrollment and producer retirement**.
- Remaining work: **4B-5C-2 and later integration (future work)**, UI/shutdown/quiesce/drain
  integration and legacy restore removal, and the rest of the Phase 4 DoD
  recorded below.

This document records checkpoints 4A-1 through 4B-2A as present in the committed source
tree at commit `59d0dfc`, 4B-2B at commit `d472153`, 4B-3 at commit `583a7b8`,
4B-4 at commit `63039d2`, verified, independently reviewed 4B-5A at commit `36f0e7f`,
4B-5B-1 committed/pushed in `34faeee`, and implemented, verified, independently reviewed
4B-5B-2 at commit `287764c`, and 4B-5C-1 in the working tree (NOT COMMITTED). It does not claim
Phase 4 completion.

## Purpose and scope

Roadmap scope: `Migrate()`, WAL, backup verification, encryption, external/secondary
backup, crash recovery, and safe restore.

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

**Still future integration after 4B-5C-1 (not implemented; 4B-5C-2 and later need separate scope):**
- SettingsWindow/UI integration;
- quiesce/drain integration and shutdown redesign;
- removal of the legacy production restore path (`BackupService.RestoreBackup` and the
  `Environment.Exit(0)` path in `Views/SettingsWindow.axaml.cs`).

## Checkpoints implemented (through 4B-5C-1; 5C-1 NOT COMMITTED)

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
| 4B-5C-1 | NOT COMMITTED | Isolated RuntimeOperationGate; IMPLEMENTED / VERIFIED / INDEPENDENTLY REVIEWED; NOT runtime-quiescent / NOT restore-safe. | ShopManager.Domain.Tests/Integration/RuntimeOperationAdmissionTests.cs |

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
- Current sequence: **4B-5B-1 — resolver poisoning fix (`34faeee`; COMMITTED / PUSHED)**; **4B-5B-2 — backup admission/timer boundary (IMPLEMENTED / VERIFIED / INDEPENDENTLY REVIEWED / COMMITTED `287764c`)**; **4B-5C-1 — isolated operation-lifetime primitive (IMPLEMENTED / VERIFIED / INDEPENDENTLY REVIEWED — NOT COMMITTED)**; **4B-5C-2 — production operation enrollment and producer retirement (next planned)**. Production `PrepareRestore` / `Arm` remains unavailable until the complete required quiesce boundary exists. Restore UI remains future work.

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

**IMPLEMENTED / VERIFIED / INDEPENDENTLY REVIEWED — NOT COMMITTED. NOT runtime-quiescent / NOT restore-safe.** Phase 4 remains **IN PROGRESS**.

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
- Next planned increment: **4B-5C-2 — production operation enrollment and producer retirement**, requiring separate scope/approval. Restore UI, unified quiescence, shutdown/updater coordination and legacy restore removal remain future integration.

**Future 4B-5C integration constraints (not implemented):**

- Never wait for DB drain while holding the `TransitionGate` write lock.
- Future production flow must close admission and prove drain before `Arm`; resolver identity must be established before future `CloseAdmission`.
- In the no-lease fresh-resolution path, marker publication currently occurs after the runtime initialization lease is released and is outside DB drain. The already-admitted factory retains its existing lease. DB drain must not be presented as proof that all resolver/marker work has completed.
- 4B-5B-2 implements the backup admission/timer primitive only; 4B-5C-1 adds an isolated operation-lifetime primitive without production enrollment. 4B-5C-2 production operation enrollment and producer retirement is next planned; unified cutoff/background/updater/multi-context completion remains future work and requires separate scope/approval. Production restore admission remains unavailable until the required complete quiesce boundary exists.

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
- [x] **4B-5C-1 — NOT COMMITTED**: isolated RuntimeOperationGate primitive; implemented / verified / independently reviewed; NOT runtime-quiescent / NOT restore-safe. Final evidence: targeted 29/29, relevant 118/118, full 413/413 PASS, 0 failed/skipped; build 0 warnings/errors; diff-check PASS; user-confirmed review PASS WITH FINDINGS, M1/M2 re-reviewed CLOSED; M3 stress linearizability remains a test gap.
- [ ] **4B-5C-2 — next planned increment:** production operation enrollment and producer retirement; separate scope/approval required.
- [ ] **4B-5C-2 and later — future integration**: restore UI wiring; quiesce/drain;
      shutdown redesign; removal of the legacy production restore path. Not implemented.
- [ ] **AR-4**: replace the legacy `BackupService.RestoreBackup` and the
      `SettingsWindow.axaml.cs:290` `Environment.Exit(0)` path with the safe restore flow.
- [ ] **AR-2**: `EnsureCreated` → a real `Migrate()`.
- [ ] **S8**: schema drift / migrations.
- [ ] **D1**: backup 3-2-1, encryption (DPAPI/AES), and secondary USB backup.
- [ ] Crash/recovery tests: kill during `SaveSale`; restore from backup → all data;
      corrupted DB → recover from backup.

## Phase 4 DoD (roadmap) — progress

| DoD item                                  | Status                                                                                                     |
| ----------------------------------------- | ---------------------------------------------------------------------------------------------------------- |
| `EnsureCreated` → `Migrate()`             | **PENDING**                                                                                                |
| DB from zero → all migrations run         | **PENDING**                                                                                                |
| Backup: copy + integrity check + restore  | **PARTIAL** — publication and validation exist (4A-1); the file-level restore engine is implemented (4B-2B, `d472153`), but the end-to-end restore is pending the subsequent Phase 4 integration. |
| Backup encryption (DPAPI/AES)             | **PENDING**                                                                                                |
| Secondary backup on USB                   | **PENDING**                                                                                                |
| Test: kill during `SaveSale` → DB healthy | **PENDING**                                                                                                |
| Test: restore from backup → all data      | **PENDING**                                                                                                |
| Test: corrupted DB → recover from backup  | **PENDING**                                                                                                |

**Evidence for the full Phase 4 DoD (a complete crash + recovery scenario): NOT produced.**

## Residual risks and limitations

- **4B-3 — VERIFICATION PENDING (not checkpoint blockers):** cross-user / cross-session /
  elevation behavior; installed GUI startup/shutdown smoke; Velopack update/restart
  overlap behavior. None is represented as verified by the harness or review.
- **4B-4 — VERIFICATION PENDING (not checkpoint blockers):** direct automated coverage
  of real `App.OnFrameworkInitializationCompleted` / `InitializeNormalDesktopStartup`;
  additional path canonicalization/alias test coverage; installed GUI blocked-window
  startup/shutdown smoke; startup latency/UX for large recovery staging files.
- Startup now consumes existing restore intents through the 4B-2B engine. End-to-end
  restore UI wiring, quiesce/drain, shutdown redesign and legacy restore removal remain
  unimplemented future work (4B-5C and later); checkpoint readiness does not close Phase 4.
- The legacy restore path is still the one the UI calls.
- Durability is enforced on each connection open; this is not a claim about power-loss
  behavior under every filesystem.
- Integration tests use temporary, isolated databases and folders; the operational
  database and user backups are not used or modified.

## Closure

**Phase 4: IN PROGRESS — do not mark complete.** Update this document as the subsequent
Phase 4 integration and the remaining DoD items are implemented and verified.
