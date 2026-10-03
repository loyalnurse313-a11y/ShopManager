# MASTER-BACKLOG

> **هدف:** مرجع واحد برای همه‌ی یافته‌های ممیزی.
> **آخرین به‌روزرسانی:** به‌روزرسانی وضعیت Phase 4 تا checkpoint `4B-2B` (پیاده‌سازی و verify شده در working tree؛ هنوز commit نشده و hash ندارد)؛ آخرین checkpoint کد production در commit: `59d0dfc`؛ commitهای بعدی تا `67453a2` فقط مستندات/قواعد عملیاتی مخزن (AGENTS.md) را تغییر داده‌اند.
> **وضعیت:** Phase 1، Phase 2 و Phase 3 کامل‌اند؛ Phase 4 — Crash Recovery + Backup **IN PROGRESS** است و تکمیل نشده.
> نام یافته‌ها و ارجاع‌های قدیمی، سابقهٔ ممیزی‌اند؛ ستون وضعیت و توضیحات closure، نتیجهٔ فعلی را مشخص می‌کنند.

---

## منبع ۱ — AUDIT-REPORT.md

| ID    | یافته                         | فایل                       | شدت | Phase  | وضعیت                                |
| ----- | ----------------------------- | -------------------------- | --- | ------ | ------------------------------------ |
| AR-1  | نبود Transaction دور SaveSale | POSWindow                  | 🔴  | 2      | ✅                                   |
| AR-2  | EnsureCreated به جای Migrate  | DatabaseService            | 🔴  | 4      | ❌                                   |
| AR-3  | XSS در ۴ پنجره                | \*HistoryWindow            | 🟠  | ad-hoc | ✅                                   |
| AR-4  | RestoreBackup ناایمن          | BackupService              | 🟠  | 4      | ❌                                   |
| AR-5  | Unique Index InvoiceNumber    | AppDbContext               | 🟠  | 3      | ✅ یکتایی در SaleOperations؛ شرح زیر |
| AR-6  | N+1 در CreateProductTile      | POSWindow                  | 🟠  | 7      | ❌                                   |
| AR-7  | Race بررسی موجودی             | POSWindow / TransferWindow | 🟠  | 3      | ✅ فروش و انتقال عادی                |
| AR-8  | Code Signing                  | release.yml                | 🟡  | 10     | ❌                                   |
| AR-9  | API key plaintext             | AppPreferences             | 🟡  | ad-hoc | ✅                                   |
| AR-10 | admin/admin                   | AuthServiceInitializer     | 🟡  | ad-hoc | ✅                                   |
| AR-11 | SaleWindow کد مرده            | Views                      | 🟢  | ad-hoc | ✅                                   |

---

## منبع ۲ — Audit 6.A (فاز ۱)

| ID    | یافته                         | فایل:خط        | شدت | Phase  | وضعیت |
| ----- | ----------------------------- | -------------- | --- | ------ | ----- |
| 6.A1  | Revenue خام                   | POSWindow:1170 | 🔴  | 1      | ✅ B3 |
| 6.A2  | Cost از LockedCost            | POSWindow:1171 | ✅  | 1      | ✅    |
| 6.A3  | Profit خام                    | POSWindow:1172 | ✅  | 1      | ✅    |
| 6.A4  | تخفیف فقط از Profit           | POSWindow:1174 | 🔴  | 1      | ✅ B4 |
| 6.A5  | Revenue خام ذخیره             | POSWindow:1190 | 🔴  | 1      | ✅ B3 |
| 6.A6  | Profit invariant نقض          | POSWindow:1191 | 🔴  | 1      | ✅ B4 |
| 6.A7  | متغیرهای مرده                 | POSWindow:1203 | 🟡  | 1      | ✅ B6 |
| 6.A8  | Customer.TotalPurchasedAmount | POSWindow:1109 | ✅  | 1      | ✅    |
| 6.A9  | Sale.DiscountAmount غایب      | POSWindow      | 🔴  | 1      | ✅ B5 |
| 6.A10 | rounding غایب                 | POSWindow      | 🟠  | 1      | ✅ B7 |
| 6.A11 | Reversal هیچ‌جا ساخته نمی‌شود | All            | 🔴  | Future | ❌    |

---

## منبع ۳ — ۱۲ بُعدی

| ID  | بُعد                      | Phase        | وضعیت                       |
| --- | ------------------------- | ------------ | --------------------------- |
| D1  | Backup 3-2-1              | 4            | ❌                          |
| D2  | Structured Logging        | 6            | ❌                          |
| D3  | Alert System              | Future       | خارج                        |
| D4  | Audit Trail               | 5            | ❌                          |
| D5  | EULA/Privacy              | 10           | ❌                          |
| D6  | Tax/Legal                 | Business Req | 🟡                          |
| D7  | Inventory Concurrency     | 3            | ✅ محدودهٔ فروش/انتقال عادی |
| D8  | Sale Transaction Boundary | 2            | ✅                          |
| D9  | Idempotency               | 3            | ✅ عملیات فروش              |
| D10 | Time/Date edge cases      | 1            | ❌                          |
| D11 | UX Loading States         | 8            | ❌                          |
| D12 | Future-Proofing           | Future       | خارج                        |

