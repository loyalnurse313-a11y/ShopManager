namespace ShopManager.Desktop.Services;

/// <summary>
/// کمکی برای escape کردن داده‌های کاربر در HTML
/// جلوگیری از XSS در فاکتورها و گزارش‌ها
/// </summary>
public static class HtmlEncoder
{
    /// <summary>escape کردن رشته برای استفاده در HTML</summary>
    public static string Encode(string? input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;
        return System.Net.WebUtility.HtmlEncode(input);
    }
}
