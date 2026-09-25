namespace ShopManager.Desktop.Models;

/// <summary>
/// تنظیمات ظاهری و کاربری
/// </summary>
public class AppPreferences
{
    /// <summary>Light یا Dark</summary>
    public string Theme { get; set; } = "Light";

    /// <summary>فونت اصلی</summary>
    public string FontFamily { get; set; } = "Vazirmatn";

    /// <summary>سایز فونت</summary>
    public double FontSize { get; set; } = 14;

    /// <summary>نام رنگ Accent (از ۱۲ تا انتخاب)</summary>
    public string AccentColor { get; set; } = "Blue";

    /// <summary>نام آخرین پایانه POS استفاده‌شده</summary>
    public string LastPOSTerminal { get; set; } = "";
}