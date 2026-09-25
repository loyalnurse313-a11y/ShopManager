using ShopManager.Domain.Enums;

namespace ShopManager.Domain.Entities;

/// <summary>
/// کالا — موجودیت اصلی سیستم
/// هر کالا یه کد یکتا داره که کاربر وارد می‌کنه
/// </summary>
public class Item
{
    /// <summary>شناسه داخلی (خودکار توسط دیتابیس)</summary>
    public int Id { get; set; }

    /// <summary>کد کالا — کاربر وارد می‌کنه، باید یکتا باشه</summary>
    public int ItemCode { get; set; }

    /// <summary>نام کالا</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>دسته‌بندی (اختیاری — مثل: یکبارمصرف، لبنیاتی)</summary>
    public string? Category { get; set; }

    /// <summary>واحد اندازه‌گیری (عدد، رول، بسته، ...)</summary>
    public string Unit { get; set; } = "عدد";

    // ─── موجودی اولیه ───
    // این مقادیر فقط در زمان راه‌اندازی تنظیم می‌شن

    /// <summary>موجودی اولیه انبار در زمان راه‌اندازی</summary>
    public decimal OpeningWarehouseQty { get; set; } = 0;

    /// <summary>
    /// بهای واحد موجودی اولیه انبار (اختیاری)
    /// اگه خالی باشه، در محاسبه میانگین لحاظ نمی‌شه
    /// </summary>
    public decimal? OpeningWarehouseUnitCost { get; set; }

    /// <summary>موجودی اولیه مغازه در زمان راه‌اندازی</summary>
    public decimal OpeningShopQty { get; set; } = 0;

    /// <summary>
    /// درصد سود روی قیمت خرید — پایه محاسبه قیمت فروش
    /// (پیش‌فرض ۳۰٪)
    /// </summary>
    public decimal MarkupPct { get; set; } = 30m;

    /// <summary>
    /// قیمت فروش دستی (Override) — اگه پر باشه، به جای محاسبه خودکار استفاده می‌شه
    /// اگه خالی باشه، قیمت فروش = آخرین قیمت خرید × (1 + MarkupPct/100)
    /// </summary>
    public decimal? SalePrice { get; set; }

    // ─── هشدار موجودی ───

    /// <summary>درصد حد بحرانی نسبت به کل ورودی تاریخی (پیش‌فرض: ۱۰٪)</summary>
    public decimal LowStockCriticalPct { get; set; } = 0.10m;

    /// <summary>درصد حد هشدار نسبت به کل ورودی تاریخی (پیش‌فرض: ۲۵٪)</summary>
    public decimal LowStockWarningPct { get; set; } = 0.25m;

    /// <summary>
    /// حد بحرانی ثابت (اختیاری)
    /// اگه پر بشه، جای درصد پیش‌فرض رو می‌گیره
    /// </summary>
    public decimal? LowStockCriticalFixed { get; set; }

    /// <summary>حد هشدار ثابت (اختیاری)</summary>
    public decimal? LowStockWarningFixed { get; set; }

    // ─── متادیتا ───

    /// <summary>تاریخ ایجاد رکورد</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>آیا کالا فعاله؟ (برای حذف نرم)</summary>
    public bool IsActive { get; set; } = true;

    // ─── Navigation Properties ───
    // این‌ها ارتباط با جدول‌های دیگه رو نشون می‌دن

    /// <summary>همه خریدهای این کالا</summary>
    public ICollection<Purchase> Purchases { get; set; } = new List<Purchase>();

    /// <summary>همه انتقال‌های این کالا</summary>
    public ICollection<Transfer> Transfers { get; set; } = new List<Transfer>();

    /// <summary>همه فروش‌های این کالا</summary>
    public ICollection<Sale> Sales { get; set; } = new List<Sale>();
}