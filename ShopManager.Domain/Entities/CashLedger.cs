using ShopManager.Domain.Enums;

namespace ShopManager.Domain.Entities;

/// <summary>
/// دفتر صندوق — تراکنش‌های مالی جانبی
/// (وام، برداشت سود، سرمایه اولیه)
/// </summary>
public class CashLedger
{
    public int Id { get; set; }

    public string DateShamsi { get; set; } = string.Empty;
    public DateTime DateGregorian { get; set; }

    /// <summary>نوع تراکنش</summary>
    public LedgerType Type { get; set; }

    /// <summary>طرف حساب (اسم شخص یا توضیح)</summary>
    public string? Counterparty { get; set; }

    /// <summary>مبلغ ورودی به صندوق (اگه ورودیه)</summary>
    public decimal? AmountIn { get; set; }

    /// <summary>مبلغ خروجی از صندوق (اگه خروجیه)</summary>
    public decimal? AmountOut { get; set; }

    /// <summary>توضیحات</summary>
    public string? Note { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}