# AUDIT-RECONCILIATION — تطبیق ممیزی با Roadmap جدید

> **هدف:** پل بین ممیزی، وضعیت فعلی، و Roadmap جدید (۱۰ Phase).
> **اصل حاکم:** هیچ Phase بدون DoD اثبات‌شده Done نیست.
> **آخرین به‌روزرسانی:** 1405/07/07
> **آخرین Commit:** 7c8279c

---

## ۱. Roadmap قطعی (۱۰ Phase)

| # | Phase | Scope | DoD کوتاه |
|:---:|---|---|---|
| 1 | Business Correctness | Sale, Return, Cost, Profit, Discount, Rounding, Stock | تست عددی |
| 2 | Transaction Boundary | Sale+Payment+Inventory+Cashbox+Rollback | شکست → صفر تغییر |
| 3 | Concurrency+Idempotency | Double-click, Stock Race, Concurrency Token | دو درخواست → یک نتیجه |
| 4 | Crash Recovery+Backup | WAL, Migrate, Integrity, Restore, Encryption | Crash → بازیابی سالم |
| 5 | Audit+Security | AuditLog, Authorization, Password | هر تغییر حساس → رکورد |
| 6 | Logging+Global Error | Structured Log, Global Handler, Rolling | خطا → فایل log |
| 7 | EF Core+Performance | N+1, AsNoTracking, Index, Pagination | 100K → سریع |
| 8 | Avalonia Reliability | UI Thread, Async, Event Leak, Timer | 8 ساعت → بدون leak |
| 9 | Tests | Unit, Integration, Regression | Coverage مشخص |
| 10 | Release Hardening | Updater, Signing, Licenses, EULA | نصب تمیز |

**Scope Creep (خارج):** Cloud Sync، Multi-terminal، Multi-store، Mobile، Plugin، Telemetry، Feature Flags، API

---

## ۲. Reconciliation — یافته‌های ممیزی

| # | یافته | Phase | وضعیت | DoD |
|:---:|---|:---:|:---:|:---:|
| F1 | نبود Transaction دور SaveSale | 2 | 🟡 | ❌ |
| F2 | EnsureCreated به جای Migrate | 4 | ❌ | ❌ |
| F3 | XSS در ۴ پنجره | خارج | ✅ | ✅ |
| F4 | RestoreBackup ناایمن | 4 | ❌ | ❌ |
| F5 | Unique Index InvoiceNumber | 3 | 🟡 | ❌ |
| F6 | N+1 در CreateProductTile | 7 | ❌ | ❌ |
| F7 | Race بررسی موجودی | 3 | ❌ | ❌ |
| F8 | Code Signing | 10 | ❌ | ❌ |
| F9 | API key plaintext | خارج | ✅ | ✅ |
| F10 | admin/admin | 5 | ✅ | ✅ |
| F11 | SaleWindow کد مرده | خارج | ✅ | ✅ |
| F12 | catch{} خالی | 6 | ❌ | ❌ |

### ۲.۲ نقاط کور ۱۲ بُعدی

| # | بُعد | Phase | وضعیت |
|:---:|---|:---:|:---:|
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

### ۲.۳ کارهای انجام‌شده (فاز قدیمی)

| کار | Phase جدید | DoD |
|---|:---:|:---:|
| VS Code setup | خارج | ✅ |
| XSS escape | خارج | ✅ |
| MaxLength+Guard | خارج | ✅ |
| حذف DeepSeek | خارج | ✅ |
| admin/admin + FirstRun | 5 | ✅ |
| CheckLegacyAdminPassword | 5 | ✅ |
| Fix CS8602 | خارج | ✅ |
| Fix ShowConfirmDialog | خارج | ✅ |
| Fix const->readonly | خارج | ✅ |
| Watermark->PlaceholderText | خارج | ✅ |
| حذف SaleWindow | خارج | ✅ |
| حذف Models خالی | خارج | ✅ |
| حذف Hosting/Tools | خارج | ✅ |

---

## ۳. Gap Analysis

### 🔴 بحرانی (Phase 1-5)

- تست محاسبات مالی (LockedCost, Cashbox, Pricing, Stock) → Phase 1
- DoD برای Sale Transaction Boundary → Phase 2
- Optimistic Concurrency روی Item → Phase 3
- Idempotency در SaveSale → Phase 3
- Migrate() واقعی → Phase 4
- RestoreBackup ایمن → Phase 4
- Backup 3-2-1 + رمزنگاری → Phase 4
- AuditLog → Phase 5

### 🟠 مهم (Phase 6-8)

