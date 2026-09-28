# Phase 1 — Business Correctness

> **شروع:** 1405/07/07 | **بستن:** 1405/07/07 | **وضعیت:** ✅ COMPLETE

---

## Scope

1. رفع G1–G5 در POSWindow.SaveSale (منطق تخفیف)
2. رفع SalePrice=0 در PricingCalculator
3. اعمال MidpointRounding.AwayFromZero
4. حذف متغیرهای مرده
5. افزودن Sale.DiscountAmount
6. تست‌های واحد + Integration

---

## Definition of Done — چک‌لیست

### الف) مدل
- [x] فیلد `Sale.DiscountAmount` (decimal, 18,4)
- [x] `ALTER TABLE` در EnsureSchemaWithAdoNet
- [x] `HasPrecision(18, 4)` در AppDbContext

### ب) PricingCalculator
- [x] شرط Override: `HasValue && Value > 0`
- [x] `decimal.Round(..., MidpointRounding.AwayFromZero)`
- [x] `IsPriceOverridden` همراستا

### ج) POSWindow.SaveSale
- [x] `discountShare = revenueRaw / totalCartRev * _discountAmount`
- [x] `Revenue = revenueNet` (خالص)
- [x] `DiscountAmount = discountShare`
- [x] `Cost = Qty × LockedCost`
- [x] `Profit = revenueNet − cost`
- [x] Rounding AwayFromZero روی همه مبالغ
- [x] حذف `totalRevenue`/`totalProfit` مرده
- [x] جبران قلم آخر (Σ=کل)
- [x] R1: round `_discountAmount` در OnDiscountClick
- [x] R2: ترتیب صحیح round

### د) Customer
- [x] بدون تغییر (خط 1109 خالص بود)

### ه) تست‌ها
- [x] `ShopManager.Domain.Tests` (xUnit, بدون FluentAssertions)
- [x] LockedCost: 8 تست
- [x] Cashbox: 6 تست
- [x] Stock: 6 تست
- [x] Pricing: 5 تست
- [x] StockAlert: 5 تست
- [x] **مجموع: 30/30 Pass**

### و) کیفیت
- [x] Build: 0W / 0E
- [x] Test: 30 Pass / 0 Fail
- [x] مستند در این فایل
- [x] Commit + Push

---

## باگ‌های رفع‌شده (اثبات تجربی)

| # | باگ | فایل | اثبات |
|:---:|---|---|---|
| B1 | `SalePrice = 0` به‌عنوان Override | PricingCalculator | T2: Expected 130, Actual 0 |
| B2 | `decimal.Round(x, 0)` = ToEven | PricingCalculator | T5: Expected 13, Actual 12 |
| B3 | `Revenue` خام ذخیره می‌شد | POSWindow | Audit 6.A5 |
| B4 | `Profit ≠ Revenue − Cost` | POSWindow | Audit 6.A4, 6.A6 |
| B5 | `DiscountAmount` ست نمی‌شد | POSWindow | Audit 6.A9 |
| B6 | متغیرهای مرده | POSWindow | Audit 6.A7 |
| B7 | rounding غایب در SaveSale | POSWindow | Audit G4 |

---

## یافته‌های جانبی (ثبت‌شده)

### Phase 2 (پاک‌سازی)
- `POSCartItem.HasDiscount` و `DiscountPct` — فیلدهای مرده
- `SaleCartItem` — مدل موازی استفاده‌نشده

### Future Backlog
- Reversal Sign Contradiction (Purchase.cs vs StockCalculator)
- Reversal Implementation (پیاده نشده)
- Schema Drift (Phase 4)
- `.Date` granularity در LockedCost

### Phase 3 (Concurrency)
- Negative-stock guard
- Optimistic Concurrency Token

### Phase 7 (Performance)
- StockAlert Warning unreachable در Fixed-only

---

## شواهد

### Build
0 Warning(s), 0 Error(s)

text

### Tests
Passed! - Failed: 0, Passed: 30, Skipped: 0, Total: 30, Duration: 71 ms

text

### Commits
- `c4c79a7` Phase 1 (step 1-3)
- `babf152` Phase 1 (step 4-6)

### فایل‌های تغییر یافته
- `Domain/Entities/Sale.cs`
- `Desktop/Services/DatabaseService.cs`
- `Infrastructure/Persistence/AppDbContext.cs`
- `Domain/Services/PricingCalculator.cs`
- `Desktop/Views/POSWindow.axaml.cs`
- `ShopManager.slnx`

### فایل‌های جدید
- `ShopManager.Domain.Tests/` (کامل + 30 تست)
- `docs/PHASE-1-BUSINESS-CORRECTNESS.md`

---

## Lessons Learned

1. DoD اجباری مؤثر بود — 7 باگ با تست اثبات شدند
2. R1/R2 توسط AI کشف شد — بازبینی مستقل ارزشمند
3. Audit قبل از Implementation — جلوگیری از دوباره‌کاری
4. Test-first برای Pricing — 2 Fail راهنمای رفع شد

---

## آماده برای Phase 2

**نقطه شروع:** پاک‌سازی کد مرده (SaleWindow + HasDiscount/DiscountPct + SaleCartItem).
