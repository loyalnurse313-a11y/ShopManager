namespace ShopManager.Domain.Enums;

/// <summary>
/// نوع تراکنش مالی جانبی در دفتر صندوق
/// </summary>
public enum LedgerType
{
    /// <summary>سرمایه اولیه</summary>
    InitialCapital = 0,

    /// <summary>وام داده‌شده به شخص</summary>
    LoanGiven = 1,

    /// <summary>بازپرداخت وام دریافت‌شده</summary>
    LoanRepaid = 2,

    /// <summary>برداشت سود شخصی</summary>
    ProfitWithdrawal = 3,

    /// <summary>تقسیم سود بین شرکا</summary>
    ProfitDistribution = 4,

    /// <summary>سایر موارد</summary>
    Other = 5
}