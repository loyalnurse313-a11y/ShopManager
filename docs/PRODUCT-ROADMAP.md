# ShopManager Product Roadmap

This document is the durable Product Vision / Feature Backlog for ShopManager. It is not a current implementation-status report and does not authorize implementation. See [PROJECT-CONTEXT.md](PROJECT-CONTEXT.md) for the current-state map and [AGENTS.md](../AGENTS.md) for operating rules.

## 1. Roadmap Rule

- Finish and close current Phase 4 before adding new product features. This document does not change its status.
- After Phase 4, follow this sequence:

  Product/Feature Architecture → Internationalization Foundation + Design System → Feature Expansion v1 → Feature Freeze → Phases 5–10 → Production Acceptance → Limited Pilot → ShopManager v1 Production.

- Every feature follows AGENTS.md: AUDIT → SCOPE → DoD → IMPL → TEST → REVIEW. The COMMIT stage remains subject to explicit instruction under AGENTS.md; this task permits no commit or push.
- Backlog approval means product direction only. This roadmap is not implementation approval; each feature needs its own approved scope and applicable gates.

## 2. Approved v1 Feature Expansion Backlog

All entries below are **PLANNED / NOT IMPLEMENTED** as roadmap deliverables. These labels do not assert that every related capability is absent from the repository; implementation status requires repository evidence.

### Management Dashboard

Show useful daily business state and Quick Actions: sales, profit, cashbox, purchases, receivables/payables, upcoming cheques, low stock, alerts and recent activity.

### Business Calculator

Provide a quick-access calculator for basic arithmetic, percentage, discount, margin/profit and price calculation.

### Financial Calendar

Provide a unified business calendar for cheque due dates, receivables/payables, reminders and other financial/business events.

### Cheque Management

Manage incoming and outgoing cheques, including relevant identifiers, bank, amount, party, issue/receipt date, due date, status and settlement. Support due-date alerts such as 7 days, 3 days, tomorrow, today and overdue where appropriate.

### Independent Receipts & Payments

Record money received/paid independently of sales/purchases. Support appropriate payment methods such as cash, card/bank transfer and cheque. Integrate correctly with party balances and cashbox rules.

### Expense Management

Record operational expenses such as rent, utilities, salary, transport, repairs and miscellaneous expenses. Keep accounting meaning explicit; do not silently redefine existing gross profit semantics.

### Document Center

Attach scanned paper invoices, images, PDFs, receipts, cheque images and other documents to relevant entities such as Sale, Purchase, Person, Cheque and Return. Design storage architecture together with backup/restore guarantees.

### Bulk Pricing

Support controlled bulk price changes with a required preview before applying changes. Preserve pricing/business invariants and auditability.

### Notification Center

Provide a central location for actionable alerts: cheque due dates, low stock, reminders, backup failures/status and important business events.

### Reorder Suggestions

Provide rule-based replenishment suggestions using inventory, minimum-stock thresholds and sales/history where appropriate. AI is not required for v1.

### Global Search

Provide fast Unicode-aware search across relevant entities: items, barcodes, people, invoices, cheques and other supported business records.

### Activity Feed

Show human-readable recent business activity. Keep this concept separate from the security Audit Trail.

### Returns

Support both Sales Return and Purchase Return:

- Allow full and partial returns.
- Preserve the original historical invoice; do not rewrite it as if the original transaction never happened.
- Record each return as an explicit, traceable business operation.
- Prevent returning more quantity than remains returnable.
- Apply correct effects to inventory, revenue/profit semantics, cashbox/payment flows and customer/supplier balances.
- Define refund/credit behavior through explicit rules.
- Distinguish sellable returned goods from damaged/non-sellable goods.
- Allow documents/photos to be attached later.
- Financial/inventory rules require their own sensitive-code scope gate before implementation.

### Stock Adjustment

Never require unexplained direct stock editing. Record controlled adjustments with previous quantity, actual quantity, difference and reason. Examples include physical-count difference, damage, loss and other justified adjustments. Future stocktake/inventory-count workflows may build on this.

