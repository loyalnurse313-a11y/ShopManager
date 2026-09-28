# MASTER-BACKLOG

> **هدف:** مرجع واحد برای همه‌ی یافته‌های ممیزی.
> **آخرین به‌روزرسانی:** 1405/07/07

---

## منبع ۱ — AUDIT-REPORT.md

| ID | یافته | فایل | شدت | Phase | وضعیت |
|---|---|---|---|---|---|
| AR-1 | نبود Transaction دور SaveSale | POSWindow | 🔴 | 2 | 🟡 |
| AR-2 | EnsureCreated به جای Migrate | DatabaseService | 🔴 | 4 | ❌ |
| AR-3 | XSS در ۴ پنجره | *HistoryWindow | 🟠 | ad-hoc | ✅ |
| AR-4 | RestoreBackup ناایمن | BackupService | 🟠 | 4 | ❌ |
| AR-5 | Unique Index InvoiceNumber | AppDbContext | 🟠 | 3 | 🟡 |
| AR-6 | N+1 در CreateProductTile | POSWindow | 🟠 | 7 | ❌ |
| AR-7 | Race بررسی موجودی | POSWindow | 🟠 | 3 | ❌ |
| AR-8 | Code Signing | release.yml | 🟡 | 10 | ❌ |
| AR-9 | API key plaintext | AppPreferences | 🟡 | ad-hoc | ✅ |
| AR-10 | admin/admin | AuthServiceInitializer | 🟡 | ad-hoc | ✅ |
| AR-11 | SaleWindow کد مرده | Views | 🟢 | ad-hoc | ✅ |

---

## منبع ۲ — Audit 6.A (فاز ۱)

| ID | یافته | فایل:خط | شدت | Phase | وضعیت |
|---|---|---|---|---|---|
| 6.A1 | Revenue خام | POSWindow:1170 | 🔴 | 1 | ✅ B3 |
| 6.A2 | Cost از LockedCost | POSWindow:1171 | ✅ | 1 | ✅ |
| 6.A3 | Profit خام | POSWindow:1172 | ✅ | 1 | ✅ |
| 6.A4 | تخفیف فقط از Profit | POSWindow:1174 | 🔴 | 1 | ✅ B4 |
| 6.A5 | Revenue خام ذخیره | POSWindow:1190 | 🔴 | 1 | ✅ B3 |
| 6.A6 | Profit invariant نقض | POSWindow:1191 | 🔴 | 1 | ✅ B4 |
| 6.A7 | متغیرهای مرده | POSWindow:1203 | 🟡 | 1 | ✅ B6 |
| 6.A8 | Customer.TotalPurchasedAmount | POSWindow:1109 | ✅ | 1 | ✅ |
| 6.A9 | Sale.DiscountAmount غایب | POSWindow | 🔴 | 1 | ✅ B5 |
| 6.A10 | rounding غایب | POSWindow | 🟠 | 1 | ✅ B7 |
| 6.A11 | Reversal هیچ‌جا ساخته نمی‌شود | All | 🔴 | Future | ❌ |

---

## منبع ۳ — ۱۲ بُعدی

| ID | بُعد | Phase | وضعیت |
|---|---|---|---|
| D1 | Backup 3-2-1 | 4 | ❌ |
| D2 | Structured Logging | 6 | ❌ |
| D3 | Alert System | Future | خارج |
| D4 | Audit Trail | 5 | ❌ |
| D5 | EULA/Privacy | 10 | ❌ |
| D6 | Tax/Legal | Business Req | 🟡 |
| D7 | Inventory Concurrency | 3 | ❌ |
| D8 | Sale Transaction Boundary | 2 | 🟡 |
| D9 | Idempotency | 3 | ❌ |
| D10 | Time/Date edge cases | 1 | ❌ |
| D11 | UX Loading States | 8 | ❌ |
| D12 | Future-Proofing | Future | خارج |

---

## منبع ۴ — PHASE-1 (B1-B7)

| ID | باگ | فایل | Phase | وضعیت |
|---|---|---|---|---|
| B1 | SalePrice=0 Override | PricingCalculator | 1 | ✅ |
| B2 | ToEven rounding | PricingCalculator | 1 | ✅ |
| B3 | Revenue خام | POSWindow | 1 | ✅ |
| B4 | Profit invariant | POSWindow | 1 | ✅ |
| B5 | DiscountAmount غایب | POSWindow+Sale | 1 | ✅ |
| B6 | متغیرهای مرده | POSWindow | 1 | ✅ |
| B7 | Rounding غایب | POSWindow | 1 | ✅ |

---

## منبع ۵ — PHASE-1 جانبی

| ID | یافته | Phase | وضعیت |
|---|---|---|---|
| S1 | POSCartItem.HasDiscount | 2 | ❌ |
| S2 | POSCartItem.DiscountPct | 2 | ❌ |
| S3 | SaleCartItem مدل موازی | 2 | ❌ |
| S4 | Negative-stock guard | 3 | ❌ |
| S5 | Optimistic Concurrency Token | 3 | ❌ |
| S6 | StockAlert Warning در Fixed-only | 7 | ❌ |
| S7 | Reversal Sign Contradiction | Future | ❌ |
| S8 | Schema Drift | 4 | ❌ |

---

## نمای Phase-Centric

### Phase 1 ✅ COMPLETE
- [B1-B7] رفع شده

### Phase 2 — پاک‌سازی
- [S1] POSCartItem.HasDiscount
- [S2] POSCartItem.DiscountPct
- [S3] SaleCartItem

### Phase 3 — Concurrency
- [AR-5] Unique Index (کد هست)
- [AR-7] Race موجودی
- [S4] Negative-stock guard
- [S5] Optimistic Concurrency Token

### Phase 4 — Crash Recovery + Backup
- [AR-2] EnsureCreated → Migrate()
- [AR-4] RestoreBackup
- [S8] Schema Drift
- [D1] Backup 3-2-1

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