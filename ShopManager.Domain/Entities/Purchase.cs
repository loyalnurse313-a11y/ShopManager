using ShopManager.Domain.Enums;

namespace ShopManager.Domain.Entities;

/// <summary>
/// تراکنش خرید از تأمین‌کننده → ورود به انبار
/// برای اصلاح اشتباه، رکورد جدید با EntryType=Reversal ثبت می‌شود
/// </summary>
public class Purchase
{
    public int Id { get; set; }

    /// <summary>کد کالای خریداری‌شده</summary>
    public int ItemId { get; set; }
    public Item? Item { get; set; }

    /// <summary>تاریخ شمسی نمایشی (مثل 1405/06/01)</summary>
    public string DateShamsi { get; set; } = string.Empty;

    /// <summary>تاریخ میلادی برای مرتب‌سازی و محاسبات</summary>
    public DateTime DateGregorian { get; set; }

    /// <summary>تعداد خریداری‌شده (برای برگشت، منفی)</summary>
    public decimal Qty { get; set; }

    /// <summary>قیمت خرید واحد</summary>
    public decimal UnitCost { get; set; }

    /// <summary>جمع = Qty × UnitCost</summary>
    public decimal TotalCost { get; set; }

    /// <summary>نام تأمین‌کننده یا توضیحات</summary>
    public string? SupplierNote { get; set; }

    /// <summary>وضعیت پرداخت (نقدی یا نسیه)</summary>
    public PaymentStatus PaymentStatus { get; set; }

    /// <summary>نوع تراکنش (عادی یا برگشت)</summary>
    public EntryType EntryType { get; set; } = EntryType.Normal;

    /// <summary>اگه برگشت باشه، به کدوم رکورد اشاره می‌کنه</summary>
    public int? ReversalOfId { get; set; }
    public Purchase? ReversalOf { get; set; }

    /// <summary>تاریخ ثبت در سیستم</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}