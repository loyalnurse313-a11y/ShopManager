namespace ShopManager.Domain.Enums;

/// <summary>
/// نوع ثبت تراکنش — عادی یا برگشت
/// </summary>
public enum EntryType
{
    /// <summary>تراکنش عادی</summary>
    Normal = 0,

    /// <summary>برگشت تراکنش (اصلاح اشتباه)</summary>
    Reversal = 1
}