using System;

namespace ShopManager.Domain.Entities;

/// <summary>
/// مشتری — اطلاعات تماس و سابقه
/// با هر فروش جدید، اگه مشتری جدید باشه، خودکار اینجا ذخیره می‌شه
/// </summary>
public class Customer
{
    public int Id { get; set; }

    /// <summary>نام مشتری</summary>
    public string Name { get; set; } = "";

    /// <summary>شماره تماس (یکتا — برای پیدا کردن مشتری)</summary>
    public string Phone { get; set; } = "";

    /// <summary>توضیحات (اختیاری)</summary>
    public string? Note { get; set; }

    /// <summary>اولین خرید</summary>
    public DateTime FirstPurchaseAt { get; set; } = DateTime.UtcNow;

    /// <summary>آخرین خرید</summary>
    public DateTime LastPurchaseAt { get; set; } = DateTime.UtcNow;

    /// <summary>مجموع مبلغ خریدها</summary>
    public decimal TotalPurchasedAmount { get; set; } = 0;

    /// <summary>تعداد دفعات خرید</summary>
    public int PurchaseCount { get; set; } = 0;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}