---

## منبع ۴ — PHASE-1 (B1-B7)

| ID  | باگ                  | فایل              | Phase | وضعیت |
| --- | -------------------- | ----------------- | ----- | ----- |
| B1  | SalePrice=0 Override | PricingCalculator | 1     | ✅    |
| B2  | ToEven rounding      | PricingCalculator | 1     | ✅    |
| B3  | Revenue خام          | POSWindow         | 1     | ✅    |
| B4  | Profit invariant     | POSWindow         | 1     | ✅    |
| B5  | DiscountAmount غایب  | POSWindow+Sale    | 1     | ✅    |
| B6  | متغیرهای مرده        | POSWindow         | 1     | ✅    |
| B7  | Rounding غایب        | POSWindow         | 1     | ✅    |

---

## منبع ۵ — PHASE-1 جانبی

| ID  | یافته                            | Phase     | وضعیت                                              |
| --- | -------------------------------- | --------- | -------------------------------------------------- |
| S1  | POSCartItem.HasDiscount          | Pre-Phase | ✅                                                 |
| S2  | POSCartItem.DiscountPct          | Pre-Phase | ✅                                                 |
| S3  | SaleCartItem مدل موازی           | Pre-Phase | ✅                                                 |
| S4  | Negative-stock guard             | 3         | ✅ کنترل authoritative داخل تراکنش                 |
| S5  | Optimistic Concurrency Token     | 3         | ✅ هدف هم‌زمانی با تراکنش SQLite؛ token پیاده نشده |
| S6  | StockAlert Warning در Fixed-only | 7         | ❌                                                 |
| S7  | Reversal Sign Contradiction      | Future    | ❌                                                 |
| S8  | Schema Drift                     | 4         | ❌                                                 |

---

## نمای Phase-Centric

### Phase 1 ✅ COMPLETE

- [B1-B7] رفع شده

### Pre-Phase — Cleanup

- [x] [S1] POSCartItem.HasDiscount
- [x] [S2] POSCartItem.DiscountPct
- [x] [S3] SaleCartItem

شواهد: [PRE-PHASE-2-CLEANUP.md](PRE-PHASE-2-CLEANUP.md)

### Phase 2 — Transaction Boundary ✅ COMPLETE

- [x] [AR-1] نبود Transaction دور SaveSale — ✅
- [x] [D8] Sale Transaction Boundary — ✅

شواهد: [PHASE-2-TRANSACTION-BOUNDARY.md](PHASE-2-TRANSACTION-BOUNDARY.md)

### Phase 3 — Concurrency + Idempotency ✅ COMPLETE

- [x] 3A (`45c6511`): جلوگیری از ورود مجدد به پرداخت در `POSWindow.OnPayClick`.
- [x] 3B-1 (`c3268af`): جدول `SaleOperations` با کلید یکتای `OperationId`، fingerprint اجباری و index یکتای `InvoiceNumber`؛ آماده‌سازی و اعتبارسنجی schema برای دیتابیس جدید/قدیمی.
- [x] 3B-2 (`5ff9a68`): snapshot/fingerprint، replay بدون ثبت مجدد، رد payload متفاوت، تفکیک pending قطعی/نامعلوم، بازیابی برخورد شمارهٔ فاکتور و کنترل مجموع ردیف‌های هم‌کالا.
- [x] 3C (`da7d5b4`): کنترل موجودی و ثبت انتقال در یک تراکنش؛ اثبات رقابت فروش‌های مستقل بدون تغییر production فروش؛ تفکیک شکست ثبت از خطای پس از commit.
- [x] [AR-7 / D7 / S4 / S5]: تراکنش SQLite پیش از خواندن موجودی، جایگزین الزام طراحی قدیمی RowVersion شد. **RowVersion یا optimistic concurrency token اضافه نشده است.**

**تطبیق AR-5:** یکتایی در سطح عملیات/فاکتور در `SaleOperations` اعمال می‌شود؛ `Sales.InvoiceNumber` به‌تنهایی unique نیست، چون یک فاکتور چند ردیف دارد. رکوردهای تاریخی backfill نشده‌اند؛ سرویس فروش برخورد با شماره‌های تاریخی `Sales` را نیز بررسی می‌کند. این closure ادعای اصلاح همهٔ schemaهای legacy یا اجرای `Migrate()` نیست.

