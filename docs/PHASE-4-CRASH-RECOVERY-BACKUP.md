# Phase 4 — Crash Recovery + Backup

## Status

**Phase 4 — Crash Recovery + Backup: IN PROGRESS — NOT complete.**

- Branch: `phase/4-crash-recovery-backup`.
- Latest implemented checkpoint: **4B-4 — implemented, verified, independently reviewed,
  checkpoint-ready; UNCOMMITTED**. No 4B-4 commit hash is assigned.
- Latest committed production-code checkpoint: `583a7b8` — `feat: add app-lifetime instance guard`
  (4B-3). Earlier checkpoints: `d472153` — `feat: implement crash-safe restore recovery engine`
  (4B-2B), and `59d0dfc` —
  `feat: add durable restore intent foundation` (4B-2A); the commits between `59d0dfc` and
  `d472153` changed documentation and repository operating rules only.
- No Phase 4 completion tag exists. The last completion tag in the repository is
  `phase-3-concurrency-idempotency-complete`.
- Documented checkpoints: **4A-1 through 4B-4**. 4A-1 through 4B-2A are committed
  (last commit `59d0dfc`); **4B-2B is committed in `d472153`; 4B-3 in `583a7b8`**.
- Remaining work: **4B-5 and later integration (future work)**, UI/shutdown/quiesce/drain
  integration and legacy restore removal, and the rest of the Phase 4 DoD
  recorded below.

This document records checkpoints 4A-1 through 4B-2A as present in the committed source
tree at commit `59d0dfc`, 4B-2B at commit `d472153`, 4B-3 at commit `583a7b8`,
and verified, independently reviewed 4B-4 in the uncommitted working tree. It does not claim
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
checkpoint-ready; UNCOMMITTED):**
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

**Still future work after 4B-4 (not implemented; 4B-5 and later need separate scope):**
- SettingsWindow/UI integration;
- quiesce/drain integration and shutdown redesign;
- removal of the legacy production restore path (`BackupService.RestoreBackup` and the
  `Environment.Exit(0)` path in `Views/SettingsWindow.axaml.cs`).

## Checkpoints implemented (through 4B-3 committed; 4B-4 UNCOMMITTED)

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
| 4B-4 | UNCOMMITTED | Startup recovery before resolver/SQLite; same-intent registered identity admission; fail-closed errors; implemented, verified, independently reviewed, checkpoint-ready. | `ShopManager.Domain.Tests/Integration/StartupRecoveryIntegrationTests.cs`; `RestoreRecoveryServiceTests.cs` |

Historical declared test-method counts through 4B-2B: BackupPublication 16, DatabasePathResolution 31,
DatabaseDurability 10, BackupLifecycle 8, RestorePreparation 13, RestoreRecoveryService 42
(18 at `59d0dfc`; 42 at `d472153` after 4B-2B).
These are `[Fact]`/`[Theory]` attribute counts in the source; a `[Theory]` expands into
several executed cases, so the total executed count can be higher than the declared count.

## What is verified now

Verified 4B-4 evidence (UNCOMMITTED; implementation/test results recorded in this session):

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
  (`Recover`); startup recovery wiring is now implemented in uncommitted 4B-4 through
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
      implemented, verified, independently reviewed, checkpoint-ready; **UNCOMMITTED**.
- [ ] **4B-5 and later — future work**: restore UI wiring; quiesce/drain;
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
  unimplemented future work (4B-5 and later); checkpoint readiness does not close Phase 4.
- The legacy restore path is still the one the UI calls.
- Durability is enforced on each connection open; this is not a claim about power-loss
  behavior under every filesystem.
- Integration tests use temporary, isolated databases and folders; the operational
  database and user backups are not used or modified.

## Closure

**Phase 4: IN PROGRESS — do not mark complete.** Update this document as the subsequent
Phase 4 integration and the remaining DoD items are implemented and verified.
