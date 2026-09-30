# Phase 2 — Transaction Boundary

## Status

**Phase 2 — Transaction Boundary: COMPLETE**

## Purpose and scope

Phase 2 establishes a reliable transaction boundary for the sale persistence path.

Scope:

- Sale
- Customer changes associated with the sale
- Inventory validation
- Payment-related sale data
- Cashbox-related sale data
- Cost / Profit
- Rollback behavior
- Post-commit failure classification

Concurrency, idempotency, duplicate-invoice protection, and optimistic concurrency
remain Phase 3 concerns.

## Official Sale Stage Map

### Stage 1 — Customer

Track changes to an existing customer or add a new customer to the `DbContext`.
No independent `SaveChanges` occurs here.

### Stage 2 — Sale Preparation

Load required data, validate stock, calculate discount, revenue, locked cost and
profit, create `Sale` entities, and add them to the `DbContext`.

### Stage 3 — SaveChanges

Persist the pending tracked changes inside the active transaction.

### Stage 4 — Commit

Commit the transaction.

## Commit Boundary

**Successful Stage 4 is the persistence boundary.**

Before successful Commit:

- persistence failures enter the rollback path;
- the original persistence/commit exception remains primary;
- rollback and cleanup failures are preserved as secondary errors;
- post-commit operations are not executed.

After successful Commit:

- the sale is treated as committed;
- cleanup, `DataChanged`, UI, refresh, display, or printing failures are
  post-commit failures;
- these failures must not roll back or report the committed sale as a
  persistence failure.

### Stage 5 — Cleanup + DataChanged

Dispose transaction/context resources and publish `DataChanged`.

Failures occurring here after successful Commit are post-commit failures.

### Stage 6 — UI / Print

Perform post-commit UI work, refresh/clear the UI, display or print the invoice,
and report completion status.

Failures here do not change the committed persistence result.

## Definition of Done

- [x] Sale persistence executes inside one explicit transaction.
- [x] Pre-commit persistence failure follows the rollback path.
- [x] Failure after earlier tracked changes does not leave a partially committed sale.
- [x] Successful sale commits the persistence unit.
- [x] `SaveChanges` failure triggers the persistence failure/rollback path.
- [x] Original persistence/commit exception remains primary when rollback or cleanup also fails.
- [x] Secondary rollback/cleanup failures remain available for operational logging.
- [x] `DataChanged` is emitted only after successful Commit for the explicit sale transaction.
- [x] Post-commit cleanup/notification/UI failures are not classified as persistence failures.
- [x] Sale flow and Commit Boundary are documented with numbered stages.

The historical DoD wording "failure at every Stage => zero DB changes" is clarified:
it applies to persistence work before a successful Commit. Stages 5 and 6 occur
after the persistence boundary and therefore must not undo an already committed sale.

## Implementation summary

Phase 2 introduced and hardened the sale persistence boundary around the existing
sale workflow.

Relevant implementation commits:

- `2399233` — `feat: implement phase 2 transaction boundary`
- `3e1e74a` — `fix: address phase 2 adversarial review findings`

The review fixes separated post-commit cleanup and UI/printing failures from
persistence failures while preserving the original exception and secondary
rollback/cleanup errors.

## Build and test evidence

Verified result:

- `dotnet build ShopManager.slnx` — **0 warnings, 0 errors**
- `dotnet test ShopManager.slnx` — **57 passed, 0 failed, 0 skipped**
- `git diff --check` — **PASS**

The 57 tests include the pre-existing suite and Phase 2 integration/failure-path
coverage.

## Review evidence

### Production adversarial re-review

**PASS**

The independent review verified:

- no additional transaction was introduced;
- financial calculation behavior was not unintentionally changed;
- pre-commit and post-commit failures are separated;
- cleanup and `DataChanged` failures after Commit remain post-commit failures;
- rollback/cleanup secondary errors remain loggable.

### Independent test-quality review

**PASS**

The review verified that:

- tests exercise the real `SalePersistenceService` and sale boundary paths rather
  than duplicating the production implementation;
- the discount-rounding test creates a real `0.5 + 0.5` rounding conflict and
  detects broken last-line compensation;
- persistence/Commit, rollback, cleanup, notification and post-commit boundaries
  are meaningfully exercised.

## Residual risks and verification limitations

- Physical printing and the full interactive UI were not runtime-tested.
- An ambiguous hardware/provider-level Commit failure cannot prove definite
  rollback solely from an exception returned by Commit.
- Cleanup-failure tests use injected/simulated failures and do not prove every
  possible provider-level resource-release failure.

These limitations do not invalidate the verified Phase 2 transaction boundary.

## Closure

**Phase 2 — Transaction Boundary: COMPLETE**

Next planned phase:

**Phase 3 — Concurrency + Idempotency**

Phase 3 has not started as part of this closure.
