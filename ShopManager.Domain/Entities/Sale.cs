using ShopManager.Domain.Enums;

namespace ShopManager.Domain.Entities;

/// <summary>
/// فروش روزانه از مغازه
/// نکته کلیدی: LockedUnitCost یه snapshot ثابته و هرگز تغییر نمی‌کنه
/// </summary>
public class Sale
{
    public int Id { get; set; }

    public int ItemId { get; set; }
    public Item? Item { get; set; }

    /// <summary>شماره فاکتور — همه اقلام یه فروش این شماره رو دارن</summary>
    public string? InvoiceNumber { get; set; }

    /// <summary>مشتری (اختیاری)</summary>
    public int? CustomerId { get; set; }
    public Customer? Customer { get; set; }

    public string DateShamsi { get; set; } = string.Empty;
    public DateTime DateGregorian { get; set; }

    /// <summary>تعداد فروخته‌شده</summary>
    public decimal Qty { get; set; }

    /// <summary>قیمت فروش واحد</summary>
    public decimal SaleUnitPrice { get; set; }

    /// <summary>
    /// بهای تمام‌شده قفل‌شده — در لحظه ثبت محاسبه می‌شه
    /// و بعداً هرگز تغییر نمی‌کنه
    /// </summary>
    public decimal LockedUnitCost { get; set; }

    /// <summary>جمع درآمد = Qty × SaleUnitPrice</summary>
    public decimal Revenue { get; set; }

    /// <summary>جمع بهای تمام‌شده = Qty × LockedUnitCost</summary>
    public decimal Cost { get; set; }

    /// <summary>سود = Revenue - Cost</summary>
    public decimal Profit { get; set; }

    public PaymentStatus PaymentStatus { get; set; }

    /// <summary>
    /// نام پایانه POS — فقط برای پرداخت کارتی
    /// مثال: «بانک ملی»، «پاسارگاد»
    /// </summary>
    public string? CardTerminal { get; set; }

    public EntryType EntryType { get; set; } = EntryType.Normal;

    public int? ReversalOfId { get; set; }
    public Sale? ReversalOf { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}