**شواهد Phase 3C:** build با 0 errors / 0 warnings؛ 134/134 tests passed، 0 failed / 0 skipped؛ `git diff --check` clean؛ final adversarial review: PASS. خروجی build/test/check از اجرای ثبت‌شدهٔ Phase 3C است؛ PASS بازبینی نهایی طبق تأیید کاربر در درخواست Documentation Closure ثبت شده است. در این کار مستندسازی build/test دوباره اجرا نشده‌اند.

خلاصهٔ Phase 3 در [PHASE-3-CONCURRENCY-IDEMPOTENCY.md](PHASE-3-CONCURRENCY-IDEMPOTENCY.md) آمده است. نگاشت دقیق رفتارها به فایل‌ها و نام تست‌ها، منشأ شواهد و حدود تضمین در [AUDIT-RECONCILIATION.md](AUDIT-RECONCILIATION.md) و راهنمای ادامه در [AI-HANDOFF.md](AI-HANDOFF.md) آمده است.

**خارج از closure:** exactly-once/recovery عمومی انتقال، بازیابی pending فروش پس از restart، crash/restore و schema drift، writerهای خارجی و همهٔ مسیرهای ویرایش موجودی. تست دستی UI/چاپ و ماتریس مستقل WAL/DELETE/timeout، جزو شواهد این closure نیستند. این موارد با موفقیت تست رقابت دو اتصال مستقل یکسان نیستند.

### Phase 4 — Crash Recovery + Backup — IN PROGRESS (تکمیل‌نشده)

**Checkpoint های انجام‌شده (کد + تست در commit `59d0dfc`):**

- [x] 4A-1 (`0944a7e`): انتشار اتمیک بکاپ SQLite — staging، اعتبارسنجی و publish ایمن.
- [x] 4A-2 (`05212da`): هویت canonical دیتابیس و توقف امن startup پیش از settings/backup/auth.
- [x] 4A-3 (`3466604`): قرارداد دوام SQLite — `journal_mode=WAL` + `synchronous=FULL` با read-back.
- [x] 4A-4 (`22ca6fa`): قابلیت اطمینان چرخهٔ حیات بکاپ — single-flight، ردیابی نسل تغییرات، پاک‌سازی staging یتیم و لاگ خطا.
- [x] 4B-1 (`461670c`): آماده‌سازی بازیابی امن پیش از تعویض — اعتبارسنجی، اسنپ‌شات ایمنی WAL-سازگار و گارد هویت فایل/hard-link؛ دیتابیس زنده دست‌نخورده می‌ماند.
- [x] 4B-2A (`59d0dfc`): بنیاد intent بازیابی ماندگار — flush → SHA-256 → انتشار اتمیک intent → مسلح‌سازی؛ gate پذیرش دیتابیس.
- [x] 4B-2B (commit نشده؛ hash ثبت نشده): offline file-level recovery engine (`RestoreRecoveryService.Recover`) — پس از intent ماندگار forward-only (forward-complete یا BLOCK، هرگز rollback)؛ وضعیت منتشرشده با SHA-256 مورد انتظار دیتابیس زنده و نبودن sidecarهای زندهٔ `-wal`/`-shm`/`-journal` تأیید می‌شود؛ فقط tombstone/incoming artifactهای دقیقاً operation-owned (بدون wildcard)؛ cleanup plan-then-execute؛ وضعیت مبهم/ناایمن fail-closed و مسلح؛ حذف intent آخرین mutation موفق؛ نبودن `SafetyBackupPath` مانع forward completion نیست.

**شواهد 4B-2B (working tree؛ تا commit شدن HEAD نیست):** `RestoreRecoveryServiceTests` 42 passed؛ کل `ShopManager.Domain.Tests` 270 passed؛ build غیرافزایشی solution با 0 warnings / 0 errors؛ adversarial/final review: PASS بدون issue مسدودکنندهٔ Critical/High/Medium. این evidence تکمیل Phase 4 را ادعا نمی‌کند.

**باقی‌مانده (pending) — Phase 4 کامل نیست:**

- [ ] Subsequent Phase 4 integration (در 4B-2B نیست و پیاده نشده): اتصال recovery به startup و مصرف restore intent، app-lifetime mutex، UI، shutdown/quiesce، و حذف legacy restore path.
- [ ] [AR-4] جایگزینی `BackupService.RestoreBackup` قدیمی و مسیر `SettingsWindow.axaml.cs:290` با `Environment.Exit(0)`.
- [ ] [AR-2] EnsureCreated → Migrate().
- [ ] [S8] Schema Drift / migrations.
- [ ] [D1] Backup 3-2-1 + رمزنگاری + بکاپ ثانویه.
- [ ] تست‌های crash (kill وسط SaveSale)، restore کامل و بازیابی از DB خراب.

شواهد و جزئیات: [PHASE-4-CRASH-RECOVERY-BACKUP.md](PHASE-4-CRASH-RECOVERY-BACKUP.md)

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
