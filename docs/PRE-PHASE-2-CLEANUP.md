# Pre-Phase 2 Cleanup

## Purpose and approved scope

Remove three audited dead-code targets from the active repository:

1. Remove `POSCartItem.HasDiscount`.
2. Remove `POSCartItem.DiscountPct`.
3. Delete the active `ShopManager.Desktop/ViewModels/SaleCartItem.cs` file.

This document records verified evidence from the audit, implementation, and adversarial review in this session. It does not declare the broader Pre-Phase complete.

## Pre-implementation audit

| Target | Original definition | Active references, excluding definition | Classification |
| --- | --- | --- | --- |
| `POSCartItem.HasDiscount` | `ShopManager.Desktop/ViewModels/POSCartItem.cs:23` | 0 | SAFE TO REMOVE |
| `POSCartItem.DiscountPct` | `ShopManager.Desktop/ViewModels/POSCartItem.cs:24` | 0 | SAFE TO REMOVE |
| `SaleCartItem` | `ShopManager.Desktop/ViewModels/SaleCartItem.cs:6` | 0 | SAFE TO REMOVE in the active repository |

Targeted searches covered C# and AXAML. Active-code verification excluded `.git`, `bin`, `obj`, `.vs`, BACKUP directories, and ignored backup copies. Backup copies contained historical `SaleCartItem` consumers; they were excluded from active-code conclusions and from the approved removal scope.

Relevant cart creation, display, and explicit mapping to `Sale` did not use either removed property. Discount calculations used `_discountAmount`. No relevant XAML binding, serialization, reflection, mapping, or other indirect dependency was found. `ViewLocator.Match` accepts `ViewModelBase`; neither cart class inherits from that base.

## Definition of Done

- [x] Audit all three targets and distinguish definitions from active references.
- [x] Verify zero active references with backup and generated/build directories excluded.
- [x] Obtain explicit approval for the three removals.
- [x] Remove the two approved properties and delete the active `SaleCartItem.cs`.
- [x] Review the actual diff for scope compliance.
- [x] Verify no explicit project/solution/props/targets reference to `SaleCartItem.cs` was found.
- [x] Run `dotnet build ShopManager.slnx`: 0 errors, 0 warnings.
- [x] Run `dotnet test ShopManager.slnx`: 30 passed, 0 failed, 0 skipped.
- [x] Verify `git diff --check` is clean.
- [x] Complete read-only adversarial review: PASS.

UI runtime testing was not performed. No commit, tag, or push was performed or authorized by this cleanup task.

## Exact implementation

- Modified `ShopManager.Desktop/ViewModels/POSCartItem.cs`: removed the `HasDiscount` and `DiscountPct` auto-properties. The edit also added a final newline; the review identified this as a minor formatting change with no behavioral effect.
- Deleted `ShopManager.Desktop/ViewModels/SaleCartItem.cs` from the active repository.
- Implementation diff: 2 files changed, 1 insertion, 20 deletions. This count excludes this documentation file.

No source logic, schema, package, configuration, or status-document changes were included.

## Build and test evidence

Both initial sandbox executions were blocked while reading `C:\Users\RK\AppData\Roaming\NuGet\NuGet.Config`. The initial build reported 4 errors and 0 warnings due to that access restriction. Both commands were rerun with approved elevated access, without a code or configuration fix.

| Command | Verified successful result |
| --- | --- |
| `dotnet build ShopManager.slnx` | Exit code 0; 0 errors; 0 warnings |
| `dotnet test ShopManager.slnx` | Exit code 0; 30 passed; 0 failed; 0 skipped; 30 total |

The test run executed `ShopManager.Domain.Tests.dll` for `net10.0`. The adversarial review compared the current implementation diff with the diff at successful verification and found them consistent. It did not rerun build or tests in read-only mode.

## Adversarial review

**Verdict: PASS.**

- No functional defect or missed active dependency was found.
- The diff contained only the approved removals and the final-newline addition described above.
- No tracked change to `SaleWindow`, backups, configuration, or existing documentation was present.
- Active references to all three removed targets were zero.
- No explicit reference to `SaleCartItem.cs` was found in project, solution, props, or targets files.
- `git diff --check` was clean; no staged changes were present.

## Residual risks reported by the review

- Executed tests belong to `ShopManager.Domain.Tests`; interactive POS UI behavior was not runtime-tested.
- Git cannot establish whether ignored backup files changed without a comparison snapshot. No such snapshot was available. No recorded operation in this session modified a backup.

## Out of scope

- `SaleWindow` (previously removed; F11, as identified by the user). The historical F11 removal was not independently re-audited in this session; no `SaleWindow` modification was made.
- Backup directories.
- Unrelated cleanup or refactoring.
- Schema, packages, and configuration.
- `MASTER-BACKLOG.md` and `AUDIT-RECONCILIATION.md`.

## Files changed/deleted

| Status | File |
| --- | --- |
| Modified | `ShopManager.Desktop/ViewModels/POSCartItem.cs` |
| Deleted | `ShopManager.Desktop/ViewModels/SaleCartItem.cs` |
| Created (documentation only) | `docs/PRE-PHASE-2-CLEANUP.md` |

## Git status before commit

Observed after creating this document, before any commit:

```text
 M ShopManager.Desktop/ViewModels/POSCartItem.cs
 D ShopManager.Desktop/ViewModels/SaleCartItem.cs
?? docs/PRE-PHASE-2-CLEANUP.md
```

No commit was performed.
