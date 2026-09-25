namespace ShopManager.Domain.Enums;

/// <summary>
/// وضعیت هشدار موجودی کالا
/// </summary>
public enum StockStatus
{
    /// <summary>موجودی کافی است</summary>
    Ok = 0,

    /// <summary>موجودی کم — هشدار زرد</summary>
    Warning = 1,

    /// <summary>موجودی بحرانی — هشدار قرمز</summary>
    Critical = 2
}