namespace ShopManager.Desktop.Models;

/// <summary>
/// تنظیمات ظاهری — سبک Solid Modern
/// </summary>
public class AppPreferences
{
    /// <summary>Light یا Dark</summary>
    public string Theme { get; set; } = "Light";

    /// <summary>فونت اصلی</summary>
    public string FontFamily { get; set; } = "Vazirmatn";

    /// <summary>سایز فونت</summary>
    public double FontSize { get; set; } = 14;

    /// <summary>نام رنگ Accent انتخاب‌شده در تب «ظاهر» — مثل Blue، Green، Red، Orange، Purple</summary>
    public string AccentColor { get; set; } = "Blue";

    /// <summary>آخرین پایانه POS استفاده‌شده</summary>
    public string LastPOSTerminal { get; set; } = "";
}
