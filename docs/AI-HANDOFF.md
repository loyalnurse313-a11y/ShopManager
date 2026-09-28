```markdown
# AI-HANDOFF — سند انتقال پروژه ShopManager

> **هدف:** این فایل مرجع کامل برای هر AI است که وارد پروژه می‌شود. قبل از هر اقدامی، این سند را کامل بخوان.
>
> **آخرین به‌روزرسانی:** 1405/07/07 — پس از اتمام فاز ۱

---

## ۱. شناسنامه‌ی پروژه

| مورد | مقدار |
|---|---|
| نام | ShopManager — اپ POS/مدیریت فروشگاهی |
| مسیر | `G:\ShopManager\ShopManager` |
| DB | `G:\ShopManager-Data\shop.db` (SQLite + WAL) |
| Backups | `G:\ShopManager-Data\Backups\` |
| Stack | Avalonia 12.1.2 / .NET 10 / EF Core 10 / SQLite |
| ساختار | 3 پروژه: `Domain` / `Infrastructure` / `Desktop` |
| IDE | VS Code + PowerShell |
| گزارش ممیزی | `AUDIT-REPORT.md` (ریشه) |
| وضعیت Build | ✅ 0 Warning, 0 Error |

---

## ۲. معماری — ۳ لایه

```
Desktop (Viewها + سرویس‌های App)
   ↓
Infrastructure (AppDbContext + Migrations + ExcelExport)
   ↓
Domain (Entities + Enums + Helpers + Services خالص)
```

**قانون طلایی:** Domain هیچ‌وقت به EF یا Avalonia وابسته نمی‌شود. اگر شد، معماری شکسته.

---

## ۳. موجودیت‌های اصلی

| Entity | نقش | نکات حیاتی |
|---|---|---|
| `Item` | کالا | `ItemCode` یکتا، `SalePrice` اختیاری (Override)، `MarkupPct` پیش‌فرض ۳۰ |
| `Purchase` | خرید از تأمین‌کننده | `EntryType.Normal/Reversal`، `PaymentStatus` |
| `Transfer` | انتقال انبار→مغازه | `EntryType` برای reversal |
| `Sale` | فروش | `LockedUnitCost` snapshot است، هرگز تغییر نمی‌کند |
| `Customer` | مشتری | `Phone` یکتا |
| `User` | کاربر | `Role` (Admin/User)، ۱۳ مجوز `Can*` |
| `LoginHistory` | Audit ورود/خروج | `UserId` nullable |
| `CashLedger` | دفتر صندوق | `AmountIn`/`AmountOut` |
| `Setting` | تنظیمات (تک‌رکورد Id=1) | `InitialCapital` |

---

## ۴. قواعد کسب‌وکار حیاتی (غیرقابل نقض)

1. **بهای تمام‌شده قفل‌شده**: `LockedCostCalculator` فقط خریدهای «تا تاریخ همان فروش» را در نظر می‌گیرد — نه بعدی‌ها.
2. **میانگین موزون خرید**: `CalculateCurrentAverageCost` (فقط برای نمایش).
3. **قیمت فروش**: اگر `Item.SalePrice` پر باشد → Override، وگرنه `آخرین قیمت خرید × (1 + Markup/100)`.
4. **موجودی انبار** = `OpeningWarehouseQty + ΣNormal(Purchases) − ΣNormal(Transfers)` (با reversal معکوس).
5. **موجودی مغازه** = `OpeningShopQty + ΣNormal(Transfers) − ΣNormal(Sales)`.
6. **صندوق** = `سرمایه + فروش نقدی − خرید نقدی + ورودی دفتر − خروجی دفتر`. کارتی وارد صندوق نمی‌شود.
7. **Reversal**: حذف فیزیکی نیست؛ ردیف جدید با `EntryType=Reversal` + `ReversalOfId` ثبت می‌شود.
8. **حد هشدار**: اگر `LowStockCriticalFixed` پر باشد اولویت دارد، وگرنه درصدی از `OpeningWarehouseQty + OpeningShopQty + همه خریدها`.
9. **موجودی منفی مجاز نیست** (در SaveSale باید چک شود).
10. **شماره فاکتور**: الگو `POS-1405-0001` — تولید در سمت کلاینت (fragile).

---

## ۵. قواعد پروژه (فاز ۱ به بعد)

### 🔒 امنیت

**XSS**: `HtmlEncoder.Encode` همیشه **بیرونی‌ترین لایه** است.

```csharp
// ✅ درست
HtmlEncoder.Encode(PersianNumber.ToPersianDigits(input))

