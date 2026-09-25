using ShopManager.Domain.Enums;

namespace ShopManager.Domain.Entities;

/// <summary>
/// انتقال کالا از انبار به مغازه
/// </summary>
public class Transfer
{
    public int Id { get; set; }

    /// <summary>کد کالای منتقل‌شده</summary>
    public int ItemId { get; set; }
    public Item? Item { get; set; }

    public string DateShamsi { get; set; } = string.Empty;
    public DateTime DateGregorian { get; set; }

    /// <summary>تعداد منتقل‌شده</summary>
    public decimal Qty { get; set; }

    /// <summary>توضیحات</summary>
    public string? Note { get; set; }

    public EntryType EntryType { get; set; } = EntryType.Normal;

    public int? ReversalOfId { get; set; }
    public Transfer? ReversalOf { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}