- Structured Logging + Global Handler → 6
- رفع catch{} خالی → 6
- N+1 در POS و Dashboard → 7
- AsNoTracking + Pagination → 7
- UI Thread violations → 8

### 🟡 پایین (Phase 9-10)

- Unit Tests → 9
- Integration Tests → 9
- EULA + Privacy + License → 10
- Code Signing → 10
- Updater تست → 10

---

## ۴. قالب Definition of Done

هر Phase فقط با چک‌لیست کامل Done می‌شود:

- [ ] تمام Scope پیاده‌سازی شد
- [ ] Build: 0 Warning, 0 Error
- [ ] Unit tests برای منطق جدید
- [ ] Integration test برای scenario اصلی
- [ ] Failure scenario تست شد
- [ ] Concurrency scenario تست شد (اگر مربوط)
- [ ] Regression: هیچ قابلیت قبلی نشکست
- [ ] مستندسازی در AI-HANDOFF به‌روز
- [ ] مستندسازی در AUDIT-RECONCILIATION به‌روز
- [ ] Commit با پیام توصیفی + tag

**قاعده:** هیچ تیک بدون شاهد قابل اثبات نیست.

---

## ۵. DoD تفصیلی هر Phase

### Phase 1 — Business Correctness

**Scope:** Sale, Return, Cost, Profit, Discount allocation, Rounding, Stock

**DoD:**
- [ ] LockedCostCalculator — چند خرید با تاریخ → میانگین درست
- [ ] CashboxCalculator — نقدی+کارتی+نسیه+دفتر → عدد دقیق
- [ ] PricingCalculator — Override + Markup → درست
- [ ] StockCalculator — Normal + Reversal → درست
- [ ] Discount allocation — مجموع = توزیع‌شده
- [ ] Rounding — در همه مسیرها
- [ ] Time-boundary: فروش ۲۳:۵۹ → درست
- [ ] Unit tests هر ۶ Service
- [ ] مستند docs/PHASE-1-BUSINESS-CORRECTNESS.md

**شواهد:** جدول تست با اعداد ورودی/خروجی.

### Phase 2 — Transaction Boundary

**Scope:** Sale, Payment, Inventory, Cashbox, Cost/Profit, Rollback

**DoD:**
- [ ] SaveSale داخل BeginTransaction
- [ ] شکست هر Stage → صفر تغییر DB
- [ ] تست: خطا در Stage ۳ → Stage ۱،۲ rollback
- [ ] تست: Sale موفق → همه commit
- [ ] تست: Exception در SaveChanges → rollback
- [ ] مستند: جریان Sale با شماره Stage

**شواهد:** ۴ سناریو با نتیجه DB بعد از شکست.

### Phase 3 — Concurrency + Idempotency

**Scope:** Stock race, Double-click, Duplicate invoice, Concurrency Token

**DoD:**
- [ ] Item دارای RowVersion یا معادل
- [ ] دو SaveSale همزمان → یکی موفق، یکی exception
- [ ] Double-click → یک Sale (نه دو)
- [ ] Invoice number یکتا در سطح DB
- [ ] تست: ۲ thread → Sale count = ۱
- [ ] تست: stock=1، دو فروش → یکی موفق

**شواهد:** خروجی تست همزمانی + اسکرین‌شات DB.

### Phase 4 — Crash Recovery + Backup

**Scope:** Migrate(), WAL, Backup verification, Encryption, External backup, Crash

**DoD:**
- [ ] EnsureCreated → Migrate()
- [ ] DB از صفر → همه Migrations اجرا
- [ ] Backup: کپی + Integrity Check + Restore
- [ ] Backup رمزنگاری (DPAPI/AES)
- [ ] Backup ثانویه روی USB
- [ ] تست: kill وسط SaveSale → DB سالم
- [ ] تست: Restore از backup → همه داده
- [ ] تست: DB خراب → از backup بازیابی

**شواهد:** سناریوی Crash + Recovery کامل.

### Phase 5 — Audit + Security

**Scope:** AuditLog, Authorization, Sensitive operations, Password policy

**DoD:**
- [ ] جدول AuditLog (UserId, Timestamp, Action, EntityType, EntityId, OldValue, NewValue)
- [ ] ثبت تغییر قیمت Item
- [ ] ثبت تغییر موجودی
- [ ] ثبت حذف (Soft Delete)
- [ ] ثبت تغییر کاربر/رمز
- [ ] هیچ رمز hardcode
- [ ] Password policy ≥ ۸ کاراکتر + حرف + عدد
- [ ] Rate limit (۵ تلاش → قفل)
- [ ] تست: تغییر قیمت → رکورد در AuditLog

**شواهد:** Query روی AuditLog بعد از عملیات نمونه.

