# Phase 3 — Concurrency + Idempotency

## Status

**Phase 3 — Concurrency + Idempotency: COMPLETE (sub-phases 3A through 3C).**

- Closure tag: `phase-3-concurrency-idempotency-complete` (at `905622c`).
- Final implementation commit: `da7d5b4`.
- Intermediate checkpoint `50a5427` is not a closure basis.

Phase 4 is tracked separately in [PHASE-4-CRASH-RECOVERY-BACKUP.md](PHASE-4-CRASH-RECOVERY-BACKUP.md).

## Purpose and scope

Completed scope:

- re-entrant payment guard;
- sale idempotency (operation identity plus request fingerprint);
- invoice-number collision recovery;
- combined stock validation for duplicate item lines;
- inventory-withdrawal race for the normal sale and transfer paths.

Out of this closure: general exactly-once/recovery for transfers, automatic sale-pending
recovery after restart, crash/restore, schema drift, external writers, and all other
inventory-edit paths.

## Sub-phases

| Sub-phase | Commit    | Result                                                                                                                                                                                                                                  |
| --------- | --------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| 3A        | `45c6511` | `_isPaymentInProgress` and button disable in `OnPayClick`; released in `finally`.                                                                                                                                                       |
| 3B-1      | `c3268af` | `SaleOperations` schema: unique `OperationId` key, mandatory `RequestFingerprint`, unique `InvoiceNumber` index; repeatable preparation/validation for new and existing databases.                                                      |
| 3B-2      | `5ff9a68` | Stable snapshot/fingerprint, replay without re-posting, conflict on changed payload, definite/uncertain pending lifecycle, invoice-collision recovery, duplicate-item stock aggregation. `50a5427` was an intermediate checkpoint only. |
| 3C        | `da7d5b4` | Transfer transaction starts before the stock read; independent sale/transfer race tests; production sale logic unchanged.                                                                                                               |

## Definition of Done

- [x] `SalePersistenceService.Save`: stable snapshot + fingerprint; identical `OperationId` and payload replays the stored result without a duplicate financial/customer effect; a different payload is a conflict.
- [x] Sale-operation record and sale/customer changes persist in the same transaction; definite rollback and post-commit error separation preserved.
- [x] `PendingSale`: a releasable definite failure makes the cart editable again; an uncertain result preserves the identity and snapshot across a later retry. A confirmed commit result is cached; after a necessary reset the UI/print does not block releasing pending.
- [x] Definite invoice collision on a releasable request: keep the cart, release the failed request, and propose a fresh number from `Sales` and `SaleOperations`; the next payment gets a new `OperationId`. If the prior attempt was uncertain, pending identity is preserved. The proposal is not a number reservation; authoritative control lives inside persistence.
- [x] Sale stock validation sums `Qty` of lines sharing an `ItemId`; line order and fingerprint do not change.
- [x] Constraint checks use SQLite codes and the stored record; correctness does not depend on the exact exception text.
- [x] `TransferPersistenceService.Save`: begin the transaction before reading item/purchase/transfer, validate stock and INSERT/SaveChanges/Commit in that transaction; `DataChanged` is raised once after a confirmed commit. A definite failure rolls back and does not clear the form; UI/cleanup/notification failure after commit is not a persistence failure.
- [x] Two sales with **different** `OperationId`/invoice competing for `stock=1`, and two independent transfers competing for `warehouse=1`, each have exactly one winner.

## Design decisions

### RowVersion / S5

The original requirement of "`Item` has `RowVersion` or equivalent" was replaced in the
approved 3C scope by transactional protection. In the current
Microsoft.Data.Sqlite/EF Core `10.0.12` provider, the default `BeginTransaction()` path
reaches a non-deferred writer transaction (`BEGIN IMMEDIATE`). Both services acquire that
transaction before reading authoritative data, so the second writer either reads fresh stock
after the first transaction releases, or fails while acquiring the lock. The current race
tests demonstrate that this design works. **No `RowVersion`, optimistic token, global
application lock, or schema change was added in 3C.** Changing provider/semantics requires
re-proving this guarantee.

### F5 / AR-5

`SaleOperations.OperationId` (primary key) and the unique `SaleOperations.InvoiceNumber`
index protect operation/invoice identity. `Sales.InvoiceNumber` alone is not unique because
one invoice has several lines. Schema preparation rejects non-canonical structures and does
not backfill or rewrite historical data. Collision with historical invoice numbers is handled
in the sale service. Global uniqueness of all legacy data and replacing `EnsureCreated` with
migrations are not claimed.

## Test map

