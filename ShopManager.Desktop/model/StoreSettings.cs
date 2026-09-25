using System.Collections.Generic;

namespace ShopManager.Desktop.Model;

/// <summary>
/// تنظیمات فروشگاه — ذخیره در فایل JSON
/// </summary>
public class StoreSettings
{
    // ═══════════ اطلاعات فروشگاه ═══════════

    public string StoreName { get; set; } = "فروشگاه ظروف یکبار مصرف خوی";
    public string Address { get; set; } = "";
    public string Phone { get; set; } = "";
    public string LogoPath { get; set; } = "";
    public string FooterText { get; set; } = "از خرید شما سپاسگزاریم";

    // ═══════════ مالی ═══════════

    public decimal DefaultMarkupPct { get; set; } = 30;

    /// <summary>لیست پایانه‌های POS</summary>
    public List<string> POSTerminals { get; set; } = new();

    // ═══════════ فاکتور و چاپ ═══════════

    /// <summary>
    /// اندازه کاغذ پیش‌فرض فاکتور
    /// مقادیر: "A4" — "A5" — "80mm" — "58mm"
    /// </summary>
    public string PaperSize { get; set; } = "A4";

    /// <summary>چاپ خودکار فاکتور بعد از ثبت فروش در POS</summary>
    public bool AutoPrintAfterSale { get; set; } = false;

    /// <summary>نمایش لوگو در فاکتور</summary>
    public bool ShowLogoOnInvoice { get; set; } = true;

    // ═══════════ پشتیبان‌گیری ═══════════

    public bool BackupAutoEnabled { get; set; } = true;
    public int BackupIntervalMinutes { get; set; } = 15;
    public int BackupKeepCount { get; set; } = 30;
}