## 3. Internationalization Foundation

ShopManager must not be architecturally Iran-only. Initial language architecture should support at least Persian (`fa`), English (`en`), Arabic (`ar`), Russian (`ru`), French (`fr`) and Chinese (`zh`), and allow additional languages such as Turkish.

**Language ≠ Region ≠ Currency ≠ Calendar.** Keep combinations such as these architecturally possible:

- English UI + Iran region + Toman display + Persian calendar.
- Arabic UI + UAE region + AED display + Gregorian calendar.

Requirements:

- Resource-based localization; avoid hard-coded UI strings.
- RTL and LTR support.
- Unicode-safe data and search.
- Culture-aware number/date/time formatting.
- Font fallback suitable for Persian/Arabic, Latin, Cyrillic and Chinese.
- Layouts that tolerate different translation lengths.
- Localizable print/PDF/invoice output.
- Configurable language preference.
- Business architecture without hard-coded dependence on Toman or the Persian calendar.

Multi-currency accounting is **not automatically in v1 scope**. Internationalization must not be confused with implementing a multi-currency accounting system.

Infrastructure comes first. Full translation into every language is not required immediately; Persian and English may be completed before other language packs.

## 4. UI/UX Design System

After Phase 4, before or alongside Feature Expansion, define a shared Design System covering:

- Light/Dark strategy; primary/accent/status colors.
- Typography and multilingual fonts.
- Sidebar/navigation, dashboard and cards.
- Tables, forms, buttons and iconography.
- Spacing, radius/shadows and dialogs.
- Toast/notifications and validation/error states.
- RTL/LTR and date picker/calendar.
- POS-specific fast workflow.
- DPI/scaling and different screen sizes.

Create and approve representative designs for at least Dashboard, POS/Sales, Cheques and Financial Calendar before broad UI expansion.

Phase 8 remains responsible for final Avalonia UI reliability, consistency and polish; it should not be the first time the visual language is defined.

## 5. Deployment / Future Architecture

Current v1 architecture remains a Windows desktop application using the existing local architecture and SQLite. Docker/Docker Compose is not a v1 requirement merely for the sake of containerization.

Do not architecturally prevent a future network/server edition. A future multi-PC/server product may introduce Desktop clients → API/server → server database (e.g. PostgreSQL). Evaluate Docker/Compose when a real server deployment requirement exists.

This is future architectural direction, not v1 implementation scope. Do not implement it as part of this roadmap task.

## 6. Release Compatibility

Phase 10 / Production Acceptance must include real clean-machine compatibility testing, not only developer-machine tests. Verification must consider:

- A clean supported Windows installation.
- Operation without Visual Studio or a .NET SDK dependency.
- Installer, install and uninstall.
- Upgrade from a supported previous release.
- Different DPI/scaling and supported locales.
- Persian/non-Latin Windows usernames where practical.
- Different valid paths.
- Offline operation where expected.
- Restart/crash scenarios and backup/restore.
- Realistic and large datasets.
- Clean Windows VM testing.

These are planned acceptance considerations, not claims of completed verification.

## 7. Product Principle

Keep ShopManager simple for daily store operation while maintaining strict correctness behind the UI. Do not turn the application into a complicated accounting suite merely by accumulating features.

Prefer simple user workflow + explicit business operations + traceable history + strong financial/inventory correctness + reliable recovery + future extensibility.

## 8. Status Semantics

- Backlog items remain **PLANNED / NOT IMPLEMENTED** unless repository evidence establishes otherwise.
- This document records product intent; it does not alter existing Phase 4 status or mark any new feature complete.
- Do not invent commits, tests, evidence, dates or implementation status.
- Use **POSSIBLE**, **NOT FOUND** or **VERIFICATION PENDING** when evidence is insufficient, as required by AGENTS.md.
- Implementation, status changes and completion claims require their own scope, evidence and review. This roadmap grants no implementation authorization.