| Test file                                                          | Related tests and what they prove                                                                                                                                                                                                                                                                                      |
| ------------------------------------------------------------------ | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `ShopManager.Domain.Tests/Integration/SaleOperationSchemaTests.cs` | `SqliteEnforcesRequiredIdentityConstraints`, `PreparationIsRepeatableAndPreservesOperations`, `HistoricalMultilineSalesNeedNoBackfillOrSchemaChanges`, the `Noncanonical*` tests and `TwoIndependentConnectionsPrepareTheSameDatabase`: identity constraints, repeatable/concurrent preparation, history preservation. |
| `ShopManager.Domain.Tests/Integration/SaleRequestTests.cs`         | `EveryMeaningfulInputAndLineOrderAffectsFingerprint`, `FingerprintIsCultureIndependentAndNormalizesEquivalentInput`, `SnapshotDefensivelyCopiesLines`: snapshot/fingerprint; `UncertainAttemptCannotBeDiscardedAfterDefinitiveRetryFailure` and `CompletedSaleReleasesPendingBeforeFallibleUi`: pending lifecycle.     |
| `ShopManager.Domain.Tests/Integration/SalePersistenceTests.cs`     | `ReplayPreservesMultilineSaleCustomerAndNotifications`, `ReplayDoesNotRevalidateConsumedStockOrSaveChanges`, `ChangedRequestIsConflictBeforeAnyMutation`, `SameOperationOnIndependentConnectionsCreatesOnlyOneSale`: replay/conflict and no duplicate effect.                                                          |
| Same file                                                          | `ActualCommitThenExceptionResolvesFromFreshContextAndRetryIsReplay`, `VerificationReadFailurePreservesOriginalErrorAndLaterRetryIsSafe`, `ConstraintResolutionUsesStoredIdentityWithoutExceptionMessage`: uncertain commit and verification against the real record.                                                   |
| Same file                                                          | `InvoiceCollisionReleasesPendingPreservesCartAndNextPaymentPersistsExactlyOnce`: fresh identity/number from the production generator, cart preserved, exactly one posting; `DuplicateItemLinesUseCombinedStockWithoutMergingPersistedLines`: combined stock validation.                                                |
| Same file                                                          | `DifferentOperationsCompetingForLastUnitPersistOnlyTheWinner`: two independent file-backed connections, different identity/invoice, `stock=1`; loser has zero stock effect and no financial/customer/sale-operation effect.                                                                                            |
| `ShopManager.Domain.Tests/Integration/TransferPersistenceTests.cs` | `IndependentTransfersCompetingForLastUnitPersistOnlyTheWinner`: `warehouse=1`, two transfers of 1, exactly one posting, `warehouse=0` and `shop=1`.                                                                                                                                                                    |
| Same file                                                          | `FailureAfterRealInsertRollsBackCompletelyAndDoesNotClearForm`: real INSERT then failure, complete rollback and form preserved, including explicit rollback failure; `SuccessPreservesFieldsAndNotifiesOnceAfterVisibleCommit`: fields and notification after a commit visible from an independent connection.         |
| Same file                                                          | `PostCommitFailureDoesNotBecomePersistenceFailure` and `CommitExceptionReportsUncertaintyWithoutRetry`: separating UI/cleanup/notification from persistence, and reporting an uncertain commit without automatic retry.                                                                                                |

## Verification record (recorded at Phase 3 closure)

| Check                                                  | Result                               | Source                                                                                                                        |
| ------------------------------------------------------ | ------------------------------------ | ----------------------------------------------------------------------------------------------------------------------------- |
| `dotnet build ShopManager.slnx --no-restore`           | 0 errors / 0 warnings                | Recorded Phase 3C run at `da7d5b4`.                                                                                           |
| `dotnet test ShopManager.slnx --no-build --no-restore` | 134/134 passed; 0 failed / 0 skipped | Recorded Phase 3C full run; 10 more than the 124 at the end of 3B-2.                                                          |
| `git diff --check`                                     | clean                                | End-of-implementation check (an LF/CRLF conversion warning on two new files was noted separately; no whitespace error).       |
| Final adversarial review                               | PASS                                 | User confirmation in the Phase 3 documentation-closure request; no standalone review report file was found in the repository. |

Post-closure note: at HEAD `59d0dfc` the full suite — which now also contains Phase 4 test
files — runs **246 passed / 0 failed / 0 skipped** with a **0 warning / 0 error** solution
build (verified during the documentation reconciliation task). The 134 figure above remains
the recorded Phase 3 closure evidence.

## Residual risks and open boundaries

- Sale pending state is in memory; automatic recovery after restart or a general
  exactly-once guarantee after a crash is not part of the Phase 3 evidence.
- Transfers have no idempotency record; an uncertain commit is reported as
  `TransferCommitUncertainException` and the UI asks the user to check history. There is no
  automatic re-posting and no general transfer recovery.
- The two-independent-connection race test is not confirmation of multi-terminal/multi-store
  support, nor of safety for every external writer or every inventory-edit path.
- An independent WAL/DELETE/timeout matrix, power loss, interactive UI and physical printing
  were not executed in this closure. Error tests are fault-injected.
- `EnsureCreated`, the legacy preparation path, and the legacy composite index on `Sales`
  still exist; schema drift, migrations, crash recovery, and safe backup/restore belong to
  Phase 4. Building `SaleOperations` in 3B-1 does not close F2/S8.
- Reversal implementation and its sign contradiction remain Future Backlog.

## Closure

**Phase 3 — Concurrency + Idempotency: COMPLETE.**

Next: **Phase 4 — Crash Recovery + Backup** — IN PROGRESS, see
[PHASE-4-CRASH-RECOVERY-BACKUP.md](PHASE-4-CRASH-RECOVERY-BACKUP.md).
