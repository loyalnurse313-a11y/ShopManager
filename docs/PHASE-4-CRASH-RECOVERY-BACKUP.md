# Phase 4 — Crash Recovery + Backup

## Status

**Phase 4 — Crash Recovery + Backup: IN PROGRESS — NOT complete.**

- Branch: `phase/4-crash-recovery-backup`.
- HEAD: `59d0dfc` — `feat: add durable restore intent foundation`.
- No Phase 4 completion tag exists. The last completion tag in the repository is
  `phase-3-concurrency-idempotency-complete`.
- Documented checkpoints: **4A-1 through 4B-2A**.
- Remaining work: **4B-2B+** and the rest of the Phase 4 DoD recorded below.

This document records only checkpoints that are present in the committed source tree at
HEAD. It does not claim Phase 4 completion.

## Purpose and scope

Roadmap scope: `Migrate()`, WAL, backup verification, encryption, external/secondary
backup, crash recovery, and safe restore.

## Checkpoints implemented (code + tests present at HEAD)

| Checkpoint | Commit    | Change                                                                                                                                                                                                                                                                             | Tests                                                                 |
| ---------- | --------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | --------------------------------------------------------------------- |
| 4A-1       | `0944a7e` | Crash-safe SQLite backup publication: snapshot to a staging `.staging.tmp`, validate, then publish atomically (`PublishStaging`, unique name plus a single retry when the destination was concurrently claimed).                                                                   | `ShopManager.Domain.Tests/Integration/BackupPublicationTests.cs`      |
| 4A-2       | `05212da` | Canonical database identity resolved once per process, persisted to `%LocalAppData%\ShopManager\database-location.json`; startup blocked via `DatabaseService.BlockedReason` before settings, backup or auth (DB presence beats drive presence; ambiguous state blocks).           | `ShopManager.Domain.Tests/Integration/DatabasePathResolutionTests.cs` |
| 4A-3       | `3466604` | Durability contract: every EF/SQLite connection open runs `DurabilityInterceptor` → `EstablishDurability`, enforcing `journal_mode=WAL` + `synchronous=FULL` with read-back; failures propagate.                                                                                   | `ShopManager.Domain.Tests/Integration/DatabaseDurabilityTests.cs`     |
| 4A-4       | `22ca6fa` | Backup lifecycle reliability: process-wide single-flight gate (`SemaphoreSlim _backupGate`), generation-based change tracking, orphan `staging.tmp`/`-wal`/`-shm` sweep, stable error log (`backup-error.log`).                                                                    | `ShopManager.Domain.Tests/Integration/BackupLifecycleTests.cs`        |
| 4B-1       | `461670c` | Safe, non-destructive restore preparation: validate on an operation-owned copy, WAL-consistent read-only safety snapshot, Windows file-identity/hard-link guard, staging construction, and operation-owned cleanup; the live DB and the selected backup source are never modified. | `ShopManager.Domain.Tests/Integration/RestorePreparationTests.cs`     |
| 4B-2A      | `59d0dfc` | Durable restore-intent foundation: fixed app-owned `restore-intent.json`; pipeline flush → SHA-256 → atomic publish → process arm. `TransitionGate` admission lease is taken by `DatabaseService.CreateContext` so a new context cannot start while a restore owns the transition. | `ShopManager.Domain.Tests/Integration/RestoreRecoveryServiceTests.cs` |

Declared test-method counts per file: BackupPublication 16, DatabasePathResolution 31,
DatabaseDurability 10, BackupLifecycle 8, RestorePreparation 13, RestoreRecoveryService 18.
These are `[Fact]`/`[Theory]` attribute counts in the source; a `[Theory]` expands into
several executed cases, so the total executed count is higher.

## What is verified now

Evidence produced during this documentation task at HEAD `59d0dfc`:

- `dotnet build ShopManager.slnx -v:m` → **Build succeeded. 0 Warning(s), 0 Error(s).**
- `dotnet test ShopManager.slnx` → **Failed: 0, Passed: 246, Skipped: 0, Total: 246** (`net10.0`).

Source-confirmed facts:

- The checkpoint symbols above exist at HEAD: `BackupService.RunExclusive` /
  `TryRunExclusive` / `CreateForcedBackup` / `CreateSmartBackup` / `TryCreateSmartBackup` /
  `PrepareRestore`; `DatabaseService.BlockedReason` / `EstablishDurability` /
  `DurabilityInterceptor`; `RestoreRecoveryService.Arm` / `ReadIntent` / `ValidateIntent` /
  `PublishIntent` / `IsArmed` / `EnterDatabaseAdmission` / `OverrideAppOwnedRootForTests`.
- `App.axaml.cs` consults `DatabaseService.BlockedReason` and verifies a non-creating
  connection **before** settings, backup, and auth initialization.
- The restore intent is **not yet consumed in production**: no production caller of
  `RestoreRecoveryService.Arm` or `ReadIntent` exists outside the service itself;
  `DatabaseService.CreateContext` only takes the admission lease.
- `RestoreRecoveryService` states in its own documentation that the real database/WAL/SHM
  swap, tombstone, and startup recovery are later phases.
- The legacy `BackupService.RestoreBackup` (a `File.Copy`-based implementation) still
  exists and is still invoked from `Views/SettingsWindow.axaml.cs:290` together with
  `Environment.Exit(0)`.

## Pending work (Phase 4 is NOT complete)

- [ ] **4B-2B+**: perform the actual `shop.db` / `-wal` / `-shm` swap, write the tombstone,
      and consume the restore intent during startup recovery.
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
| Backup: copy + integrity check + restore  | **PARTIAL** — publication and validation exist (4A-1); the actual restore application is pending (4B-2B+). |
| Backup encryption (DPAPI/AES)             | **PENDING**                                                                                                |
| Secondary backup on USB                   | **PENDING**                                                                                                |
| Test: kill during `SaveSale` → DB healthy | **PENDING**                                                                                                |
| Test: restore from backup → all data      | **PENDING**                                                                                                |
| Test: corrupted DB → recover from backup  | **PENDING**                                                                                                |

**Evidence for the full Phase 4 DoD (a complete crash + recovery scenario): NOT produced.**

## Residual risks and limitations

- Restore is fail-closed but not yet executable end-to-end: the intent can be armed and
  read, but nothing applies it, so a restore cannot complete.
- The legacy restore path is still the one the UI calls.
- Durability is enforced on each connection open; this is not a claim about power-loss
  behavior under every filesystem.
- Integration tests use temporary, isolated databases and folders; the operational
  database and user backups are not used or modified.

## Closure

**Phase 4: IN PROGRESS — do not mark complete.** Update this document as 4B-2B+ and the
remaining DoD items are implemented and verified.