### Phase 6 — Logging + Global Error Handling

**Scope:** Structured Logging, Global Handler, Rolling File

**DoD:**
- [ ] Global AppDomain.UnhandledException handler
- [ ] Global TaskScheduler.UnobservedTaskException handler
- [ ] Log به فایل با rolling روزانه
- [ ] فرمت: Timestamp | Level | User | Operation | Exception | StackTrace
- [ ] حذف catch{} خالی (همه → ErrorHandler.LogError)
- [ ] مسیر: G:\ShopManager-Data\logs\
- [ ] تست: Exception دستی → فایل log

**شواهد:** محتوای فایل log.

### Phase 7 — EF Core + Performance

**Scope:** N+1, AsNoTracking, DbContext, Index, Pagination

**DoD:**
- [ ] CreateProductTile → ۳ کوئری (نه ۹۰)
- [ ] Historyها → Pagination (100 ردیف)
- [ ] کوئری‌های read-only → AsNoTracking
- [ ] DbContext کوتاه‌عمر
- [ ] Index روی (DateGregorian, PaymentStatus)
- [ ] تست: POS ۳۰ کالا → < ۱ ثانیه
- [ ] تست: History ۱۰K ردیف → < ۵۰۰ms

**شواهد:** زمان‌سنجی + profiler.

### Phase 8 — Avalonia Reliability

**Scope:** UI Thread, Async, Event Leak, Timer Lifecycle

**DoD:**
- [ ] همه I/O روی background thread
- [ ] هیچ async void خارج از event handler
- [ ] همه Event Subscriptions → Unsubscribe در dispose
- [ ] همه Timers → Stop در close
- [ ] تست: ۸ ساعت باز → صفر leak
- [ ] تست: ۵۰ بار باز/بسته → صفر leak

**شواهد:** Task Manager قبل/بعد.

### Phase 9 — Tests

**Scope:** Unit, Integration, Regression

**DoD:**
- [ ] Coverage Domain.Services ≥ ۸۰٪
- [ ] Coverage Domain.Entities ≥ ۵۰٪
- [ ] Integration Test هر Service
- [ ] Regression فازهای ۱-۸
- [ ] dotnet test → ۰ fail

**شواهد:** Coverage report + خروجی.

### Phase 10 — Release Hardening

**Scope:** Updater, Signing, Licenses, EULA, Privacy

**DoD:**
- [ ] EULA.md
- [ ] PRIVACY.md
- [ ] LICENSES.md
- [ ] Updater تست (n → n+1)
- [ ] Code Signing (اگر گواهی)
- [ ] README کامل
- [ ] نصب روی ویندوز تمیز

**شواهد:** Screenshot نصب + signtool verify.

---

## ۶. Workflow هر Phase
AUDIT → وضعیت فعلی (CONFIRMED/POSSIBLE/NOT FOUND)
↓

SCOPE → داخل/خارج
↓

DoD → چک‌لیست قابل اثبات
↓

IMPL → پیاده‌سازی
↓

TEST → تست + شواهد
↓

REVIEW → مطابقت با DoD
↓

COMMIT → با پیام + tag

text

**اگر هر مرحله fail → برگرد به قبل. هیچ Phase نیمه‌کاره commit نمی‌شود.**

---

## ۷. وضعیت فعلی

| مورد | مقدار |
|---|---|
| فازهای کامل (Framework جدید) | 0 |
| فازهای با کد بدون DoD | 2 (Transaction, Unique Index) |
| فازهای شروع‌نشده | 8 |
| فازهای حذف‌شده از Roadmap | Alert، Cloud، Multi-terminal |
| کارهای انجام‌شده (تولید محکم) | ۱۳ |
| Build | 0W / 0E |
| Git | پاک، commit 7c8279c |

**نقطه شروع: Phase 1 — Business Correctness Audit.**

---

## ۸. Decision Log

| تاریخ | تصمیم | دلیل |
|---|---|---|
| 1405/07/07 | Framework جدید (۱۲ بُعد + Audit + Reconciliation + Roadmap) | جلوگیری از Scope Creep |
| 1405/07/07 | Alert System حذف | معماری زود است |
| 1405/07/07 | Logging در Phase 6 (نه 2.5) | Transaction خودش تست‌پذیر |
| 1405/07/07 | EULA/Privacy در Phase 10 | Release فقط |
| 1405/07/07 | Tax/Legal = Business Requirement | Scope question |
| 1405/07/07 | DoD اجباری | جلوگیری از پیشرفت کاذب |

---

**پایان سند. نقطه شروع: Phase 1 — Business Correctness Audit.**
