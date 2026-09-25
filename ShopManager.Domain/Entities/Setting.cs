namespace ShopManager.Domain.Entities;

/// <summary>
/// تنظیمات کلی سیستم — فقط یه رکورد داره (Id=1)
/// </summary>
public class Setting
{
    /// <summary>همیشه مقدارش ۱ هست — فقط یه رکورد</summary>
    public int Id { get; set; } = 1;

    /// <summary>سرمایه اولیه واریز‌شده به صندوق</summary>
    public decimal InitialCapital { get; set; } = 0;

    /// <summary>درصد پیش‌فرض حد بحرانی (پیش‌فرض: ۱۰٪)</summary>
    public decimal CriticalPct { get; set; } = 0.10m;

    /// <summary>درصد پیش‌فرض حد هشدار (پیش‌فرض: ۲۵٪)</summary>
    public decimal WarningPct { get; set; } = 0.25m;
}