// ❌ غلط — entity خراب می‌شود
PersianNumber.ToPersianDigits(HtmlEncoder.Encode(input))
```

**MaxLength**: در XAML فقط UI را محدود می‌کند → **گارد سمت منطق اجباری است**:
```csharp
if (text.Length > N) { StatusText.Text = "..."; return; }
```

**رمز**: هیچ hardcode. `admin/admin` حذف شد. کاربر اول در `FirstRunSetupWindow` ساخته می‌شود.

### 🗄️ دیتابیس

- **`DeleteBehavior.Restrict`** روی FK‌های مالی تاریخی.
- **Soft Delete**: `Item.IsActive = false` (نه حذف فیزیکی).
- **Transaction**: هر عملیات چندمرحله‌ای مالی داخل `BeginTransaction`.
- **`AsNoTracking()`** روی همه کوئری‌های read-only.

### 💾 بکاپ و بازیابی

- **D4 - قاعده بحرانی**: بعد از هر `Copy-Item` از `masterbak`، اجباری:
  ```powershell
  Set-ItemProperty "...\shop.db" -Name IsReadOnly -Value $false
  ```
  وگرنه SQLite خطای `attempt to write a readonly database` می‌دهد.
- **بدون wildcard**: هرگز `Remove-Item "shop.db*"` (باگ D1).
- **Copy نه Move**: بازیابی از masterbak با `Copy-Item` (باگ D2).

### 🧪 تست DB

- `masterbak` = بکاپ مادر، ReadOnly، ۱ هفته نگه داشته می‌شود.
- برای تست دستکاری DB، همیشه از `masterbak` استفاده کن.
- هش legacy SHA256: `Base64(SHA256(password + salt))` بدون پیشوند `v2:`.

### 📝 Build و Commit

- **بعد از هر تغییر**: `dotnet build -t:Rebuild` و تأیید `0 Warning, 0 Error`.
- **یک فایل در هر گام** (طبق سبک فعلی).

---

## ۶. الگوی پاسخ AI (الزامی)

1. **فارسی فنی**، کد انگلیسی + کامنت فارسی
2. **ساختار:** جدول + bullet + code block
3. **بدون مقدمه**، بدون تکرار مطلب قبلی
4. **POSSIBLE / NOT FOUND** اگر شک داری (هرگز حدس نزن)
5. **قبل از هر تغییر**: مسیر فایل + شماره خط + کد قبل + کد بعد
6. **بعد از هر تغییر**: Build test + گزارش
7. **هیچ تغییری بدون اجازه‌ی صریح کاربر**

---

## ۷. وضعیت فازها

| فاز | عنوان | وضعیت |
|:---:|---|:---:|
| ۰ | زیرساخت VS Code | ✅ |
| ۱ | امنیت (XSS + API key + admin) | ✅ |
| ۲ | پاک‌سازی کد مرده (`SaleWindow`) | ⏳ بعدی |
| ۲.۵ | رفع `catch{}` خالی | ⏳ |
| ۲.۷ | Structured Logging + Crash Reporting | ⏳ |
| ۳ | بهینه‌سازی داده (N+1 + Pagination) | ⏳ |
| ۳.۵ | بهینه‌سازی داشبورد | ⏳ |
| ۴ | RestoreBackup + `SettingsWindow:290` | ⏳ |
| ۴.۵ | Backup 3-2-1 + Encryption | ⏳ |
| ۵ | Migrate + Race + Denormalized Stock | ⏳ |
| ۵.۵ | Audit Trail | ⏳ |
| ۶ | Code Signing | 🔒 نیاز گواهی |
| ۷ | تست‌های واحد Domain | ⏳ |
| ۸ | Archiving + Backup بهبود | ⏳ |
| ۸.۵ | Alert System | ⏳ |
| ۰.۵ | EULA + Privacy + License | ⏳ |

---

## ۸. منشور ۱۲ بُعدی کیفیت (چک‌لیست هر تغییر)

قبل از هر تغییر، این ۱۲ سؤال را بپرس:

1. **عملکرد**: آیا واقعاً همان کار را می‌کند؟
2. **امنیت**: آیا XSS/SQLi/Authorization را تضعیف می‌کند؟
3. **عملکرد/مقیاس**: با ۱۰۰K رکورد هنوز سریع است؟
4. **قابلیت اطمینان**: اگر برق برود وسط این تغییر چه می‌شود؟
5. **قابلیت نگهداری**: ۶ ماه دیگر کسی می‌فهمد؟
6. **نظارت**: اگر خطا رخ دهد، چطور می‌فهمم؟
7. **پشتیبان‌گیری**: اگر دیسک بمیرد، داده بازیابی می‌شود؟
8. **تاریخ/زمان**: ساعت سیستم روی این اثر دارد؟
9. **صحت کسب‌وکار**: قانون مالیاتی رعایت می‌شود؟
10. **UX**: کاربر بدون آموزش می‌فهمد؟
11. **انطباق**: حریم خصوصی / مجوز نرم‌افزار رعایت است؟
12. **آینده‌نگری**: اگر ۲ شعبه بشود کار می‌کند؟

---

## ۹. Backlog مشترک (فاز ۲+)

### فاز ۲ — پاک‌سازی
- حذف `SaleWindow.axaml` + `.axaml.cs` (~۵۸۴ خط)
- حذف پوشه‌ی خالی `Models`
- حذف بسته‌های تکراری در `Desktop.csproj` (اگر در `Infrastructure` هستند)
- حذف `Microsoft.Extensions.Hosting` (اگر بلااستفاده)

### فاز ۲.۵ — Server-side Validation
- `PurchaseWindow` → `SupplierNote` (۵۰۰)
- `TransferWindow` → `Note` (۵۰۰) — قبلاً انجام شد
- `UserEditWindow` → `FullName` (۲۰۰) + `Username` (۵۰) — قبلاً انجام شد

### Excel Formula Injection (فاز ۲)
- `TransferHistoryWindow.axaml.cs:299` → `t.Note`
- `LoginHistoryWindow.axaml.cs:348,349,356` → `h.Username`, `h.FullName`, `h.Note`
- `ExcelExportService.cs` → همه‌ی `.Value = string`

### فاز ۴ — RestoreBackup
- `SettingsWindow.axaml.cs:290` → `Environment.Exit(0)` بعد از Restore → `Shutdown()`

### سیاست Legacy (بازبینی آینده)
- کاربران با هش قدیمی روی ورود موفق **خودکار ارتقا** می‌یابند (گزینه A)
- بدون اجبار تغییر رمز
- اگر روزی سیاست سخت‌گیرانه‌تر خواستی: تعمیم `CheckLegacyAdminPassword` به همه‌ی `NeedsUpgrade`

### D4 — بازیابی از ReadOnly
- بعد از هر `Copy-Item` از `masterbak`، `Set-ItemProperty IsReadOnly $false` **اجباری**

---

## ۱۰. فایل‌های کلیدی (نقشه سریع)

| فایل | نقش |
|---|---|
| `Services/DatabaseService.cs` | ساخت DbContext + migration دستی |
| `Services/AuthService.cs` | Login + auto-upgrade hash |
| `Services/AuthServiceInitializer.cs` | `HasAnyUser`, `CreateInitialAdmin`, `CheckLegacyAdminPassword` |
| `Services/PasswordHasher.cs` | PBKDF2 v2 + SHA256 legacy |
| `Services/BackupService.cs` | بکاپ خودکار + RestoreBackup |
| `Services/HtmlEncoder.cs` | escape HTML |
| `Services/SaleInvoiceHtmlBuilder.cs` | سازنده HTML فاکتور |
| `Views/FirstRunSetupWindow.axaml.cs` | راه‌اندازی اولیه (جدید) |
| `Views/POSWindow.axaml.cs` | POS — God Class ۱۶۵۷ خط |
| `Views/DashboardWindow.axaml.cs` | داشبورد — God Class ۱۷۴۵ خط |
| `Infrastructure/Persistence/AppDbContext.cs` | مدل EF |
| `App.axaml.cs` | سوییچ بین FirstRunSetup / Login |

---

## ۱۱. ابزار AI فعلی

- **ابزار اصلی:** DeepSeek Chat
- **پیشنهاد:** نصب Cline در VS Code + اتصال به DeepSeek API برای ریفکتور مقیاس‌دار
- **قاعده:** برای کارهای حساس (`SaveSale`, `PasswordHasher`, Migration) → فقط با چت
- **قاعده:** برای ریفکتور تکراری (۲۴ فایل XSS) → Cline

---

## ۱۲. هشدارهای مهم

### ⚠️ قبل از هر تغییر
- Build فعلی سبز است؟ (`0W/0E`)
- DB متصل به برنامه بسته است؟
- بکاپ امروز گرفته شده؟

### ⚠️ بعد از هر تغییر
- Build مجدد
- `grep` تأیید (اگر حذف کردی)
- گزارش در backlog

### ⚠️ ممنوع مطلق
- `Remove-Item "shop.db*"` (wildcard)
- `Move-Item` برای بازیابی از بکاپ
- ویرایش `masterbak`
- حذف بی‌تأیید فایل‌های کد
- `Environment.Exit` در مسیرهایی که `ShutdownRequested` باید اجرا شود

------

**پایان سند.** هر AI که این را می‌خواند، آماده‌ی کار روی پروژه است.